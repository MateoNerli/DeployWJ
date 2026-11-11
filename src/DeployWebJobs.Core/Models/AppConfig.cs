namespace DeployWebJobs.Core.Models;

/// <summary>Contenido de config.json. No contiene secretos.</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public DeploySettings Settings { get; set; } = new();
    public List<ClientConfig> Clients { get; set; } = new();
    public List<WebJobDef> WebJobs { get; set; } = new();
}

/// <summary>Parámetros generales del deploy.</summary>
public sealed class DeploySettings
{
    /// <summary>Cuántos clientes se publican a la vez (1 = secuencial).</summary>
    public int MaxParallelism { get; set; } = 3;

    /// <summary>Reintentos ante errores de red, además del primer intento.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Espera base entre reintentos (se duplica en cada reintento).</summary>
    public double RetryBaseDelaySeconds { get; set; } = 2;

    public int RequestTimeoutSeconds { get; set; } = 60;
    public int UploadTimeoutSeconds { get; set; } = 300;
    public int VerifyTimeoutSeconds { get; set; } = 30;

    /// <summary>Pedir confirmación antes de publicar de verdad.</summary>
    public bool ConfirmBeforeDeploy { get; set; } = true;

    /// <summary>
    /// Archivos que NO se suben. Patrones tipo glob: "*" cualquier cosa dentro de una carpeta,
    /// "**" cruza carpetas. Si el patrón no tiene "/" se compara solo contra el nombre del archivo.
    /// OJO: *.pdb se sube por defecto porque el deploy original también lo hacía (stack traces con
    /// número de línea en producción). Agregá "*.pdb" acá si no lo querés.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new()
    {
        "appsettings.Development.json",
        "*.Development.json",
        "*.vshost.exe",
        "*.vshost.exe.config",
        "*.nupkg",
        "*.zip",
        ".vs/**",
        ".git/**"
    };
}
