namespace DeployWebJobs.Core.Packaging;

/// <summary>Un archivo que va a formar parte del paquete.</summary>
public sealed record PackageFile(string RelativePath, string FullPath, long Size);

/// <summary>
/// Qué se subiría, sin haber creado nada todavía. Es lo que muestra el modo simulación (dry-run).
/// </summary>
public sealed record PackagePlan(
    string SourceFolder,
    IReadOnlyList<PackageFile> Files,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<string> Warnings)
{
    public long TotalBytes => Files.Sum(f => f.Size);
}

/// <summary>El ZIP temporal ya generado. Al hacer Dispose se borra el archivo temporal.</summary>
public sealed class PackageInfo : IDisposable
{
    public PackageInfo(string zipPath, PackagePlan plan)
    {
        ZipPath = zipPath;
        Plan = plan;
        ZipBytes = new FileInfo(zipPath).Length;
    }

    public string ZipPath { get; }
    public PackagePlan Plan { get; }
    public long ZipBytes { get; }

    public void Dispose()
    {
        try { if (File.Exists(ZipPath)) File.Delete(ZipPath); }
        catch (IOException) { /* si no se puede borrar ahora, el SO limpia el temp más tarde */ }
        catch (UnauthorizedAccessException) { }
    }
}
