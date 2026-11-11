using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Core.Deploy;

/// <summary>Lo que pide el usuario: qué carpeta, qué WebJob y a qué clientes.</summary>
public sealed class DeployRequest
{
    public required string SourceFolder { get; init; }
    public required string WebJobName { get; init; }
    public WebJobType WebJobType { get; init; } = WebJobType.Continuous;
    public required IReadOnlyList<string> ClientIds { get; init; }

    /// <summary>Simulación: muestra qué haría, sin conectarse ni subir nada.</summary>
    public bool DryRun { get; init; }

    /// <summary>Si el WebJob debe existir ya en cada cliente, o ser nuevo (evita errores de nombre).</summary>
    public WebJobExistence Existence { get; init; } = WebJobExistence.Any;

    /// <summary>Si es null se usa el valor de la configuración. 1 = secuencial.</summary>
    public int? MaxParallelism { get; init; }
}

/// <summary>Evento de avance. La UI (consola o gráfica) solo tiene que dibujarlo.</summary>
public sealed record DeployProgress(
    string ClientId,
    string ClientName,
    DeployStep Step,
    string Message,
    long BytesDone = 0,
    long BytesTotal = 0,
    int Attempt = 1,
    DeployOutcome? Outcome = null); // distinto de null = evento FINAL de ese cliente

public sealed record ClientDeployResult(
    string ClientId,
    string ClientName,
    DeployOutcome Outcome,
    string Message,
    TimeSpan Elapsed,
    int Attempts);

public sealed record DeployRunResult(
    IReadOnlyList<ClientDeployResult> Clients,
    TimeSpan Elapsed,
    PackagePlan Plan,
    bool DryRun)
{
    public int OkCount => Clients.Count(c => c.Outcome == DeployOutcome.Ok);
    public int ErrorCount => Clients.Count(c => c.Outcome == DeployOutcome.Error);
    public int SimulatedCount => Clients.Count(c => c.Outcome == DeployOutcome.Simulado);
    public int OtherCount => Clients.Count - OkCount - ErrorCount - SimulatedCount;
}

/// <summary>Todo lo que una estrategia necesita para publicar en UN cliente.</summary>
public sealed class DeployContext
{
    public required ClientConfig Client { get; init; }
    public required ClientCredential Credential { get; init; }
    public required WebJobDef WebJob { get; init; }
    public required PackagePlan Plan { get; init; }
    public required DeploySettings Settings { get; init; }

    /// <summary>Qué se espera del WebJob en Azure (ver <see cref="WebJobExistence"/>).</summary>
    public WebJobExistence Existence { get; init; } = WebJobExistence.Any;

    /// <summary>ZIP ya armado. Es null en simulación.</summary>
    public PackageInfo? Package { get; init; }

    /// <summary>Número de intento actual (lo actualiza el orquestador).</summary>
    public int Attempt { get; set; } = 1;

    public DeployProgress Progress(DeployStep step, string message, long done = 0, long total = 0) =>
        new(Client.Id, Client.Name, step, message, done, total, Attempt);
}

/// <summary>Error de deploy con información sobre si vale la pena reintentar.</summary>
public sealed class DeployException : Exception
{
    public DeployException(string message, bool isTransient = false, DeployStep? step = null, Exception? inner = null)
        : base(message, inner)
    {
        IsTransient = isTransient;
        Step = step;
    }

    /// <summary>true = error pasajero (red, 5xx, timeout): se reintenta. false = no tiene sentido reintentar.</summary>
    public bool IsTransient { get; }
    public DeployStep? Step { get; }
}

/// <summary>
/// Forma de subir el paquete a un cliente. Hoy: Zip Deploy (Kudu). Mañana: FTPS u otra,
/// sin tocar el resto de la aplicación.
/// </summary>
public interface IDeployStrategy
{
    DeployMethod Method { get; }

    /// <summary>Texto de lo que se haría (para el dry-run). No toca la red.</summary>
    IReadOnlyList<string> DescribePlan(DeployContext context);

    Task DeployAsync(DeployContext context, IProgress<DeployProgress> progress, CancellationToken cancellationToken);
}

internal sealed class NullProgress<T> : IProgress<T>
{
    public static readonly NullProgress<T> Instance = new();
    public void Report(T value) { }
}

/// <summary>Progress síncrono (el de .NET "Progress&lt;T&gt;" encola en el SynchronizationContext).</summary>
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _action;
    public InlineProgress(Action<T> action) => _action = action;
    public void Report(T value) => _action(value);
}
