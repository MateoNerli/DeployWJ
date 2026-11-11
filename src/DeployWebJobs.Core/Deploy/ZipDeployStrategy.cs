using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;
using Microsoft.Extensions.Logging;

namespace DeployWebJobs.Core.Deploy;

/// <summary>
/// Publica un WebJob con Zip Deploy contra la API Kudu (SCM) del App Service, todo por HTTPS:
///   1) GET  /api/{tipo}webjobs                         -> prueba la conexión y las credenciales
///   2) POST /api/continuouswebjobs/{job}/stop          -> libera el .exe (solo WebJobs continuos)
///   3) PUT  /api/zip/site/wwwroot/app_data/jobs/{tipo}/{job}/  -> descomprime el ZIP en la carpeta del WebJob
///   4) POST /api/continuouswebjobs/{job}/start         -> arranca de nuevo
///   5) GET  /api/{tipo}webjobs/{job}                   -> verifica el estado (Running / Success / PendingRestart; Starting se tolera)
/// El paso 3 SOBREESCRIBE archivos pero no borra los que ya existían (igual que el FTP anterior).
/// </summary>
public sealed class ZipDeployStrategy : IDeployStrategy
{
    /// <summary>Nombre del HttpClient registrado en la inyección de dependencias.</summary>
    public const string HttpClientName = "kudu";

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<ZipDeployStrategy> _logger;
    private readonly TimeSpan _pollInterval;

