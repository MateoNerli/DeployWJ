using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeployWebJobs.Ui.Mvvm;

/// <summary>Base de los ViewModels: avisa a la pantalla cuando cambia una propiedad.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Pantallas que necesitan refrescarse al entrar (por ejemplo, para ver clientes recién agregados).</summary>
public interface IActivatable
{
    void OnActivated();
}
