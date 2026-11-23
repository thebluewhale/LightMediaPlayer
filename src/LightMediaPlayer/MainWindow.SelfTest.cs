using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using LibVLCSharp.Shared;

namespace LightMediaPlayer;

public partial class MainWindow
{
    // Opt-in integration probe: uses the same WPF window, packaged engine and commands.
    // Invoked only by scripts/test.ps1; no separate fake playback implementation.
    private async Task RunSelfTestAsync()
    {
        var index = Array.IndexOf(_arguments, "--self-test-report");
        var report = _arguments[index + 1];
        var file = _arguments[index + 2];
        var checks = new List<object>();
        var failed = false;
        void Check(string name, bool ok, object? details = null)
        {
            checks.Add(new { name, passed = ok, details });
            AppLog.Write($"CHECK {name}: {ok}");
            if (!ok) failed = true;
        }
        async Task<bool> Until(Func<bool> predicate, int timeout = 15000)
        {
            var start = Environment.TickCount64;
            while (Environment.TickCount64 - start < timeout) { if (predicate()) return true; await Task.Delay(100); }
            return predicate();
        }
        async Task<bool> UntilStable(Func<bool> predicate, int timeout = 15000)
        {
            // LibVLC track getters may briefly report a requested track before
            // the decoder has finished switching. Require a sustained result.
            long? since = null;
            return await Until(() =>
            {
                if (!predicate()) { since = null; return false; }
                since ??= Environment.TickCount64;
                return Environment.TickCount64 - since.Value >= 200;
            }, timeout);
        }
        try
        {
            await OpenPathAsync(file);
            Check("initial_playlist_contains_only_opened_file", _queue.Count == 1 && _queue[0].Path == Path.GetFullPath(file));
            var initialGeneration = _generation;
            await MoveFileAsync(1);
            await MoveFileAsync(-1);
            Check("single_item_navigation_does_not_discover_siblings", _generation == initialGeneration && _queue.Count == 1 && !PreviousButton.IsEnabled && !NextButton.IsEnabled);
            Check("native_playback_started", await Until(() => _player?.IsPlaying == true && _player.Time > 700), _path);
            Check("video_and_audio_tracks", _player!.VideoTrackCount > 0 && _player.AudioTrackCount >= 2, new { video = _player.VideoTrackCount, audio = _player.AudioTrackCount });
            Check("duration_and_seek", _player.Length > 15000 && _player.IsSeekable, _player.Length);
            var prior = _player.Time;
            await Task.Delay(700);
            Check("playback_clock_advances", _player.Time > prior);
            Check("video_frames_displayed", await Until(() => _media!.Statistics.DisplayedPictures > 0), _media!.Statistics.DisplayedPictures);
            Check("audio_decoded", _media.Statistics.DecodedAudio > 0, _media.Statistics.DecodedAudio);
            await RunAspectRatioMenuTestsAsync(Check, Until);
            await TogglePlaybackAsync();
            Check("pause_state", await Until(() => _player!.State == VLCState.Paused));
            await Task.Delay(200);
            prior = _player!.Time;
            await Task.Delay(600);
            Check("paused_clock_stable", Math.Abs(_player.Time - prior) < 150);
            Check("pause_button_becomes_play", (string)PlayButton.Content == "\uE768");
            SeekTo(9000);
            Check("seek_while_paused", await Until(() => Math.Abs(_player!.Time - 9000) < 1600));
            await TogglePlaybackAsync();
            Check("resume_playback", await Until(() => _player!.IsPlaying));
            VolumeBar.Value = 32;
            Check("volume", await Until(() => _player!.Volume == 32), _player!.Volume);
            if (!_muted) ToggleMute();
            Check("mute", await Until(() => _player.Mute), _player.Mute);
            ToggleMute();
            Check("unmute", await Until(() => !_player.Mute), _player.Mute);
            var tracks = _player.AudioTrackDescription.Where(t => t.Id >= 0).ToArray();
            if (tracks.Length >= 2) { _player.SetAudioTrack(tracks[1].Id); Check("audio_track_selection", await Until(() => _player!.AudioTrack == tracks[1].Id)); }
            else Check("audio_track_selection", false);
            await RunSoundMenuTestsAsync(Check, UntilStable);
            var sub = Path.ChangeExtension(file, ".srt");
            if (File.Exists(sub)) await LoadSubtitleAsync(sub);
            Check("subtitle_load", await UntilStable(() => _player!.SpuDescription.Any(t => t.Id >= 0)));
            var subId = _player.SpuDescription.FirstOrDefault(t => t.Id >= 0).Id;
            _player.SetSpu(subId);
            await UntilStable(() => _player.Spu == subId);
            ToggleSubtitles();
            Check("subtitle_hide", await UntilStable(() => _player.Spu == -1));
            ToggleSubtitles();
            Check("subtitle_show", await UntilStable(() => _player!.Spu >= 0));
            await RunSubtitleMenuTestsAsync(Check, UntilStable);
            void PressKey(Key key) => RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, key) { RoutedEvent = PreviewKeyDownEvent });
            WindowState = WindowState.Normal;
            var before = new Rect(Left, Top, Width, Height);
            PressKey(Key.Enter);
            await Task.Delay(300);
            Check("fullscreen", _fullscreen && TitleBar.Visibility == Visibility.Collapsed && WindowState == WindowState.Maximized);
            PressKey(Key.Enter);
            await Task.Delay(300);
            Check("fullscreen_restore", !_fullscreen && TitleBar.Visibility == Visibility.Visible && new Rect(Left, Top, Width, Height) == before);
            PressKey(Key.Enter);
            PressKey(Key.Escape);
            Check("fullscreen_escape_minimizes", !_fullscreen && WindowState == WindowState.Minimized && TitleBar.Visibility == Visibility.Visible);
            WindowState = WindowState.Normal;
            Check("escape_restore_bounds", new Rect(Left, Top, Width, Height) == before);
            Check("original_aspect_ratio", _player.Scale == 0 && string.IsNullOrEmpty(_player.AspectRatio));
            await StopPlaybackAsync();
            Check("stop_releases_decoder", _player == null && _stopped && SeekBar.Value == 0);
            var stoppedSound = (System.Windows.Controls.MenuItem)BuildVideoMenu().Items[3];
            Check("sound_tracks_without_player_disabled", !((System.Windows.Controls.MenuItem)stoppedSound.Items[3]).IsEnabled && ((System.Windows.Controls.MenuItem)stoppedSound.Items[0]).IsEnabled);
            var stoppedSubtitles = (System.Windows.Controls.MenuItem)BuildVideoMenu().Items[1];
            Check("subtitle_menu_without_player_disabled", stoppedSubtitles.Items.Cast<System.Windows.Controls.MenuItem>().All(item => !item.IsEnabled));
            await TogglePlaybackAsync();
            Check("restart_from_beginning", await Until(() => _player?.IsPlaying == true && _player.Time > 100), _player?.Time);
            Check("restart_position", _player!.Time < 4000);
            await OpenPathAsync(file, software: true, resume: 6000);
            Check("software_decode_reopen", await Until(() => _player?.IsPlaying == true && _player.Time >= 5500), _player?.Time);
            await RunFilterMenuTestsAsync(report, Check, Until);
            var original = _path;
            await OpenPathAsync(Path.Combine(Path.GetDirectoryName(file)!, "does-not-exist.mp4"));
            Check("missing_file_keeps_current_playback", _path == original && _player!.IsPlaying && StatusText.Text.Contains("찾을 수"));
            // Hold playback so two concurrently launched probes overlap in time.
            AppLog.Write("CONCURRENCY_START");
            await Task.Delay(3500);
            Check("independent_playback_alive", _player!.IsPlaying);
            AppLog.Write("CONCURRENCY_END");
            SeekTo(_player.Length - 1000);
            Check("end_of_file", await Until(() => _ended, 7000));
            Check("end_button_becomes_play", (string)PlayButton.Content == "\uE768");
            await TogglePlaybackAsync();
            Check("replay_after_end", await Until(() => _player?.IsPlaying == true && !_ended));
            var dir = Path.GetDirectoryName(file)!;
            var selection = new[] { Path.Combine(dir, "영상10.mp4"), Path.Combine(dir, "음악.flac"), Path.Combine(dir, "영상2.mkv") };
            await OpenFilesAsync(selection);
            Check("selection_plays_first_and_queues_in_order", _path == selection[0] && _neighbors.SequenceEqual(selection) && await Until(() => _player?.IsPlaying == true));
            ReplaceQueue([]);
            await OpenPathAsync(Path.Combine(dir, "영상2.mkv"));
            await Until(() => _player?.IsPlaying == true);
            AddPlaylistFiles([Path.Combine(dir, "영상10.mp4")]);
            Check("explicit_add_preserves_current_and_appends", _queue.Count == 2 && _queue[0].Path == _path && Path.GetFileName(_queue[1].Path) == "영상10.mp4");
            await MoveFileAsync(1);
            Check("next_file_from_explicit_playlist", Path.GetFileName(_path) == "영상10.mp4" && await Until(() => _player?.IsPlaying == true));
            await MoveFileAsync(-1);
            Check("previous_file", Path.GetFileName(_path) == "영상2.mkv" && await Until(() => _player?.IsPlaying == true));
            await OpenPathAsync(Path.Combine(dir, "음악.flac"));
            Check("audio_only_playback", await Until(() => _player?.IsPlaying == true && EmptyTitle.Text == "음악 재생" && EmptyState.Visibility == Visibility.Visible));
            await RunVideoTrackMenuTestsAsync(Path.Combine(dir, "two-video-tracks.mkv"), Check, Until);
            await OpenPathAsync(Path.Combine(dir, "sample.m3u"));
            Check("local_playlist_open", Path.GetFileName(_path) == "영상2.mkv" && _neighbors.Length == 2 && await Until(() => _player?.IsPlaying == true));
            var koreanSubtitle = Path.Combine(dir, "cp949.srt");
            var normalized = await NormalizeSubtitleAsync(koreanSubtitle);
            Check("korean_subtitle_encoding", (await File.ReadAllTextAsync(normalized)).Contains("한글 자막"));
            using (var heldSubtitle = File.Open(normalized, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check("subtitle_cache_reused_while_in_use", await NormalizeSubtitleAsync(koreanSubtitle) == normalized);
            var concurrentSubtitle = Path.Combine(Path.GetDirectoryName(report)!, $"concurrent-{Environment.ProcessId}.srt");
            var subtitleText = $"1\n00:00:00,000 --> 00:00:01,000\n한글 {Guid.NewGuid():N}\n";
            await File.WriteAllTextAsync(concurrentSubtitle, subtitleText, Encoding.Unicode);
            var concurrentPaths = await Task.WhenAll(NormalizeSubtitleAsync(concurrentSubtitle), NormalizeSubtitleAsync(concurrentSubtitle));
            Check("subtitle_cache_parallel_publish", concurrentPaths[0] == concurrentPaths[1] && await File.ReadAllTextAsync(concurrentPaths[0]) == subtitleText);
            await LoadSubtitleAsync(koreanSubtitle);
            Check("external_subtitle_attachment", _externalSubtitle == koreanSubtitle);
            var settingsPath = Path.Combine(Path.GetDirectoryName(report)!, $"settings-{Environment.ProcessId}.json");
            new PlayerSettings { Volume = 43, Muted = true, Width = 880, Height = 520, Left = 40, Top = 50 }.Save(settingsPath);
            var settings = PlayerSettings.Load(settingsPath);
            Check("settings_round_trip", settings.Volume == 43 && settings.Muted && settings.Width == 880 && settings.Top == 50);
            await File.WriteAllTextAsync(settingsPath, "invalid json");
            Check("corrupt_settings_recovery", PlayerSettings.Load(settingsPath).Volume == 75);
            await RunInteractionTestsAsync(file, report, Check, Until);
            var invalid = Path.Combine(Path.GetDirectoryName(file)!, "broken.mp4");
            if (File.Exists(invalid))
            {
                await OpenPathAsync(invalid);
                Check("unsupported_file_error_and_fallback", await Until(() => _software && _stopped && StatusText.Text.Contains("지원하지")), StatusText.Text);
            }
        }
        catch (Exception ex) { failed = true; checks.Add(new { name = "exception", passed = false, details = ex.ToString() }); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { processId = Environment.ProcessId, passed = !failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}
