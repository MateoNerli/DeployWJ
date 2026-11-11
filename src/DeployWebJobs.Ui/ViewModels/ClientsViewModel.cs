using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Import;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;
using DeployWebJobs.Ui.Mvvm;
using DeployWebJobs.Ui.Services;

namespace DeployWebJobs.Ui.ViewModels;

/// <summary>Una fila de la lista de clientes (con sus propios botones Editar / Quitar).</summary>
public sealed class ClientRow
{
    public ClientRow(ClientConfig client, bool hasCredentials, ICommand edit, ICommand delete)
    {
        Id = client.Id;
        Name = client.Name;
        Host = client.EffectiveScmHost;
        StatusText = client.Enabled ? "Habilitado" : "Deshabilitado";
        StatusBrush = client.Enabled ? Palette.Ok : Palette.Muted;
        CredentialsText = hasCredentials ? "Credenciales guardadas" : "Faltan credenciales";
        CredentialsBrush = hasCredentials ? Palette.Ok : Palette.Error;
        EditCommand = edit;
        DeleteCommand = delete;
    }

    public string Id { get; }
    public string Name { get; }
    public string Host { get; }
    public string StatusText { get; }
    public IBrush StatusBrush { get; }
    public string CredentialsText { get; }
    public IBrush CredentialsBrush { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
}

/// <summary>Alta, edición, baja e importación de clientes.</summary>
public sealed class ClientsViewModel : ObservableObject, IActivatable
{
    private readonly ConfigService _config;
    private readonly ICredentialStore _credentials;
    private readonly IDialogService _dialogs;

    public ClientsViewModel(ConfigService config, ICredentialStore credentials, IDialogService dialogs)
    {
        _config = config;
        _credentials = credentials;
        _dialogs = dialogs;

        AddCommand = new AsyncRelayCommand(AddAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        ExportPackageCommand = new AsyncRelayCommand(ExportPackageAsync);
        ImportPackageCommand = new AsyncRelayCommand(() => ImportPackageAsync(null));
        Refresh();
    }

    public ObservableCollection<ClientRow> Rows { get; } = new();

    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand ExportPackageCommand { get; }
    public AsyncRelayCommand ImportPackageCommand { get; }

    public bool HasNoClients => Rows.Count == 0;
    public string CountText => Rows.Count == 1 ? "1 cliente" : $"{Rows.Count} clientes";

    public void OnActivated() => Refresh();

    private void Refresh()
    {
        Rows.Clear();
        foreach (var client in _config.Clients.ToList())
        {
            var c = client;
            Rows.Add(new ClientRow(
                c,
                _credentials.Get(c.Id) is not null,
                new AsyncRelayCommand(() => EditAsync(c)),
                new AsyncRelayCommand(() => DeleteAsync(c))));
        }

        OnPropertyChanged(nameof(HasNoClients));
        OnPropertyChanged(nameof(CountText));
    }

    private async Task AddAsync()
    {
        var vm = new ClientEditViewModel(_config, _credentials, null);
        if (await _dialogs.EditClientAsync(vm)) Refresh();
    }

    private async Task EditAsync(ClientConfig client)
    {
        var vm = new ClientEditViewModel(_config, _credentials, client);
        if (await _dialogs.EditClientAsync(vm)) Refresh();
    }

    private async Task DeleteAsync(ClientConfig client)
    {
        var ok = await _dialogs.ConfirmAsync(
            "Quitar cliente",
            $"¿Quitar a '{client.Name}' de la lista?\n\nSe borran también sus credenciales guardadas en este equipo. No se toca nada en Azure.",
            "Quitar", "Cancelar");
        if (!ok) return;

        _config.RemoveClient(client.Id);
        Refresh();
    }

    // ------------------------------------------------------------------ paquete para compañeros

    /// <summary>
    /// Al abrir la app por primera vez: si no hay clientes y junto al programa hay un archivo .dwjclients,
    /// se ofrece cargarlo (así alcanza con pasarle al compañero el .exe y ese archivo).
    /// </summary>
    public async Task OfferStartupImportAsync()
    {
        if (_config.Clients.Count > 0) return;

        string? file;
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(dir)) return;
            file = Directory.EnumerateFiles(dir, "*" + ClientPackage.Extension).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (file is null) return;

        var ok = await _dialogs.ConfirmAsync(
            "Clientes listos para cargar",
            $"Encontré el archivo '{Path.GetFileName(file)}' junto al programa, con la lista de clientes.\n\n¿Querés cargarla ahora?",
            "Cargar clientes", "Ahora no");
        if (ok) await ImportPackageAsync(file);
    }

