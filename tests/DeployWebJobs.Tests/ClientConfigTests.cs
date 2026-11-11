using DeployWebJobs.Core.Models;

namespace DeployWebJobs.Tests;

public class ClientConfigTests
{
    [Theory]
    [InlineData(@"mi-sitio\$mi-sitio", "mi-sitio")]
    [InlineData(@"$mi-sitio", "mi-sitio")]
    [InlineData(@"mi-sitio", "mi-sitio")]
    public void Obtiene_el_nombre_del_sitio_desde_el_usuario(string user, string esperado) =>
        Assert.Equal(esperado, ClientConfig.SiteNameFromUser(user));

    [Fact]
    public void Usuario_de_Kudu_es_la_parte_posterior_a_la_barra() =>
        Assert.Equal("$mi-sitio", ClientConfig.KuduUserName(@"mi-sitio\$mi-sitio"));

    [Fact]
    public void Url_scm_por_defecto_es_https()
    {
        var c = new ClientConfig { SiteName = "mi-sitio" };
        Assert.Equal("https://mi-sitio.scm.azurewebsites.net/", c.ScmBaseUri.ToString());
    }

    [Fact]
    public void ScmHost_personalizado_se_normaliza_y_fuerza_https()
    {
        var c = new ClientConfig { SiteName = "x", ScmHost = "http://mi-sitio-abc.scm.azurewebsites.net/" };
        Assert.Equal("https://mi-sitio-abc.scm.azurewebsites.net/", c.ScmBaseUri.ToString());
    }

    [Theory]
    [InlineData("Aval OC (Prod)", "aval-oc-prod")]
    [InlineData("Garantía Ñandú", "garantia-nandu")]
    [InlineData("  --Hola--  ", "hola")]
    public void Slugify(string texto, string esperado) =>
        Assert.Equal(esperado, ClientConfig.Slugify(texto));
}
