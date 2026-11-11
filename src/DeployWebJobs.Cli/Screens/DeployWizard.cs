using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>
/// Asistente de publicación, en el orden que se usa en la práctica:
///   1) carpeta ya publicada  2) nombre del WebJob  3) revisar archivos  4) clientes  5) opciones y confirmación
/// y después la pantalla en vivo y los resultados.
/// </summary>
internal sealed class DeployWizard
{
    private readonly ConfigService _config;
    private readonly ICredentialStore _credentials;
    private readonly DeployOrchestrator _orchestrator;
    private readonly DeployView _view;
    private readonly ResultsScreen _results;

    public DeployWizard(ConfigService config, ICredentialStore credentials, DeployOrchestrator orchestrator,
        DeployView view, ResultsScreen results)
    {
        _config = config;
        _credentials = credentials;
        _orchestrator = orchestrator;
        _view = view;
        _results = results;
    }

    public async Task RunAsync()
    {
        Ui.Header("Publicar un WebJob");
        var settings = _config.Config.Settings;

        var enabled = _config.Clients.Where(c => c.Enabled)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (enabled.Count == 0)
        {
            Ui.Warn("No hay clientes habilitados. Agregá o importá clientes desde el menú 'Clientes'.");
            Ui.Pause();
            return;
        }

        // ---- Paso 1: carpeta
        var plan = AskFolder();
        if (plan is null) return;

        // ---- Paso 2: WebJob
        AnsiConsole.WriteLine();
        var webJob = AskWebJob(plan.SourceFolder);
        if (webJob is null) return;

        // ---- Paso 3: revisar lo que se va a subir
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Paso 3 de 5 · Archivos que se van a subir[/]");
        ShowPlan(plan);
        if (!AnsiConsole.Confirm("¿Es la carpeta correcta?", true)) return;

        // ---- Paso 4: clientes
        AnsiConsole.WriteLine();
        var withoutCredentials = enabled.Where(c => _credentials.Get(c.Id) is null).Select(c => c.Id).ToHashSet();
        var selected = AskClients(enabled, withoutCredentials);
        if (selected is null || selected.Count == 0) return;

        // ---- Paso 5: opciones
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Paso 5 de 5 · Opciones[/]");

        const string realOption = "Publicar de verdad";
        const string dryOption = "Simulación (mostrar qué haría, sin subir nada)";
        var real = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("¿Cómo querés ejecutarlo?")
            .AddChoices(realOption, dryOption)) == realOption;

        int? parallelism = null;
        if (real && selected.Count > 1)
        {
            var parallelOption = $"En paralelo (más rápido)";
            var sequentialOption = "Uno por uno (más fácil de seguir)";
            var mode = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("¿Cómo querés publicar?")
                .AddChoices(parallelOption, sequentialOption));

            if (mode == sequentialOption)
            {
                parallelism = 1;
            }
            else
            {
                parallelism = AnsiConsole.Prompt(new TextPrompt<int>("¿Cuántos clientes a la vez?")
                    .DefaultValue(Math.Clamp(settings.MaxParallelism, 1, 16))
                    .Validate(n => n is >= 1 and <= 16
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Tiene que ser un número entre 1 y 16[/]")));
            }
        }

        // ---- Resumen y confirmación
        AnsiConsole.WriteLine();
        ShowSummary(plan, webJob, selected, real, parallelism ?? settings.MaxParallelism, withoutCredentials);

        if (real && settings.ConfirmBeforeDeploy)
        {
            if (!AnsiConsole.Confirm($"[red]¿Publicar ahora en {selected.Count} cliente(s)? Esto actualiza producción.[/]", false))
            {
                Ui.Warn("Cancelado. No se subió nada.");
                Ui.Pause();
                return;
            }
        }

        // ---- Ejecución (con "Reintentar los que fallaron")
        var merged = new Dictionary<string, ClientDeployResult>();
        var pendingIds = selected.Select(c => c.Id).ToList();

        while (pendingIds.Count > 0)
        {
            Ui.Header(real ? $"Publicando {webJob.Name}" : $"Simulación · {webJob.Name}");
            AnsiConsole.MarkupLine("[grey]Ctrl+C cancela: lo ya subido queda subido y lo pendiente no se toca.[/]");
            AnsiConsole.WriteLine();

            var request = new DeployRequest
            {
                SourceFolder = plan.SourceFolder,
                WebJobName = webJob.Name,
                WebJobType = webJob.Type,
                ClientIds = pendingIds,
                DryRun = !real,
                MaxParallelism = parallelism
            };

            var clientsToRun = pendingIds.Select(id => _config.FindClient(id)).OfType<ClientConfig>().ToList();
            var outcome = await _view.RunAsync(request, clientsToRun);

            foreach (var r in outcome.Result.Clients)
                merged[r.ClientId] = r;

            if (!real)
            {
                ShowSimulation(outcome);
                Ui.Pause();
                return;
            }

            AnsiConsole.WriteLine();
            Ui.Pause("Presioná una tecla para ver el resultado...");
            pendingIds = _results.Show(merged.Values, webJob.Name, outcome.Result.Elapsed);
        }
    }

