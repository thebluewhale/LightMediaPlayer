using System.Windows;
using System.Windows.Threading;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private readonly DispatcherTimer _osdTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private WindowMagnet? _magnet;
    private long? _seekTarget;
    private long _seekRequestedAt;

    private void ShowOsd(string text)
    {
        if (_closing) return;
        OsdText.Text = text;
        OsdBubble.Visibility = Visibility.Visible;
        _osdTimer.Stop();
        _osdTimer.Start();
    }
    internal static string FormatOsdTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }
    private long EffectiveSeekTime()
    {
        if (_seekTarget.HasValue && Environment.TickCount64 - _seekRequestedAt < 700) return _seekTarget.Value;
        return Math.Max(0, _player?.Time ?? 0);
    }
    private int SeekStep(bool ctrl, bool shift) => ctrl ? _settings.CtrlSeekSeconds : shift ? _settings.ShiftSeekSeconds : _settings.SeekSeconds;
    private void SeekByKey(int direction, bool ctrl, bool shift) => SeekTo(EffectiveSeekTime() + direction * (long)SeekStep(ctrl, shift) * 1000);
    private void RefreshSeekPreferences()
    {
        var saved = PlayerSettings.Load();
        _settings.SeekSeconds = saved.SeekSeconds;
        _settings.CtrlSeekSeconds = saved.CtrlSeekSeconds;
        _settings.ShiftSeekSeconds = saved.ShiftSeekSeconds;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void OpenSettings()
    {
        RefreshSeekPreferences();
        var dialog = new SettingsWindow(_settings, (plain, ctrl, shift) =>
        {
            PlayerSettings.Update(saved => { saved.SeekSeconds = plain; saved.CtrlSeekSeconds = ctrl; saved.ShiftSeekSeconds = shift; });
            _settings.SeekSeconds = plain;
            _settings.CtrlSeekSeconds = ctrl;
            _settings.ShiftSeekSeconds = shift;
            ShowOsd("이동시간 설정 저장됨");
        }) { Owner = this };
        dialog.ShowDialog();
    }
}
