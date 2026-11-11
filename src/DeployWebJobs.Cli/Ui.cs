using Spectre.Console;

namespace DeployWebJobs.Cli;

/// <summary>Pequeños helpers de presentación compartidos por todas las pantallas.</summary>
internal static class Ui
{
    public static void Header(string? subtitle = null)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold deepskyblue1]DeployWebJobs[/] [grey]v{AppInfo.Version}[/]")
            .LeftJustified()
            .RuleStyle("grey"));
        if (subtitle is not null)
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(subtitle)}[/]");
        AnsiConsole.WriteLine();
    }

    public static void Pause(string text = "Presioná una tecla para continuar...")
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(text)}[/]");
        Console.ReadKey(intercept: true);
    }

    public static void Error(string message) =>
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {Markup.Escape(message)}");

    public static void Warn(string message) =>
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");

    public static void Ok(string message) =>
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.#} {units[i]}";
    }

    public static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>Recorta a una línea de como máximo <paramref name="max"/> caracteres.</summary>
    public static string Short(string text, int max)
    {
        var oneLine = text.ReplaceLineEndings(" ");
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 3)] + "...";
    }
}