    // =====================================================================================

    private PackagePlan? AskFolder()
    {
        AnsiConsole.MarkupLine("[bold]Paso 1 de 5 · Carpeta de publicación[/]");
        AnsiConsole.MarkupLine("[grey]Es la carpeta que ya generaste con 'dotnet publish' o con 'Publicar' de Visual Studio,[/]");
        AnsiConsole.MarkupLine("[grey]por ejemplo ...\\bin\\Release\\net472\\publish. Pegá la ruta o arrastrá la carpeta a esta ventana.[/]");
        AnsiConsole.MarkupLine("[grey]Escribí 0 para volver al menú.[/]");

        var last = _config.WebJobs
            .OrderByDescending(w => w.LastDeployedUtc)
            .Select(w => w.LastFolder)
            .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));

        while (true)
        {
            var prompt = new TextPrompt<string>("Carpeta:").AllowEmpty();
            if (last is not null) prompt.DefaultValue(last);

            var input = AnsiConsole.Prompt(prompt).Trim().Trim('"').Trim();
            if (input.Length == 0 || input == "0") return null;

            try
            {
                return _orchestrator.Preview(input);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidOperationException or UnauthorizedAccessException)
            {
                Ui.Error(ex.Message);
            }
        }
    }

    private WebJobDef? AskWebJob(string folder)
    {
        AnsiConsole.MarkupLine("[bold]Paso 2 de 5 · ¿Qué WebJob es?[/]");

        var guess = WebJobNameGuesser.Guess(folder);
        var known = _config.WebJobs
            .OrderByDescending(w => string.Equals(w.Name, guess, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(w => w.LastDeployedUtc)
            .ToList();

        // Opciones: [sugerido por la carpeta] + conocidos + "otro"
        var options = new List<(string Label, WebJobDef? Job, bool IsNew, bool IsOther)>();
        if (guess is not null && known.All(w => !string.Equals(w.Name, guess, StringComparison.OrdinalIgnoreCase)))
            options.Add(($"{Markup.Escape(guess)}  [green](sugerido por la carpeta)[/]", new WebJobDef { Name = guess }, true, false));

        foreach (var w in known)
        {
            var hint = string.Equals(w.Name, guess, StringComparison.OrdinalIgnoreCase) ? "  [green](sugerido por la carpeta)[/]" : "";
            var last = w.LastDeployedUtc is { } d ? $" · último deploy {d.ToLocalTime():dd/MM/yyyy}" : "";
            options.Add(($"{Markup.Escape(w.Name)}  [grey]({w.Type.Describe()}{last})[/]{hint}", w, false, false));
        }

        const string otherLabel = "Escribir otro nombre...";
        const string backLabel = "« Volver";
        options.Add((otherLabel, null, false, true));
        options.Add((backLabel, null, false, false));

        var labels = options.Select(o => o.Label).ToList();
        var pickedLabel = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Elegí el WebJob (podés escribir para buscar):")
            .PageSize(12)
            .EnableSearch()
            .AddChoices(labels));

        var picked = options[labels.IndexOf(pickedLabel)];
        if (pickedLabel == backLabel) return null;

        WebJobDef job;
        if (picked.IsOther)
        {
            var name = AnsiConsole.Prompt(new TextPrompt<string>("Nombre exacto del WebJob en Azure (carpeta dentro de app_data/jobs):")
                .Validate(n => WebJobDef.IsValidName(n.Trim())
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Solo letras, números, punto, guion y guion bajo (sin espacios ni barras)[/]")));
            job = new WebJobDef { Name = name.Trim() };
            job.Type = AskType();
        }
        else
        {
            job = picked.Job!;
            if (picked.IsNew) job.Type = AskType();
        }

        // Copia: no tocamos el objeto guardado hasta que se publique con éxito.
        return new WebJobDef { Name = job.Name, Type = job.Type };
    }

    private static WebJobType AskType()
    {
        const string continuous = "Continuo (corre siempre)";
        const string triggered = "Programado / Triggered (se ejecuta a demanda o por horario)";
        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("¿Qué tipo de WebJob es?")
            .AddChoices(continuous, triggered));
        return choice == continuous ? WebJobType.Continuous : WebJobType.Triggered;
    }

    private static void ShowPlan(PackagePlan plan)
    {
        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn("Archivo a subir")
            .AddColumn(new TableColumn("Tamaño").RightAligned());

        foreach (var f in plan.Files.Take(12))
            table.AddRow(Markup.Escape(f.RelativePath), Ui.FormatBytes(f.Size));
        if (plan.Files.Count > 12)
            table.AddRow($"[grey]... y {plan.Files.Count - 12} archivos más[/]", "");
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine($"Total: [bold]{plan.Files.Count}[/] archivos, [bold]{Ui.FormatBytes(plan.TotalBytes)}[/] sin comprimir.");
        if (plan.Excluded.Count > 0)
        {
            var sample = string.Join(", ", plan.Excluded.Take(6)) + (plan.Excluded.Count > 6 ? ", ..." : "");
            AnsiConsole.MarkupLine($"[grey]No se suben ({plan.Excluded.Count}): {Markup.Escape(sample)}[/]");
        }
        foreach (var w in plan.Warnings)
            AnsiConsole.MarkupLine($"[yellow]Aviso: {Markup.Escape(w)}[/]");
    }

    private List<ClientConfig>? AskClients(List<ClientConfig> enabled, HashSet<string> withoutCredentials)
    {
        AnsiConsole.MarkupLine("[bold]Paso 4 de 5 · Clientes[/]");
        if (withoutCredentials.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]{withoutCredentials.Count} cliente(s) no tienen credenciales guardadas y van a dar error (se cargan en 'Clientes').[/]");

        string Label(ClientConfig c) =>
            Markup.Escape(c.Name) + $" [grey]({Markup.Escape(c.SiteName)})[/]" +
            (withoutCredentials.Contains(c.Id) ? " [red](sin credenciales)[/]" : "");

        var allLabel = $"Todos los clientes habilitados ({enabled.Count})";
        const string someLabel = "Elegir algunos (con casillas)";
        const string oneLabel = "Solo uno";
        const string backLabel = "« Volver";

        var mode = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("¿A qué clientes publicás?")
            .AddChoices(allLabel, someLabel, oneLabel, backLabel));

        if (mode == backLabel) return null;
        if (mode == allLabel) return enabled;

        if (mode == oneLabel)
        {
            var one = AnsiConsole.Prompt(new SelectionPrompt<ClientConfig>()
                .Title("Elegí el cliente (podés escribir para buscar):")
                .PageSize(15)
                .EnableSearch()
                .UseConverter(Label)
                .AddChoices(enabled));
            return new List<ClientConfig> { one };
        }

        return AnsiConsole.Prompt(new MultiSelectionPrompt<ClientConfig>()
            .Title("Marcá los clientes:")
            .Required()
            .PageSize(15)
            .MoreChoicesText("[grey](subí/bajá para ver más)[/]")
            .InstructionsText("[grey](Espacio = marcar/desmarcar, Enter = confirmar)[/]")
            .UseConverter(Label)
            .AddChoices(enabled));
    }

    private static void ShowSummary(PackagePlan plan, WebJobDef webJob, List<ClientConfig> selected, bool real,
        int parallelism, HashSet<string> withoutCredentials)
    {
        var names = string.Join(", ", selected.Take(8).Select(c => c.Name)) + (selected.Count > 8 ? $" y {selected.Count - 8} más" : "");

        var table = new Table().Border(TableBorder.None).HideHeaders().AddColumn("").AddColumn("");
        table.AddRow("[grey]WebJob[/]", $"[bold]{Markup.Escape(webJob.Name)}[/] ({webJob.Type.Describe()})");
        table.AddRow("[grey]Carpeta[/]", Markup.Escape(plan.SourceFolder));
        table.AddRow("[grey]Archivos[/]", $"{plan.Files.Count} ({Ui.FormatBytes(plan.TotalBytes)} sin comprimir)");
        table.AddRow("[grey]Clientes[/]", $"{selected.Count}: {Markup.Escape(names)}");
        table.AddRow("[grey]Modo[/]", real
            ? $"[red]PUBLICAR DE VERDAD[/] ({(parallelism <= 1 ? "uno por uno" : $"hasta {parallelism} a la vez")})"
            : "[blue]Simulación (no se sube nada)[/]");

        var missing = selected.Count(c => withoutCredentials.Contains(c.Id));
        if (missing > 0)
            table.AddRow("[grey]Atención[/]", $"[yellow]{missing} cliente(s) sin credenciales: van a dar error[/]");

        AnsiConsole.Write(new Panel(table)
            .Header(real ? "[bold red] RESUMEN [/]" : "[bold blue] RESUMEN (SIMULACIÓN) [/]")
            .RoundedBorder());
    }

    private static void ShowSimulation(LiveOutcome outcome)
    {
        AnsiConsole.WriteLine();
        if (outcome.PlanClient is not null && outcome.PlanLines.Count > 0)
        {
            var tree = new Tree($"[bold]Esto haría con cada cliente[/] [grey](ejemplo: {Markup.Escape(outcome.PlanClient)})[/]");
            foreach (var line in outcome.PlanLines)
                tree.AddNode(Markup.Escape(line));
            AnsiConsole.Write(tree);
        }

        var simulated = outcome.Result.SimulatedCount;
        AnsiConsole.MarkupLine($"[blue]{simulated} cliente(s) simulados.[/] [grey]No se subió nada ni se modificó ningún sitio.[/]");

        var problems = outcome.Result.Clients.Where(c => c.Outcome != DeployOutcome.Simulado).ToList();
        foreach (var p in problems)
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(p.ClientName)}: {Markup.Escape(p.Message)}[/]");
        AnsiConsole.WriteLine();
    }
}
