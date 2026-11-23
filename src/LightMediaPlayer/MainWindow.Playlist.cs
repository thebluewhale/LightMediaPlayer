using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibVLCSharp.Shared;
using Microsoft.Win32;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private readonly ObservableCollection<PlaylistEntry> _queue = [];
    private PlaylistEntry? _currentEntry;
    private int _detachedIndex;
    private int _queueRevision;
    private CancellationTokenSource? _sortCancellation;
    private bool _playlistOpen;
    private double _playlistExpandedBy;
    private string[] _neighbors => _queue.Select(e => e.Path).ToArray();

    private void CancelSorting()
    {
        _sortCancellation?.Cancel();
        _queueRevision++;
    }
    private void ReplaceQueue(IEnumerable<string> paths)
    {
        CancelSorting();
        _queue.Clear();
        foreach (var path in paths) _queue.Add(new PlaylistEntry(path));
        _detachedIndex = 0;
        RefreshQueue();
    }
    private void RefreshQueue()
    {
        for (var i = 0; i < _queue.Count; i++) { _queue[i].Index = i + 1; _queue[i].IsCurrent = ReferenceEquals(_queue[i], _currentEntry); }
        PlaylistInfo.Text = $"{_queue.Count}개 파일";
        PlaylistDelete.IsEnabled = PlaylistList.SelectedItem is PlaylistEntry;
        PlaylistSort.IsEnabled = _queue.Count > 1;
        UpdateButtons();
    }
    private int NavigationTarget(int direction)
    {
        var index = _currentEntry == null ? -1 : _queue.IndexOf(_currentEntry);
        if (index >= 0) return index + direction;
        // Removing the currently playing row never stops playback. Next continues
        // from its former slot; previous continues from the preceding slot.
        return direction > 0 ? Math.Min(_detachedIndex, _queue.Count) : Math.Min(_detachedIndex, _queue.Count) - 1;
    }
    private async Task PlayEntryAsync(PlaylistEntry entry)
    {
        if (_queue.Contains(entry)) await OpenPathAsync(entry.Path, entry: entry);
    }
    private void PlaylistToggle_Click(object sender, RoutedEventArgs e) => TogglePlaylist();
    private void TogglePlaylist()
    {
        if (_fullscreen) return;
        _playlistOpen = !_playlistOpen;
        if (_playlistOpen)
        {
            if (WindowState == WindowState.Normal)
            {
                _playlistExpandedBy = Math.Min(300, WindowMagnet.SpaceToRight(this));
                Width += _playlistExpandedBy;
            }
            PlaylistToggle.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        }
        else
        {
            if (WindowState == WindowState.Normal) Width = Math.Max(MinWidth, Width - _playlistExpandedBy);
            _playlistExpandedBy = 0;
            PlaylistToggle.ClearValue(ForegroundProperty);
        }
        UpdatePlaylistLayout();
    }
    private void UpdatePlaylistLayout()
    {
        var visible = _playlistOpen && !_fullscreen;
        PlaylistPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PlaylistColumn.Width = new GridLength(visible ? Math.Clamp(ActualWidth - 480, 220, 300) : 0);
    }
    private async void Playlist_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var container = ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.Content is PlaylistEntry entry) { e.Handled = true; await PlayEntryAsync(entry); }
    }
    private void Playlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlaylistDelete != null) PlaylistDelete.IsEnabled = PlaylistList.SelectedItem is PlaylistEntry;
        // Deliberately no playback action on selection/focus.
    }
    private void PlaylistAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "재생목록에 추가", Filter = MediaFiles.OpenFilter, Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) AddPlaylistFiles(dialog.FileNames);
    }
    private void Playlist_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Playlist_Drop(object sender, DragEventArgs e)
    {
        // Consume the tunneled event before it can reach the window's open/play
        // handler, including drops on a list row, empty space, or footer button.
        e.Handled = true;
        AppendPlaylistDrop(e.Data);
    }
    private void AppendPlaylistDrop(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] paths) AddPlaylistFiles(paths);
    }
    private void AddPlaylistFiles(IEnumerable<string> paths)
    {
        CancelSorting();
        var files = ReadPlaylistFiles(paths);
        foreach (var file in files) _queue.Add(new PlaylistEntry(file));
        RefreshQueue();
        PlaylistInfo.Text = files.Count > 0 ? $"{_queue.Count}개 파일 · {files.Count}개 추가됨" : "추가할 미디어 파일이 없습니다";
    }
    private static List<string> ReadPlaylistFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path) || MediaFiles.Subtitles.Contains(Path.GetExtension(path))) continue;
            try
            {
                var entries = PlaylistFiles.Read(path) ?? [Path.GetFullPath(path)];
                files.AddRange(entries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { AppLog.Write(ex.Message); }
        }
        return files;
    }
    private void PlaylistDelete_Click(object sender, RoutedEventArgs e) => RemoveSelectedEntry();
    private void RemoveSelectedEntry()
    {
        if (PlaylistList.SelectedItem is not PlaylistEntry entry) return;
        CancelSorting();
        var index = _queue.IndexOf(entry);
        if (ReferenceEquals(entry, _currentEntry)) _detachedIndex = index;
        else if (!_queue.Contains(_currentEntry!) && index < _detachedIndex) _detachedIndex--;
        _queue.Remove(entry);
        PlaylistList.SelectedIndex = Math.Min(index, _queue.Count - 1);
        RefreshQueue();
    }
    private void PlaylistSort_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PlaylistSort };
        foreach (var (label, mode) in new[] { ("제목으로 정렬", "title"), ("파일 크기로 정렬", "size"), ("재생 시간으로 정렬", "duration"), ("순서 섞기", "shuffle") })
        {
            var item = new MenuItem { Header = label };
            item.Click += async (_, _) => await SortPlaylistAsync(mode);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private async Task SortPlaylistAsync(string mode)
    {
        CancelSorting();
        if (_queue.Count < 2) { RefreshQueue(); return; }
        using var cancellation = new CancellationTokenSource();
        _sortCancellation = cancellation;
        var token = cancellation.Token;
        var revision = _queueRevision;
        var snapshot = _queue.ToArray();
        var selected = PlaylistList.SelectedItem;
        try
        {
            PlaylistEntry[] sorted;
            if (mode == "shuffle")
            {
                sorted = snapshot.ToArray();
                Random.Shared.Shuffle(sorted);
                if (sorted.SequenceEqual(snapshot)) (sorted[0], sorted[1]) = (sorted[1], sorted[0]);
            }
            else if (mode == "title") sorted = snapshot.OrderBy(e => e.FileName, MediaFiles.NaturalComparer).ToArray();
            else
            {
                PlaylistInfo.Text = mode == "duration" ? "재생 시간 확인 중…" : "파일 크기 확인 중…";
                var keys = await Task.Run(() =>
                {
                    var values = new Dictionary<Guid, long>();
                    // Metadata parsing owns a separate engine, never the playing engine.
                    using var probe = mode == "duration" ? new LibVLC("--no-video-title-show", "--no-stats") : null;
                    foreach (var entry in snapshot)
                    {
                        token.ThrowIfCancellationRequested();
                        var value = long.MaxValue; // Missing/unknown values sort last.
                        try
                        {
                            if (mode == "size") value = new FileInfo(entry.Path).Length;
                            else if (File.Exists(entry.Path))
                            {
                                using var media = new Media(probe!, entry.Path, FromType.FromPath);
                                // Keep disposal on this worker. LibVLCSharp 3.x can complete
                                // Parse inline on VLC's callback thread; disposing the engine
                                // from that continuation can release a thread's own native state.
                                // Cancellation is checked between bounded (3 s) parses so the
                                // UI never calls the blocking native stop routine.
                                var status = media.Parse(MediaParseOptions.ParseLocal, 3000).GetAwaiter().GetResult();
                                token.ThrowIfCancellationRequested();
                                if (status == MediaParsedStatus.Done && media.Duration >= 0) value = media.Duration;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("Metadata: " + ex.Message); }
                        values[entry.Id] = value;
                    }
                    return values;
                }, token);
                sorted = snapshot.OrderBy(e => keys[e.Id]).ThenBy(e => e.FileName, MediaFiles.NaturalComparer).ToArray();
            }
            if (token.IsCancellationRequested || _closing || revision != _queueRevision) return;
            _queue.Clear();
            foreach (var item in sorted) _queue.Add(item);
            _queueRevision++;
            PlaylistList.SelectedItem = selected;
            RefreshQueue();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing) PlaylistInfo.Text = "정렬하지 못했습니다: " + ex.Message; AppLog.Write(ex.ToString()); }
        finally { if (ReferenceEquals(_sortCancellation, cancellation)) _sortCancellation = null; }
    }
}
