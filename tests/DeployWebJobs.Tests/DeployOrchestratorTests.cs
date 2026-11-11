using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeployWebJobs.Tests;

public class DeployOrchestratorTests
{
    /// <summary>Estrategia falsa: permite simular éxito, fallos y demoras sin red.</summary>
    private sealed class FakeStrategy : IDeployStrategy
    {
        private int _running;
        private int _maxRunning;
        private readonly object _lock = new();
        public int DeployCalls;
        public Dictionary<string, int> CallsPerClient { get; } = new();
        public Func<DeployContext, int, CancellationToken, Task> Behavior { get; set; } = (_, _, _) => Task.CompletedTask;
        public int MaxConcurrent => _maxRunning;

        public DeployMethod Method => DeployMethod.ZipDeploy;
        public IReadOnlyList<string> DescribePlan(DeployContext context) => new[] { "paso simulado" };

        public async Task DeployAsync(DeployContext context, IProgress<DeployProgress> progress, CancellationToken ct)
        {
            int callNumber;
            lock (_lock)
            {
                DeployCalls++;
                CallsPerClient[context.Client.Id] = callNumber = CallsPerClient.GetValueOrDefault(context.Client.Id) + 1;
                _running++;
                _maxRunning = Math.Max(_maxRunning, _running);
            }
            try { await Behavior(context, callNumber, ct); }
            finally { lock (_lock) { _running--; } }
        }
    }

    private sealed class Env : IDisposable
    {
        public TempDir Dir { get; } = new();
        public TempDir Publish { get; } = new();
        public InMemoryCredentialStore Credentials { get; } = new();
        public ConfigService Config { get; }
        public FakeStrategy Strategy { get; } = new();
        public DeployOrchestrator Orchestrator { get; }

        public Env()
        {
            Publish.File("Job.exe");
            Config = new ConfigService(Path.Combine(Dir.Path, "config.json"), Credentials);
            Config.Config.Settings.RetryBaseDelaySeconds = 0; // sin esperas en los tests
            Orchestrator = new DeployOrchestrator(Config, Credentials, new PackageBuilder(),
                new IDeployStrategy[] { Strategy }, NullLogger<DeployOrchestrator>.Instance);
        }

        public string AddClient(string name, bool withCredentials = true, bool enabled = true)
        {
            var client = Config.AddOrUpdateClient(new ClientConfig { Name = name, SiteName = ClientConfig.Slugify(name), Enabled = enabled });
            if (withCredentials) Credentials.Set(client.Id, new ClientCredential("u", "p"));
            return client.Id;
        }

        public DeployRequest Request(IEnumerable<string> ids, bool dryRun = false, int? parallelism = null) => new()
        {
            SourceFolder = Publish.Path,
            WebJobName = "Mi.Job",
            ClientIds = ids.ToList(),
            DryRun = dryRun,
            MaxParallelism = parallelism
        };

        public void Dispose() { Dir.Dispose(); Publish.Dispose(); }
    }

    [Fact]
    public async Task Publica_a_todos_y_recuerda_el_webjob()
    {
        using var env = new Env();
        var ids = new[] { env.AddClient("Uno"), env.AddClient("Dos") };

        var result = await env.Orchestrator.RunAsync(env.Request(ids), null, CancellationToken.None);

        Assert.Equal(2, result.OkCount);
        Assert.Equal(0, result.ErrorCount);
        var job = Assert.Single(env.Config.WebJobs);
        Assert.Equal("Mi.Job", job.Name);
    }

