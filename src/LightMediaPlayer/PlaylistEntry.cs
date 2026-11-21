using System.ComponentModel;
using System.IO;

namespace LightMediaPlayer;

internal sealed class PlaylistEntry(string path) : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; } = path;
    public string FileName => System.IO.Path.GetFileName(Path);
    private int _index;
    public int Index { get => _index; set { _index = value; Changed(nameof(DisplayName)); } }
    public string DisplayName => $"{Index:00}. {FileName}";
    private bool _current;
    public bool IsCurrent { get => _current; set { if (_current != value) { _current = value; Changed(nameof(IsCurrent)); } } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
