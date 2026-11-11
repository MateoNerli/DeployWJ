using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Ui.Mvvm;

namespace DeployWebJobs.Ui.ViewModels;

public sealed record HelpSection(string Title, string Body);

/// <summary>Pantalla "¿Cómo funciona?": explica el proceso completo, de la carpeta publicada al resultado.</summary>
public sealed class HelpViewModel : ObservableObject, IActivatable
{
    private readonly ConfigService _config;
    private IReadOnlyList<HelpSection> _sections = Array.Empty<HelpSection>();

    public HelpViewModel(ConfigService config)
    {
        _config = config;
        Build();
    }

    public IReadOnlyList<HelpSection> Sections
    {
        get => _sections;
        private set => SetProperty(ref _sections, value);
    }

    public void OnActivated() => Build();

    private void Build()
    {
        var settings = _config.Config.Settings;
        var patterns = settings.ExcludePatterns.Count == 0
            ? "  (ninguno)"
            : string.Join("\n", settings.ExcludePatterns.Select(p => "  • " + p));

        Sections = new[]
        {
            new HelpSection("1. La carpeta de publicación (lo que se sube)",
                "Esta herramienta no compila: sube lo que ya compilaste. Antes de usarla generás la carpeta de publicación del WebJob:\n" +
                "  • Visual Studio: clic derecho en el proyecto > Publicar > Carpeta.\n" +
                "  • Línea de comandos: dotnet publish -c Release (proyectos de estilo SDK).\n" +
                "El resultado queda, por ejemplo, en ...\\bin\\Release\\net472\\publish. Ahí están el .exe, el .exe.config, " +
                "las DLL de las que depende, el appsettings.json y las subcarpetas que haya: todo lo que el WebJob necesita para correr en Azure."),

            new HelpSection("2. Qué se sube y qué no",
                "Se sube todo lo de la carpeta (incluidas subcarpetas), salvo lo que coincida con estos patrones (se editan en Ajustes):\n" +
                patterns + "\n" +
                "Los archivos .pdb sí se suben, como hacía la versión anterior: sirven para ver números de línea en los errores de producción. " +
                "Si no los querés, agregá *.pdb en Ajustes.\n" +
                "No se siguen enlaces simbólicos y ninguna ruta puede salir de la carpeta elegida."),

            new HelpSection("3. Qué pasa con cada cliente",
                "1. Validando configuración: el cliente está habilitado y tiene credenciales.\n" +
                "2. Preparando paquete: la carpeta se comprime en un ZIP (una sola vez para todos los clientes).\n" +
                "3. Conectando: conexión HTTPS al sitio (API Kudu/SCM) y prueba de credenciales.\n" +
                "4. Deteniendo el WebJob (solo los continuos), para liberar el .exe.\n" +
                "5. Subiendo: el ZIP se descomprime en /site/wwwroot/app_data/jobs/continuous/NOMBRE. " +
                "Sobrescribe archivos, pero no borra los que ya estaban.\n" +
                "6. Iniciando el WebJob.\n" +
                "7. Verificando que el WebJob quedó en ejecución (Running).\n" +
                $"Ante un error de red se reintenta solo (hasta {settings.MaxRetries} veces). Un cliente con error no frena a los demás."),

            new HelpSection("4. Simulación y cancelación",
                "Simulación muestra qué haría con cada cliente sin conectarse ni subir nada: es la forma segura de revisar antes de publicar.\n" +
                "Cancelar durante una publicación corta de forma ordenada: lo ya subido queda subido y lo pendiente no se toca."),

            new HelpSection("5. Seguridad",
                "Las contraseñas se guardan cifradas en el Administrador de credenciales de Windows " +
                "(Panel de control > Administrador de credenciales > Credenciales de Windows > DeployWebJobs/...). " +
                "No se escriben en el archivo de configuración, ni en pantalla, ni en los logs. Todo viaja por HTTPS."),

            new HelpSection("6. Dónde está todo",
                $"Configuración (sin secretos): {_config.FilePath}\n" +
                $"Logs (un archivo por día): {Path.Combine(ConfigService.DefaultDirectory, "logs")}")
        };
    }
}
