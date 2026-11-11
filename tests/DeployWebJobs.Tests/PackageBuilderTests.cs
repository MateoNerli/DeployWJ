using System.IO.Compression;
using DeployWebJobs.Core.Packaging;

namespace DeployWebJobs.Tests;

public class PackageBuilderTests
{
    private static readonly string[] Excludes = { "*.pdb", "appsettings.Development.json" };

    [Fact]
    public void Plan_aplica_exclusiones_y_ordena()
    {
        using var dir = new TempDir();
        dir.File("App.exe");
        dir.File("App.exe.config");
        dir.File("App.pdb");
        dir.File("appsettings.Development.json");
        dir.File("lib/Dep.dll");

        var plan = new PackageBuilder().Plan(dir.Path, Excludes);

        Assert.Equal(new[] { "App.exe", "App.exe.config", "lib/Dep.dll" }, plan.Files.Select(f => f.RelativePath).ToArray());
        Assert.Equal(new[] { "App.pdb", "appsettings.Development.json" }, plan.Excluded.ToArray());
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Plan_advierte_si_no_hay_ejecutable()
    {
        using var dir = new TempDir();
        dir.File("readme.txt");

        var plan = new PackageBuilder().Plan(dir.Path, Excludes);

        Assert.Single(plan.Warnings);
    }

    [Fact]
    public void Plan_falla_si_la_carpeta_no_existe()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new PackageBuilder().Plan(Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N")), Excludes));
    }

    [Fact]
    public void Plan_falla_si_todo_queda_excluido()
    {
        using var dir = new TempDir();
        dir.File("App.pdb");

        Assert.Throws<InvalidOperationException>(() => new PackageBuilder().Plan(dir.Path, Excludes));
    }

    [Fact]
    public void Build_crea_un_zip_con_las_entradas_esperadas_y_Dispose_lo_borra()
    {
        using var dir = new TempDir();
        dir.File("App.exe", "contenido");
        dir.File("lib/Dep.dll", "dll");
        dir.File("App.pdb");

        var builder = new PackageBuilder();
        var plan = builder.Plan(dir.Path, Excludes);
        string zipPath;

        using (var package = builder.Build(plan))
        {
            zipPath = package.ZipPath;
            Assert.True(File.Exists(zipPath));
            Assert.True(package.ZipBytes > 0);

            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "App.exe", "lib/Dep.dll" }, names);
        }

        Assert.False(File.Exists(zipPath));
    }
}
