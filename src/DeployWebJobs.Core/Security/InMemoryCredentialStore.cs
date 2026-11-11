namespace DeployWebJobs.Core.Security;

/// <summary>Almacén en memoria. Se usa en tests (y como reemplazo fuera de Windows). No persiste nada.</summary>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, ClientCredential> _items = new(StringComparer.Ordinal);

    public ClientCredential? Get(string clientId) => _items.TryGetValue(clientId, out var c) ? c : null;
    public void Set(string clientId, ClientCredential credential) => _items[clientId] = credential;
    public void Delete(string clientId) => _items.Remove(clientId);
}
