using System.Collections.ObjectModel;
using System.Windows.Input;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Ui.Mvvm;
using DeployWebJobs.Ui.Services;

namespace DeployWebJobs.Ui.ViewModels;

public sealed class WebJobRow
{
    public WebJobRow(WebJobDef job, ICommand delete)
    {
        Name = job.Name;
        TypeText = job.Type.Describe();
        LastFolder = string.IsNullOrWhiteSpace(job.LastFolder) ? "—" : job.LastFolder!;
        LastDeployText = job.LastDeployedUtc is { } utc
            ? utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : "Nunca publicado desde acá";
        DeleteCommand = delete;
    }

    public string Name { get; }
    public string TypeText { get; }
    public string LastFolder { get; }
    public string LastDeployText { get; }
    public ICommand DeleteCommand { get; }
}

/// <summary>
/// WebJobs que la herramienta ya conoce (se agregan solos al publicar). Acá se pueden quitar de la lista;
/// eso NO borra nada en Azure.
/// </summary>
public sealed class WebJobsViewModel : ObservableObject, IActivatable
{
    private readonly ConfigService _config;
    private readonly IDialogService _dialogs;

    public WebJobsViewModel(ConfigService config, IDialogService dialogs)
    {
        _config = config;
        _dialogs = dialogs;
        Refresh();
    }

    public ObservableCollection<WebJobRow> Rows { get; } = new();
    public bool HasNone => Rows.Count == 0;

    public void OnActivated() => Refresh();

    private void Refresh()
    {
        Rows.Clear();
        foreach (var job in _config.WebJobs.ToList())
        {
            var j = job;
            Rows.Add(new WebJobRow(j, new AsyncRelayCommand(() => DeleteAsync(j))));
        }

        OnPropertyChanged(nameof(HasNone));
    }

    private async Task DeleteAsync(WebJobDef job)
    {
        var ok = await _dialogs.ConfirmAsync(
            "Quitar WebJob de la lista",
            $"¿Quitar '{job.Name}' de la lista de WebJobs conocidos?\n\nNo se borra nada en Azure. Si lo volvés a publicar, vuelve a aparecer solo.",
            "Quitar", "Cancelar");
        if (!ok) return;

        _config.RemoveWebJob(job.Name);
        Refresh();
    }
}
