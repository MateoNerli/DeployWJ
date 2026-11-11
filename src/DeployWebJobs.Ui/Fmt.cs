using Avalonia.Media;

namespace DeployWebJobs.Ui;

/// <summary>Formatos de texto y colores compartidos por los ViewModels.</summary>
internal static class Fmt
{
    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.#} {units[i]}";
    }

    public static string Elapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public static string Short(string text, int max)
    {
        var oneLine = text.ReplaceLineEndings(" ");
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 3)] + "...";
    }
}

internal static class Palette
{
    public static readonly IBrush Ok = SolidColorBrush.Parse("#43A047");
    public static readonly IBrush Error = SolidColorBrush.Parse("#E53935");
    public static readonly IBrush Warn = SolidColorBrush.Parse("#FB8C00");
    public static readonly IBrush Info = SolidColorBrush.Parse("#1E88E5");
    public static readonly IBrush Muted = SolidColorBrush.Parse("#9E9E9E");
}
