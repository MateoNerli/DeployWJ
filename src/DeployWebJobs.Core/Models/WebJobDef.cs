using System.Text.RegularExpressions;

namespace DeployWebJobs.Core.Models;

/// <summary>
/// Un WebJob conocido por la herramienta. No hace falta registrarlo de antemano: se agrega
/// solo la primera vez que se publica (así se pueden manejar 20+ WebJobs, nuevos o viejos).
/// </summary>
public sealed class WebJobDef
{
    private static readonly Regex NameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.Compiled);

    /// <summary>Nombre de la carpeta del WebJob en Azure (app_data/jobs/continuous/{Name}).</summary>
    public string Name { get; set; } = "";

    public WebJobType Type { get; set; } = WebJobType.Continuous;

    /// <summary>Última carpeta de publicación usada (para sugerirla la próxima vez).</summary>
    public string? LastFolder { get; set; }

    public DateTime? LastDeployedUtc { get; set; }

    public string? Notes { get; set; }

    /// <summary>Evita nombres con barras, "..", espacios, etc. (se usa para armar rutas remotas).</summary>
    public static bool IsValidName(string? name) => name is not null && NameRegex.IsMatch(name);
}
