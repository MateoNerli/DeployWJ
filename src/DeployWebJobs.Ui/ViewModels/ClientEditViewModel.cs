using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;
using DeployWebJobs.Ui.Mvvm;

namespace DeployWebJobs.Ui.ViewModels;

/// <summary>Formulario de alta / edición de un cliente (datos en config.json, contraseña en el Administrador de credenciales).</summary>
public sealed class ClientEditViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ICredentialStore _credentials;
    private readonly ClientConfig? _existing;
    private readonly bool _hasStoredPassword;

    private string _name = "";
    private string _siteName = "";
    private string _scmHost = "";
    private string _userName = "";
    private string _password = "";
    private bool _enabled = true;
    private string _errorText = "";

    public ClientEditViewModel(ConfigService config, ICredentialStore credentials, ClientConfig? existing)
    {
        _config = config;
        _credentials = credentials;
        _existing = existing;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));

        if (existing is not null)
        {
            _name = existing.Name;
            _siteName = existing.SiteName;
            _scmHost = existing.ScmHost ?? "";
            _enabled = existing.Enabled;
            var credential = credentials.Get(existing.Id);
            _userName = credential?.UserName ?? "";
            _hasStoredPassword = credential is not null;
        }
    }

    /// <summary>La ventana se cierra cuando esto se dispara (true = se guardó).</summary>
    public event Action<bool>? CloseRequested;

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool IsEdit => _existing is not null;
    public string Title => IsEdit ? "Editar cliente" : "Agregar cliente";
    public string PasswordHint => IsEdit && _hasStoredPassword
        ? "Dejá vacío para mantener la contraseña actual"
        : "Contraseña de publicación";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string SiteName { get => _siteName; set => SetProperty(ref _siteName, value); }
    public string ScmHost { get => _scmHost; set => SetProperty(ref _scmHost, value); }
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorText);

    private void Save()
    {
        ErrorText = "";

        var name = Name.Trim();
        var site = SiteName.Trim();
        var scmHost = ScmHost.Trim();
        var user = UserName.Trim();

        if (user.Length == 0 && site.Length > 0) user = "$" + site;

        // Validación previa (con un id de relleno si es un cliente nuevo: el id real lo arma ConfigService).
        var probe = new ClientConfig
        {
            Id = _existing?.Id ?? "nuevo",
            Name = name,
            SiteName = site,
            ScmHost = scmHost.Length == 0 ? null : scmHost
        };

        var errors = probe.Validate().ToList();
        if (user.Length == 0) errors.Add("Falta el usuario de publicación.");
        if (Password.Length == 0 && !_hasStoredPassword) errors.Add("Falta la contraseña de publicación.");

        if (errors.Count > 0)
        {
            ErrorText = string.Join("\n", errors);
            return;
        }

        try
        {
            var client = new ClientConfig
            {
                Id = _existing?.Id ?? "",
                Name = name,
                SiteName = site,
                ScmHost = probe.ScmHost,
                FtpHost = _existing?.FtpHost,
                Method = _existing?.Method ?? DeployMethod.ZipDeploy,
                Notes = _existing?.Notes,
                Enabled = Enabled
            };

            var saved = _config.AddOrUpdateClient(client);

            var password = Password.Length > 0 ? Password : _credentials.Get(saved.Id)?.Password ?? "";
            if (password.Length == 0)
            {
                ErrorText = "Falta la contraseña de publicación.";
                return;
            }

            _credentials.Set(saved.Id, new ClientCredential(user, password));
            Password = "";
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is ArgumentException or ConfigException or IOException)
        {
            ErrorText = ex.Message;
        }
    }
}