    public ZipDeployStrategy(IHttpClientFactory factory, ILogger<ZipDeployStrategy> logger, TimeSpan? pollInterval = null)
    {
        _factory = factory;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    public DeployMethod Method => DeployMethod.ZipDeploy;

    private static string Kind(WebJobType type) => type == WebJobType.Continuous ? "continuous" : "triggered";

    public IReadOnlyList<string> DescribePlan(DeployContext c)
    {
        var kind = Kind(c.WebJob.Type);
        var host = c.Client.EffectiveScmHost;
        var lines = new List<string> { $"Conectar a https://{host} (autenticación básica SCM)" };

        if (c.WebJob.Type == WebJobType.Continuous)
            lines.Add($"Detener el WebJob continuo '{c.WebJob.Name}'");

        lines.Add($"Subir {c.Plan.Files.Count} archivos ({FormatBytes(c.Plan.TotalBytes)} sin comprimir) a " +
                  $"/site/wwwroot/app_data/jobs/{kind}/{c.WebJob.Name}/");

        if (c.WebJob.Type == WebJobType.Continuous)
            lines.Add($"Iniciar el WebJob '{c.WebJob.Name}'");

        if (c.Existence == WebJobExistence.MustExist)
            lines.Insert(1, $"Verificar que el WebJob '{c.WebJob.Name}' YA exista con ese nombre exacto (si no, no se sube nada)");
        else if (c.Existence == WebJobExistence.MustBeNew)
            lines.Insert(1, $"Verificar que NO exista ya un WebJob '{c.WebJob.Name}' (si existe, no se sube nada)");

        lines.Add("Verificar que el WebJob quede en ejecución");
        return lines;
    }

    public async Task DeployAsync(DeployContext c, IProgress<DeployProgress> progress, CancellationToken ct)
    {
        if (c.Package is null)
            throw new InvalidOperationException("No hay un paquete ZIP armado para subir.");

        var http = _factory.CreateClient(HttpClientName);
        var auth = BuildAuth(c.Credential);
        var kind = Kind(c.WebJob.Type);
        var isContinuous = c.WebJob.Type == WebJobType.Continuous;
        var jobApi = $"api/{kind}webjobs/{c.WebJob.Name}";
        var zipApi = $"api/zip/site/wwwroot/app_data/jobs/{kind}/{c.WebJob.Name}/";
        var requestTimeout = TimeSpan.FromSeconds(c.Settings.RequestTimeoutSeconds);

        // 1) Conexión + credenciales
        progress.Report(c.Progress(DeployStep.Conectando, $"Conectando a {c.Client.EffectiveScmHost}..."));
        string listBody = "";
        using (var response = await SendAsync(http, c, HttpMethod.Get, $"api/{kind}webjobs", auth, null, requestTimeout, ct).ConfigureAwait(false))
        {
            await EnsureSuccessAsync(response, "Conexión con Kudu", DeployStep.Conectando, ct).ConfigureAwait(false);
            if (c.Existence != WebJobExistence.Any)
                listBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        // 1b) Verificación de nombre: ya existe / es nuevo (antes de tocar nada)
        CheckExistence(c, kind, listBody);

        // 2) Detener (solo continuos). 404 = el WebJob todavía no existe (primer deploy): no es un error.
        if (isContinuous)
        {
            progress.Report(c.Progress(DeployStep.DeteniendoJob, $"Deteniendo '{c.WebJob.Name}'..."));
            using var response = await SendAsync(http, c, HttpMethod.Post, $"{jobApi}/stop", auth, null, requestTimeout, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NotFound)
                await EnsureSuccessAsync(response, "Detener el WebJob", DeployStep.DeteniendoJob, ct).ConfigureAwait(false);
        }

        // 3) Subir el ZIP (con progreso de bytes)
        var total = c.Package.ZipBytes;
        progress.Report(c.Progress(DeployStep.Subiendo, "Subiendo paquete...", 0, total));
        var uploadProgress = new InlineProgress<long>(sent =>
            progress.Report(c.Progress(DeployStep.Subiendo, "Subiendo paquete...", sent, total)));

        using (var content = new ProgressFileContent(c.Package.ZipPath, uploadProgress))
        using (var response = await SendAsync(http, c, HttpMethod.Put, zipApi, auth, content,
                   TimeSpan.FromSeconds(c.Settings.UploadTimeoutSeconds), ct).ConfigureAwait(false))
        {
            await EnsureSuccessAsync(response, "Subida del paquete", DeployStep.Subiendo, ct).ConfigureAwait(false);
        }
        progress.Report(c.Progress(DeployStep.Subiendo, "Paquete subido.", total, total));

        // 4) Iniciar
        if (isContinuous)
        {
            progress.Report(c.Progress(DeployStep.IniciandoJob, $"Iniciando '{c.WebJob.Name}'..."));
            using var response = await SendAsync(http, c, HttpMethod.Post, $"{jobApi}/start", auth, null, requestTimeout, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Iniciar el WebJob", DeployStep.IniciandoJob, ct).ConfigureAwait(false);
        }

        // 5) Verificar
        progress.Report(c.Progress(DeployStep.Verificando, "Verificando estado del WebJob..."));
        await VerifyAsync(http, c, jobApi, auth, isContinuous, requestTimeout, progress, ct).ConfigureAwait(false);
    }

    private async Task VerifyAsync(HttpClient http, DeployContext c, string jobApi, AuthenticationHeaderValue auth,
        bool isContinuous, TimeSpan requestTimeout, IProgress<DeployProgress> progress, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(c.Settings.VerifyTimeoutSeconds);
        string? lastStatus = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using (var response = await SendAsync(http, c, HttpMethod.Get, jobApi, auth, null, requestTimeout, ct).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {
                    if (!isContinuous)
                    {
                        progress.Report(c.Progress(DeployStep.Verificando, "WebJob detectado."));
                        return;
                    }

                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    lastStatus = ReadStatus(body);
                    // Un WebJob continuo que arranca, trabaja y termina (como los que leen una cola) pasa por
                    // Running -> Success -> PendingRestart y vuelve a arrancar solo: todos esos estados son normales.
                    if (IsHealthyStatus(lastStatus))
                    {
                        progress.Report(c.Progress(DeployStep.Verificando, $"WebJob OK (estado: {lastStatus})."));
                        return;
                    }
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    lastStatus = "no encontrado";
                }
                else
                {
                    await EnsureSuccessAsync(response, "Verificar el WebJob", DeployStep.Verificando, ct).ConfigureAwait(false);
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                // "Starting" es un estado de transición: Azure ya recibió los archivos y está arrancando el job.
                if (string.Equals(lastStatus, "Starting", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lastStatus, "Initializing", StringComparison.OrdinalIgnoreCase))
                {
                    progress.Report(c.Progress(DeployStep.Verificando, $"WebJob todavía arrancando (estado: {lastStatus}); se dio por publicado."));
                    return;
                }
            }

            if (DateTime.UtcNow >= deadline)
                throw new DeployException(
                    $"El paquete se subió, pero el WebJob no quedó en ejecución (estado: {lastStatus ?? "desconocido"}).",
                    isTransient: false, DeployStep.Verificando);

            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Compara el nombre pedido con los WebJobs que ya tiene el cliente. Si no coincide con lo esperado
    /// lanza un error NO reintentable y no se sube nada.
    /// </summary>
    private static void CheckExistence(DeployContext c, string kind, string listJson)
    {
        if (c.Existence == WebJobExistence.Any) return;

        var names = ReadJobNames(listJson);
        var name = c.WebJob.Name;

        if (c.Existence == WebJobExistence.MustExist)
        {
            if (names.Contains(name, StringComparer.Ordinal)) return;

            var similar = names.Where(n => LooksSimilar(n, name)).ToList();
            var hint = similar.Count > 0
                ? $" ¿Será {string.Join(" o ", similar.Select(n => "'" + n + "'"))}? Usá el nombre exacto."
                : names.Count == 0
                    ? $" Este cliente no tiene ningún WebJob {kind}."
                    : $" Los que tiene este cliente: {string.Join(", ", names.Take(8))}{(names.Count > 8 ? ", ..." : "")}.";

            throw new DeployException(
                $"El WebJob '{name}' no existe en este cliente (lo marcaste como existente).{hint} No se subió nada.",
                isTransient: false, DeployStep.Conectando);
        }

        // MustBeNew
        var clash = names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            throw new DeployException(
                $"Ya existe un WebJob llamado '{clash}' en este cliente (lo marcaste como nuevo). No se subió nada para no pisarlo; " +
                "si querés actualizarlo, marcalo como existente.",
                isTransient: false, DeployStep.Conectando);
    }

    private static List<string> ReadJobNames(string json)
    {
        var names = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return names;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                foreach (var prop in item.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "name", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var n = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                    }
                }
            }
        }
        catch (JsonException)
        {
            throw new DeployException(
                "No se pudo leer la lista de WebJobs del cliente para verificar el nombre. No se subió nada.",
                isTransient: false, DeployStep.Conectando);
        }

        return names;
    }

    /// <summary>"WJ.SGR.Foo" y "wjsgrfoo" se consideran parecidos (ignora mayúsculas y puntuación).</summary>
    private static bool LooksSimilar(string a, string b)
    {
        static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var x = Norm(a);
        var y = Norm(b);
        if (x.Length == 0 || y.Length == 0) return false;
        return x == y || x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal);
    }

    private static bool IsHealthyStatus(string? status) =>
        status is not null &&
        (status.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Success", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("PendingRestart", StringComparison.OrdinalIgnoreCase));

    private static AuthenticationHeaderValue BuildAuth(ClientCredential credential)
    {
        // Kudu espera "$sitio:password"; el usuario guardado puede venir como "sitio\$sitio" (formato FTP).
        var user = ClientConfig.KuduUserName(credential.UserName);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{credential.Password}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, DeployContext c, HttpMethod method,
        string relativePath, AuthenticationHeaderValue auth, HttpContent? content, TimeSpan timeout, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(c.Client.ScmBaseUri, relativePath));
        request.Headers.Authorization = auth;
        if (content is not null) request.Content = content;
        else if (method == HttpMethod.Post) request.Content = new ByteArrayContent(Array.Empty<byte>());

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Venció nuestro timeout (no fue Ctrl+C): se considera error de red pasajero.
            throw new DeployException($"Tiempo de espera agotado ({timeout.TotalSeconds:0}s).", isTransient: true);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string what, DeployStep step, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var code = (int)response.StatusCode;
        string body;
        try { body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { body = ""; }
        if (body.Length > 300) body = body[..300] + "...";

        var transient = code == 408 || code == 429 || code >= 500;
        var hint = code switch
        {
            401 => " Credenciales rechazadas: revisá usuario/contraseña de publicación del sitio.",
            403 => " Acceso denegado: puede estar deshabilitada la autenticación básica SCM o haber restricción de IP en el sitio.",
            404 => " No se encontró el recurso: revisá el nombre del sitio / host SCM.",
            _ => ""
        };

        _logger.LogWarning("{What} falló con HTTP {Code}", what, code);
        throw new DeployException(
            $"{what} falló (HTTP {code} {response.ReasonPhrase}).{hint}" + (string.IsNullOrWhiteSpace(body) ? "" : $" Detalle: {body}"),
            transient, step);
    }

    private static string? ReadStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "status", StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString();
            }
        }
        catch (JsonException) { /* respuesta no JSON: se trata como estado desconocido */ }
        return null;
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.#} {units[i]}";
    }
}
