using System.Diagnostics;
using Avalonia.Media;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Ui.Mvvm;

namespace DeployWebJobs.Ui.ViewModels;

/// <summary>Un cliente de la lista con su casilla para marcarlo.</summary>
public sealed class ClientItem : ObservableObject
{
    private readonly ClientConfig _client;
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public ClientItem(ClientConfig client, bool hasCredentials, Action selectionChanged)
    {
        _client = client;
        _selectionChanged = selectionChanged;
        HasCredentials = hasCredentials;
    }

    public string Id => _client.Id;
    public string Name => _client.Name;
    public bool HasCredentials { get; }

    /// <summary>Solo se puede publicar en clientes habilitados y con credenciales cargadas.</summary>
    public bool CanSelect => _client.Enabled && HasCredentials;

    public string Detail =>
        !_client.Enabled ? "deshabilitado" :
        !HasCredentials ? "faltan credenciales" :
        _client.EffectiveScmHost;

    public IBrush DetailBrush => CanSelect ? Palette.Muted : Palette.Warn;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !CanSelect) return;
            if (SetProperty(ref _isSelected, value)) _selectionChanged();
        }
    }
}

/// <summary>Una fila de la pantalla "publicando": estado en vivo de un cliente.</summary>
public sealed class RunRowViewModel : ObservableObject
{
    private readonly Stopwatch _stopwatch = new();
    private string _statusText = "En espera";
    private string _detail = "";
    private double _percent;
    private bool _isIndeterminate;
    private string _elapsedText = "";
    private IBrush _statusBrush = Palette.Muted;
    private bool _isDone;

    public RunRowViewModel(string clientId, string name)
    {
        ClientId = clientId;
        Name = name;
    }

    public string ClientId { get; }
    public string Name { get; }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public double Percent { get => _percent; private set => SetProperty(ref _percent, value); }
    public bool IsIndeterminate { get => _isIndeterminate; private set => SetProperty(ref _isIndeterminate, value); }
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }
    public IBrush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }
    public bool IsDone { get => _isDone; private set => SetProperty(ref _isDone, value); }

    /// <summary>Aplica el último evento recibido de este cliente (se llama desde el hilo de la pantalla).</summary>
    public void Apply(DeployProgress p)
    {
        if (IsDone) return;

        if (!_stopwatch.IsRunning) _stopwatch.Start();

        if (p.Outcome is DeployOutcome outcome)
        {
            _stopwatch.Stop();
            ElapsedText = Fmt.Elapsed(_stopwatch.Elapsed);
            StatusText = outcome.Describe();
            Detail = Fmt.Short(p.Message, 110);
            StatusBrush = BrushFor(outcome);
            IsIndeterminate = false;
            Percent = 100;
            IsDone = true;
            return;
        }

        StatusText = p.Attempt > 1 ? $"{p.Step.Describe()} (intento {p.Attempt})" : p.Step.Describe();
        StatusBrush = Palette.Info;

        if (p.Step == DeployStep.Subiendo && p.BytesTotal > 0)
        {
            IsIndeterminate = false;
            Percent = Math.Min(100, p.BytesDone * 100.0 / p.BytesTotal);
            Detail = $"{Fmt.Bytes(p.BytesDone)} de {Fmt.Bytes(p.BytesTotal)}";
        }
        else
        {
            IsIndeterminate = true;
            Detail = Fmt.Short(p.Message, 110);
        }
    }

    /// <summary>Refresca el cronómetro mientras el cliente está en curso.</summary>
    public void Tick()
    {
        if (_stopwatch.IsRunning) ElapsedText = Fmt.Elapsed(_stopwatch.Elapsed);
    }

    internal static IBrush BrushFor(DeployOutcome outcome) => outcome switch
    {
        DeployOutcome.Ok => Palette.Ok,
        DeployOutcome.Error => Palette.Error,
        DeployOutcome.Simulado => Palette.Info,
        _ => Palette.Warn
    };
}

/// <summary>Una fila de la pantalla de resultados / errores.</summary>
public sealed class ResultRowViewModel
{
    public ResultRowViewModel(ClientDeployResult result)
    {
        ClientId = result.ClientId;
        Name = result.ClientName;
        Outcome = result.Outcome;
        OutcomeText = result.Outcome.Describe();
        Message = result.Message;
        ElapsedText = Fmt.Elapsed(result.Elapsed);
        AttemptsText = result.Attempts > 1 ? $"{result.Attempts} intentos" : "";
        Brush = RunRowViewModel.BrushFor(result.Outcome);
    }

    public string ClientId { get; }
    public string Name { get; }
    public DeployOutcome Outcome { get; }
    public string OutcomeText { get; }
    public string Message { get; }
    public string ElapsedText { get; }
    public string AttemptsText { get; }
    public IBrush Brush { get; }
}
