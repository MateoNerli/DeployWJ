using DeployWebJobs.Ui.Mvvm;

namespace DeployWebJobs.Ui.ViewModels;

/// <summary>Un ítem del menú de la izquierda y la pantalla que muestra.</summary>
public sealed class NavItem
{
    public NavItem(string title, ObservableObject page)
    {
        Title = title;
        Page = page;
    }

    public string Title { get; }
    public ObservableObject Page { get; }
}

/// <summary>Ventana principal: menú lateral + la pantalla elegida.</summary>
public sealed class MainViewModel : ObservableObject
{
    private NavItem _selectedNav;

    public MainViewModel(
        PublishViewModel publish,
        ClientsViewModel clients,
        WebJobsViewModel webJobs,
        SettingsViewModel settings,
        HelpViewModel help)
    {
        NavItems = new[]
        {
            new NavItem("Publicar", publish),
            new NavItem("Clientes", clients),
            new NavItem("WebJobs", webJobs),
            new NavItem("Ajustes", settings),
            new NavItem("¿Cómo funciona?", help)
        };
        _selectedNav = NavItems[0];
    }

    public IReadOnlyList<NavItem> NavItems { get; }

    public NavItem SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null) return;
            if (!SetProperty(ref _selectedNav, value)) return;
            OnPropertyChanged(nameof(Current));
            (value.Page as IActivatable)?.OnActivated();
        }
    }

    public ObservableObject Current => _selectedNav.Page;

    public string WindowTitle => $"DeployWebJobs {AppInfo.Version}";
    public string VersionText => $"Versión {AppInfo.Version}";
}
