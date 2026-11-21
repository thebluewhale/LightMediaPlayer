using System.Windows.Controls;

namespace LightMediaPlayer;

public partial class MainWindow
{
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
        foreach (var title in new[] { "자막", "영상", "소리", "필터", "스킨", "화면 비율" })
            menu.Items.Add(new MenuItem { Header = title, IsEnabled = false, ToolTip = "세부 메뉴 준비 중" });
        var fullscreen = new MenuItem { Header = "전체 화면", IsCheckable = true, IsChecked = _fullscreen };
        fullscreen.Click += (_, _) => ToggleFullscreen();
        menu.Items.Add(fullscreen);
        var settings = new MenuItem { Header = "환경 설정" };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);
        menu.Closed += (_, _) => playback.Reset();
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
