using System.Text;
using System.Text.RegularExpressions;

namespace DeployWebJobs.Core.Packaging;

/// <summary>
/// Matcher de patrones simples tipo .gitignore para decidir qué archivos NO se suben.
///   *   cualquier cosa dentro de un nivel de carpeta
///   **  cruza carpetas
///   ?   un carácter
/// Si el patrón no contiene "/" se compara solo contra el NOMBRE del archivo (en cualquier carpeta);
/// si contiene "/" se compara contra la ruta relativa completa. No distingue mayúsculas.
/// </summary>
public sealed class GlobMatcher
{
    private readonly List<(Regex Regex, bool MatchFullPath)> _rules = new();

    public GlobMatcher(IEnumerable<string> patterns)
    {
        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var pattern = raw.Trim().Replace('\\', '/');
            var regex = new Regex(ToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            _rules.Add((regex, pattern.Contains('/')));
        }
    }

    /// <param name="relativePath">Ruta relativa con "/" como separador.</param>
    public bool IsMatch(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        var name = path[(path.LastIndexOf('/') + 1)..];

        foreach (var (regex, fullPath) in _rules)
        {
            if (regex.IsMatch(fullPath ? path : name))
                return true;
        }
        return false;
    }

    private static string ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?"); // "**/" = cero o más carpetas
                        i += 3;
                    }
                    else
                    {
                        sb.Append(".*");
                        i += 2;
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                    i++;
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }
        return sb.Append('$').ToString();
    }
}
