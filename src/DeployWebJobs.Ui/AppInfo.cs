using System.Reflection;

namespace DeployWebJobs.Ui;

internal static class AppInfo
{
    public static string Version
    {
        get
        {
            var version = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = version.IndexOf('+');
            return plus > 0 ? version[..plus] : version;
        }
    }
}
