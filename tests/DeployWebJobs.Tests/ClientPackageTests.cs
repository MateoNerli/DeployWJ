using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Import;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Tests;

public class ClientPackageTests
{
    private static (ConfigService Config, InMemoryCredentialStore Creds) NewConfig(TempDir dir, string name)
    {
        var creds = new InMemoryCredentialStore();
        var config = new ConfigService(Path.Combine(dir.Path, name + ".json"), creds);
        return (config, creds);
    }

    [Fact]
    public void Exporta_con_credenciales_cifradas_e_importa_en_otra_PC()
    {
        using var dir = new TempDir();
        var (source, sourceCreds) = NewConfig(dir, "origen");
        source.AddOrUpdateClient(new ClientConfig { Name = "Aval OC", SiteName = "aval-oc" });
        sourceCreds.Set("aval-oc", new ClientCredential("$aval-oc", "clave-super-secreta"));
        source.RecordDeploy("Mi.Job", WebJobType.Triggered, @"C:\privado\ruta");

        var file = Path.Combine(dir.Path, "clientes" + ClientPackage.Extension);
        Assert.Equal(1, ClientPackage.Export(file, source, sourceCreds, "frase-del-paquete"));

        var text = File.ReadAllText(file);
        Assert.DoesNotContain("clave-super-secreta", text);
        Assert.DoesNotContain("privado", text);
        Assert.True(ClientPackage.HasCredentials(file));

        var (target, targetCreds) = NewConfig(dir, "destino");
        var result = ClientPackage.Import(file, target, targetCreds, "frase-del-paquete", overwrite: false);

        Assert.Equal(new[] { "Aval OC" }, result.Imported.ToArray());
        Assert.Equal("clave-super-secreta", targetCreds.Get("aval-oc")!.Password);
        Assert.Equal("$aval-oc", targetCreds.Get("aval-oc")!.UserName);
        var job = Assert.Single(target.WebJobs);
        Assert.Equal(WebJobType.Triggered, job.Type);
        Assert.Null(job.LastFolder);
    }

    [Fact]
    public void Con_contrasena_equivocada_no_importa_nada()
    {
        using var dir = new TempDir();
        var (source, sourceCreds) = NewConfig(dir, "origen");
        source.AddOrUpdateClient(new ClientConfig { Name = "Cliente", SiteName = "cli" });
        sourceCreds.Set("cliente", new ClientCredential("$cli", "pw"));

        var file = Path.Combine(dir.Path, "c" + ClientPackage.Extension);
        ClientPackage.Export(file, source, sourceCreds, "correcta");

        var (target, targetCreds) = NewConfig(dir, "destino");
        var ex = Assert.Throws<ConfigException>(() => ClientPackage.Import(file, target, targetCreds, "incorrecta", false));

        Assert.Contains("no es correcta", ex.Message);
        Assert.Empty(target.Clients);
        Assert.Null(targetCreds.Get("cliente"));
    }

    [Fact]
    public void Sin_contrasena_de_paquete_no_se_exportan_credenciales()
    {
        using var dir = new TempDir();
        var (source, sourceCreds) = NewConfig(dir, "origen");
        source.AddOrUpdateClient(new ClientConfig { Name = "Cliente", SiteName = "cli" });
        sourceCreds.Set("cliente", new ClientCredential("$cli", "pw-secreta"));

        var file = Path.Combine(dir.Path, "c" + ClientPackage.Extension);
        ClientPackage.Export(file, source, sourceCreds, null);

        Assert.False(ClientPackage.HasCredentials(file));
        Assert.DoesNotContain("pw-secreta", File.ReadAllText(file));

        var (target, targetCreds) = NewConfig(dir, "destino");
        var result = ClientPackage.Import(file, target, targetCreds, null, false);

        Assert.Single(result.Imported);
        Assert.Null(targetCreds.Get("cliente"));
    }

    [Fact]
    public void Los_clientes_existentes_se_omiten_salvo_que_se_pida_sobrescribir()
    {
        using var dir = new TempDir();
        var (source, sourceCreds) = NewConfig(dir, "origen");
        source.AddOrUpdateClient(new ClientConfig { Name = "Cliente", SiteName = "nuevo-sitio" });
        var file = Path.Combine(dir.Path, "c" + ClientPackage.Extension);
        ClientPackage.Export(file, source, sourceCreds, null);

        var (target, targetCreds) = NewConfig(dir, "destino");
        target.AddOrUpdateClient(new ClientConfig { Name = "Cliente", SiteName = "viejo-sitio" });

        var skip = ClientPackage.Import(file, target, targetCreds, null, overwrite: false);
        Assert.Single(skip.Skipped);
        Assert.Equal("viejo-sitio", target.FindClient("cliente")!.SiteName);

        var over = ClientPackage.Import(file, target, targetCreds, null, overwrite: true);
        Assert.Single(over.Imported);
        Assert.Equal("nuevo-sitio", target.FindClient("cliente")!.SiteName);
    }

    [Fact]
    public void Un_archivo_cualquiera_se_rechaza()
    {
        using var dir = new TempDir();
        var file = dir.File("raro.dwjclients", "{\"hola\": 1}");
        var (target, targetCreds) = NewConfig(dir, "destino");

        Assert.Throws<ConfigException>(() => ClientPackage.Import(file, target, targetCreds, null, false));
    }
}
