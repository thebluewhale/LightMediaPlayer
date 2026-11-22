using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private async Task RunFilterMenuTestsAsync(string report, Action<string, bool, object?> check, Func<Func<bool>, int, Task<bool>> until)
    {
        static DelayedSubmenuItem Filters(ContextMenu menu) =>
            menu.Items.OfType<DelayedSubmenuItem>().Single(item => Equals(item.Header, "필터"));

        var menu = BuildVideoMenu();
        var filters = Filters(menu);
        var items = filters.Items.Cast<MenuItem>().ToArray();
        check("filter_choices", items.Select(i => (string)i.Header).SequenceEqual(new[] { "필터 없음", "채도 낮추기", "채도 높이기", "밝기 높이기" }), null);
        check("filter_defaults_to_original", items.Single(i => i.IsChecked) == items[0], null);
        menu.PlacementTarget = TracksButton;
        menu.IsOpen = true;
        try
        {
            await until(() => filters.IsLoaded, 3000);
            filters.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = PreviewMouseLeftButtonDownEvent, Source = filters });
            check("filter_click_opens", filters.IsSubmenuOpen, null);
            filters.Reset();
            filters.IsSubmenuOpen = true;
            check("filter_blocks_early_open", !filters.IsSubmenuOpen, null);
            using var hover = new HoverIntent(filters.OpenFromInput);
            hover.Start();
            check("filter_delayed_open", await until(() => filters.IsSubmenuOpen, 2000), null);
        }
        finally { menu.IsOpen = false; }

        var directory = Path.Combine(Path.GetDirectoryName(report)!, "visuals");
        Directory.CreateDirectory(directory);
        var player = _player!;
        var audio = player.AudioTrack;
        foreach (var label in new[] { "채도 낮추기", "채도 높이기", "밝기 높이기", "필터 없음" })
        {
            var pictures = _media!.Statistics.DisplayedPictures;
            items.Single(i => Equals(i.Header, label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var advancing = await until(() => _media.Statistics.DisplayedPictures >= pictures + 4, 3000);
            var selected = Filters(BuildVideoMenu()).Items.Cast<MenuItem>().Where(i => i.IsChecked).Select(i => (string)i.Header);
            check("filter_live_" + label, advancing && player == _player && player.IsPlaying && player.AudioTrack == audio
                && selected.SequenceEqual(new[] { label }), null);
            var path = Path.Combine(directory, $"filter-{Environment.ProcessId}-{label}.png");
            if (!player.TakeSnapshot(0, path, 320, 0) || !await until(() => File.Exists(path), 3000))
            {
                check("filter_snapshot_" + label, false, path);
                continue;
            }
            // The fixture has a solid red bar here. Compare a decoded pixel so a
            // successful API call or an unreadable GPU snapshot cannot pass alone.
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var rgb = new FormatConvertedBitmap(frame, PixelFormats.Rgb24, null, 0);
            var pixel = new byte[3];
            rgb.CopyPixels(new Int32Rect(20, 100, 1, 1), pixel, 3, 0);
            var matches = label switch
            {
                "채도 낮추기" => pixel[0] - pixel[1] is > 30 and < 150 && Math.Abs(pixel[1] - pixel[2]) < 10,
                "밝기 높이기" => pixel[0] > 230 && pixel[1] > 25 && pixel[2] > 25,
                _ => pixel[0] > 230 && pixel[1] < 15 && pixel[2] < 15
            };
            var saturation = player.AdjustFloat(VideoAdjustOption.Saturation);
            check("filter_snapshot_" + label, matches && (label != "채도 높이기" || saturation > 1), new { pixel = pixel.Select(b => (int)b).ToArray(), path });
        }
    }

    private async Task RunAspectRatioMenuTestsAsync(Action<string, bool, object?> check, Func<Func<bool>, int, Task<bool>> until)
    {
        static DelayedSubmenuItem AspectMenu(ContextMenu menu) =>
            menu.Items.OfType<DelayedSubmenuItem>().Single(item => Equals(item.Header, "화면 비율"));

        var menu = BuildVideoMenu();
        var ratios = AspectMenu(menu);
        var items = ratios.Items.Cast<MenuItem>().ToArray();
        check("aspect_ratio_choices", items.Select(item => (string)item.Header).SequenceEqual(new[] { "원본 비율", "16:9", "16:10", "4:3" }), null);
        check("aspect_ratio_defaults_to_source", items.Single(item => item.IsChecked) == items[0], null);
        menu.PlacementTarget = TracksButton;
        menu.IsOpen = true;
        try
        {
            await until(() => ratios.IsLoaded, 3000);
            ratios.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = PreviewMouseLeftButtonDownEvent, Source = ratios });
            check("aspect_ratio_click_opens", ratios.IsSubmenuOpen, null);
            ratios.Reset();
            ratios.IsSubmenuOpen = true;
            check("aspect_ratio_blocks_early_open", !ratios.IsSubmenuOpen, null);
            using var hover = new HoverIntent(ratios.OpenFromInput);
            hover.Start();
            check("aspect_ratio_delayed_open", await until(() => ratios.IsSubmenuOpen, 2000), null);
        }
        finally { menu.IsOpen = false; }

        var player = _player!;
        foreach (var label in new[] { "16:9", "16:10", "4:3", "원본 비율" })
        {
            var item = AspectMenu(BuildVideoMenu()).Items.Cast<MenuItem>().Single(item => Equals(item.Header, label));
            var pictures = _media!.Statistics.DisplayedPictures;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var expected = label == "원본 비율" ? string.Empty : label;
            var applied = (player.AspectRatio ?? string.Empty) == expected && player.Scale == 0;
            var playing = await until(() => player.IsPlaying && _media.Statistics.DisplayedPictures > pictures, 3000);
            var checkedLabels = AspectMenu(BuildVideoMenu()).Items.Cast<MenuItem>().Where(i => i.IsChecked).Select(i => (string)i.Header);
            check("aspect_ratio_playing_" + label, applied && playing && checkedLabels.SequenceEqual(new[] { label }), player.AspectRatio);
        }
    }

    private async Task RunVideoTrackMenuTestsAsync(string file, Action<string, bool, object?> check, Func<Func<bool>, int, Task<bool>> until)
    {
        DelayedSubmenuItem TrackMenu(ContextMenu menu)
        {
            var video = menu.Items.OfType<DelayedSubmenuItem>().Single(item => Equals(item.Header, "영상"));
            return (DelayedSubmenuItem)video.Items[0];
        }
        // Called while the audio-only fixture is playing.
        check("video_menu_disabled_for_audio", !TrackMenu(BuildVideoMenu()).IsEnabled, null);
        await OpenPathAsync(file);
        if (!await until(() => _player?.IsPlaying == true && _player.VideoTrackDescription.Count(t => t.Id >= 0) == 2, 10000))
        {
            check("video_menu_two_tracks_available", false, file);
            return;
        }
        var player = _player!;
        var audio = player.AudioTrack;
        var target = player.VideoTrackDescription.First(t => t.Id >= 0 && t.Id != player.VideoTrack);
        var menu = BuildVideoMenu();
        var tracks = TrackMenu(menu);
        var video = (DelayedSubmenuItem)menu.Items[2];
        menu.PlacementTarget = TracksButton;
        menu.IsOpen = true;
        try
        {
            await until(() => video.IsLoaded, 3000);
            video.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = PreviewMouseLeftButtonDownEvent, Source = video });
            check("video_header_click_opens", video.IsSubmenuOpen && await until(() => tracks.IsLoaded, 3000), null);
            tracks.IsSubmenuOpen = true;
            check("video_tracks_block_early_open", !tracks.IsSubmenuOpen, null);
            using var hover = new HoverIntent(tracks.OpenFromInput);
            hover.Start();
            check("video_tracks_delayed_open", await until(() => tracks.IsSubmenuOpen, 2000), null);
            var item = tracks.Items.Cast<MenuItem>().Single(i => Equals(i.Header, target.Name));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally { menu.IsOpen = false; }
        check("video_menu_switches_native_track", await until(() => player.VideoTrack == target.Id && player.IsPlaying && player.AudioTrack == audio, 5000), target.Name);
        var selected = TrackMenu(BuildVideoMenu()).Items.Cast<MenuItem>().Where(i => i.IsChecked).ToArray();
        check("video_menu_marks_selected_track", selected.Length == 1 && Equals(selected[0].Header, target.Name), null);
    }

    private async Task RunSubtitleMenuTestsAsync(Action<string, bool, object?> check, Func<Func<bool>, int, Task<bool>> until)
    {
        void Check(string name, bool ok) => check(name, ok, null);
        var menu = BuildVideoMenu();
        var subtitles = (DelayedSubmenuItem)menu.Items[1];
        Check("subtitle_menu_three_actions", subtitles.Items.Cast<MenuItem>().Select(item => (string)item.Header).SequenceEqual(new[] { "자막 열기", "자막 표시/숨김", "자막 트랙 선택" }));
        var toggle = (MenuItem)subtitles.Items[1];
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("subtitle_menu_hide", await until(() => _player!.Spu == -1, 3000));
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("subtitle_menu_show", await until(() => _player!.Spu >= 0, 3000));

        menu = BuildVideoMenu();
        subtitles = (DelayedSubmenuItem)menu.Items[1];
        var tracks = (DelayedSubmenuItem)subtitles.Items[2];
        var descriptions = _player!.SpuDescription.ToArray();
        Check("subtitle_menu_tracks_and_selection", tracks.Items.Count == descriptions.Length && tracks.Items.Cast<MenuItem>().Where(item => item.IsChecked).Count() == 1);
        menu.PlacementTarget = TracksButton;
        menu.IsOpen = true;
        await until(() => subtitles.IsLoaded, 3000);
        subtitles.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = PreviewMouseLeftButtonDownEvent, Source = subtitles });
        Check("subtitle_menu_click_opens", subtitles.IsSubmenuOpen);
        await until(() => tracks.IsLoaded, 3000);
        tracks.IsSubmenuOpen = true;
        Check("subtitle_tracks_block_early_open", !tracks.IsSubmenuOpen);
        using (var hover = new HoverIntent(tracks.OpenFromInput))
        {
            hover.Start();
            await Task.Delay(500);
            Check("subtitle_tracks_wait_one_second", !tracks.IsSubmenuOpen);
            Check("subtitle_tracks_delayed_open", await until(() => tracks.IsSubmenuOpen, 1500));
        }
        var disabledIndex = Array.FindIndex(descriptions, track => track.Id == -1);
        ((MenuItem)tracks.Items[disabledIndex]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("subtitle_menu_track_disable", await until(() => _player.Spu == -1, 3000));
        var selectedIndex = Array.FindLastIndex(descriptions, track => track.Id >= 0);
        ((MenuItem)tracks.Items[selectedIndex]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("subtitle_menu_track_select", await until(() => _player.Spu == descriptions[selectedIndex].Id && _lastSubtitle == descriptions[selectedIndex].Id, 3000));
        var refreshedTracks = (MenuItem)((MenuItem)BuildVideoMenu().Items[1]).Items[2];
        Check("subtitle_menu_selected_track_checked", ((MenuItem)refreshedTracks.Items[selectedIndex]).IsChecked);
        menu.IsOpen = false;
        Check("subtitle_menu_nested_close_resets", await until(() => !subtitles.IsSubmenuOpen && !tracks.IsSubmenuOpen, 2000));
    }

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
