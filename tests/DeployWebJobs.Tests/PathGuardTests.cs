using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Tests;

public class PathGuardTests
{
    [Fact]
    public void Acepta_rutas_dentro_de_la_raiz()
    {
        using var dir = new TempDir();
        Assert.True(PathGuard.IsInside(dir.Path, Path.Combine(dir.Path, "a", "b.txt")));
    }

    [Fact]
    public void Rechaza_rutas_que_escapan_con_punto_punto()
    {
        using var dir = new TempDir();
        Assert.False(PathGuard.IsInside(dir.Path, Path.Combine(dir.Path, "..", "otro.txt")));
        Assert.Throws<UnauthorizedAccessException>(() => PathGuard.EnsureInside(dir.Path, Path.Combine(dir.Path, "..", "otro.txt")));
    }

    [Fact]
    public void Rechaza_carpeta_hermana_con_mismo_prefijo()
    {
        using var dir = new TempDir();
        Assert.False(PathGuard.IsInside(dir.Path, dir.Path + "2" + Path.DirectorySeparatorChar + "x.txt"));
    }

    [Theory]
    [InlineData("a/b.txt", true)]
    [InlineData("../a.txt", false)]
    [InlineData("a/../../b.txt", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/x.txt", false)]
    [InlineData("", false)]
    public void Valida_rutas_relativas(string path, bool esperado) =>
        Assert.Equal(esperado, PathGuard.IsSafeRelativePath(path));
}
