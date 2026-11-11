using DeployWebJobs.Core.Packaging;

namespace DeployWebJobs.Tests;

public class GlobMatcherTests
{
    private static readonly string[] Patterns = { "*.pdb", "appsettings.Development.json", ".vs/**", "logs/*.txt" };

    [Theory]
    [InlineData("App.pdb")]
    [InlineData("sub/dir/App.PDB")]               // en cualquier carpeta y sin distinguir mayúsculas
    [InlineData("appsettings.Development.json")]
    [InlineData(".vs/config/x.json")]
    [InlineData("logs/a.txt")]
    public void Excluye_lo_que_coincide(string path) =>
        Assert.True(new GlobMatcher(Patterns).IsMatch(path));

    [Theory]
    [InlineData("App.exe")]
    [InlineData("appsettings.json")]
    [InlineData("lib/Dep.dll")]
    [InlineData("logs/sub/a.txt")]                // "logs/*.txt" no cruza carpetas
    public void No_excluye_lo_demas(string path) =>
        Assert.False(new GlobMatcher(Patterns).IsMatch(path));

    [Fact]
    public void Doble_asterisco_con_barra_acepta_cero_carpetas()
    {
        var m = new GlobMatcher(new[] { "**/secret.json" });
        Assert.True(m.IsMatch("secret.json"));
        Assert.True(m.IsMatch("a/b/secret.json"));
    }
}
