using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LibVLCSharp.Shared;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private async Task RunDropAndMenuTestsAsync(string large, string small, string visualDir, Action<string, bool, object?> check, Func<Func<bool>, int, Task<bool>> until)
    {
        void Check(string name, bool ok, object? detail = null) => check(name, ok, detail);
        ReplaceQueue([large]);
        await PlayEntryAsync(_queue[0]);
        await until(() => _player?.IsPlaying == true && _player.Time > 100, 10000);
        var player = _player;
        var generation = _generation;
        var current = _currentEntry;
        AppendPlaylistDrop(new DataObject(DataFormats.FileDrop, new[] { small, large }));
        Check("playlist_drop_appends_in_order", _queue.Select(e => e.Path).SequenceEqual(new[] { large, small, large }));
        Check("playlist_drop_keeps_current_playback", ReferenceEquals(_player, player) && _player!.IsPlaying && _generation == generation && _currentEntry == current && _path == large);
        AppendPlaylistDrop(new DataObject(DataFormats.Text, "not a file drop"));
        Check("playlist_nonfile_drop_ignored", _queue.Count == 3 && _generation == generation);
        _player!.SetPause(true);
        await until(() => _player.State == VLCState.Paused, 5000);
        AppendPlaylistDrop(new DataObject(DataFormats.FileDrop, new[] { small }));
        Check("playlist_drop_keeps_paused_state", _queue.Count == 4 && _player.State == VLCState.Paused && _generation == generation);

        UpdateLayout();
        var glyph = new FormattedText((string)SettingsButton.Content, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(SettingsButton.FontFamily, SettingsButton.FontStyle, SettingsButton.FontWeight, SettingsButton.FontStretch), SettingsButton.FontSize, Brushes.White, VisualTreeHelper.GetDpi(SettingsButton).PixelsPerDip);
        Check("settings_icon_has_room", SettingsButton.ActualWidth - SettingsButton.Padding.Left - SettingsButton.Padding.Right >= glyph.WidthIncludingTrailingWhitespace, new { available = SettingsButton.ActualWidth - SettingsButton.Padding.Left - SettingsButton.Padding.Right, glyph = glyph.WidthIncludingTrailingWhitespace });
        RenderControl(RightControls, Path.Combine(visualDir, $"toolbar-{Environment.ProcessId}.png"), 152, 44);
        RenderControl(RightControls, Path.Combine(visualDir, $"toolbar-150pct-{Environment.ProcessId}.png"), 152, 44, 1.5);

        var menu = BuildVideoMenu();
        Check("video_menu_exact_nine_items", menu.Items.Cast<MenuItem>().Select(m => (string)m.Header).SequenceEqual(new[] { "재생", "자막", "영상", "소리", "필터", "스킨", "화면 비율", "전체 화면", "환경 설정" }));
        var playback = (DelayedSubmenuItem)menu.Items[0];
        Check("playback_submenu_exact_four_items", playback.Items.Cast<MenuItem>().Select(m => (string)m.Header).SequenceEqual(new[] { "재생", "일시 정지", "이전 목록 재생", "다음 목록 재생" }));
        Check("fullscreen_and_settings_are_leaf_actions", !((MenuItem)menu.Items[7]).HasItems && !((MenuItem)menu.Items[8]).HasItems);
        menu.PlacementTarget = TracksButton;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
        await until(() => playback.IsLoaded && menu.ActualWidth > 0, 5000);
        RenderControl(menu, Path.Combine(visualDir, $"video-menu-{Environment.ProcessId}.png"), menu.ActualWidth, menu.ActualHeight);
        playback.IsSubmenuOpen = true;
        Check("submenu_blocks_default_early_open", !playback.IsSubmenuOpen);
        using (var hover = new HoverIntent(playback.OpenFromInput))
        {
            hover.Start();
            await Task.Delay(500);
            Check("submenu_stays_closed_before_one_second", !playback.IsSubmenuOpen);
            Check("submenu_opens_after_hover_delay", await until(() => playback.IsSubmenuOpen, 1500));
        }
        if (playback.Template.FindName("PART_Popup", playback) is Popup popup && popup.Child is FrameworkElement child)
        {
            child.UpdateLayout();
            RenderControl(child, Path.Combine(visualDir, $"playback-submenu-{Environment.ProcessId}.png"), child.ActualWidth, child.ActualHeight);
        }
        playback.Reset();
        var fired = false;
        using (var hover = new HoverIntent(() => fired = true)) { hover.Start(); await Task.Delay(250); hover.Stop(); await Task.Delay(1050); }
        Check("hover_leave_cancels_pending_open", !fired && !playback.IsSubmenuOpen);
        playback.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = PreviewMouseLeftButtonDownEvent, Source = playback });
        Check("submenu_header_click_opens_immediately", playback.IsSubmenuOpen);
        menu.IsOpen = false;
        Check("closed_menu_resets_submenu", await until(() => !playback.IsSubmenuOpen, 2000));

        var beforeFullscreen = _fullscreen;
        var beforeSettings = Application.Current.Windows.OfType<SettingsWindow>().Count();
        foreach (var index in new[] { 7, 8 })
        {
            var leaf = (MenuItem)menu.Items[index];
            leaf.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = MouseEnterEvent, Source = leaf });
        }
        await Task.Delay(1100);
        Check("leaf_hover_does_not_execute", _fullscreen == beforeFullscreen && Application.Current.Windows.OfType<SettingsWindow>().Count() == beforeSettings);
        await PlayFromMenuAsync();
        Check("menu_play_resumes", await until(() => _player!.IsPlaying, 5000));
        await PlayFromMenuAsync();
        Check("menu_play_never_toggles_pause", _player!.IsPlaying);
        PauseFromMenu();
        Check("menu_pause_action", await until(() => _player!.State == VLCState.Paused, 5000));
        PauseFromMenu();
        Check("menu_pause_never_toggles_play", _player!.State == VLCState.Paused);
        menu = BuildVideoMenu();
        ((MenuItem)menu.Items[7]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("fullscreen_menu_click", _fullscreen != beforeFullscreen);
        ToggleFullscreen();
        var settingsOpened = false;
        _ = Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.Owner == this);
            settingsOpened = dialog?.IsVisible == true;
            dialog?.Close();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        ((MenuItem)menu.Items[8]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("settings_menu_click_opens_dialog", settingsOpened);
    }
}
