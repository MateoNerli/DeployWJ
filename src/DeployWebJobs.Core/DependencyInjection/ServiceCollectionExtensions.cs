using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace DeployWebJobs.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra toda la lógica de deploy (sin nada de interfaz). La usan tanto la consola como la ventana.
    /// </summary>
    /// <param name="configFilePath">Ruta de config.json; null = %AppData%\DeployWebJobs\config.json</param>
    public static IServiceCollection AddDeployWebJobsCore(this IServiceCollection services, string? configFilePath = null)
    {
        // Credenciales: Administrador de credenciales de Windows (en otros sistemas, memoria: solo para pruebas).
        services.AddSingleton<ICredentialStore>(_ =>
            OperatingSystem.IsWindows() ? (ICredentialStore)new WindowsCredentialStore() : new InMemoryCredentialStore());

        services.AddSingleton(sp => new ConfigService(configFilePath, sp.GetRequiredService<ICredentialStore>()));
        services.AddSingleton<PackageBuilder>();

        // Sin timeout global: cada pedido tiene el suyo (ver DeploySettings).
        services.AddHttpClient(ZipDeployStrategy.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IDeployStrategy, ZipDeployStrategy>();
        services.AddSingleton<DeployOrchestrator>();
        return services;
    }
}
