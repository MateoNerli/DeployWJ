using System.Diagnostics;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;
using Microsoft.Extensions.Logging;

namespace DeployWebJobs.Core.Deploy;

/// <summary>
/// Coordina un deploy completo:
///   valida el pedido -> arma el paquete UNA vez -> por cada cliente (en paralelo con un límite):
///   valida, obtiene credenciales, ejecuta la estrategia con reintentos y registra el resultado.
/// No imprime nada: informa por IProgress, así sirve igual para la consola o para una UI gráfica.
/// Cada cliente termina SIEMPRE con un evento final (DeployProgress.Outcome != null), sea OK, error,
/// omitido, cancelado o simulado. Un error en un cliente nunca frena a los demás.
/// Con Ctrl+C (CancellationToken) corta limpio.
/// </summary>
public sealed class DeployOrchestrator
{
    private readonly ConfigService _config;
    private readonly ICredentialStore _credentials;
    private readonly PackageBuilder _packageBuilder;
    private readonly IReadOnlyDictionary<DeployMethod, IDeployStrategy> _strategies;
    private readonly ILogger<DeployOrchestrator> _logger;

    public DeployOrchestrator(
        ConfigService config,
        ICredentialStore credentials,
        PackageBuilder packageBuilder,
        IEnumerable<IDeployStrategy> strategies,
        ILogger<DeployOrchestrator> logger)
    {
        _config = config;
        _credentials = credentials;
        _packageBuilder = packageBuilder;
        _strategies = strategies.ToDictionary(s => s.Method);
        _logger = logger;
    }

    /// <summary>Muestra qué se subiría (sin crear el ZIP). Útil para mostrar un resumen antes de confirmar.</summary>
    public PackagePlan Preview(string sourceFolder) =>
        _packageBuilder.Plan(sourceFolder, _config.Config.Settings.ExcludePatterns);

    public async Task<DeployRunResult> RunAsync(DeployRequest request, IProgress<DeployProgress>? progress, CancellationToken ct)
    {
        progress ??= NullProgress<DeployProgress>.Instance;
        var total = Stopwatch.StartNew();
        var settings = _config.Config.Settings;

        if (!WebJobDef.IsValidName(request.WebJobName))
            throw new ArgumentException("El nombre del WebJob es inválido (solo letras, números, punto, guion y guion bajo).");
        if (request.ClientIds.Count == 0)
            throw new ArgumentException("No se seleccionó ningún cliente.");

        var plan = _packageBuilder.Plan(request.SourceFolder, settings.ExcludePatterns);
        _logger.LogInformation("Deploy de {Job} ({Type}) desde {Folder}: {Files} archivos, {Excluded} excluidos, {Clients} clientes, simulación={Dry}",
            request.WebJobName, request.WebJobType, plan.SourceFolder, plan.Files.Count, plan.Excluded.Count,
            request.ClientIds.Count, request.DryRun);

        PackageInfo? package = null;
        try
        {
            if (!request.DryRun)
                package = await Task.Run(() => _packageBuilder.Build(plan), ct).ConfigureAwait(false);

            var parallelism = Math.Clamp(request.MaxParallelism ?? settings.MaxParallelism, 1, 16);
            using var gate = new SemaphoreSlim(parallelism);

            var tasks = request.ClientIds.Distinct()
                .Select(id => RunClientAsync(id, request, settings, plan, package, gate, progress, ct))
                .ToList();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            if (!request.DryRun && results.Any(r => r.Outcome == DeployOutcome.Ok))
                _config.RecordDeploy(request.WebJobName, request.WebJobType, plan.SourceFolder);

            total.Stop();
            _logger.LogInformation("Deploy terminado en {Elapsed}: {Ok} OK, {Err} con error", total.Elapsed,
                results.Count(r => r.Outcome == DeployOutcome.Ok), results.Count(r => r.Outcome == DeployOutcome.Error));

            return new DeployRunResult(results, total.Elapsed, plan, request.DryRun);
        }
        finally
        {
            package?.Dispose(); // borra el ZIP temporal
        }
    }

