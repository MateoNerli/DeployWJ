using Avalonia;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.DependencyInjection;
using DeployWebJobs.Ui.Services;
using DeployWebJobs.Ui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DeployWebJobs.Ui;

internal static class Program
{
    /// <summary>Contenedor de servicios (se arma antes de abrir la ventana).</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        // Mismo log que la consola: un archivo por día en %AppData%\DeployWebJobs\logs. Nunca se escriben contraseñas.
        var logDirectory = Path.Combine(ConfigService.DefaultDirectory, "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "deploywebjobs-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(b =>
            {
                b.ClearProviders();
                b.AddSerilog(Log.Logger, dispose: false);
            });
            services.AddDeployWebJobsCore();   // lógica de deploy (proyecto Core), igual que la consola

            services.AddSingleton<DialogService>();
            services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());

            services.AddSingleton<PublishViewModel>();
            services.AddSingleton<ClientsViewModel>();
            services.AddSingleton<WebJobsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<HelpViewModel>();
            services.AddSingleton<MainViewModel>();

            Services = services.BuildServiceProvider();

            Log.Information("DeployWebJobsUI {Version} iniciado", AppInfo.Version);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error fatal al iniciar la ventana");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
