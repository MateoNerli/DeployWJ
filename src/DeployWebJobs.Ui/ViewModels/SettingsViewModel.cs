using System.Diagnostics;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Ui.Mvvm;
using DeployWebJobs.Ui.Services;

namespace DeployWebJobs.Ui.ViewModels;

/// <summary>Ajustes generales (se guardan al apretar "Guardar").</summary>
public sealed class SettingsViewModel : ObservableObject, IActivatable
{
    private readonly ConfigService _config;
    private readonly IDialogService _dialogs;

    private decimal? _maxParallelism;
    private decimal? _maxRetries;
    private decimal? _uploadTimeoutSeconds;
    private bool _confirmBeforeDeploy;
    private string _excludeText = "";
    private string _statusText = "";

    public SettingsViewModel(ConfigService config, IDialogService dialogs)
    {
        _config = config;
        _dialogs = dialogs;

        SaveCommand = new RelayCommand(Save);
        RestoreExcludesCommand = new RelayCommand(RestoreExcludes);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        Load();
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand RestoreExcludesCommand { get; }
    public RelayCommand OpenLogsCommand { get; }

    public string ConfigPath => _config.FilePath;
    public string LogsPath => Path.Combine(ConfigService.DefaultDirectory, "logs");

    public decimal? MaxParallelism { get => _maxParallelism; set => SetProperty(ref _maxParallelism, value); }
    public decimal? MaxRetries { get => _maxRetries; set => SetProperty(ref _maxRetries, value); }
    public decimal? UploadTimeoutSeconds { get => _uploadTimeoutSeconds; set => SetProperty(ref _uploadTimeoutSeconds, value); }
    public bool ConfirmBeforeDeploy { get => _confirmBeforeDeploy; set => SetProperty(ref _confirmBeforeDeploy, value); }
    public string ExcludeText { get => _excludeText; set => SetProperty(ref _excludeText, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public void OnActivated() => Load();

    private void Load()
    {
        var s = _config.Config.Settings;
        MaxParallelism = s.MaxParallelism;
        MaxRetries = s.MaxRetries;
        UploadTimeoutSeconds = s.UploadTimeoutSeconds;
        ConfirmBeforeDeploy = s.ConfirmBeforeDeploy;
        ExcludeText = string.Join(Environment.NewLine, s.ExcludePatterns);
        StatusText = "";
    }

    private void Save()
    {
        var s = _config.Config.Settings;
        s.MaxParallelism = Clamp(MaxParallelism, 1, 16, s.MaxParallelism);
        s.MaxRetries = Clamp(MaxRetries, 0, 10, s.MaxRetries);
        s.UploadTimeoutSeconds = Clamp(UploadTimeoutSeconds, 30, 3600, s.UploadTimeoutSeconds);
        s.ConfirmBeforeDeploy = ConfirmBeforeDeploy;
        s.ExcludePatterns = ExcludeText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _config.Save();
        Load();
        StatusText = "Ajustes guardados.";
    }

    private void RestoreExcludes() =>
        ExcludeText = string.Join(Environment.NewLine, new DeploySettings().ExcludePatterns);

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(LogsPath);
            Process.Start(new ProcessStartInfo(LogsPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "No se pudo abrir la carpeta de logs: " + ex.Message;
        }
    }

    private static int Clamp(decimal? value, int min, int max, int fallback) =>
        value is null ? fallback : (int)Math.Clamp(value.Value, min, max);
}
