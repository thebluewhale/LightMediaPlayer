using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightMediaPlayer;

public partial class MainWindow
{
    // Opt-in probe of this test instance's HWND only. No desktop capture/input.
    private async Task RunResizeTestAsync()
    {
        var index = Array.IndexOf(_arguments, "--self-test-report");
        var report = _arguments[index + 1];
        var checks = new List<object>();
        var passed = true;
        void Check(string name, bool ok, object? details = null)
        { checks.Add(new { name, passed = ok, details }); passed &= ok; AppLog.Write($"CHECK {name}: {ok}"); }
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        var hiddenTransitions = 0;
        var staticBrushRequests = 0;
        nint Observe(nint handle, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x0138) staticBrushRequests++; // WM_CTLCOLORSTATIC
            if (message == 0x007D && wParam == -16 && lParam != 0)
            {
                var styles = Marshal.PtrToStructure<ResizeProbeNative.StyleChange>(lParam);
                if ((styles.Old & 0x10000000) != 0 && (styles.New & 0x10000000) == 0) hiddenTransitions++;
            }
            return 0;
        }
        source.AddHook(Observe);
        try
        {
            WindowState = WindowState.Normal;
            var originalSize = new Size(Width, Height);
            var blackSamples = 0;
            var idleWithoutHost = true;
            ResizeProbeNative.SendMessage(hwnd, 0x0231, 0, 0);
            try
            {
                for (var i = 0; i < 80; i++)
                {
                    var d = Math.Min(i % 20, 20 - i % 20) * 9;
                    Width = 880 + d;
                    Height = 570 + d;
                    await Task.Delay(16);
                    idleWithoutHost &= _player == null && _path == null && Window.GetWindow(VideoOverlay) == this &&
                        EmptyOpenButton.IsVisible && ResizeProbeNative.FindWindowEx(hwnd, 0, "Static", null) == 0;
                    if (HasBlackIdleBackground()) blackSamples++;
                }
            }
            finally
            {
                ResizeProbeNative.SendMessage(hwnd, 0x0232, 0, 0);
                Width = originalSize.Width;
                Height = originalSize.Height;
            }
            Check("idle_resize_without_native_video_host", idleWithoutHost);
            Check("idle_resize_no_static_brush_requests", staticBrushRequests == 0, staticBrushRequests);
            Check("idle_resize_background_black", blackSamples == 80, blackSamples);
            await OpenPathAsync(_arguments[index + 2]);
            for (var attempt = 0; attempt < 100 && _player?.IsPlaying != true; attempt++) await Task.Delay(100);
            Check("resize_playback_started", _player?.IsPlaying == true);
            Check("first_playback_attaches_video_host", _player?.Hwnd is nint output && output != 0 &&
                ResizeProbeNative.IsChild(hwnd, output) && Window.GetWindow(VideoOverlay)?.Owner == this);
            WindowState = WindowState.Normal;
            await Task.Delay(200);
            ResizeProbeNative.GetWindowRect(hwnd, out var original);
            var player = _player;
            var generation = _generation;
            var frames = _media?.Statistics.DisplayedPictures ?? 0;
            var region = ResizeProbeNative.CreateRectRgn(0, 0, 0, 0);
            var regionFree = true;
            hiddenTransitions = 0;
            // Enter/exit the native move/size rendering mode, varying all four edges.
            ResizeProbeNative.SendMessage(hwnd, 0x0231, 0, 0);
            try
            {
                for (var i = 0; i < 40; i++)
                {
                    var amount = (i % 10) * 8;
                    ResizeProbeNative.SetWindowPos(hwnd, 0, original.Left + amount, original.Top + amount / 2,
                        original.Right - original.Left + (i % 2 == 0 ? amount : -amount),
                        original.Bottom - original.Top + (i % 2 == 0 ? amount : -amount), 0x0014);
                    regionFree &= ResizeProbeNative.GetWindowRgn(hwnd, region) == 0;
                    await Task.Delay(25);
                }
            }
            finally
            {
                ResizeProbeNative.DeleteObject(region);
                ResizeProbeNative.SendMessage(hwnd, 0x0232, 0, 0);
                ResizeProbeNative.SetWindowPos(hwnd, 0, original.Left, original.Top, original.Right - original.Left, original.Bottom - original.Top, 0x0014);
            }
            Check("resize_never_hides_window", hiddenTransitions == 0, hiddenTransitions);
            Check("resize_has_no_changing_clip_region", regionFree);
            Check("resize_keeps_player", ReferenceEquals(player, _player) && _generation == generation && _player?.IsPlaying == true);
            await Task.Delay(700);
            Check("resize_keeps_video_frames", (_media?.Statistics.DisplayedPictures ?? 0) > frames);
            hiddenTransitions = 0;
            var title = Title;
            for (var i = 0; i < 10; i++) Title = title + " " + i;
            Title = title;
            Check("title_update_never_hides_window", hiddenTransitions == 0, hiddenTransitions);
            int HitAt(int x, int y) => (int)ResizeProbeNative.SendMessage(hwnd, 0x0084, 0, (nint)((y << 16) | (x & 0xffff)));
            var midX = (original.Left + original.Right) / 2;
            var midY = (original.Top + original.Bottom) / 2;
            Check("native_eight_resize_edges", new[]
            {
                HitAt(original.Left + 1, midY), HitAt(original.Right - 1, midY),
                HitAt(midX, original.Top + 1), HitAt(midX, original.Bottom - 1),
                HitAt(original.Left + 1, original.Top + 1), HitAt(original.Right - 1, original.Top + 1),
                HitAt(original.Left + 1, original.Bottom - 1), HitAt(original.Right - 1, original.Bottom - 1)
            }.SequenceEqual(new[] { 10, 11, 12, 15, 13, 14, 16, 17 }));
            var captionPoint = TitleBar.PointToScreen(new Point(80, 22));
            var buttonPoint = MaximizeButton.PointToScreen(new Point(20, 20));
            Check("native_caption_and_buttons", HitAt((int)captionPoint.X, (int)captionPoint.Y) == 2 && HitAt((int)buttonPoint.X, (int)buttonPoint.Y) == 1);
            var minMaxPointer = Marshal.AllocHGlobal(Marshal.SizeOf<PlayerWindowFrame.MinMaxInfo>());
            try
            {
                Marshal.StructureToPtr(new PlayerWindowFrame.MinMaxInfo(), minMaxPointer, false);
                ResizeProbeNative.SendMessage(hwnd, 0x0024, 0, minMaxPointer);
                var minMax = Marshal.PtrToStructure<PlayerWindowFrame.MinMaxInfo>(minMaxPointer);
                var dpi = PlayerWindowFrame.GetDpiForWindow(hwnd) / 96.0;
                Check("native_minimum_size_uses_dpi", minMax.MinTrackSize.X == (int)Math.Ceiling(MinWidth * dpi) && minMax.MinTrackSize.Y == (int)Math.Ceiling(MinHeight * dpi));
            }
            finally { Marshal.FreeHGlobal(minMaxPointer); }
            WindowState = WindowState.Maximized;
            await Task.Delay(150);
            Check("resize_maximize", WindowState == WindowState.Maximized && ResizeProbeNative.IsWindowVisible(hwnd));
            var monitor = new PlayerWindowFrame.MonitorInfo { Size = Marshal.SizeOf<PlayerWindowFrame.MonitorInfo>() };
            PlayerWindowFrame.GetMonitorInfo(PlayerWindowFrame.MonitorFromWindow(hwnd, 2), ref monitor);
            ResizeProbeNative.GetWindowRect(hwnd, out var maximized);
            Check("maximize_respects_work_area", maximized.Left == monitor.Work.Left && maximized.Top == monitor.Work.Top && maximized.Right == monitor.Work.Right && maximized.Bottom == monitor.Work.Bottom, maximized);
            WindowState = WindowState.Minimized;
            await Task.Delay(150);
            Check("resize_minimize", ResizeProbeNative.IsIconic(hwnd));
            WindowState = WindowState.Normal;
            await Task.Delay(150);
            Check("resize_restore", !ResizeProbeNative.IsIconic(hwnd) && ResizeProbeNative.IsWindowVisible(hwnd));
            // Restoring a minimized maximized HWND restores its maximized state.
            // Explicitly return to normal before testing resizable fullscreen restore.
            WindowState = WindowState.Normal;
            await Task.Delay(150);
            ResizeProbeNative.GetWindowRect(hwnd, out var beforeFullscreen);
            bool VideoUsesPlaylistSpace() => !PlaylistPanel.IsVisible && PlaylistColumn.ActualWidth == 0 &&
                Math.Abs(VideoSurface.ActualWidth - ((FrameworkElement)VideoSurface.Parent).ActualWidth) < 1;
            ToggleFullscreen();
            await Task.Delay(150);
            ResizeProbeNative.GetWindowRect(hwnd, out var fullscreen);
            Check("fullscreen_fills_monitor", fullscreen.Left == monitor.Monitor.Left && fullscreen.Top == monitor.Monitor.Top && fullscreen.Right == monitor.Monitor.Right && fullscreen.Bottom == monitor.Monitor.Bottom, fullscreen);
            var videoTopLeft = VideoSurface.PointToScreen(new Point(0, 0));
            var videoBottomRight = VideoSurface.PointToScreen(new Point(VideoSurface.ActualWidth, VideoSurface.ActualHeight));
            Check("fullscreen_video_covers_monitor", Math.Abs(videoTopLeft.X - fullscreen.Left) < 1 && Math.Abs(videoTopLeft.Y - fullscreen.Top) < 1 &&
                Math.Abs(videoBottomRight.X - fullscreen.Right) < 1 && Math.Abs(videoBottomRight.Y - fullscreen.Bottom) < 1);
            Check("fullscreen_bars_in_video_overlay", TitleSlide.Child == TitleBar && ControlsSlide.Child == ControlBar &&
                Window.GetWindow(ControlBar) == Window.GetWindow(VideoOverlay) && TitleEdge.ActualHeight > 0 && ControlsEdge.ActualHeight > 0);
            var videoSize = VideoSurface.RenderSize;
            // Keep the physical pointer from triggering an unrelated hover during the probe.
            ControlsEdge.IsHitTestVisible = false;
            System.Windows.Input.Mouse.Synchronize();
            await Task.Delay(250);
            Check("fullscreen_progress_visible_before_seek", FullscreenProgress.IsVisible && FullscreenProgress.Value > 0 &&
                FullscreenProgress.ActualHeight == 5 && FullscreenProgress.Opacity == 0.75 && !FullscreenProgress.IsHitTestVisible);
            SlideFullscreenBar(ControlsSlide, true, ControlsEdge.ActualHeight);
            Check("fullscreen_progress_hidden_as_controls_enter", !FullscreenProgress.IsVisible);
            await Task.Delay(250);
            Check("controls_slide_over_stable_video", ControlsSlide.IsVisible && ((TranslateTransform)ControlsSlide.RenderTransform).Y == 0 && VideoSurface.RenderSize == videoSize);
            SlideFullscreenBar(ControlsSlide, false, ControlsEdge.ActualHeight);
            Check("fullscreen_progress_hidden_while_controls_leave", !FullscreenProgress.IsVisible);
            await Task.Delay(250);
            Check("controls_slide_away_without_resizing", !ControlsSlide.IsVisible && VideoSurface.RenderSize == videoSize);
            Check("fullscreen_progress_restored_after_controls_leave", FullscreenProgress.IsVisible);
            SeekByKey(1, false, false);
            var target = _seekTarget!.Value;
            Check("fullscreen_progress_updated_on_seek", FullscreenProgress.Value == target && FullscreenProgress.Maximum == _player!.Length);
            await Task.Delay(1500);
            var fill = (FrameworkElement)FullscreenProgress.Template.FindName("PART_Indicator", FullscreenProgress);
            var expectedWidth = FullscreenProgress.ActualWidth * FullscreenProgress.Value / FullscreenProgress.Maximum;
            Check("fullscreen_progress_persists_and_tracks_playback", FullscreenProgress.IsVisible && FullscreenProgress.Opacity == 0.75 &&
                FullscreenProgress.Value > target && Math.Abs(fill.ActualWidth - expectedWidth) < 1);
            ControlsEdge.IsHitTestVisible = true;
            Check("fullscreen_disables_resize", HitAt(fullscreen.Left + 1, fullscreen.Top + 1) == 1);
            Check("closed_playlist_stays_hidden_in_fullscreen", VideoUsesPlaylistSpace());
            ToggleFullscreen();
            await Task.Delay(150);
            ResizeProbeNative.GetWindowRect(hwnd, out var restored);
            Check("fullscreen_restores_resizing", HitAt(restored.Left + 1, restored.Top + 1) == 13,
                new { state = WindowState.ToString(), resizeMode = ResizeMode.ToString() });
            Check("fullscreen_restores_bounds", restored.Left == beforeFullscreen.Left && restored.Top == beforeFullscreen.Top && restored.Right == beforeFullscreen.Right && restored.Bottom == beforeFullscreen.Bottom,
                new { before = new { beforeFullscreen.Left, beforeFullscreen.Top, beforeFullscreen.Right, beforeFullscreen.Bottom }, after = new { restored.Left, restored.Top, restored.Right, restored.Bottom } });
            Check("closed_playlist_stays_hidden_on_return", VideoUsesPlaylistSpace());
            Check("windowed_bars_restored", TitleBar.Parent == WindowLayout && ControlBar.Parent == WindowLayout &&
                TitleBar.IsVisible && ControlBar.IsVisible && !FullscreenLayer.IsVisible && !VideoOverlay.ForceCursor);
            var closedWidth = Width;
            TogglePlaylist();
            UpdateLayout();
            var openWidth = Width;
            var columnWidth = PlaylistColumn.ActualWidth;
            PlaylistList.SelectedIndex = 0;
            var selected = PlaylistList.SelectedItem;
            ToggleFullscreen();
            UpdateLayout();
            Check("open_playlist_yields_full_width_to_video", columnWidth > 0 && VideoUsesPlaylistSpace());
            ToggleFullscreen();
            UpdateLayout();
            Check("open_playlist_restored_on_return", PlaylistPanel.IsVisible && PlaylistColumn.ActualWidth == columnWidth &&
                Width == openWidth && selected != null && PlaylistList.SelectedItem == selected && _player == player && _generation == generation);
            TogglePlaylist();
            Check("closing_restored_playlist_recovers_original_width", Width == closedWidth);
            Hide();
            Check("intentional_hide_still_works", !ResizeProbeNative.IsWindowVisible(hwnd));
            Show();
            Check("intentional_show_still_works", ResizeProbeNative.IsWindowVisible(hwnd));
        }
        catch (Exception ex) { Check("exception", false, ex.ToString()); }
        finally { source.RemoveHook(Observe); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { processId = Environment.ProcessId, passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
    private bool HasBlackIdleBackground()
    {
        // Render this window's video area. Sample all four corners, clear of the welcome text.
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(VideoSurface), null, new Rect(0, 0, 64, 64));
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var pixels = new int[64 * 64];
        bitmap.CopyPixels(pixels, 64 * 4, 0);
        const int black = unchecked((int)0xff000000);
        return pixels[65] == black && pixels[126] == black && pixels[3969] == black && pixels[4030] == black;
    }
}

internal static class ResizeProbeNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string className, string? name);
    [DllImport("user32.dll")] internal static extern bool IsChild(nint parent, nint child);
    [StructLayout(LayoutKind.Sequential)] internal struct StyleChange { public uint Old, New; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint value);
}