    private async Task ExportPackageAsync()
    {
        if (_config.Clients.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Exportar clientes", "Todavía no hay clientes para exportar.");
            return;
        }

        var withPasswords = await _dialogs.ConfirmAsync(
            "Exportar clientes para un compañero",
            "El archivo lleva la lista de clientes y los WebJobs conocidos.\n\n" +
            "• Con contraseñas: quedan CIFRADAS con una contraseña que elegís ahora; tu compañero la necesita para abrirlas.\n" +
            "• Sin contraseñas: tu compañero tiene que cargarlas a mano en cada cliente.",
            "Con contraseñas (cifradas)", "Solo los clientes");

        string? passphrase = null;
        if (withPasswords)
        {
            passphrase = await _dialogs.PromptSecretAsync(
                "Contraseña del paquete",
                "Elegí una contraseña para proteger el paquete (mínimo 8 caracteres). Pasásela a tu compañero por otro medio, no junto con el archivo.");
            if (passphrase is null) return;

            if (passphrase.Length < 8)
            {
                await _dialogs.ShowMessageAsync("Contraseña muy corta", "Tiene que tener al menos 8 caracteres.");
                return;
            }

            var again = await _dialogs.PromptSecretAsync("Contraseña del paquete", "Repetí la contraseña para confirmar.");
            if (again is null) return;

            if (!string.Equals(passphrase, again, StringComparison.Ordinal))
            {
                await _dialogs.ShowMessageAsync("No coinciden", "Las dos contraseñas no son iguales. No se exportó nada.");
                return;
            }
        }

        var path = await _dialogs.PickSaveFileAsync(
            "Guardar paquete de clientes", "clientes-deploywebjobs" + ClientPackage.Extension,
            "Paquete de clientes", "*" + ClientPackage.Extension, ClientPackage.Extension.TrimStart('.'));
        if (path is null) return;

        var pass = passphrase;
        var count = await Task.Run(() => ClientPackage.Export(path, _config, _credentials, pass));

        await _dialogs.ShowMessageAsync(
            "Paquete creado",
            $"Se exportaron {count} cliente(s) a:\n{path}\n\n" +
            "Para que tu compañero los tenga cargados: pasale el programa y este archivo (en la misma carpeta, o con Clientes → Importar paquete)." +
            (pass is null ? "" : "\n\nNo le mandes la contraseña del paquete junto con el archivo."));
    }

    private async Task ImportPackageAsync(string? path)
    {
        path ??= await _dialogs.PickOpenFileAsync(
            "Elegí el paquete de clientes", "Paquete de clientes", "*" + ClientPackage.Extension);
        if (path is null) return;

        string? passphrase = null;
        try
        {
            if (ClientPackage.HasCredentials(path))
            {
                passphrase = await _dialogs.PromptSecretAsync(
                    "Contraseña del paquete",
                    "Este paquete trae las contraseñas de los clientes protegidas. Escribí la contraseña del paquete (te la pasó quien lo exportó).");
                if (passphrase is null) return;
            }
        }
        catch (ConfigException ex)
        {
            await _dialogs.ShowMessageAsync("No se pudo abrir el paquete", ex.Message);
            return;
        }

        var overwrite = _config.Clients.Count > 0 && await _dialogs.ConfirmAsync(
            "Importar paquete",
            "Si algún cliente del paquete ya existe en tu lista, ¿qué hacemos?\n\n" +
            "• Sobrescribir: se reemplazan sus datos y credenciales.\n" +
            "• Omitir los existentes: solo se agregan los nuevos.",
            "Sobrescribir", "Omitir los existentes");

        ImportResult result;
        try
        {
            var pass = passphrase;
            result = await Task.Run(() => ClientPackage.Import(path, _config, _credentials, pass, overwrite));
        }
        catch (ConfigException ex)
        {
            await _dialogs.ShowMessageAsync("No se pudo importar", ex.Message);
            return;
        }

        Refresh();

        var lines = new List<string> { $"Importados: {result.Imported.Count}" };
        if (result.Skipped.Count > 0) lines.Add($"Omitidos: {result.Skipped.Count}");
        if (result.Errors.Count > 0)
        {
            lines.Add($"Con errores: {result.Errors.Count}");
            lines.AddRange(result.Errors.Take(8).Select(e => "  • " + e));
        }

        if (passphrase is null)
            lines.Add("\nEl paquete no traía contraseñas: cargalas en Clientes → Editar (usuario y contraseña de publicación de cada sitio).");

        await _dialogs.ShowMessageAsync("Importación terminada", string.Join("\n", lines));
    }

    private async Task ImportAsync()
    {
        var path = await _dialogs.PickExcelAsync();
        if (path is null) return;

        var overwrite = await _dialogs.ConfirmAsync(
            "Importar clientes desde Excel",
            "Si algún cliente del Excel ya existe en la lista, ¿qué hacemos?\n\n" +
            "• Sobrescribir: se reemplazan sus datos y credenciales.\n" +
            "• Omitir los existentes: solo se agregan los nuevos.",
            "Sobrescribir", "Omitir los existentes");

        ImportResult result;
        try
        {
            result = await Task.Run(() => ExcelClientImporter.Import(path, _config, _credentials, overwrite));
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("No se pudo importar", ex.Message);
            return;
        }

        Refresh();

        var lines = new List<string> { $"Importados: {result.Imported.Count}" };
        if (result.Skipped.Count > 0) lines.Add($"Omitidos: {result.Skipped.Count}");
        if (result.Errors.Count > 0)
        {
            lines.Add($"Con errores: {result.Errors.Count}");
            lines.AddRange(result.Errors.Take(8).Select(e => "  • " + e));
            if (result.Errors.Count > 8) lines.Add($"  … y {result.Errors.Count - 8} más");
        }

        lines.Add("");
        lines.Add("Las contraseñas ya están en el Administrador de credenciales de Windows. " +
                  "Borrá el Excel con contraseñas (y sacalo de git) cuando verifiques que todo funciona.");

        await _dialogs.ShowMessageAsync("Importación terminada", string.Join("\n", lines));
    }
}
