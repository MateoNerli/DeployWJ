using System.Text.RegularExpressions;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Import;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>Alta, edición y baja de clientes, e importación única desde el Excel anterior.</summary>
internal sealed class ClientsScreen
{
    private const string BackLabel = "« Volver";
    private static readonly Regex SiteRegex = new(@"^[A-Za-z0-9][A-Za-z0-9-]*$");

    private readonly ConfigService _config;
    private readonly ICredentialStore _credentials;

    public ClientsScreen(ConfigService config, ICredentialStore credentials)
    {
        _config = config;
        _credentials = credentials;
    }

    public void Run()
    {
        const string add = "Agregar un cliente";
        const string edit = "Editar un cliente";
        const string remove = "Eliminar un cliente";
        const string import = "Importar desde el Excel anterior";
        const string back = "Volver al menú";

        while (true)
        {
            Ui.Header("Clientes");
            ShowTable();

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("¿Qué querés hacer?")
                .AddChoices(add, edit, remove, import, back));

            try
            {
                switch (choice)
                {
                    case add: Add(); break;
                    case edit: Edit(); break;
                    case remove: Remove(); break;
                    case import: Import(); break;
                    default: return;
                }
            }
            catch (ArgumentException ex)
            {
                Ui.Error(ex.Message);
                Ui.Pause();
            }
        }
    }

    private void ShowTable()
    {
        if (_config.Clients.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No hay clientes cargados.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn("Cliente")
            .AddColumn("App Service")
            .AddColumn("Estado")
            .AddColumn("Credenciales")
            .AddColumn("Método");

        foreach (var c in _config.Clients)
        {
            table.AddRow(
                Markup.Escape(c.Name),
                Markup.Escape(c.SiteName),
                c.Enabled ? "[green]habilitado[/]" : "[grey]deshabilitado[/]",
                _credentials.Get(c.Id) is null ? "[red]faltan[/]" : "[green]guardadas[/]",
                c.Method.ToString());
        }
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    // ------------------------------------------------------------------ alta

    private void Add()
    {
        Ui.Header("Nuevo cliente");
        AnsiConsole.MarkupLine("[grey]Los datos de publicación están en el Azure Portal: App Service > Centro de implementación >[/]");
        AnsiConsole.MarkupLine("[grey]Credenciales locales / de FTPS (el usuario empieza con $).[/]");
        AnsiConsole.WriteLine();

        var name = AskRequired("Nombre del cliente:");
        var site = AnsiConsole.Prompt(new TextPrompt<string>("Nombre del App Service en Azure (ej: miempresa-sgr):")
            .Validate(v => SiteRegex.IsMatch(v.Trim())
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Solo letras, números y guiones[/]")));
        site = site.Trim();

        var scmHost = AnsiConsole.Prompt(new TextPrompt<string>("Host SCM (Enter = el estándar del sitio):").AllowEmpty()).Trim();
        var user = AnsiConsole.Prompt(new TextPrompt<string>("Usuario de publicación:").DefaultValue("$" + site)).Trim();
        var password = AnsiConsole.Prompt(new TextPrompt<string>("Contraseña de publicación:").Secret());

        var client = _config.AddOrUpdateClient(new ClientConfig
        {
            Name = name,
            SiteName = site,
            ScmHost = scmHost.Length == 0 ? null : scmHost
        });
        _credentials.Set(client.Id, new ClientCredential(user, password));

        Ui.Ok($"Cliente '{client.Name}' guardado. La contraseña quedó en el Administrador de credenciales de Windows.");
        Ui.Pause();
    }

    // ------------------------------------------------------------------ edición

    private void Edit()
    {
        var client = PickClient("¿Qué cliente querés editar?");
        if (client is null) return;

        const string rename = "Cambiar el nombre";
        const string site = "Cambiar el App Service";
        const string host = "Cambiar el host SCM";
        const string toggle = "Habilitar / deshabilitar";
        const string creds = "Cargar o cambiar las credenciales";
        const string back = "Volver";

        while (true)
        {
            Ui.Header($"Editar · {client.Name}");

            var table = new Table().Border(TableBorder.None).HideHeaders().AddColumn("").AddColumn("");
            table.AddRow("[grey]Nombre[/]", Markup.Escape(client.Name));
            table.AddRow("[grey]App Service[/]", Markup.Escape(client.SiteName));
            table.AddRow("[grey]Host SCM[/]", Markup.Escape(client.EffectiveScmHost));
            table.AddRow("[grey]Estado[/]", client.Enabled ? "[green]habilitado[/]" : "[grey]deshabilitado[/]");
            table.AddRow("[grey]Credenciales[/]", _credentials.Get(client.Id) is null ? "[red]faltan[/]" : "[green]guardadas[/]");
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("¿Qué querés cambiar?")
                .AddChoices(rename, site, host, toggle, creds, back));

            try
            {
                switch (choice)
                {
                    case rename:
                    {
                        var copy = Clone(client);
                        copy.Name = AskRequired("Nuevo nombre:", client.Name);
                        client = _config.AddOrUpdateClient(copy);
                        break;
                    }
                    case site:
                    {
                        var copy = Clone(client);
                        copy.SiteName = AnsiConsole.Prompt(new TextPrompt<string>("Nombre del App Service:")
                            .DefaultValue(client.SiteName)
                            .Validate(v => SiteRegex.IsMatch(v.Trim())
                                ? ValidationResult.Success()
                                : ValidationResult.Error("[red]Solo letras, números y guiones[/]"))).Trim();
                        client = _config.AddOrUpdateClient(copy);
                        break;
                    }
                    case host:
                    {
                        var copy = Clone(client);
                        var value = AnsiConsole.Prompt(new TextPrompt<string>("Host SCM (Enter vacío = el estándar del sitio):").AllowEmpty()).Trim();
                        copy.ScmHost = value.Length == 0 ? null : value;
                        client = _config.AddOrUpdateClient(copy);
                        break;
                    }
                    case toggle:
                    {
                        var copy = Clone(client);
                        copy.Enabled = !client.Enabled;
                        client = _config.AddOrUpdateClient(copy);
                        break;
                    }
                    case creds:
                        EditCredentials(client);
                        break;
                    default:
                        return;
                }
            }
            catch (ArgumentException ex)
            {
                Ui.Error(ex.Message);
                Ui.Pause();
            }
        }
    }

    private void EditCredentials(ClientConfig client)
    {
        var current = _credentials.Get(client.Id);

        var user = AnsiConsole.Prompt(new TextPrompt<string>("Usuario de publicación:")
            .DefaultValue(current?.UserName ?? "$" + client.SiteName)).Trim();

        var passwordPrompt = new TextPrompt<string>(current is null
                ? "Contraseña de publicación:"
                : "Contraseña nueva (Enter = dejar la actual):")
            .Secret();
        if (current is not null) passwordPrompt.AllowEmpty();

        var password = AnsiConsole.Prompt(passwordPrompt);
        if (password.Length == 0) password = current?.Password ?? "";

        if (password.Length == 0)
        {
            Ui.Error("La contraseña no puede quedar vacía.");
            Ui.Pause();
            return;
        }

        _credentials.Set(client.Id, new ClientCredential(user, password));
        Ui.Ok("Credenciales guardadas.");
        Ui.Pause();
    }

    // ------------------------------------------------------------------ baja

    private void Remove()
    {
        var client = PickClient("¿Qué cliente querés eliminar?");
        if (client is null) return;

        if (!AnsiConsole.Confirm($"[red]¿Eliminar a '{Markup.Escape(client.Name)}' y borrar su contraseña guardada?[/] (no toca nada en Azure)", false))
            return;

        _config.RemoveClient(client.Id);
        Ui.Ok("Cliente eliminado.");
        Ui.Pause();
    }

    // ------------------------------------------------------------------ importar

    private void Import()
    {
        Ui.Header("Importar desde el Excel anterior");
        AnsiConsole.MarkupLine("[grey]Lee el ConexionesFTP.xlsx de la versión anterior (columnas: Cliente, Publicar, Host FTP, Usuario, Contraseña).[/]");
        AnsiConsole.MarkupLine("[grey]Los clientes van a config.json SIN contraseñas; las contraseñas pasan al Administrador de credenciales.[/]");
        AnsiConsole.MarkupLine("[grey]Dejá vacío para volver.[/]");
        AnsiConsole.WriteLine();

        var prompt = new TextPrompt<string>("Ruta del Excel:").AllowEmpty();
        var found = FindExcel();
        if (found is not null) prompt.DefaultValue(found);

        var path = AnsiConsole.Prompt(prompt).Trim().Trim('"').Trim();
        if (path.Length == 0) return;
        if (!File.Exists(path))
        {
            Ui.Error("No existe ese archivo.");
            Ui.Pause();
            return;
        }

        var overwrite = AnsiConsole.Confirm("¿Sobrescribir los clientes que ya existen?", false);
        var result = ExcelClientImporter.Import(path, _config, _credentials, overwrite);

        AnsiConsole.WriteLine();
        Ui.Ok($"Importados: {result.Imported.Count}");
        if (result.Skipped.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]Omitidos ({result.Skipped.Count}):[/] {Markup.Escape(string.Join(", ", result.Skipped))}");
        foreach (var error in result.Errors)
            Ui.Error(error);

        if (result.Imported.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Importante:[/] las contraseñas ya están guardadas de forma segura. El Excel las tiene en texto plano,");
            AnsiConsole.MarkupLine("[yellow]así que conviene borrarlo (y no volver a subirlo a git) cuando verifiques que todo funciona.[/]");
        }
        Ui.Pause();
    }

    /// <summary>Busca ConexionesFTP.xlsx junto al .exe y en las carpetas superiores de la actual.</summary>
    private static string? FindExcel()
    {
        const string fileName = "ConexionesFTP.xlsx";

        var next = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(next)) return next;

        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ------------------------------------------------------------------ helpers

    private ClientConfig? PickClient(string title)
    {
        if (_config.Clients.Count == 0)
        {
            Ui.Warn("No hay clientes cargados.");
            Ui.Pause();
            return null;
        }

        var byLabel = new Dictionary<string, ClientConfig>();
        foreach (var c in _config.Clients)
        {
            var label = $"{Markup.Escape(c.Name)} [grey]({Markup.Escape(c.SiteName)})[/]";
            if (byLabel.ContainsKey(label)) label += $" [grey]#{Markup.Escape(c.Id)}[/]";
            byLabel[label] = c;
        }

        var picked = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title(title)
            .PageSize(15)
            .EnableSearch()
            .AddChoices(byLabel.Keys.Append(BackLabel)));

        return picked == BackLabel ? null : byLabel[picked];
    }

    private static string AskRequired(string question, string? defaultValue = null)
    {
        var prompt = new TextPrompt<string>(question)
            .Validate(v => string.IsNullOrWhiteSpace(v)
                ? ValidationResult.Error("[red]No puede quedar vacío[/]")
                : ValidationResult.Success());
        if (defaultValue is not null) prompt.DefaultValue(defaultValue);
        return AnsiConsole.Prompt(prompt).Trim();
    }

    private static ClientConfig Clone(ClientConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Enabled = c.Enabled,
        SiteName = c.SiteName,
        ScmHost = c.ScmHost,
        FtpHost = c.FtpHost,
        Method = c.Method,
        Notes = c.Notes
    };
}