    [Fact]
    public async Task Un_error_en_un_cliente_no_frena_a_los_demas()
    {
        using var env = new Env();
        var bueno = env.AddClient("Bueno");
        var malo = env.AddClient("Malo");
        env.Strategy.Behavior = (ctx, _, _) =>
            ctx.Client.Id == malo ? throw new DeployException("401 credenciales", isTransient: false) : Task.CompletedTask;

        var result = await env.Orchestrator.RunAsync(env.Request(new[] { bueno, malo }), null, CancellationToken.None);

        Assert.Equal(1, result.OkCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Contains("401", result.Clients.Single(c => c.ClientId == malo).Message);
        Assert.Equal(1, env.Strategy.CallsPerClient[malo]); // error definitivo: sin reintentos
    }

    [Fact]
    public async Task Reintenta_errores_pasajeros()
    {
        using var env = new Env();
        var id = env.AddClient("Red inestable");
        env.Strategy.Behavior = (_, call, _) =>
            call < 3 ? throw new DeployException("timeout", isTransient: true) : Task.CompletedTask;

        var result = await env.Orchestrator.RunAsync(env.Request(new[] { id }), null, CancellationToken.None);

        var r = Assert.Single(result.Clients);
        Assert.Equal(DeployOutcome.Ok, r.Outcome);
        Assert.Equal(3, r.Attempts);
    }

    [Fact]
    public async Task Respeta_el_limite_de_paralelismo()
    {
        using var env = new Env();
        var ids = Enumerable.Range(1, 6).Select(i => env.AddClient($"Cliente {i}")).ToList();
        env.Strategy.Behavior = async (_, _, ct) => await Task.Delay(40, ct);

        await env.Orchestrator.RunAsync(env.Request(ids, parallelism: 2), null, CancellationToken.None);

        Assert.InRange(env.Strategy.MaxConcurrent, 1, 2);
        Assert.Equal(6, env.Strategy.DeployCalls);
    }

    [Fact]
    public async Task Secuencial_hace_de_a_uno()
    {
        using var env = new Env();
        var ids = Enumerable.Range(1, 3).Select(i => env.AddClient($"Cliente {i}")).ToList();
        env.Strategy.Behavior = async (_, _, ct) => await Task.Delay(20, ct);

        await env.Orchestrator.RunAsync(env.Request(ids, parallelism: 1), null, CancellationToken.None);

        Assert.Equal(1, env.Strategy.MaxConcurrent);
    }

    [Fact]
    public async Task Simulacion_no_sube_nada_ni_guarda_el_webjob()
    {
        using var env = new Env();
        var ids = new[] { env.AddClient("Uno"), env.AddClient("Dos") };
        var messages = new List<string>();

        var result = await env.Orchestrator.RunAsync(env.Request(ids, dryRun: true),
            new SyncProgress(p => messages.Add(p.Message)), CancellationToken.None);

        Assert.Equal(0, env.Strategy.DeployCalls);
        Assert.Equal(2, result.SimulatedCount);
        Assert.Empty(env.Config.WebJobs);
        Assert.Contains(messages, m => m.Contains("[Simulación]"));
    }

    [Fact]
    public async Task Cliente_sin_credenciales_da_error_claro_y_los_otros_siguen()
    {
        using var env = new Env();
        var sin = env.AddClient("Sin credenciales", withCredentials: false);
        var con = env.AddClient("Con credenciales");

        var result = await env.Orchestrator.RunAsync(env.Request(new[] { sin, con }), null, CancellationToken.None);

        Assert.Equal(DeployOutcome.Error, result.Clients.Single(c => c.ClientId == sin).Outcome);
        Assert.Equal(DeployOutcome.Ok, result.Clients.Single(c => c.ClientId == con).Outcome);
    }

    [Fact]
    public async Task Cliente_deshabilitado_se_omite()
    {
        using var env = new Env();
        var id = env.AddClient("Pausado", enabled: false);

        var result = await env.Orchestrator.RunAsync(env.Request(new[] { id }), null, CancellationToken.None);

        Assert.Equal(DeployOutcome.Omitido, Assert.Single(result.Clients).Outcome);
        Assert.Equal(0, env.Strategy.DeployCalls);
    }

    [Fact]
    public async Task Cancelar_corta_limpio_sin_lanzar_excepcion()
    {
        using var env = new Env();
        var ids = new[] { env.AddClient("Uno"), env.AddClient("Dos") };
        env.Strategy.Behavior = async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var result = await env.Orchestrator.RunAsync(env.Request(ids), null, cts.Token);

        Assert.All(result.Clients, c => Assert.Equal(DeployOutcome.Cancelado, c.Outcome));
        Assert.Empty(env.Config.WebJobs); // nada se registró como publicado
    }

    [Fact]
    public async Task Rechaza_nombres_de_webjob_peligrosos()
    {
        using var env = new Env();
        var id = env.AddClient("Uno");
        var request = new DeployRequest { SourceFolder = env.Publish.Path, WebJobName = "../x", ClientIds = new[] { id } };

        await Assert.ThrowsAsync<ArgumentException>(() => env.Orchestrator.RunAsync(request, null, CancellationToken.None));
    }

    [Fact]
    public async Task Cada_cliente_termina_con_un_evento_final_con_su_resultado()
    {
        using var env = new Env();
        var ok = env.AddClient("Bien");
        var sinCred = env.AddClient("Sin cred", withCredentials: false);
        var pausado = env.AddClient("Pausado", enabled: false);
        var malo = env.AddClient("Malo");
        env.Strategy.Behavior = (ctx, _, _) =>
            ctx.Client.Id == malo ? throw new DeployException("falla definitiva", isTransient: false) : Task.CompletedTask;
        var finales = new List<DeployProgress>();

        await env.Orchestrator.RunAsync(env.Request(new[] { ok, sinCred, pausado, malo }),
            new SyncProgress(p => { if (p.Outcome is not null) finales.Add(p); }), CancellationToken.None);

        Assert.Equal(4, finales.Count);
        Assert.Equal(DeployOutcome.Ok, finales.Single(f => f.ClientId == ok).Outcome);
        Assert.Equal(DeployOutcome.Error, finales.Single(f => f.ClientId == sinCred).Outcome);
        Assert.Equal(DeployOutcome.Omitido, finales.Single(f => f.ClientId == pausado).Outcome);
        Assert.Equal(DeployOutcome.Error, finales.Single(f => f.ClientId == malo).Outcome);
    }

    private sealed class SyncProgress : IProgress<DeployProgress>
    {
        private readonly Action<DeployProgress> _action;
        public SyncProgress(Action<DeployProgress> action) => _action = action;
        public void Report(DeployProgress value) { lock (this) _action(value); }
    }
}
