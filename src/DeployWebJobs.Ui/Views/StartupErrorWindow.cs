using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DeployWebJobs.Ui.Views;

/// <summary>Ventana de emergencia si la app no pudo arrancar (por ejemplo, config.json dañado).</summary>
public static class StartupErrorWindow
{
    public static Window Create(string message)
    {
        var close = new Button { Content = "Cerrar", HorizontalAlignment = HorizontalAlignment.Right };

        var window = new Window
        {
            Title = "DeployWebJobs - no se pudo iniciar",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "No se pudo iniciar la aplicación",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = "El detalle técnico quedó en la carpeta de logs (%AppData%\\DeployWebJobs\\logs).",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.7
                    },
                    close
                }
            }
        };

        close.Click += (_, _) => window.Close();
        return window;
    }
}
