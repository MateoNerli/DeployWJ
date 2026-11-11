using System.Net;
using System.Text;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeployWebJobs.Tests;

public class ZipDeployStrategyTests
{
    /// <summary>HttpMessageHandler falso: anota cada llamada y responde según una función.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = new();
        public List<string?> AuthHeaders { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            AuthHeaders.Add(request.Headers.Authorization?.ToString());
            if (request.Content is not null)
                await request.Content.CopyToAsync(Stream.Null, cancellationToken); // fuerza la serialización (progreso)
            return Responder(request);
        }
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (DeployContext Context, PackageInfo Package, TempDir Dir) NewContext(WebJobType type = WebJobType.Continuous)
    {
        var dir = new TempDir();
        dir.File("Job.exe", "binario");
        dir.File("Job.exe.config", "<configuration/>");

        var builder = new PackageBuilder();
        var plan = builder.Plan(dir.Path, Array.Empty<string>());
        var package = builder.Build(plan);

        var context = new DeployContext
        {
            Client = new ClientConfig { Id = "cli", Name = "Cliente", SiteName = "mi-sitio" },
            Credential = new ClientCredential(@"mi-sitio\$mi-sitio", "pw-secreta"),
            WebJob = new WebJobDef { Name = "Mi.Job", Type = type },
            Plan = plan,
            Package = package,
            Settings = new DeploySettings { VerifyTimeoutSeconds = 1 }
        };
        return (context, package, dir);
    }

    private static ZipDeployStrategy NewStrategy(FakeHandler handler) =>
        new(new FakeFactory(handler), NullLogger<ZipDeployStrategy>.Instance, pollInterval: TimeSpan.Zero);

    [Fact]
    public async Task Continuo_hace_conectar_detener_subir_iniciar_verificar_en_orden()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;

        var handler = new FakeHandler
        {
            Responder = r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/Mi.Job")
                ? Json("{\"name\":\"Mi.Job\",\"status\":\"Running\"}")
                : new HttpResponseMessage(HttpStatusCode.OK)
        };
        var steps = new List<DeployStep>();

        await NewStrategy(handler).DeployAsync(context, new InlineProgress(p => steps.Add(p.Step)), CancellationToken.None);

        Assert.Equal(new[]
        {
            "GET /api/continuouswebjobs",
            "POST /api/continuouswebjobs/Mi.Job/stop",
            "PUT /api/zip/site/wwwroot/app_data/jobs/continuous/Mi.Job/",
            "POST /api/continuouswebjobs/Mi.Job/start",
            "GET /api/continuouswebjobs/Mi.Job"
        }, handler.Calls.ToArray());

