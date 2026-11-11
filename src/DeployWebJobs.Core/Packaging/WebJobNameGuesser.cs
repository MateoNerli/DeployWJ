using DeployWebJobs.Core.Models;

namespace DeployWebJobs.Core.Packaging;

/// <summary>
/// Adivina el nombre del WebJob a partir de la carpeta de publicación. Las carpetas de publicación
/// suelen ser ...\NombreDelProyecto\bin\Release\net472\publish, así que el nombre es la carpeta
/// que está justo antes de "bin".
/// </summary>
public static class WebJobNameGuesser
{
    public static string? Guess(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;

        var segments = folder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 1; i--)
        {
            if (segments[i].Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = segments[i - 1];
                return WebJobDef.IsValidName(candidate) ? candidate : null;
            }
        }
        return null;
    }
}
