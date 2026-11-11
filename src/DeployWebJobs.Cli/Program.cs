using System.Text;
using DeployWebJobs.Cli;
using DeployWebJobs.Cli.Screens;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.DependencyInjection;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.WriteLine($"DeployWebJobs {AppInfo.Version}");
    return 0;
}

// ---------------------------------------------------------------- Logs (Serilog, a archivo)
// Un archivo por día en %AppData%\DeployWebJobs\logs. Nunca se escriben contraseñas.
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
    // ------------------------------------------------------------ Inyección de dependencias
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();                    // sin logs en la consola: la UI es de Spectre
    builder.Logging.AddSerilog(Log.Logger, dispose: false);

    // Credenciales, configuración, empaquetado, Zip Deploy y orquestador (proyecto Core, compartido con la ventana)
    builder.Services.AddDeployWebJobsCore();

    // Pantallas de la consola
    builder.Services.AddSingleton<DeployView>();
    builder.Services.AddSingleton<ResultsScreen>();
    builder.Services.AddSingleton<DeployWizard>();
    builder.Services.AddSingleton<ClientsScreen>();
    builder.Services.AddSingleton<WebJobsScreen>();
    builder.Services.AddSingleton<SettingsScreen>();
    builder.Services.AddSingleton<HelpScreen>();
    builder.Services.AddSingleton<MainMenu>();

    using var host = builder.Build();   // (no se usa host.Run(): el control de Ctrl+C lo manejamos nosotros)

    Log.Information("DeployWebJobs {Version} iniciado", AppInfo.Version);
    await host.Services.GetRequiredService<MainMenu>().RunAsync();
    return 0;
}
catch (ConfigException ex)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
    Log.Error(ex, "Configuración inválida");
    return 2;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Error inesperado");
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    AnsiConsole.MarkupLine($"[grey]Más detalle en: {Markup.Escape(logDirectory)}[/]");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
