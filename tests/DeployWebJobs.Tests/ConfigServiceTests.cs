using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Alta_genera_id_guarda_json_sin_secretos_y_se_puede_recargar()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "config.json");
        var creds = new InMemoryCredentialStore();
        var service = new ConfigService(path, creds);

        var client = service.AddOrUpdateClient(new ClientConfig { Name = "Aval OC (Prod)", SiteName = "aval-oc" });
        creds.Set(client.Id, new ClientCredential("aval-oc\\$aval-oc", "SuperSecreta123"));

        Assert.Equal("aval-oc-prod", client.Id);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("SuperSecreta123", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);

        var reloaded = new ConfigService(path, creds);
        Assert.Single(reloaded.Clients);
        Assert.Equal("aval-oc", reloaded.Clients[0].SiteName);
    }

    [Fact]
    public void Ids_repetidos_reciben_sufijo()
    {
        using var dir = new TempDir();
        var service = new ConfigService(Path.Combine(dir.Path, "c.json"));

        var a = service.AddOrUpdateClient(new ClientConfig { Name = "Cliente", SiteName = "uno" });
        var b = service.AddOrUpdateClient(new ClientConfig { Name = "Cliente", SiteName = "dos" });

        Assert.Equal("cliente", a.Id);
        Assert.Equal("cliente-2", b.Id);
    }

    [Fact]
    public void Quitar_cliente_borra_tambien_su_credencial()
    {
        using var dir = new TempDir();
        var creds = new InMemoryCredentialStore();
        var service = new ConfigService(Path.Combine(dir.Path, "c.json"), creds);
        var client = service.AddOrUpdateClient(new ClientConfig { Name = "X", SiteName = "x" });
        creds.Set(client.Id, new ClientCredential("u", "p"));

        Assert.True(service.RemoveClient(client.Id));

        Assert.Empty(service.Clients);
        Assert.Null(creds.Get(client.Id));
    }

    [Fact]
    public void Rechaza_clientes_invalidos()
    {
        using var dir = new TempDir();
        var service = new ConfigService(Path.Combine(dir.Path, "c.json"));

        Assert.Throws<ArgumentException>(() => service.AddOrUpdateClient(new ClientConfig { Name = "", SiteName = "x" }));
        Assert.Throws<ArgumentException>(() => service.AddOrUpdateClient(new ClientConfig { Name = "Ok", SiteName = "con espacios!" }));
    }

    [Fact]
    public void RecordDeploy_crea_el_webjob_y_recuerda_la_carpeta()
    {
        using var dir = new TempDir();
        var service = new ConfigService(Path.Combine(dir.Path, "c.json"));

        service.RecordDeploy("Webjob.CentralDeConsultaSql", WebJobType.Continuous, @"C:\x\publish");

        var job = Assert.Single(service.WebJobs);
        Assert.Equal(@"C:\x\publish", job.LastFolder);
        Assert.NotNull(job.LastDeployedUtc);
    }

    [Fact]
    public void Config_dañada_da_un_error_claro()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json", "{ esto no es json");

        Assert.Throws<ConfigException>(() => new ConfigService(path));
    }

    [Theory]
    [InlineData("Webjob.Central_1-a", true)]
    [InlineData("../hack", false)]
    [InlineData("con espacio", false)]
    [InlineData("", false)]
    public void Valida_nombres_de_webjob(string name, bool esperado) =>
        Assert.Equal(esperado, WebJobDef.IsValidName(name));
}
