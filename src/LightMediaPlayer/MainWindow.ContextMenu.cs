using System.Windows.Controls;
using LibVLCSharp.Shared;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private sealed record VideoFilter(string Label, float Saturation = 1f, float Brightness = 1f);
    private static readonly VideoFilter NoFilter = new("필터 없음");
    private static readonly VideoFilter[] VideoFilters =
        [NoFilter, new("채도 낮추기", Saturation: 0.35f), new("채도 높이기", Saturation: 1.5f), new("밝기 높이기", Brightness: 1.2f)];
    private VideoFilter _videoFilter = NoFilter;

    private ContextMenu BuildVideoMenu()
    {
        var menu = new ContextMenu();
        var playback = new DelayedSubmenuItem { Header = "재생" };
        var play = new MenuItem { Header = "재생", IsEnabled = _ready && (_player?.IsPlaying != true || _stopped || _ended) };
        play.Click += async (_, _) => await PlayFromMenuAsync();
        var pause = new MenuItem { Header = "일시 정지", IsEnabled = _player?.IsPlaying == true && !_stopped && !_ended };
        pause.Click += (_, _) => PauseFromMenu();
        var previous = new MenuItem { Header = "이전 목록 재생", IsEnabled = PreviousButton.IsEnabled };
        previous.Click += async (_, _) => await MoveFileAsync(-1);
        var next = new MenuItem { Header = "다음 목록 재생", IsEnabled = NextButton.IsEnabled };
        next.Click += async (_, _) => await MoveFileAsync(1);
        playback.Items.Add(play); playback.Items.Add(pause); playback.Items.Add(previous); playback.Items.Add(next);
        menu.Items.Add(playback);
        var subtitles = new DelayedSubmenuItem { Header = "자막" };
        var canLoadSubtitle = _player != null && !_stopped && !_ended;
        var openSubtitle = new MenuItem { Header = "자막 열기", InputGestureText = "Ctrl+Shift+O", IsEnabled = canLoadSubtitle };
        openSubtitle.Click += (_, _) => SubtitleDialog();
        var toggleSubtitle = new MenuItem { Header = "자막 표시/숨김", InputGestureText = "V", IsEnabled = _player?.SpuDescription.Any(t => t.Id >= 0) == true };
        toggleSubtitle.Click += (_, _) => ToggleSubtitles();
        var subtitleTracks = CreateSubtitleTrackMenu(delayed: true);
        subtitles.Items.Add(openSubtitle); subtitles.Items.Add(toggleSubtitle); subtitles.Items.Add(subtitleTracks);
        menu.Items.Add(subtitles);
        var video = new DelayedSubmenuItem { Header = "영상" };
        var videoTracks = CreateVideoTrackMenu();
        video.Items.Add(videoTracks);
        menu.Items.Add(video);
        var sound = new DelayedSubmenuItem { Header = "소리" };
        var mute = new MenuItem { Header = "음소거", InputGestureText = "M", IsCheckable = true, IsChecked = _muted };
        mute.Click += (_, _) => ToggleMute();
        var volumeUp = new MenuItem { Header = "음량 높이기", InputGestureText = "↑", IsEnabled = VolumeBar.Value < 100 };
        volumeUp.Click += (_, _) => ChangeVolume(5);
        var volumeDown = new MenuItem { Header = "음량 낮추기", InputGestureText = "↓", IsEnabled = VolumeBar.Value > 0 };
        volumeDown.Click += (_, _) => ChangeVolume(-5);
        var audioTracks = CreateAudioTrackMenu(delayed: true);
        sound.Items.Add(mute); sound.Items.Add(volumeUp); sound.Items.Add(volumeDown); sound.Items.Add(audioTracks);
        menu.Items.Add(sound);
        var filters = CreateFilterMenu();
        menu.Items.Add(filters);
        menu.Items.Add(new MenuItem { Header = "스킨", IsEnabled = false, ToolTip = "세부 메뉴 준비 중" });
        var aspectRatio = CreateAspectRatioMenu();
        menu.Items.Add(aspectRatio);
        var fullscreen = new MenuItem { Header = "전체 화면", IsCheckable = true, IsChecked = _fullscreen };
        fullscreen.Click += (_, _) => ToggleFullscreen();
        menu.Items.Add(fullscreen);
        var settings = new MenuItem { Header = "환경 설정" };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);
        menu.Closed += (_, _) =>
        {
            playback.Reset(); subtitles.Reset(); ((DelayedSubmenuItem)subtitleTracks).Reset();
            video.Reset();
            videoTracks.Reset();
            sound.Reset(); ((DelayedSubmenuItem)audioTracks).Reset();
            filters.Reset();
            aspectRatio.Reset();
        };
        return menu;
    }

    private DelayedSubmenuItem CreateFilterMenu()
    {
        var menu = new DelayedSubmenuItem { Header = "필터" };
        foreach (var filter in VideoFilters)
        {
            var item = new MenuItem { Header = filter.Label, IsCheckable = true, IsChecked = filter == _videoFilter };
            item.Click += (_, _) =>
            {
                _videoFilter = filter;
                if (_player != null) ApplyVideoFilter(_player);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private void ApplyVideoFilter(MediaPlayer player)
    {
        if (_videoFilter == NoFilter)
        {
            player.SetAdjustInt(VideoAdjustOption.Enable, 0);
            return;
        }
        player.SetAdjustFloat(VideoAdjustOption.Saturation, _videoFilter.Saturation);
        player.SetAdjustFloat(VideoAdjustOption.Brightness, _videoFilter.Brightness);
        player.SetAdjustInt(VideoAdjustOption.Enable, 1);
    }

    private DelayedSubmenuItem CreateAspectRatioMenu()
    {
        var menu = new DelayedSubmenuItem { Header = "화면 비율" };
        foreach (var ratio in new[] { string.Empty, "16:9", "16:10", "4:3" })
        {
            var item = new MenuItem
            {
                Header = ratio.Length == 0 ? "원본 비율" : ratio,
                IsCheckable = true,
                IsChecked = ratio == _aspectRatio
            };
            item.Click += (_, _) =>
            {
                _aspectRatio = ratio;
                if (_player != null) _player.AspectRatio = ratio;
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private DelayedSubmenuItem CreateVideoTrackMenu()
    {
        var menu = new DelayedSubmenuItem { Header = "비디오 트랙 선택" };
        var player = _player;
        var generation = _generation;
        var tracks = player?.VideoTrackDescription.ToArray() ?? [];
        menu.IsEnabled = tracks.Any(track => track.Id >= 0);
        var selectedId = player?.VideoTrack;
        foreach (var track in tracks)
        {
            var id = track.Id;
            var item = new MenuItem
            {
                Header = id == -1 ? "사용 안 함" : track.Name,
                IsCheckable = true,
                IsChecked = selectedId == id
            };
            item.Click += (_, _) =>
            {
                if (generation == _generation && _player is { } current && !current.SetVideoTrack(id))
                    SetStatus("비디오 트랙을 변경할 수 없습니다.", true);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private MenuItem CreateAudioTrackMenu(bool delayed = false)
    {
        MenuItem menu = delayed ? new DelayedSubmenuItem() : new MenuItem();
        menu.Header = delayed ? "오디오 트랙 선택" : "오디오 트랙";
        var generation = _generation;
        var tracks = _player?.AudioTrackDescription.ToArray() ?? [];
        menu.IsEnabled = tracks.Any(t => t.Id >= 0);
        foreach (var track in tracks)
        {
            var id = track.Id;
            var item = new MenuItem { Header = id == -1 ? "사용 안 함" : track.Name, IsCheckable = true, IsChecked = _player?.AudioTrack == id };
            item.Click += (_, _) => { if (_generation == generation) _player?.SetAudioTrack(id); };
            menu.Items.Add(item);
        }
        return menu;
    }
    private MenuItem CreateSubtitleTrackMenu(bool delayed = false)
    {
        MenuItem menu = delayed ? new DelayedSubmenuItem() : new MenuItem();
        menu.Header = delayed ? "자막 트랙 선택" : "자막 트랙";
        var generation = _generation;
        var tracks = _player?.SpuDescription.ToArray() ?? [];
        menu.IsEnabled = tracks.Any(t => t.Id >= 0);
        foreach (var track in tracks)
        {
            var id = track.Id;
            var item = new MenuItem { Header = id == -1 ? "사용 안 함" : track.Name, IsCheckable = true, IsChecked = _player?.Spu == id };
            item.Click += (_, _) =>
            {
                if (_generation != generation || _player == null) return;
                _player.SetSpu(id);
                if (id >= 0) _lastSubtitle = id;
            };
            menu.Items.Add(item);
        }
        return menu;
    }
    private async Task PlayFromMenuAsync()
    {
        if (!_ready || _closing) return;
        if (_path == null) { OpenDialog(); return; }
        if (_stopped || _ended) await OpenPathAsync(_path, _software, retainSubtitle: true);
        else _player?.SetPause(false);
    }
    private void PauseFromMenu()
    {
        if (_player?.IsPlaying == true && !_stopped && !_ended) _player.SetPause(true);
    }
}
