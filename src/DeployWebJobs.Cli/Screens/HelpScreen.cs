using DeployWebJobs.Core.Configuration;
using Spectre.Console;

namespace DeployWebJobs.Cli.Screens;

/// <summary>Opción "¿Cómo funciona?": explica el proceso completo, de la carpeta publicada al resultado.</summary>
internal sealed class HelpScreen
{
    private readonly ConfigService _config;

    public HelpScreen(ConfigService config) => _config = config;

    public void Show()
    {
        Ui.Header("¿Cómo funciona?");
        var settings = _config.Config.Settings;

        Section("1. La carpeta de publicación (lo que se sube)",
            "Esta herramienta [bold]no compila[/]: sube lo que ya compilaste. Antes de usarla generás la carpeta de publicación del WebJob:\n" +
            "  • Visual Studio: clic derecho en el proyecto > Publicar > Carpeta.\n" +
            "  • Línea de comandos: [blue]dotnet publish -c Release[/] (proyectos de estilo SDK).\n" +
            "El resultado queda, por ejemplo, en [blue]...\\bin\\Release\\net472\\publish[/]. Ahí están el .exe, el .exe.config, " +
            "las DLL de las que depende, el appsettings.json y las subcarpetas que haya: todo lo que el WebJob necesita para correr en Azure.");

        var patterns = settings.ExcludePatterns.Count == 0
            ? "  (ninguno)"
            : string.Join("\n", settings.ExcludePatterns.Select(p => "  • " + Markup.Escape(p)));
        Section("2. Qué se sube y qué no",
            "Se sube [bold]todo[/] lo de la carpeta (incluidas subcarpetas), salvo lo que coincida con estos patrones (se editan en Ajustes):\n" +
            patterns + "\n" +
            "Los archivos .pdb [bold]sí se suben[/], como hacía la versión anterior: sirven para ver números de línea en los errores de producción. " +
            "Si no los querés, agregá *.pdb en Ajustes.\n" +
            "No se siguen enlaces simbólicos y ninguna ruta puede salir de la carpeta elegida.");

        Section("3. Qué pasa con cada cliente",
            "[bold]1.[/] Validando configuración: el cliente está habilitado y tiene credenciales.\n" +
            "[bold]2.[/] Preparando paquete: la carpeta se comprime en un ZIP (una sola vez para todos los clientes).\n" +
            "[bold]3.[/] Conectando: conexión HTTPS al sitio (API Kudu/SCM) y prueba de credenciales.\n" +
            "[bold]4.[/] Deteniendo el WebJob (solo los continuos), para liberar el .exe.\n" +
            "[bold]5.[/] Subiendo: el ZIP se descomprime en [blue]/site/wwwroot/app_data/jobs/continuous/NOMBRE[/]. " +
            "Sobrescribe archivos, pero no borra los que ya estaban.\n" +
            "[bold]6.[/] Iniciando el WebJob.\n" +
            "[bold]7.[/] Verificando que el WebJob quedó en ejecución (Running).\n" +
            $"Ante un error de red se reintenta solo (hasta {settings.MaxRetries} veces). Un cliente con error no frena a los demás.");

        Section("4. Simulación y cancelación",
            "[bold]Simulación[/] muestra qué haría con cada cliente sin conectarse ni subir nada: es la forma segura de revisar antes de publicar.\n" +
            "[bold]Ctrl+C[/] durante una publicación cancela de forma ordenada: lo ya subido queda subido y lo pendiente no se toca.");

        Section("5. Seguridad",
            "Las contraseñas se guardan cifradas en el [bold]Administrador de credenciales de Windows[/] " +
            "(Panel de control > Administrador de credenciales > Credenciales de Windows > DeployWebJobs/...). " +
            "No se escriben en el archivo de configuración, ni en pantalla, ni en los logs. Todo viaja por HTTPS.");

        Section("6. Dónde está todo",
            $"Configuración (sin secretos): [blue]{Markup.Escape(_config.FilePath)}[/]\n" +
            $"Logs (un archivo por día): [blue]{Markup.Escape(Path.Combine(ConfigService.DefaultDirectory, "logs"))}[/]");

        AnsiConsole.WriteLine();
        Ui.Pause();
    }

    private static void Section(string title, string markup) =>
        AnsiConsole.Write(new Panel(new Markup(markup))
            .Header($"[bold] {Markup.Escape(title)} [/]")
            .RoundedBorder()
            .Expand());
}
