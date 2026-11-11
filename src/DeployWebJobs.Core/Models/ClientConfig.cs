using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DeployWebJobs.Core.Models;

/// <summary>
/// Un cliente = un App Service de Azure donde se publican WebJobs.
/// IMPORTANTE: esta clase se guarda en config.json, por eso NUNCA lleva usuario ni contraseña.
/// Las credenciales viven en el Administrador de credenciales de Windows (ver ICredentialStore).
/// </summary>
public sealed class ClientConfig
{
    private static readonly Regex SiteNameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9-]*$", RegexOptions.Compiled);
    private static readonly Regex IdRegex = new(@"^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    /// <summary>Identificador estable (slug). También es la clave de la credencial guardada.</summary>
    public string Id { get; set; } = "";

    /// <summary>Nombre para mostrar (ej. "Aval OC").</summary>
    public string Name { get; set; } = "";

    /// <summary>Si es false, el cliente aparece en la lista pero no se puede publicar.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nombre del App Service en Azure (ej. "sgr-miCliente").</summary>
    public string SiteName { get; set; } = "";

    /// <summary>
    /// Host SCM/Kudu si NO es el estándar "{SiteName}.scm.azurewebsites.net"
    /// (por ejemplo sitios con nombre de host único o nubes soberanas). Opcional.
    /// </summary>
    public string? ScmHost { get; set; }

    /// <summary>Host FTP original (solo informativo / para una futura estrategia FTPS).</summary>
    public string? FtpHost { get; set; }

    public DeployMethod Method { get; set; } = DeployMethod.ZipDeploy;

    public string? Notes { get; set; }

    /// <summary>Host SCM efectivo, sin esquema ni barra final.</summary>
    [JsonIgnore]
    public string EffectiveScmHost
    {
        get
        {
            var host = ScmHost?.Trim();
            if (string.IsNullOrEmpty(host))
                return $"{SiteName}.scm.azurewebsites.net";

            host = host.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
            return host.TrimEnd('/');
        }
    }

    /// <summary>URL base de Kudu. Siempre HTTPS.</summary>
    [JsonIgnore]
    public Uri ScmBaseUri => new($"https://{EffectiveScmHost}/");

    /// <summary>Devuelve la lista de problemas de configuración (vacía si está todo bien).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("El nombre del cliente es obligatorio.");

        if (string.IsNullOrWhiteSpace(Id) || !IdRegex.IsMatch(Id))
            errors.Add("El identificador del cliente es inválido (solo minúsculas, números y guiones).");

        if (string.IsNullOrWhiteSpace(ScmHost))
        {
            if (!SiteNameRegex.IsMatch(SiteName ?? ""))
                errors.Add("El nombre del sitio de Azure está vacío o tiene caracteres inválidos.");
        }
        else if (Uri.CheckHostName(EffectiveScmHost) != UriHostNameType.Dns)
        {
            errors.Add("El host SCM indicado no es un nombre de host válido.");
        }

        return errors;
    }

    /// <summary>"waws\$mi-sitio" o "$mi-sitio" -> "mi-sitio".</summary>
    public static string SiteNameFromUser(string user)
    {
        var u = KuduUserName((user ?? "").Trim());
        return u.TrimStart('$');
    }

    /// <summary>
    /// El usuario FTP tiene la forma "sitio\$sitio"; Kudu (HTTPS) espera solo "$sitio".
    /// </summary>
    public static string KuduUserName(string user)
    {
        user ??= "";
        var i = user.LastIndexOf('\\');
        return i >= 0 ? user[(i + 1)..] : user;
    }

    /// <summary>"Aval OC (Prod)" -> "aval-oc-prod".</summary>
    public static string Slugify(string text)
    {
        var normalized = (text ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // saca tildes
            sb.Append(ch < 128 && char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }
        return Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
    }
}
