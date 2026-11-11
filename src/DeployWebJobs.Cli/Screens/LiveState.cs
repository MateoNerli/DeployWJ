using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DeployWebJobs.Cli.Screens;

/// <summary>
/// Estado de la pantalla en vivo. Recibe los eventos del orquestador (desde varios hilos, por eso
/// el lock) y los convierte en una tabla: clientes en curso + los últimos terminados + un resumen.
/// Así la pantalla no se desborda aunque haya 30 clientes.
/// </summary>
internal sealed class LiveState : IProgress<DeployProgress>
{
    private sealed class Row
    {
        public string Name = "";
        public DeployStep Step = DeployStep.Validando;
        public string Message = "En cola";
        public int Percent;
        public int Attempt = 1;
        public long Done;
        public long Total;
        public DeployOutcome? Outcome;
        public DateTime? Started;
        public DateTime? Ended;
        public int FinishOrder;
    }

    private const int RecentFinishedToShow = 8;

    private readonly object _lock = new();
    private readonly Dictionary<string, Row> _rows = new();
    private readonly Dictionary<string, List<string>> _plans = new();
    private int _finished;
    private int _tick;

    public LiveState(IEnumerable<ClientConfig> clients)
    {
        foreach (var c in clients)
            _rows[c.Id] = new Row { Name = c.Name };
    }

    public bool CancelRequested { get; set; }

    public void Report(DeployProgress p)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(p.ClientId, out var row))
            {
                row = new Row { Name = p.ClientName };
                _rows[p.ClientId] = row;
            }

            row.Started ??= DateTime.UtcNow;
            row.Step = p.Step;
            row.Message = p.Message;
            row.Attempt = p.Attempt;
            row.Done = p.BytesDone;
            row.Total = p.BytesTotal;
            row.Percent = PercentFor(p);

            if (p.Message.StartsWith("[Simulación]", StringComparison.Ordinal))
            {
                if (!_plans.TryGetValue(p.ClientId, out var lines))
                    _plans[p.ClientId] = lines = new List<string>();
                lines.Add(p.Message["[Simulación]".Length..].Trim());
            }

            if (p.Outcome is { } outcome && row.Outcome is null)
            {
                row.Outcome = outcome;
                row.Ended = DateTime.UtcNow;
                row.Percent = 100;
                row.FinishOrder = ++_finished;
            }
        }
    }

    /// <summary>Pasos que se habrían ejecutado (simulación) para el primer cliente que los informó.</summary>
    public (string ClientName, IReadOnlyList<string> Lines)? SamplePlan()
    {
        lock (_lock)
        {
            foreach (var (id, lines) in _plans)
                return (_rows[id].Name, lines.ToList());
            return null;
        }
    }

    private static int PercentFor(DeployProgress p) => p.Step switch
    {
        DeployStep.Validando => 3,
        DeployStep.PreparandoPaquete => 5,
        DeployStep.Conectando => 10,
        DeployStep.DeteniendoJob => 20,
        DeployStep.Subiendo => p.BytesTotal > 0 ? 25 + (int)(60.0 * p.BytesDone / p.BytesTotal) : 25,
        DeployStep.IniciandoJob => 88,
        DeployStep.Verificando => 94,
        DeployStep.Finalizado => 100,
        _ => 0
    };

    public IRenderable Render()
    {
        lock (_lock)
        {
            _tick++;
            var spinner = "|/-\\"[_tick % 4];

            var total = _rows.Count;
            var done = _rows.Values.Count(r => r.Outcome is not null);
            var ok = _rows.Values.Count(r => r.Outcome == DeployOutcome.Ok);
            var err = _rows.Values.Count(r => r.Outcome == DeployOutcome.Error);
            var running = _rows.Values.Count(r => r.Started is not null && r.Outcome is null);
            var queued = _rows.Values.Count(r => r.Started is null);

            var header = $"[bold]{done}/{total}[/] terminados   [green]{ok} OK[/]   [red]{err} con error[/]   " +
                         $"[yellow]{running} en curso[/]   [grey]{queued} en cola[/]";
            if (CancelRequested)
                header += "   [bold yellow]CANCELANDO... esperando que terminen las operaciones en curso[/]";

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumn("Cliente");
            table.AddColumn("Estado");
            table.AddColumn("Progreso");
            table.AddColumn(new TableColumn("Tiempo").RightAligned());

            var recent = _rows.Values.Where(r => r.Outcome is not null)
                .OrderByDescending(r => r.FinishOrder).Take(RecentFinishedToShow).Reverse();
            var active = _rows.Values.Where(r => r.Started is not null && r.Outcome is null)
                .OrderBy(r => r.Started);

            foreach (var row in recent.Concat(active))
            {
                var elapsed = (row.Ended ?? DateTime.UtcNow) - (row.Started ?? DateTime.UtcNow);
                table.AddRow(
                    Markup.Escape(Ui.Short(row.Name, 30)),
                    StatusText(row, spinner),
                    ProgressBar(row),
                    Ui.FormatElapsed(elapsed));
            }

            return new Rows(new Markup(header), table);
        }
    }

    private static string StatusText(Row row, char spinner)
    {
        if (row.Outcome is { } outcome)
        {
            var detail = Markup.Escape(Ui.Short(row.Message, 60));
            return outcome switch
            {
                DeployOutcome.Ok => "[green]OK[/] publicado",
                DeployOutcome.Error => $"[red]ERROR[/] {detail}",
                DeployOutcome.Omitido => $"[grey]Omitido[/] {detail}",
                DeployOutcome.Cancelado => "[yellow]Cancelado[/]",
                DeployOutcome.Simulado => "[blue]Simulado[/]",
                _ => detail
            };
        }

        if (row.Message.StartsWith("Falló", StringComparison.Ordinal))
            return $"[yellow]{spinner}[/] [yellow]{Markup.Escape(Ui.Short(row.Message, 60))}[/]";

        var text = $"[yellow]{spinner}[/] {Markup.Escape(row.Step.Describe())}";
        if (row.Step == DeployStep.Subiendo && row.Total > 0)
            text += $" [grey]{Ui.FormatBytes(row.Done)} / {Ui.FormatBytes(row.Total)}[/]";
        if (row.Attempt > 1)
            text += $" [grey](intento {row.Attempt})[/]";
        return text;
    }

    private static string ProgressBar(Row row)
    {
        const int width = 20;
        var color = row.Outcome switch
        {
            DeployOutcome.Ok => "green",
            DeployOutcome.Error => "red",
            DeployOutcome.Cancelado => "yellow",
            DeployOutcome.Simulado => "blue",
            DeployOutcome.Omitido => "grey",
            _ => "yellow"
        };

        var filled = Math.Clamp(row.Percent * width / 100, 0, width);
        var bar = new string('█', filled) + new string('░', width - filled);
        return $"[{color}]{bar}[/] {row.Percent,3}%";
    }
}
