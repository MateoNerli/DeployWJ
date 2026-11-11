using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>Ajustes generales: paralelismo, reintentos, tiempos, confirmación y archivos excluidos.</summary>
internal sealed class SettingsScreen
{
    private readonly ConfigService _config;

    public SettingsScreen(ConfigService config) => _config = config;

    public void Run()
    {
        const string parallel = "Cuántos clientes publicar a la vez";
        const string retries = "Cantidad de reintentos ante errores de red";
        const string upload = "Tiempo máximo de subida por cliente";
        const string confirm = "Pedir confirmación antes de publicar (sí / no)";
        const string excludes = "Archivos que NO se suben";
        const string back = "Volver al menú";

        while (true)
        {
            var s = _config.Config.Settings;
            Ui.Header("Ajustes");

            var table = new Table().Border(TableBorder.Rounded).AddColumn("Ajuste").AddColumn("Valor");
            table.AddRow("Clientes a la vez (paralelo)", s.MaxParallelism.ToString());
            table.AddRow("Reintentos ante errores de red", s.MaxRetries.ToString());
            table.AddRow("Tiempo máximo de subida", $"{s.UploadTimeoutSeconds} s");
            table.AddRow("Pedir confirmación", s.ConfirmBeforeDeploy ? "sí" : "no");
            table.AddRow("Patrones excluidos", s.ExcludePatterns.Count.ToString());
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]Archivo de configuración: {Markup.Escape(_config.FilePath)}[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("¿Qué querés cambiar?")
                .AddChoices(parallel, retries, upload, confirm, excludes, back));

            switch (choice)
            {
                case parallel:
                    s.MaxParallelism = AskInt("¿Cuántos clientes a la vez? (1 = uno por uno)", s.MaxParallelism, 1, 16);
                    _config.Save();
                    break;
                case retries:
                    s.MaxRetries = AskInt("¿Cuántos reintentos?", s.MaxRetries, 0, 10);
                    _config.Save();
                    break;
                case upload:
                    s.UploadTimeoutSeconds = AskInt("Segundos máximos para subir a un cliente:", s.UploadTimeoutSeconds, 30, 3600);
                    _config.Save();
                    break;
                case confirm:
                    s.ConfirmBeforeDeploy = !s.ConfirmBeforeDeploy;
                    _config.Save();
                    break;
                case excludes:
                    EditExcludes(s);
                    break;
                default:
                    return;
            }
        }
    }

    private void EditExcludes(DeploySettings s)
    {
        const string add = "Agregar un patrón";
        const string remove = "Quitar patrones";
        const string reset = "Restablecer los valores por defecto";
        const string back = "Volver";

        while (true)
        {
            Ui.Header("Archivos que NO se suben");
            AnsiConsole.MarkupLine("[grey]Patrones: * = cualquier cosa dentro de una carpeta, ** = cruza carpetas.[/]");
            AnsiConsole.MarkupLine("[grey]Sin barra (/) se compara con el nombre del archivo; con barra, con la ruta relativa.[/]");
            AnsiConsole.WriteLine();
            foreach (var p in s.ExcludePatterns)
                AnsiConsole.MarkupLine($"  • {Markup.Escape(p)}");
            if (s.ExcludePatterns.Count == 0)
                AnsiConsole.MarkupLine("  [grey](ninguno: se sube todo)[/]");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("¿Qué querés hacer?")
                .AddChoices(add, remove, reset, back));

            switch (choice)
            {
                case add:
                {
                    var pattern = AnsiConsole.Prompt(new TextPrompt<string>("Patrón (ej: *.pdb o logs/**):").AllowEmpty()).Trim();
                    if (pattern.Length > 0 && !s.ExcludePatterns.Contains(pattern))
                    {
                        s.ExcludePatterns.Add(pattern);
                        _config.Save();
                    }
                    break;
                }
                case remove:
                {
                    if (s.ExcludePatterns.Count == 0) break;
                    var toRemove = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
                        .Title("Marcá los que querés quitar:")
                        .NotRequired()
                        .InstructionsText("[grey](Espacio = marcar, Enter = confirmar)[/]")
                        .UseConverter(Markup.Escape)
                        .AddChoices(s.ExcludePatterns));
                    foreach (var p in toRemove) s.ExcludePatterns.Remove(p);
                    _config.Save();
                    break;
                }
                case reset:
                    s.ExcludePatterns = new DeploySettings().ExcludePatterns;
                    _config.Save();
                    break;
                default:
                    return;
            }
        }
    }

    private static int AskInt(string question, int current, int min, int max) =>
        AnsiConsole.Prompt(new TextPrompt<int>(question)
            .DefaultValue(current)
            .Validate(n => n >= min && n <= max
                ? ValidationResult.Success()
                : ValidationResult.Error($"[red]Tiene que ser un número entre {min} y {max}[/]")));
}