    private async Task<ClientDeployResult> RunClientAsync(
        string clientId, DeployRequest request, DeploySettings settings, PackagePlan plan, PackageInfo? package,
        SemaphoreSlim gate, IProgress<DeployProgress> progress, CancellationToken ct)
    {
        var client = _config.FindClient(clientId);
        if (client is null)
        {
            var missing = new ClientDeployResult(clientId, clientId, DeployOutcome.Error,
                "El cliente no existe en la configuración.", TimeSpan.Zero, 0);
            progress.Report(new DeployProgress(clientId, clientId, DeployStep.Finalizado, missing.Message, Outcome: DeployOutcome.Error));
            return missing;
        }

        try
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Finish(progress, client, DeployOutcome.Cancelado, "Cancelado antes de empezar.", TimeSpan.Zero, 0);
        }

        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        try
        {
            progress.Report(new DeployProgress(client.Id, client.Name, DeployStep.Validando, "Validando configuración..."));

            if (!client.Enabled)
                return Finish(progress, client, DeployOutcome.Omitido, "Cliente deshabilitado.", stopwatch.Elapsed, attempts);

            var errors = client.Validate();
            if (errors.Count > 0)
                return Finish(progress, client, DeployOutcome.Error, string.Join(" ", errors), stopwatch.Elapsed, attempts);

            var credential = _credentials.Get(client.Id);
            if (credential is null)
                return Finish(progress, client, DeployOutcome.Error,
                    "No hay credenciales guardadas para este cliente. Cargalas desde Clientes > Editar.", stopwatch.Elapsed, attempts);

            if (!_strategies.TryGetValue(client.Method, out var strategy))
                return Finish(progress, client, DeployOutcome.Error,
                    $"El método de deploy '{client.Method}' no está disponible en esta versión.", stopwatch.Elapsed, attempts);

            var context = new DeployContext
            {
                Client = client,
                Credential = credential,
                WebJob = new WebJobDef { Name = request.WebJobName, Type = request.WebJobType },
                Plan = plan,
                Settings = settings,
                Existence = request.Existence,
                Package = package
            };

            if (request.DryRun)
            {
                foreach (var line in strategy.DescribePlan(context))
                    progress.Report(context.Progress(DeployStep.Validando, "[Simulación] " + line));
                return Finish(progress, client, DeployOutcome.Simulado, "Simulación: no se subió nada.", stopwatch.Elapsed, attempts);
            }

            await RetryPolicy.ExecuteAsync(
                async (attempt, token) =>
                {
                    attempts = attempt;
                    context.Attempt = attempt;
                    await strategy.DeployAsync(context, progress, token).ConfigureAwait(false);
                },
                maxAttempts: settings.MaxRetries + 1,
                baseDelay: TimeSpan.FromSeconds(settings.RetryBaseDelaySeconds),
                onRetry: (attempt, ex, wait) =>
                {
                    _logger.LogWarning("[{Client}] intento {Attempt} falló: {Message}. Reintento en {Wait}s",
                        client.Name, attempt, ex.Message, wait.TotalSeconds);
                    progress.Report(context.Progress(DeployStep.Conectando,
                        $"Falló el intento {attempt}: {ex.Message} Reintentando en {wait.TotalSeconds:0}s..."));
                },
                cancellationToken: ct).ConfigureAwait(false);

            _logger.LogInformation("[{Client}] publicado OK en {Elapsed}", client.Name, stopwatch.Elapsed);
            return Finish(progress, client, DeployOutcome.Ok, "Publicado correctamente.", stopwatch.Elapsed, attempts);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Finish(progress, client, DeployOutcome.Cancelado, "Cancelado por el usuario.", stopwatch.Elapsed, attempts);
        }
        catch (Exception ex)
        {
            _logger.LogError("[{Client}] error: {Message}", client.Name, ex.Message);
            return Finish(progress, client, DeployOutcome.Error, ex.Message, stopwatch.Elapsed, attempts);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Arma el resultado y emite el evento final del cliente.</summary>
    private static ClientDeployResult Finish(IProgress<DeployProgress> progress, ClientConfig client,
        DeployOutcome outcome, string message, TimeSpan elapsed, int attempts)
    {
        var result = new ClientDeployResult(client.Id, client.Name, outcome, message, elapsed, attempts);
        progress.Report(new DeployProgress(client.Id, client.Name, DeployStep.Finalizado, message,
            Attempt: Math.Max(attempts, 1), Outcome: outcome));
        return result;
    }
}
