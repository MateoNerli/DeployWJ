namespace DeployWebJobs.Core.Security;

/// <summary>
/// Protección contra "path traversal": garantiza que nada que se suba esté fuera de la carpeta
/// de publicación elegida (por ejemplo con "..\..\" o enlaces simbólicos).
/// </summary>
public static class PathGuard
{
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>true si <paramref name="candidate"/> está dentro de <paramref name="root"/> (y no es la raíz misma).</summary>
    public static bool IsInside(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullRoot, Comparison);
    }

    /// <summary>Devuelve la ruta completa o lanza si queda fuera de la raíz.</summary>
    public static string EnsureInside(string root, string candidate)
    {
        if (!IsInside(root, candidate))
            throw new UnauthorizedAccessException($"Ruta fuera de la carpeta de publicación: {candidate}");
        return Path.GetFullPath(candidate);
    }

    /// <summary>Valida que una ruta relativa (nombre de entrada del ZIP) no escape hacia arriba ni sea absoluta.</summary>
    public static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        if (Path.IsPathRooted(relativePath)) return false;
        if (relativePath.Contains(':')) return false;

        var segments = relativePath.Split('/', '\\');
        return !segments.Any(s => s == "..");
    }
}
