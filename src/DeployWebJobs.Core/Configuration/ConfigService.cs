using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Core.Configuration;

public sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Lee y guarda config.json (clientes, WebJobs conocidos y ajustes). Sin secretos:
/// las contraseñas van por <see cref="ICredentialStore"/>.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ICredentialStore? _credentials;

    /// <summary>Carpeta por defecto: %AppData%\DeployWebJobs</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeployWebJobs");

    public static string DefaultFilePath => Path.Combine(DefaultDirectory, "config.json");

    public string FilePath { get; }

    public AppConfig Config { get; private set; } = new();

    public ConfigService(string? filePath = null, ICredentialStore? credentials = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
        _credentials = credentials;
        Load();
    }

    /// <summary>Carga el archivo. Si no existe, arranca con una configuración vacía.</summary>
    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            Config = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"El archivo de configuración está dañado: {FilePath}. {ex.Message}", ex);
        }
    }

    /// <summary>Guarda de forma atómica (escribe un temporal y lo reemplaza).</summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Config, JsonOptions), new UTF8Encoding(false));
        File.Move(tmp, FilePath, overwrite: true);
    }

    // ------------------------- Clientes -------------------------

    public IReadOnlyList<ClientConfig> Clients => Config.Clients;

    public ClientConfig? FindClient(string id) => Config.Clients.FirstOrDefault(c => c.Id == id);

    /// <summary>Alta o edición. Si no trae Id, lo genera a partir del nombre.</summary>
    public ClientConfig AddOrUpdateClient(ClientConfig client)
    {
        if (string.IsNullOrWhiteSpace(client.Id))
            client.Id = UniqueClientId(ClientConfig.Slugify(client.Name));

        var errors = client.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));

        var index = Config.Clients.FindIndex(c => c.Id == client.Id);
        if (index >= 0) Config.Clients[index] = client;
        else Config.Clients.Add(client);

        Config.Clients.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        Save();
        return client;
    }

    /// <summary>Quita el cliente y borra también su credencial guardada.</summary>
    public bool RemoveClient(string id)
    {
        var removed = Config.Clients.RemoveAll(c => c.Id == id) > 0;
        if (!removed) return false;

        _credentials?.Delete(id);
        Save();
        return true;
    }

    private string UniqueClientId(string baseId)
    {
        if (string.IsNullOrEmpty(baseId)) baseId = "cliente";
        var id = baseId;
        for (var n = 2; Config.Clients.Any(c => c.Id == id); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    // ------------------------- WebJobs -------------------------

    public IReadOnlyList<WebJobDef> WebJobs => Config.WebJobs;

    public WebJobDef? FindWebJob(string name) =>
        Config.WebJobs.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

    public WebJobDef AddOrUpdateWebJob(WebJobDef job)
    {
        if (!WebJobDef.IsValidName(job.Name))
            throw new ArgumentException("El nombre del WebJob solo puede tener letras, números, punto, guion y guion bajo.");

        var index = Config.WebJobs.FindIndex(w => string.Equals(w.Name, job.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) Config.WebJobs[index] = job;
        else Config.WebJobs.Add(job);

        Config.WebJobs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        Save();
        return job;
    }

    public bool RemoveWebJob(string name)
    {
        var removed = Config.WebJobs.RemoveAll(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Save();
        return removed;
    }

    /// <summary>Recuerda la carpeta y la fecha del último deploy (crea el WebJob si es la primera vez).</summary>
    public void RecordDeploy(string name, WebJobType type, string folder)
    {
        var job = FindWebJob(name) ?? new WebJobDef { Name = name };
        job.Type = type;
        job.LastFolder = folder;
        job.LastDeployedUtc = DateTime.UtcNow;
        AddOrUpdateWebJob(job);
    }
}
