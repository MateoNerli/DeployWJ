namespace DeployWebJobs.Core.Security;

/// <summary>Credenciales de publicación de un App Service. ToString() oculta la contraseña.</summary>
public sealed record ClientCredential(string UserName, string Password)
{
    // Evita que la contraseña aparezca por accidente en logs o en la consola.
    public override string ToString() => $"ClientCredential {{ UserName = {UserName}, Password = ***** }}";
}
