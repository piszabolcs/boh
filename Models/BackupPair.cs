using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BOH.Models;

/// <summary>One source folder backed up into one destination folder.</summary>
public class BackupPair : INotifyPropertyChanged
{
    private string _name = "Backup";
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private bool _enabled = true;

    /// <summary>Stable id, written into every backup's marker file so retention only touches this pair's folders.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get => _name; set => Set(ref _name, value); }
    public string SourcePath { get => _sourcePath; set => Set(ref _sourcePath, value); }
    public string DestinationPath { get => _destinationPath; set => Set(ref _destinationPath, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
