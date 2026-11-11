using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Core.Import;

/// <summary>
/// Paquete de clientes para pasarle a un compañero (archivo .dwjclients).
/// Lleva los clientes y los WebJobs conocidos. Opcionalmente lleva también las credenciales, pero
/// SIEMPRE cifradas con una contraseña del paquete (AES-256-GCM, clave derivada con PBKDF2):
/// sin esa contraseña no se pueden leer. La contraseña del paquete se comparte por otro medio
/// (nunca junto con el archivo).
/// </summary>
public static class ClientPackage
{
    public const string Extension = ".dwjclients";

    private const string FormatName = "dwj-clients";
    private const int FormatVersion = 1;
    private const int Pbkdf2Iterations = 600_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class PackageFile
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public List<ClientConfig> Clients { get; set; } = new();
        public List<WebJobDef> WebJobs { get; set; } = new();
        public EncryptedBlob? Credentials { get; set; }
    }

    private sealed class EncryptedBlob
    {
        public int Iterations { get; set; }
        public byte[] Salt { get; set; } = Array.Empty<byte>();
        public byte[] Nonce { get; set; } = Array.Empty<byte>();
        public byte[] Tag { get; set; } = Array.Empty<byte>();
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    private sealed class CredentialEntry
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// Arma el archivo. Si <paramref name="packagePassphrase"/> viene vacío NO se incluyen credenciales
    /// (quien lo importe tendrá que cargarlas a mano). Devuelve cuántos clientes se exportaron.
    /// </summary>
    public static int Export(string path, ConfigService config, ICredentialStore credentials, string? packagePassphrase)
    {
        var file = new PackageFile { Format = FormatName, Version = FormatVersion };

        foreach (var c in config.Clients)
        {
            file.Clients.Add(new ClientConfig
            {
                Id = c.Id, Name = c.Name, Enabled = c.Enabled, SiteName = c.SiteName, ScmHost = c.ScmHost,
                FtpHost = c.FtpHost, Method = c.Method, Notes = c.Notes
            });
        }

        // Los WebJobs se comparten sin rutas locales ni fechas (son de cada PC).
        foreach (var w in config.WebJobs)
            file.WebJobs.Add(new WebJobDef { Name = w.Name, Type = w.Type, Notes = w.Notes });

        if (!string.IsNullOrEmpty(packagePassphrase))
        {
            var entries = new Dictionary<string, CredentialEntry>(StringComparer.Ordinal);
            foreach (var c in config.Clients)
            {
                var cred = credentials.Get(c.Id);
                if (cred is not null) entries[c.Id] = new CredentialEntry { UserName = cred.UserName, Password = cred.Password };
            }

            file.Credentials = Encrypt(JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions), packagePassphrase);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
        return file.Clients.Count;
    }

    /// <summary>true si el paquete trae contraseñas cifradas (hay que pedir la contraseña del paquete).</summary>
    public static bool HasCredentials(string path) => Read(path).Credentials is not null;

    /// <summary>
    /// Importa clientes (y credenciales si las trae). Si la contraseña del paquete es incorrecta no se modifica nada.
    /// Los clientes que ya existen se omiten salvo que <paramref name="overwrite"/> sea true.
    /// </summary>
    public static ImportResult Import(string path, ConfigService config, ICredentialStore credentials,
        string? packagePassphrase, bool overwrite)
    {
        var file = Read(path);

        Dictionary<string, CredentialEntry> entries = new(StringComparer.Ordinal);
        if (file.Credentials is not null)
        {
            if (string.IsNullOrEmpty(packagePassphrase))
                throw new ConfigException("Este paquete trae contraseñas protegidas: hace falta la contraseña del paquete.");

            byte[] plain;
            try
            {
                plain = Decrypt(file.Credentials, packagePassphrase);
            }
            catch (CryptographicException)
            {
                throw new ConfigException("La contraseña del paquete no es correcta.");
            }

            try
            {
                entries = JsonSerializer.Deserialize<Dictionary<string, CredentialEntry>>(plain, JsonOptions)
                          ?? new Dictionary<string, CredentialEntry>();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        var imported = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        foreach (var client in file.Clients)
        {
            try
            {
                if (config.FindClient(client.Id) is not null && !overwrite)
                {
                    skipped.Add($"{client.Name} (ya existía)");
                    continue;
                }

                config.AddOrUpdateClient(client);

                if (entries.TryGetValue(client.Id, out var entry) &&
                    !string.IsNullOrEmpty(entry.UserName) && !string.IsNullOrEmpty(entry.Password))
                {
                    credentials.Set(client.Id, new ClientCredential(entry.UserName, entry.Password));
                }

                imported.Add(client.Name);
            }
            catch (Exception ex)
            {
                // Nunca se incluyen contraseñas en los mensajes.
                errors.Add($"{client.Name}: {ex.Message}");
            }
        }

        foreach (var job in file.WebJobs)
        {
            if (!WebJobDef.IsValidName(job.Name) || config.FindWebJob(job.Name) is not null) continue;
            config.AddOrUpdateWebJob(new WebJobDef { Name = job.Name, Type = job.Type, Notes = job.Notes });
        }

        return new ImportResult(imported, skipped, errors);
    }

    // ------------------------------------------------------------------ internos

    private static PackageFile Read(string path)
    {
        PackageFile? file;
        try
        {
            file = JsonSerializer.Deserialize<PackageFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException("El archivo no es un paquete de clientes válido.", ex);
        }

        if (file is null || file.Format != FormatName)
            throw new ConfigException("El archivo no es un paquete de clientes de DeployWebJobs.");
        if (file.Version > FormatVersion)
            throw new ConfigException("El paquete es de una versión más nueva de DeployWebJobs: actualizá el programa.");

        return file;
    }

    private static EncryptedBlob Encrypt(byte[] plain, string passphrase)
    {
        var blob = new EncryptedBlob
        {
            Iterations = Pbkdf2Iterations,
            Salt = RandomNumberGenerator.GetBytes(SaltSize),
            Nonce = RandomNumberGenerator.GetBytes(NonceSize),
            Tag = new byte[TagSize],
            Data = new byte[plain.Length]
        };

        var key = DeriveKey(passphrase, blob.Salt, blob.Iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(blob.Nonce, plain, blob.Data, blob.Tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        return blob;
    }

    private static byte[] Decrypt(EncryptedBlob blob, string passphrase)
    {
        if (blob.Iterations < 100_000 || blob.Iterations > 5_000_000 ||
            blob.Salt.Length != SaltSize || blob.Nonce.Length != NonceSize || blob.Tag.Length != TagSize)
            throw new CryptographicException("Paquete con formato inválido.");

        var key = DeriveKey(passphrase, blob.Salt, blob.Iterations);
        try
        {
            var plain = new byte[blob.Data.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.Nonce, blob.Data, blob.Tag, plain); // lanza CryptographicException si la clave es incorrecta
            return plain;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
}
