using System.Reflection;

namespace DeployWebJobs.Cli;

internal static class AppInfo
{
    /// <summary>Versión visible en la app (sale de la propiedad Version del proyecto).</summary>
    public static string Version
    {
        get
        {
            var version = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = version.IndexOf('+');   // .NET agrega "+hash-de-git"
            return plus > 0 ? version[..plus] : version;
        }
    }
}
