using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Deploy;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Packaging;
using DeployWebJobs.Core.Security;
using DeployWebJobs.Ui.Mvvm;
using DeployWebJobs.Ui.Services;
using Microsoft.Extensions.Logging;

namespace DeployWebJobs.Ui.ViewModels;

public enum PublishStage { Form, Running, Results, Errors }

/// <summary>
/// Pantalla principal: elegir carpeta, WebJob y clientes, publicar con avance en vivo,
/// y ver resultados / errores. Usa el mismo DeployOrchestrator que la consola.
/// </summary>
public sealed class PublishViewModel : ObservableObject, IActivatable
{
    private readonly ConfigService _config;
    private readonly DeployOrchestrator _orchestrator;
    private readonly ICredentialStore _credentials;
    private readonly IDialogService _dialogs;
    private readonly ILogger<PublishViewModel> _logger;

    // ---- formulario
    private PublishStage _stage = PublishStage.Form;
    private string _folder = "";
    private PackagePlan? _plan;
    private string _folderError = "";
    private string _planSummary = "";
    private string _planWarnings = "";
    private string _filePreview = "";
    private bool _isAnalyzing;
    private string _webJobName = "";
    private string? _selectedKnownWebJob;
    private bool _isContinuous = true;
    private bool _isExisting = true;
    private string _searchText = "";
    private bool _isDryRun;
    private bool _runParallel;
    private decimal? _parallelismValue = 3;
    private bool _parallelTouched;

    // ---- ejecución
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _timer;
    private readonly ConcurrentDictionary<string, DeployProgress> _latest = new();
    private readonly System.Diagnostics.Stopwatch _overall = new();
    private double _overallPercent;
    private string _overallText = "";
    private bool _isCancelling;
    private DeployRequest? _lastRequest;

    // ---- resultados
    private int _okCount;
    private int _errorCount;
    private int _otherCount;
    private string _resultTitle = "";
    private IBrush _resultBrush = Palette.Ok;
    private string _resultElapsed = "";
    private bool _isDryRunResult;

    public PublishViewModel(
        ConfigService config,
        DeployOrchestrator orchestrator,
        ICredentialStore credentials,
        IDialogService dialogs,
        ILogger<PublishViewModel> logger)
    {
        _config = config;
        _orchestrator = orchestrator;
        _credentials = credentials;
        _dialogs = dialogs;
        _logger = logger;

        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        PublishCommand = new AsyncRelayCommand(PublishAsync, () => CanPublish);
        CancelCommand = new RelayCommand(Cancel);
        RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync);
        NewRunCommand = new RelayCommand(NewRun);
        ShowErrorsCommand = new RelayCommand(() => Stage = PublishStage.Errors);
        BackToResultsCommand = new RelayCommand(() => Stage = PublishStage.Results);

