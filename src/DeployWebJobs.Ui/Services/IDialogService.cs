using DeployWebJobs.Ui.ViewModels;

namespace DeployWebJobs.Ui.Services;

/// <summary>Cuadros de diálogo y selectores de archivo. Los ViewModels usan esto sin conocer las ventanas.</summary>
public interface IDialogService
{
    Task<string?> PickFolderAsync(string title, string? suggestedPath);
    Task<string?> PickExcelAsync();

    /// <summary>Elige un archivo existente con la extensión dada (ej. "*.dwjclients"). Devuelve null si se cancela.</summary>
    Task<string?> PickOpenFileAsync(string title, string typeName, string pattern);

    /// <summary>Elige dónde guardar un archivo nuevo. Devuelve null si se cancela.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string typeName, string pattern, string defaultExtension);

    /// <summary>Pide una contraseña (el texto se muestra oculto). Devuelve null si se cancela.</summary>
    Task<string?> PromptSecretAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Aceptar", string cancelText = "Cancelar");
    Task ShowMessageAsync(string title, string message);
    Task<bool> EditClientAsync(ClientEditViewModel viewModel);
}
