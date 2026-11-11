using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using DeployWebJobs.Ui.ViewModels;
using DeployWebJobs.Ui.Views;
using Avalonia;

namespace DeployWebJobs.Ui.Services;

public sealed class DialogService : IDialogService
{
    private Window? _owner;

    public void SetOwner(Window owner) => _owner = owner;

    private Window Owner => _owner ?? throw new InvalidOperationException("La ventana principal todavía no está lista.");

    public async Task<string?> PickFolderAsync(string title, string? suggestedPath)
    {
        var storage = Owner.StorageProvider;

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(suggestedPath) && Directory.Exists(suggestedPath))
            start = await storage.TryGetFolderFromPathAsync(suggestedPath);

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickExcelAsync()
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elegí el Excel de conexiones anterior (ConexionesFTP.xlsx)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFileAsync(string title, string typeName, string pattern)
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = new[] { pattern } } }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string typeName, string pattern, string defaultExtension)
    {
        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension,
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { pattern } } }
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PromptSecretAsync(string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var box = new TextBox { PasswordChar = '●' };
        var ok = new Button { Content = "Aceptar", IsDefault = true };
        ok.Classes.Add("accent");
        var cancel = new Button { Content = "Cancelar", IsCancel = true };

        ok.Click += (_, _) => window.Close(box.Text ?? "");
        cancel.Click += (_, _) => window.Close(null);

        window.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel }
                }
            }
        };

        window.Opened += (_, _) => box.Focus();
        return await window.ShowDialog<string?>(Owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Aceptar", string cancelText = "Cancelar")
    {
        var window = BuildMessageWindow(title, message, confirmText, cancelText);
        return await window.ShowDialog<bool>(Owner);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = BuildMessageWindow(title, message, confirmText: null, cancelText: "Cerrar");
        await window.ShowDialog<bool>(Owner);
    }

    public async Task<bool> EditClientAsync(ClientEditViewModel viewModel)
    {
        var window = new ClientEditWindow { DataContext = viewModel };
        viewModel.CloseRequested += accepted => window.Close(accepted);
        return await window.ShowDialog<bool>(Owner);
    }

    /// <summary>Ventana simple de mensaje / confirmación armada por código.</summary>
    private static Window BuildMessageWindow(string title, string message, string? confirmText, string cancelText)
    {
        var window = new Window
        {
            Title = title,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        if (confirmText is not null)
        {
            var confirm = new Button { Content = confirmText };
            confirm.Classes.Add("accent");
            confirm.Click += (_, _) => window.Close(true);
            buttons.Children.Add(confirm);
        }

        var cancel = new Button { Content = cancelText };
        cancel.Click += (_, _) => window.Close(false);
        buttons.Children.Add(cancel);

        window.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };

        return window;
    }
}
