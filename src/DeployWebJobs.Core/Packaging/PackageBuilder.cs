using System.IO.Compression;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Core.Packaging;

/// <summary>
/// Arma el paquete a subir a partir de la carpeta de publicación que elige el usuario:
///   1) Plan(): recorre la carpeta, descarta lo excluido y valida cada ruta (PathGuard).
///   2) Build(): comprime lo que quedó en un ZIP temporal (el mismo ZIP se sube a todos los clientes).
/// Los enlaces simbólicos / junctions se ignoran para no salir de la carpeta elegida.
/// </summary>
public sealed class PackageBuilder
{
    // Extensiones que un WebJob sabe ejecutar. Si no hay ninguna, probablemente se eligió mal la carpeta.
    private static readonly string[] RunnableExtensions =
        { ".exe", ".cmd", ".bat", ".ps1", ".sh", ".js", ".py", ".php", ".fsx", ".dll" };

    public PackagePlan Plan(string sourceFolder, IEnumerable<string> excludePatterns)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"No existe la carpeta de publicación: {sourceFolder}");

        var root = Path.GetFullPath(sourceFolder);
        var matcher = new GlobMatcher(excludePatterns);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint // no seguir symlinks/junctions
        };

        var files = new List<PackageFile>();
        var excluded = new List<string>();

        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            var full = PathGuard.EnsureInside(root, path);
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');

            if (!PathGuard.IsSafeRelativePath(relative))
                throw new UnauthorizedAccessException($"Ruta no permitida en el paquete: {relative}");

            if (matcher.IsMatch(relative))
            {
                excluded.Add(relative);
                continue;
            }

            files.Add(new PackageFile(relative, full, new FileInfo(full).Length));
        }

        if (files.Count == 0)
            throw new InvalidOperationException("La carpeta no tiene archivos para subir (o todos quedaron excluidos).");

        files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        excluded.Sort(StringComparer.Ordinal);

        var warnings = new List<string>();
        if (!files.Any(f => RunnableExtensions.Contains(Path.GetExtension(f.RelativePath), StringComparer.OrdinalIgnoreCase)))
            warnings.Add("No se encontró ningún ejecutable (.exe, .cmd, .bat, .ps1, .js, .py...). ¿Es la carpeta de publicación correcta?");

        return new PackagePlan(root, files, excluded, warnings);
    }

    public PackageInfo Build(PackagePlan plan)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"deploywebjobs-{Guid.NewGuid():N}.zip");
        try
        {
            using (var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var file in plan.Files)
                    zip.CreateEntryFromFile(file.FullPath, file.RelativePath, CompressionLevel.Optimal);
            }
            return new PackageInfo(zipPath, plan);
        }
        catch
        {
            try { File.Delete(zipPath); } catch { /* ignorar */ }
            throw;
        }
    }
}
