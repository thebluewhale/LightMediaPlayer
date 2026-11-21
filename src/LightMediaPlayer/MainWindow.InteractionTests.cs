using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private async Task RunInteractionTestsAsync(string file, string report, Action<string, bool, object?> check, Func<Func<bool>, int, Task<bool>> until)
    {
        void Check(string name, bool ok, object? detail = null) => check(name, ok, detail);
        var settingsPath = Path.Combine(Path.GetDirectoryName(report)!, $"preferences-{Environment.ProcessId}.json");
        var defaults = new PlayerSettings();
        Check("seek_defaults_5_60_300", defaults.SeekSeconds == 5 && defaults.CtrlSeekSeconds == 60 && defaults.ShiftSeekSeconds == 300);
        Check("seek_integer_validation", SettingsWindow.TrySeconds("1", out _) && SettingsWindow.TrySeconds("86400", out _) && !SettingsWindow.TrySeconds("0", out _) && !SettingsWindow.TrySeconds("1.5", out _) && !SettingsWindow.TrySeconds("999999999999", out _));
        new PlayerSettings { Volume = 38 }.Save(settingsPath);
        PlayerSettings.Update(s => { s.SeekSeconds = 7; s.CtrlSeekSeconds = 61; s.ShiftSeekSeconds = 301; }, settingsPath);
        PlayerSettings.Update(s => { s.Width = 1110; s.Volume = 42; }, settingsPath);
        var saved = PlayerSettings.Load(settingsPath);
        Check("seek_preferences_persist_and_merge", saved.SeekSeconds == 7 && saved.CtrlSeekSeconds == 61 && saved.ShiftSeekSeconds == 301 && saved.Volume == 42 && saved.Width == 1110);
        var dialog = new SettingsWindow(defaults, (_, _, _) => { });
        Check("settings_seven_tabs", dialog.Categories.Items.Cast<TabItem>().Select(t => (string)t.Header).SequenceEqual(new[] { "기본", "재생", "자막", "장치", "코덱/필터", "영상", "소리" }));
        Check("unused_settings_tabs_empty", dialog.Categories.Items.Cast<TabItem>().Where(t => (string)t.Header != "재생").All(t => t.Content == null));
        dialog.PlainSeconds.Text = "-1";
        Check("invalid_setting_disables_save", !dialog.Validate() && !dialog.SaveButton.IsEnabled);
        dialog.PlainSeconds.Text = "5";
        Check("valid_setting_enables_save", dialog.Validate() && dialog.SaveButton.IsEnabled);
        var visualDir = Path.Combine(Path.GetDirectoryName(report)!, "visuals");
        Directory.CreateDirectory(visualDir);
        RenderControl((FrameworkElement)dialog.Content, Path.Combine(visualDir, $"settings-{Environment.ProcessId}.png"), 614, 380);
        dialog.Close();

        await OpenPathAsync(file);
        await until(() => _player?.IsPlaying == true && _player.Time > 200, 10000);
        _player!.SetPause(true);
        await until(() => _player.State == LibVLCSharp.Shared.VLCState.Paused, 5000);
        await Task.Delay(200);
        _settings.SeekSeconds = 2; _settings.CtrlSeekSeconds = 3; _settings.ShiftSeekSeconds = 4;
        SeekTo(5000);
        await until(() => Math.Abs(_player.Time - 5000) < 500, 5000);
        SeekByKey(1, false, false);
        Check("plain_seek_key_and_osd", await until(() => Math.Abs(_player.Time - 7000) < 500, 5000) && OsdText.Text == "2초 앞으로, 00:00:07", OsdText.Text);
        SeekByKey(1, true, false);
        Check("ctrl_seek_key_and_osd", await until(() => Math.Abs(_player.Time - 10000) < 500, 5000) && OsdText.Text == "3초 앞으로, 00:00:10", OsdText.Text);
        SeekByKey(-1, false, true);
        Check("shift_seek_key_and_osd", await until(() => Math.Abs(_player.Time - 6000) < 500, 5000) && OsdText.Text == "4초 뒤로, 00:00:06", OsdText.Text);
        SeekByKey(1, false, false); SeekByKey(1, false, false);
        Check("rapid_seeks_accumulate", await until(() => Math.Abs(_player.Time - 10000) < 500, 5000));
        VolumeBar.Value = 47;
        Check("volume_osd", OsdBubble.Visibility == Visibility.Visible && OsdText.Text == "47%");
        Check("osd_auto_hides", await until(() => OsdBubble.Visibility == Visibility.Collapsed, 3500));
        Check("osd_time_includes_hours", FormatOsdTime(3661000) == "01:01:01");

        var work = new WindowMagnet.NativeRect(-1920, 0, 0, 1040);
        var snapped = WindowMagnet.Snap(new(-1910, 12, -1110, 612), work, 16);
        Check("magnet_negative_monitor_top_left", snapped.Left == -1920 && snapped.Top == 0 && snapped.Right - snapped.Left == 800 && snapped.Bottom - snapped.Top == 600);
        snapped = WindowMagnet.Snap(new(-810, 430, -10, 1030), work, 16);
        Check("magnet_workarea_bottom_right", snapped.Right == 0 && snapped.Bottom == 1040 && snapped.Right - snapped.Left == 800);
        snapped = WindowMagnet.Snap(new(-1800, 100, -1000, 700), work, 16);
        Check("magnet_leaves_distant_window", snapped.Left == -1800 && snapped.Top == 100);

        var testDir = Path.Combine(Path.GetDirectoryName(file)!, "playlist");
        var small = Path.Combine(testDir, "clip2.mp4");
        var large = Path.Combine(testDir, "clip10.mp4");
        ReplaceQueue([large, small]);
        await PlayEntryAsync(_queue[0]);
        Check("playlist_item_playback", await until(() => _player?.IsPlaying == true, 10000) && _path == large);
        var current = _currentEntry;
        var generation = _generation;
        PlaylistList.SelectedItem = _queue[1];
        await Task.Delay(250);
        Check("playlist_single_selection_does_not_play", _generation == generation && _path == large);
        var initialWidth = Width;
        TogglePlaylist();
        UpdateLayout();
        Check("playlist_toggle_open", PlaylistPanel.Visibility == Visibility.Visible && PlaylistColumn.Width.Value == 300);
        Check("playlist_index_labels", _queue[0].DisplayName == "01. clip10.mp4" && _queue[1].DisplayName == "02. clip2.mp4");
        RenderControl(PlaylistPanel, Path.Combine(visualDir, $"playlist-{Environment.ProcessId}.png"), 300, 550);
        TogglePlaylist();
        Check("playlist_toggle_closed", PlaylistPanel.Visibility == Visibility.Collapsed && PlaylistColumn.Width.Value == 0 && Math.Abs(Width - initialWidth) < 2);
        await SortPlaylistAsync("title");
        Check("playlist_title_sort_and_selection", _queue[0].Path == small && PlaylistList.SelectedItem == _queue[0] && _currentEntry == current && _generation == generation);
        await SortPlaylistAsync("size");
        Check("playlist_size_sort", _queue.Select(e => new FileInfo(e.Path).Length).SequenceEqual(_queue.Select(e => new FileInfo(e.Path).Length).Order()));
        await SortPlaylistAsync("duration");
        Check("playlist_duration_sort_without_interrupt", _queue[0].Path == small && _queue[1].Path == large && _currentEntry == current && _generation == generation && _player!.IsPlaying);
        var shuffled = true;
        for (var i = 0; i < 8; i++) { var prior = _queue.ToArray(); await SortPlaylistAsync("shuffle"); shuffled &= !prior.SequenceEqual(_queue); }
        Check("shuffle_changes_every_time", shuffled);
        var priorCount = _queue.Count;
        AddPlaylistFiles([small]);
        Check("playlist_append_no_playback", _queue.Count == priorCount + 1 && _queue[^1].Path == small && _generation == generation);
        var duplicate = _queue[^1];
        await PlayEntryAsync(duplicate);
        await until(() => _player?.IsPlaying == true, 5000);
        Check("duplicate_entry_identity", ReferenceEquals(_currentEntry, duplicate));
        generation = _generation;
        PlaylistList.SelectedItem = _queue.First(e => e.Path == small && !ReferenceEquals(e, duplicate));
        RemoveSelectedEntry();
        Check("remove_selected_only", _queue.Contains(duplicate) && _queue.Count == 2 && _generation == generation && File.Exists(small));
        PlaylistList.SelectedItem = duplicate;
        RemoveSelectedEntry();
        Check("remove_playing_item_keeps_playback", _queue.Count == 1 && _player!.IsPlaying && _generation == generation && !_queue.Contains(duplicate));
        AddPlaylistFiles([small]);
        await SortPlaylistAsync("title");
        await PlayEntryAsync(_queue[0]);
        await MoveFileAsync(1);
        Check("playlist_next", _path == large);
        await MoveFileAsync(-1);
        Check("playlist_previous", _path == small);
        var pendingSort = SortPlaylistAsync("duration");
        AddPlaylistFiles([large]);
        await pendingSort;
        Check("playlist_edit_cancels_stale_sort", _queue.Count == 3 && _queue[^1].Path == large);
        PlaylistList.SelectedIndex = 0;
        while (_queue.Count > 0) { PlaylistList.SelectedIndex = 0; RemoveSelectedEntry(); }
        Check("empty_playlist_navigation_disabled", !PreviousButton.IsEnabled && !NextButton.IsEnabled && !PlaylistDelete.IsEnabled);
        AddPlaylistFiles([small]);
        await MoveFileAsync(1);
        Check("play_from_empty_then_added_queue", _path == small && _queue.Count == 1);
    }

    // Render only controls owned by this test instance, not the desktop or any user window.
    private static void RenderControl(FrameworkElement element, string path, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
