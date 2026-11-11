using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DeployWebJobs.Ui.Mvvm;
using DeployWebJobs.Ui.Services;
using DeployWebJobs.Ui.ViewModels;
using DeployWebJobs.Ui.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DeployWebJobs.Ui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var services = Program.Services;
                var dialogs = services.GetRequiredService<DialogService>();

                // Si algo inesperado pasa dentro de un botón: se anota en el log y se avisa, sin cerrar la app.
                AsyncRelayCommand.GlobalErrorHandler = ex =>
                {
                    Log.Error(ex, "Error inesperado en la interfaz");
                    _ = dialogs.ShowMessageAsync("Error inesperado",
                        ex.Message + "\n\nEl detalle técnico quedó en el archivo de log.");
                };

                var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };
                dialogs.SetOwner(window);
                desktop.MainWindow = window;

                // Si no hay clientes y junto al .exe hay un paquete .dwjclients, se ofrece cargarlo.
                window.Opened += async (_, _) =>
                {
                    try
                    {
                        await services.GetRequiredService<ClientsViewModel>().OfferStartupImportAsync();
                        services.GetRequiredService<PublishViewModel>().OnActivated();
                    }
                    catch (Exception ex)
                    {
                        AsyncRelayCommand.GlobalErrorHandler?.Invoke(ex);
                    }
                };
            }
            catch (Exception ex)
            {
                // Por ejemplo, un config.json dañado: se muestra el motivo en vez de cerrar sin decir nada.
                Log.Fatal(ex, "No se pudo iniciar la ventana principal");
                desktop.MainWindow = StartupErrorWindow.Create(ex.Message);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
