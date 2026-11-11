using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>
/// Lista de WebJobs que la herramienta conoce. Se llena sola al publicar; acá se puede
/// cambiar el tipo o quitar los viejos. Es solo una lista LOCAL: no toca nada en Azure.
/// </summary>
internal sealed class WebJobsScreen
{
    private readonly ConfigService _config;

    public WebJobsScreen(ConfigService config) => _config = config;

    public void Run()
    {
        const string changeType = "Cambiar el tipo de un WebJob";
        const string remove = "Quitar un WebJob de la lista";
        const string back = "Volver al menú";

        while (true)
        {
            Ui.Header("WebJobs conocidos");
            AnsiConsole.MarkupLine("[grey]Esta lista se arma sola cuando publicás. Quitar un WebJob de acá NO lo borra de Azure.[/]");
            AnsiConsole.WriteLine();

            if (_config.WebJobs.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]Todavía no hay WebJobs. Aparecen después del primer deploy exitoso.[/]");
                AnsiConsole.WriteLine();
                Ui.Pause();
                return;
            }

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("WebJob")
                .AddColumn("Tipo")
                .AddColumn("Último deploy")
                .AddColumn("Última carpeta");

            foreach (var w in _config.WebJobs)
            {
                table.AddRow(
                    Markup.Escape(w.Name),
                    w.Type.Describe(),
                    w.LastDeployedUtc is { } d ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "-",
                    Markup.Escape(Ui.Short(w.LastFolder ?? "-", 60)));
            }
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("¿Qué querés hacer?")
                .AddChoices(changeType, remove, back));
            if (choice == back) return;

            var job = Pick(choice == remove ? "¿Cuál querés quitar?" : "¿A cuál le cambiás el tipo?");
            if (job is null) continue;

            if (choice == remove)
            {
                if (AnsiConsole.Confirm($"¿Quitar '{Markup.Escape(job.Name)}' de la lista?", false))
                    _config.RemoveWebJob(job.Name);
            }
            else
            {
                job.Type = job.Type == WebJobType.Continuous ? WebJobType.Triggered : WebJobType.Continuous;
                _config.AddOrUpdateWebJob(job);
                Ui.Ok($"'{job.Name}' ahora es {job.Type.Describe()}.");
                Ui.Pause();
            }
        }
    }

    private WebJobDef? Pick(string title)
    {
        const string backLabel = "« Volver";
        var names = _config.WebJobs.Select(w => w.Name).Append(backLabel).ToList();
        var picked = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title(title)
            .PageSize(15)
            .EnableSearch()
            .AddChoices(names.Select(Markup.Escape)));

        if (picked == backLabel) return null;
        return _config.WebJobs.FirstOrDefault(w => Markup.Escape(w.Name) == picked);
    }
}
