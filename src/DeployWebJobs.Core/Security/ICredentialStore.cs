namespace DeployWebJobs.Core.Security;

/// <summary>Guarda y recupera credenciales por id de cliente, siempre cifradas por el sistema.</summary>
public interface ICredentialStore
{
    ClientCredential? Get(string clientId);
    void Set(string clientId, ClientCredential credential);

    /// <summary>Borra la credencial. No falla si no existe.</summary>
    void Delete(string clientId);
}