        Assert.Contains(DeployStep.Subiendo, steps);
        Assert.Contains(DeployStep.Verificando, steps);
    }

    [Fact]
    public async Task Usa_autenticacion_basica_con_el_usuario_de_Kudu()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler { Responder = r => Json("{\"status\":\"Running\"}") };

        await NewStrategy(handler).DeployAsync(context, new InlineProgress(_ => { }), CancellationToken.None);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("$mi-sitio:pw-secreta"));
        Assert.All(handler.AuthHeaders, h => Assert.Equal(expected, h));
    }

    [Fact]
    public async Task Triggered_no_detiene_ni_inicia()
    {
        var (context, package, dir) = NewContext(WebJobType.Triggered);
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler { Responder = r => Json("{}") };

        await NewStrategy(handler).DeployAsync(context, new InlineProgress(_ => { }), CancellationToken.None);

        Assert.Equal(new[]
        {
            "GET /api/triggeredwebjobs",
            "PUT /api/zip/site/wwwroot/app_data/jobs/triggered/Mi.Job/",
            "GET /api/triggeredwebjobs/Mi.Job"
        }, handler.Calls.ToArray());
    }

    [Fact]
    public async Task Detener_con_404_no_es_error_primer_deploy()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler
        {
            Responder = r => r.RequestUri!.AbsolutePath.EndsWith("/stop")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json("{\"status\":\"Running\"}")
        };

        await NewStrategy(handler).DeployAsync(context, new InlineProgress(_ => { }), CancellationToken.None);

        Assert.Contains("PUT /api/zip/site/wwwroot/app_data/jobs/continuous/Mi.Job/", handler.Calls);
    }

    [Fact]
    public async Task Error_401_no_es_pasajero_y_no_filtra_la_contrasena()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) };

        var ex = await Assert.ThrowsAsync<DeployException>(() =>
            NewStrategy(handler).DeployAsync(context, new InlineProgress(_ => { }), CancellationToken.None));

        Assert.False(ex.IsTransient);
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain("pw-secreta", ex.Message);
        Assert.Single(handler.Calls); // cortó en el primer paso
    }

    [Fact]
    public async Task Error_503_es_pasajero()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };

        var ex = await Assert.ThrowsAsync<DeployException>(() =>
            NewStrategy(handler).DeployAsync(context, new InlineProgress(_ => { }), CancellationToken.None));

        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task Falla_si_el_job_no_queda_Running()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler { Responder = r => Json("{\"status\":\"Stopped\"}") };

        var ex = await Assert.ThrowsAsync<DeployException>(() =>
            NewStrategy(handler).DeployAsync(context, new InlineProgress(_ => { }), CancellationToken.None));

        Assert.Contains("Stopped", ex.Message);
    }

    [Fact]
    public async Task Informa_progreso_de_bytes_al_subir()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler { Responder = r => Json("{\"status\":\"Running\"}") };
        var maxDone = 0L;
        var total = 0L;

        await NewStrategy(handler).DeployAsync(context, new InlineProgress(p =>
        {
            if (p.Step == DeployStep.Subiendo) { maxDone = Math.Max(maxDone, p.BytesDone); total = p.BytesTotal; }
        }), CancellationToken.None);

        Assert.True(total > 0);
        Assert.Equal(total, maxDone);
    }

    [Fact]
    public void DescribePlan_no_toca_la_red_y_menciona_la_ruta_remota()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = new FakeHandler();

        var lines = NewStrategy(handler).DescribePlan(context);

        Assert.Empty(handler.Calls);
        Assert.Contains(lines, l => l.Contains("app_data/jobs/continuous/Mi.Job/"));
    }

    /// <summary>IProgress síncrono para los tests (Progress&lt;T&gt; reparte en otro hilo).</summary>
    private sealed class InlineProgress : IProgress<DeployProgress>
    {
        private readonly Action<DeployProgress> _action;
        public InlineProgress(Action<DeployProgress> action) => _action = action;
        public void Report(DeployProgress value) => _action(value);
    }

    private static FakeHandler HandlerWithJobs(string listJson) => new()
    {
        Responder = r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/api/continuouswebjobs"
            ? Json(listJson)
            : Json("{\"name\":\"Mi.Job\",\"status\":\"Running\"}")
    };

    [Fact]
    public async Task MustExist_con_nombre_exacto_publica()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = HandlerWithJobs("[{\"name\":\"Otro\"},{\"name\":\"Mi.Job\"}]");

        await NewStrategy(handler).DeployAsync(
            new DeployContext
            {
                Client = context.Client, Credential = context.Credential, WebJob = context.WebJob, Plan = context.Plan,
                Package = context.Package, Settings = context.Settings, Existence = WebJobExistence.MustExist
            },
            new InlineProgress(_ => { }), CancellationToken.None);

        Assert.Contains("PUT /api/zip/site/wwwroot/app_data/jobs/continuous/Mi.Job/", handler.Calls);
    }

    [Fact]
    public async Task MustExist_con_nombre_distinto_no_sube_nada_y_sugiere_el_parecido()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = HandlerWithJobs("[{\"name\":\"mi.job\"}]");

        var ex = await Assert.ThrowsAsync<DeployException>(() => NewStrategy(handler).DeployAsync(
            new DeployContext
            {
                Client = context.Client, Credential = context.Credential, WebJob = context.WebJob, Plan = context.Plan,
                Package = context.Package, Settings = context.Settings, Existence = WebJobExistence.MustExist
            },
            new InlineProgress(_ => { }), CancellationToken.None));

        Assert.False(ex.IsTransient);
        Assert.Contains("no existe", ex.Message);
        Assert.Contains("'mi.job'", ex.Message);
        Assert.DoesNotContain(handler.Calls, call => call.StartsWith("PUT") || call.Contains("/stop"));
    }

    [Fact]
    public async Task MustBeNew_si_ya_existe_no_sube_nada()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = HandlerWithJobs("[{\"name\":\"MI.JOB\"}]"); // mayúsculas distintas: igual es el mismo en Windows

        var ex = await Assert.ThrowsAsync<DeployException>(() => NewStrategy(handler).DeployAsync(
            new DeployContext
            {
                Client = context.Client, Credential = context.Credential, WebJob = context.WebJob, Plan = context.Plan,
                Package = context.Package, Settings = context.Settings, Existence = WebJobExistence.MustBeNew
            },
            new InlineProgress(_ => { }), CancellationToken.None));

        Assert.Contains("Ya existe", ex.Message);
        Assert.DoesNotContain(handler.Calls, call => call.StartsWith("PUT"));
    }

    [Fact]
    public async Task MustBeNew_si_no_existe_publica()
    {
        var (context, package, dir) = NewContext();
        using var _1 = dir; using var _2 = package;
        var handler = HandlerWithJobs("[]");

        await NewStrategy(handler).DeployAsync(
            new DeployContext
            {
                Client = context.Client, Credential = context.Credential, WebJob = context.WebJob, Plan = context.Plan,
                Package = context.Package, Settings = context.Settings, Existence = WebJobExistence.MustBeNew
            },
            new InlineProgress(_ => { }), CancellationToken.None);

        Assert.Contains("PUT /api/zip/site/wwwroot/app_data/jobs/continuous/Mi.Job/", handler.Calls);
    }
}