        SyncParallelFromSettings();
        LoadKnownWebJobs();
        LoadClients();
    }

    // =====================================================================
    //  Comandos
    // =====================================================================

    public AsyncRelayCommand BrowseCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncRelayCommand PublishCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand RetryFailedCommand { get; }
    public RelayCommand NewRunCommand { get; }
    public RelayCommand ShowErrorsCommand { get; }
    public RelayCommand BackToResultsCommand { get; }

    // =====================================================================
    //  Etapa actual
    // =====================================================================

    public PublishStage Stage
    {
        get => _stage;
        private set
        {
            if (!SetProperty(ref _stage, value)) return;
            OnPropertyChanged(nameof(IsForm));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsResults));
            OnPropertyChanged(nameof(IsErrors));
        }
    }

    public bool IsForm => _stage == PublishStage.Form;
    public bool IsRunning => _stage == PublishStage.Running;
    public bool IsResults => _stage == PublishStage.Results;
    public bool IsErrors => _stage == PublishStage.Errors;

    // =====================================================================
    //  1. Carpeta
    // =====================================================================

    public string Folder
    {
        get => _folder;
        set
        {
            if (!SetProperty(ref _folder, value)) return;
            ClearPlan();
            RaiseState();
        }
    }

    public bool HasPlan => _plan is not null;
    public string FolderError { get => _folderError; private set { if (SetProperty(ref _folderError, value)) OnPropertyChanged(nameof(HasFolderError)); } }
    public bool HasFolderError => !string.IsNullOrEmpty(_folderError);
    public string PlanSummary { get => _planSummary; private set => SetProperty(ref _planSummary, value); }
    public string PlanWarnings { get => _planWarnings; private set { if (SetProperty(ref _planWarnings, value)) OnPropertyChanged(nameof(HasPlanWarnings)); } }
    public bool HasPlanWarnings => !string.IsNullOrEmpty(_planWarnings);
    public string FilePreview { get => _filePreview; private set => SetProperty(ref _filePreview, value); }
    public bool IsAnalyzing { get => _isAnalyzing; private set => SetProperty(ref _isAnalyzing, value); }

    private async Task BrowseAsync()
    {
        string? suggested = Directory.Exists(Folder) ? Folder : null;
        if (suggested is null && !string.IsNullOrWhiteSpace(SelectedKnownWebJob))
            suggested = _config.FindWebJob(SelectedKnownWebJob)?.LastFolder;

        var picked = await _dialogs.PickFolderAsync("Elegí la carpeta de publicación (la que ya compilaste)", suggested);
        if (picked is null) return;

        Folder = picked;
        await AnalyzeAsync();
    }

    private async Task AnalyzeAsync()
    {
        ClearPlan();

        var folder = (Folder ?? "").Trim().Trim('"');
        if (folder.Length == 0)
        {
            FolderError = "Elegí la carpeta de publicación.";
            return;
        }

        if (!Directory.Exists(folder))
        {
            FolderError = "La carpeta no existe.";
            return;
        }

        IsAnalyzing = true;
        try
        {
            var plan = await Task.Run(() => _orchestrator.Preview(folder));

            _folder = folder;
            OnPropertyChanged(nameof(Folder));

            _plan = plan;
            PlanSummary = $"{plan.Files.Count} archivos · {Fmt.Bytes(plan.TotalBytes)} · {plan.Excluded.Count} excluidos por las reglas";
            PlanWarnings = string.Join("\n", plan.Warnings);

            var preview = plan.Files.Take(15).Select(f => f.RelativePath).ToList();
            if (plan.Files.Count > preview.Count) preview.Add($"… y {plan.Files.Count - preview.Count} más");
            FilePreview = string.Join("\n", preview);

            OnPropertyChanged(nameof(HasPlan));

            if (string.IsNullOrWhiteSpace(WebJobName) && WebJobNameGuesser.Guess(folder) is { } guess)
            {
                WebJobName = guess;
                var known = _config.FindWebJob(guess);
                if (known is not null) IsContinuous = known.Type == WebJobType.Continuous;
            }
        }
        catch (Exception ex)
        {
            FolderError = ex.Message;
        }
        finally
        {
            IsAnalyzing = false;
            RaiseState();
        }
    }

    private void ClearPlan()
    {
        _plan = null;
        FolderError = "";
        PlanSummary = "";
        PlanWarnings = "";
        FilePreview = "";
        OnPropertyChanged(nameof(HasPlan));
    }

    // =====================================================================
    //  2. WebJob
    // =====================================================================

    public ObservableCollection<string> KnownWebJobs { get; } = new();

    public string? SelectedKnownWebJob
    {
        get => _selectedKnownWebJob;
        set
        {
            if (!SetProperty(ref _selectedKnownWebJob, value) || value is null) return;

            WebJobName = value;
            var def = _config.FindWebJob(value);
            if (def is null) return;

            IsContinuous = def.Type == WebJobType.Continuous;
            IsExisting = true;

            // Sugiere la última carpeta usada con este WebJob (si todavía no se eligió una).
            if (string.IsNullOrWhiteSpace(Folder) && !string.IsNullOrWhiteSpace(def.LastFolder) && Directory.Exists(def.LastFolder))
            {
                Folder = def.LastFolder!;
                _ = AnalyzeAsync();
            }
        }
    }

    public string WebJobName
    {
        get => _webJobName;
        set
        {
            if (SetProperty(ref _webJobName, value)) RaiseState();
        }
    }

    public bool IsContinuous
    {
        get => _isContinuous;
        set
        {
            if (!SetProperty(ref _isContinuous, value)) return;
            OnPropertyChanged(nameof(IsTriggered));
        }
    }

    /// <summary>true = el WebJob ya existe en Azure (hay que usar el nombre exacto); false = es nuevo.</summary>
    public bool IsExisting
    {
        get => _isExisting;
        set
        {
            if (!SetProperty(ref _isExisting, value)) return;
            OnPropertyChanged(nameof(IsNewJob));
        }
    }

    public bool IsNewJob
    {
        get => !_isExisting;
        set => IsExisting = !value;
    }

    public bool IsTriggered
    {
        get => !_isContinuous;
        set => IsContinuous = !value;
    }

    private void LoadKnownWebJobs()
    {
        var names = _config.WebJobs.Select(w => w.Name).ToList();
        if (names.SequenceEqual(KnownWebJobs)) return;

        KnownWebJobs.Clear();
        foreach (var n in names) KnownWebJobs.Add(n);
    }

    // =====================================================================
    //  3. Clientes
    // =====================================================================

    private readonly List<ClientItem> _allClients = new();

    public ObservableCollection<ClientItem> VisibleClients { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplyFilter();
        }
    }

    public int SelectedCount => _allClients.Count(c => c.IsSelected);
    public string SelectedText => SelectedCount switch
    {
        0 => "Ninguno marcado",
        1 => "1 cliente marcado",
        var n => $"{n} clientes marcados"
    };

    public bool HasNoClients => _allClients.Count == 0;

    private void LoadClients()
    {
        var previouslySelected = _allClients.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet();

        _allClients.Clear();
        foreach (var client in _config.Clients)
        {
            var item = new ClientItem(client, _credentials.Get(client.Id) is not null, OnSelectionChanged);
            _allClients.Add(item);
            if (previouslySelected.Contains(client.Id)) item.IsSelected = true;
        }

        ApplyFilter();
        OnPropertyChanged(nameof(HasNoClients));
        OnSelectionChanged();
    }

    private void ApplyFilter()
    {
        var text = (SearchText ?? "").Trim();
        VisibleClients.Clear();
        foreach (var c in _allClients)
        {
            if (text.Length == 0 || c.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                VisibleClients.Add(c);
        }
    }

    private void SetAll(bool selected)
    {
        foreach (var c in VisibleClients.ToList())
        {
            if (selected && !c.CanSelect) continue;
            c.IsSelected = selected;
        }
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        RaiseState();
    }

    // =====================================================================
    //  4. Opciones
    // =====================================================================

    public bool IsDryRun { get => _isDryRun; set => SetProperty(ref _isDryRun, value); }

    public bool RunParallel
    {
        get => _runParallel;
        set
        {
            _parallelTouched = true;
            SetProperty(ref _runParallel, value);
        }
    }

    public decimal? ParallelismValue
    {
        get => _parallelismValue;
        set
        {
            _parallelTouched = true;
            SetProperty(ref _parallelismValue, value);
        }
    }

    private void SyncParallelFromSettings()
    {
        var max = _config.Config.Settings.MaxParallelism;
        _runParallel = max > 1;
        _parallelismValue = Math.Max(2, max);
        OnPropertyChanged(nameof(RunParallel));
        OnPropertyChanged(nameof(ParallelismValue));
    }

    public bool CanPublish => Stage == PublishStage.Form && MissingText.Length == 0 && !IsAnalyzing;

    public string MissingText
    {
        get
        {
            if (!HasPlan) return "Elegí la carpeta de publicación y analizala.";
            if (!WebJobDef.IsValidName((WebJobName ?? "").Trim()))
                return "Indicá el nombre del WebJob (letras, números, punto, guion y guion bajo).";
            if (SelectedCount == 0) return "Marcá al menos un cliente.";
            return "";
        }
    }

    public bool HasMissing => MissingText.Length > 0;

    private void RaiseState()
    {
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(MissingText));
        OnPropertyChanged(nameof(HasMissing));
        PublishCommand.RaiseCanExecuteChanged();
    }

    public void OnActivated()
    {
        if (Stage == PublishStage.Running) return;

        if (!_parallelTouched) SyncParallelFromSettings();
        LoadKnownWebJobs();
        LoadClients();
    }

    // =====================================================================
    //  Publicar
    // =====================================================================

    private async Task PublishAsync()
    {
        if (!CanPublish || _plan is null) return;

        var settings = _config.Config.Settings;
        var name = WebJobName.Trim();
        var type = IsContinuous ? WebJobType.Continuous : WebJobType.Triggered;
        var selected = _allClients.Where(c => c.IsSelected).ToList();

        if (!IsDryRun && settings.ConfirmBeforeDeploy)
        {
            var names = selected.Take(10).Select(c => "  • " + c.Name).ToList();
            if (selected.Count > 10) names.Add($"  … y {selected.Count - 10} más");

            var message =
                $"Vas a publicar el WebJob '{name}' ({type.Describe()}) en {selected.Count} cliente(s).\n" +
                (IsExisting
                    ? "Marcaste que YA EXISTE: en cada cliente se verifica que el nombre sea exacto; si no, ese cliente no se toca.\n\n"
                    : "Marcaste que es NUEVO: en cada cliente se verifica que no exista uno con ese nombre; si existe, ese cliente no se toca.\n\n") +
                string.Join("\n", names) + "\n\n" +
                $"Archivos: {_plan.Files.Count} ({Fmt.Bytes(_plan.TotalBytes)}).\n" +
                "Esto reemplaza los archivos del WebJob en producción (los que ya estaban y no están en la carpeta no se borran).";

            if (!await _dialogs.ConfirmAsync("Confirmar publicación", message, "Publicar", "Cancelar"))
                return;
        }

        var parallel = RunParallel ? Math.Clamp((int)(ParallelismValue ?? 1), 1, 16) : 1;

        var request = new DeployRequest
        {
            SourceFolder = _plan.SourceFolder,
            WebJobName = name,
            WebJobType = type,
            ClientIds = selected.Select(c => c.Id).ToList(),
            DryRun = IsDryRun,
            Existence = IsExisting ? WebJobExistence.MustExist : WebJobExistence.MustBeNew,
            MaxParallelism = parallel
        };

        await RunAsync(request);
    }

    private async Task RunAsync(DeployRequest request)
    {
        _lastRequest = request;
        _latest.Clear();
        Rows.Clear();
        foreach (var id in request.ClientIds)
            Rows.Add(new RunRowViewModel(id, _config.FindClient(id)?.Name ?? id));

        OverallPercent = 0;
        OverallText = "Preparando el paquete…";
        IsCancelling = false;
        _overall.Restart();
        Stage = PublishStage.Running;
        RaiseState();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += (_, _) => Tick();
        }

        _timer.Start();

        DeployRunResult? result = null;
        Exception? failure = null;

        try
        {
            var progress = new LatestProgress(_latest);
            result = await Task.Run(() => _orchestrator.RunAsync(request, progress, token));
        }
        catch (OperationCanceledException)
        {
            // se informa abajo
        }
        catch (Exception ex)
        {
            failure = ex;
            _logger.LogError(ex, "Falló la publicación de {Job}", request.WebJobName);
        }
        finally
        {
            _timer.Stop();
            _cts?.Dispose();
            _cts = null;
        }

        Tick(); // último refresco de pantalla

        if (failure is not null)
        {
            Stage = PublishStage.Form;
            RaiseState();
            await _dialogs.ShowMessageAsync("No se pudo publicar", failure.Message);
            return;
        }

        if (result is null)
        {
            Stage = PublishStage.Form;
            RaiseState();
            await _dialogs.ShowMessageAsync("Publicación cancelada", "Se canceló antes de terminar. Lo que ya se había subido quedó subido.");
            return;
        }

        ShowResults(result);
        LoadKnownWebJobs();
    }


    private void Tick()
    {
        foreach (var row in Rows)
        {
            if (_latest.TryGetValue(row.ClientId, out var progress)) row.Apply(progress);
            row.Tick();
        }

        var done = Rows.Count(r => r.IsDone);
        OverallPercent = Rows.Count == 0 ? 0 : done * 100.0 / Rows.Count;
        OverallText = $"{done} de {Rows.Count} clientes terminados · {Fmt.Elapsed(_overall.Elapsed)}";
    }

    private void Cancel()
    {
        if (_cts is null || IsCancelling) return;
        IsCancelling = true;
        OverallText = "Cancelando… se espera a que terminen los pasos en curso.";
        _cts.Cancel();
    }

    public ObservableCollection<RunRowViewModel> Rows { get; } = new();

    public double OverallPercent { get => _overallPercent; private set => SetProperty(ref _overallPercent, value); }
    public string OverallText { get => _overallText; private set => SetProperty(ref _overallText, value); }
    public bool IsCancelling { get => _isCancelling; private set => SetProperty(ref _isCancelling, value); }

    private sealed class LatestProgress : IProgress<DeployProgress>
    {
        private readonly ConcurrentDictionary<string, DeployProgress> _target;
        public LatestProgress(ConcurrentDictionary<string, DeployProgress> target) => _target = target;

        // Se llama desde hilos de trabajo: solo se guarda el último evento; la pantalla lo lee con su timer.
        public void Report(DeployProgress value) => _target[value.ClientId] = value;
    }

    // =====================================================================
    //  Resultados y errores
    // =====================================================================

    public ObservableCollection<ResultRowViewModel> ResultRows { get; } = new();
    public ObservableCollection<ResultRowViewModel> ErrorRows { get; } = new();

    public int OkCount { get => _okCount; private set => SetProperty(ref _okCount, value); }
    public int ErrorCount { get => _errorCount; private set { if (SetProperty(ref _errorCount, value)) OnPropertyChanged(nameof(HasErrors)); } }
    public int OtherCount { get => _otherCount; private set { if (SetProperty(ref _otherCount, value)) OnPropertyChanged(nameof(HasOthers)); } }
    public bool HasErrors => _errorCount > 0;
    public bool HasOthers => _otherCount > 0;
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public IBrush ResultBrush { get => _resultBrush; private set => SetProperty(ref _resultBrush, value); }
    public string ResultElapsed { get => _resultElapsed; private set => SetProperty(ref _resultElapsed, value); }
    public bool IsDryRunResult { get => _isDryRunResult; private set => SetProperty(ref _isDryRunResult, value); }
    public string OkLabel => IsDryRunResult ? "Simuladas" : "Publicadas";
    public string RetryText => $"Reintentar solo los {ErrorCount} que fallaron";

    private void ShowResults(DeployRunResult result)
    {
        ResultRows.Clear();
        ErrorRows.Clear();
        foreach (var c in result.Clients)
        {
            var row = new ResultRowViewModel(c);
            ResultRows.Add(row);
            if (c.Outcome == DeployOutcome.Error) ErrorRows.Add(row);
        }

        IsDryRunResult = result.DryRun;
        OnPropertyChanged(nameof(OkLabel));
        OkCount = result.OkCount + result.SimulatedCount;
        ErrorCount = result.ErrorCount;
        OtherCount = result.OtherCount;
        OnPropertyChanged(nameof(RetryText));
        ResultElapsed = $"Tiempo total: {Fmt.Elapsed(result.Elapsed)}";

        if (result.ErrorCount > 0)
        {
            ResultTitle = "Terminó con errores";
            ResultBrush = Palette.Error;
        }
        else if (result.OtherCount > 0)
        {
            ResultTitle = "Terminó, pero algunos clientes no se publicaron";
            ResultBrush = Palette.Warn;
        }
        else if (result.DryRun)
        {
            ResultTitle = "Simulación terminada (no se subió nada)";
            ResultBrush = Palette.Info;
        }
        else
        {
            ResultTitle = "¡Listo! Todo se publicó correctamente";
            ResultBrush = Palette.Ok;
        }

        Stage = PublishStage.Results;
        RaiseState();
    }

    private async Task RetryFailedAsync()
    {
        if (_lastRequest is null || ErrorRows.Count == 0) return;

        var retry = new DeployRequest
        {
            SourceFolder = _lastRequest.SourceFolder,
            WebJobName = _lastRequest.WebJobName,
            WebJobType = _lastRequest.WebJobType,
            ClientIds = ErrorRows.Select(r => r.ClientId).ToList(),
            DryRun = _lastRequest.DryRun,
            Existence = _lastRequest.Existence,
            MaxParallelism = _lastRequest.MaxParallelism
        };

        await RunAsync(retry);
    }

    private void NewRun()
    {
        foreach (var c in _allClients) c.IsSelected = false;
        Stage = PublishStage.Form;
        LoadClients();
    }
}
