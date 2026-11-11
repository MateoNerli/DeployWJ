using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>
/// Pantalla de resultados: tabla final (OK / Error / Tiempo) y, si hubo errores, una pantalla
/// aparte con el detalle y la opción de reintentar solo los que fallaron.
/// </summary>
internal sealed class ResultsScreen
{
    /// <returns>Ids de clientes a reintentar (vacío = terminar).</returns>
    public List<string> Show(IReadOnlyCollection<ClientDeployResult> all, string webJobName, TimeSpan lastRunElapsed)
    {
        while (true)
        {
            Ui.Header($"Resultado · {webJobName}");

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("Cliente")
                .AddColumn("Resultado")
                .AddColumn(new TableColumn("Intentos").RightAligned())
                .AddColumn(new TableColumn("Tiempo").RightAligned())
                .AddColumn("Detalle");

            foreach (var x in all.OrderBy(Severity).ThenBy(r => r.ClientName, StringComparer.CurrentCultureIgnoreCase))
            {
                table.AddRow(
                    Markup.Escape(x.ClientName),
                    OutcomeText(x.Outcome),
                    x.Attempts > 0 ? x.Attempts.ToString() : "-",
                    Ui.FormatElapsed(x.Elapsed),
                    Markup.Escape(Ui.Short(x.Message, 70)));
            }
            AnsiConsole.Write(table);

            var ok = all.Count(x => x.Outcome == DeployOutcome.Ok);
            var errors = all.Where(x => x.Outcome == DeployOutcome.Error).ToList();
            var others = all.Count - ok - errors.Count;

            AnsiConsole.Write(new Panel(new Markup(
                    $"[green]{ok} publicados[/]    [red]{errors.Count} con error[/]    [grey]{others} otros[/]    " +
                    $"Tiempo de esta ejecución: [bold]{Ui.FormatElapsed(lastRunElapsed)}[/]"))
                .Header("Resumen")
                .RoundedBorder());

            AnsiConsole.MarkupLine($"[grey]Log detallado en: {Markup.Escape(Path.Combine(ConfigService.DefaultDirectory, "logs"))}[/]");
            AnsiConsole.WriteLine();

            if (errors.Count == 0)
            {
                Ui.Pause();
                return new List<string>();
            }

            const string seeErrors = "Ver el detalle de los errores";
            const string back = "Volver al menú";
            var retry = $"Reintentar solo los {errors.Count} que fallaron";

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("¿Qué querés hacer?")
                .AddChoices(seeErrors, retry, back));

            if (choice == seeErrors) ShowErrors(errors);
            else if (choice == retry) return errors.Select(x => x.ClientId).ToList();
            else return new List<string>();
        }
    }

    private static void ShowErrors(IReadOnlyList<ClientDeployResult> errors)
    {
        Ui.Header("Detalle de los errores");

        foreach (var e in errors)
        {
            var body = new Markup(
                $"{Markup.Escape(e.Message)}\n[grey]Intentos: {e.Attempts}   Tiempo: {Ui.FormatElapsed(e.Elapsed)}[/]");
            AnsiConsole.Write(new Panel(body)
                .Header($"[red] {Markup.Escape(e.ClientName)} [/]")
                .RoundedBorder()
                .BorderColor(Color.Red)
                .Expand());
        }

        AnsiConsole.MarkupLine("[grey]Pistas: 401 = usuario/contraseña de publicación incorrectos. 403 = autenticación básica SCM " +
                               "deshabilitada o restricción de IP en el sitio. Tiempo agotado = problema de red, probá reintentar.[/]");
        AnsiConsole.WriteLine();
        Ui.Pause();
    }

    private static int Severity(ClientDeployResult r) => r.Outcome switch
    {
        DeployOutcome.Error => 0,
        DeployOutcome.Cancelado => 1,
        DeployOutcome.Omitido => 2,
        DeployOutcome.Ok => 3,
        _ => 4
    };

    private static string OutcomeText(DeployOutcome outcome) => outcome switch
    {
        DeployOutcome.Ok => "[green]OK[/]",
        DeployOutcome.Error => "[red]ERROR[/]",
        DeployOutcome.Omitido => "[grey]Omitido[/]",
        DeployOutcome.Cancelado => "[yellow]Cancelado[/]",
        DeployOutcome.Simulado => "[blue]Simulado[/]",
        _ => outcome.ToString()
    };
}
