using DeployWebJobs.Core.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>Menú principal. Cada opción abre una pantalla; un error en una pantalla no cierra la app.</summary>
internal sealed class MainMenu
{
    private readonly ConfigService _config;
    private readonly DeployWizard _wizard;
    private readonly ClientsScreen _clients;
    private readonly WebJobsScreen _webJobs;
    private readonly SettingsScreen _settings;
    private readonly HelpScreen _help;
    private readonly ILogger<MainMenu> _logger;

    public MainMenu(ConfigService config, DeployWizard wizard, ClientsScreen clients, WebJobsScreen webJobs,
        SettingsScreen settings, HelpScreen help, ILogger<MainMenu> logger)
    {
        _config = config;
        _wizard = wizard;
        _clients = clients;
        _webJobs = webJobs;
        _settings = settings;
        _help = help;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        const string publish = "Publicar un WebJob";
        const string clients = "Clientes (agregar, editar, importar del Excel)";
        const string webJobs = "WebJobs conocidos";
        const string settings = "Ajustes";
        const string help = "¿Cómo funciona?";
        const string exit = "Salir";

        while (true)
        {
            Ui.Header();
            AnsiConsole.MarkupLine($"[grey]{_config.Clients.Count} clientes · {_config.WebJobs.Count} WebJobs conocidos[/]");
            if (_config.Clients.Count == 0)
                AnsiConsole.MarkupLine("[yellow]Todavía no hay clientes. Entrá a 'Clientes' y elegí 'Importar desde el Excel anterior'.[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Menú principal")
                .AddChoices(publish, clients, webJobs, settings, help, exit));

            try
            {
                switch (choice)
                {
                    case publish: await _wizard.RunAsync(); break;
                    case clients: _clients.Run(); break;
                    case webJobs: _webJobs.Run(); break;
                    case settings: _settings.Run(); break;
                    case help: _help.Show(); break;
                    default: return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en la opción '{Option}'", choice);
                AnsiConsole.WriteLine();
                Ui.Error(ex.Message);
                AnsiConsole.MarkupLine("[grey]El detalle técnico quedó en el archivo de log.[/]");
                Ui.Pause();
            }
        }
    }
}
