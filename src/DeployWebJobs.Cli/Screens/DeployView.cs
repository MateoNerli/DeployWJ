using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>Resultado de una ejecución en vivo, más (en simulación) un ejemplo de los pasos.</summary>
internal sealed record LiveOutcome(
    DeployRunResult Result,
    string? PlanClient,
    IReadOnlyList<string> PlanLines);

/// <summary>
/// Pantalla de progreso en vivo mientras se publica. Ctrl+C cancela el deploy sin romper nada:
/// lo que ya se subió queda subido, lo pendiente no se toca, y se muestra el resumen.
/// </summary>
internal sealed class DeployView
{
    private readonly DeployOrchestrator _orchestrator;

    public DeployView(DeployOrchestrator orchestrator) => _orchestrator = orchestrator;

    public async Task<LiveOutcome> RunAsync(DeployRequest request, IReadOnlyList<ClientConfig> clients)
    {
        var state = new LiveState(clients);
        using var cts = new CancellationTokenSource();

        // Ctrl+C solo se intercepta MIENTRAS se publica; en los menús sigue funcionando como siempre.
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;              // evita que el proceso muera de golpe
            state.CancelRequested = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            DeployRunResult? result = null;

            await AnsiConsole.Live(state.Render())
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .Cropping(VerticalOverflowCropping.Top)
                .StartAsync(async ctx =>
                {
                    var run = _orchestrator.RunAsync(request, state, cts.Token);
                    while (!run.IsCompleted)
                    {
                        ctx.UpdateTarget(state.Render());
                        await Task.WhenAny(run, Task.Delay(120));
                    }

                    result = await run;           // si falló algo inesperado, la excepción sale acá
                    ctx.UpdateTarget(state.Render());
                });

            var sample = state.SamplePlan();
            return new LiveOutcome(result!, sample?.ClientName, sample?.Lines ?? Array.Empty<string>());
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
