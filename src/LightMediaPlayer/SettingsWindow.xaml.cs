using System.Windows;
using System.Windows.Controls;

namespace LightMediaPlayer;

public partial class SettingsWindow : Window
{
    private readonly Action<int, int, int> _save;
    internal SettingsWindow(PlayerSettings settings, Action<int, int, int> save)
    {
        _save = save;
        InitializeComponent();
        PlainSeconds.Text = settings.SeekSeconds.ToString();
        CtrlSeconds.Text = settings.CtrlSeekSeconds.ToString();
        ShiftSeconds.Text = settings.ShiftSeekSeconds.ToString();
        Validate();
    }
    internal static bool TrySeconds(string text, out int seconds) => int.TryParse(text, out seconds) && seconds is >= 1 and <= 86400;
    internal bool Validate()
    {
        if (SaveButton == null || PlainSeconds == null || CtrlSeconds == null || ShiftSeconds == null) return false;
        var valid = TrySeconds(PlainSeconds.Text, out _) && TrySeconds(CtrlSeconds.Text, out _) && TrySeconds(ShiftSeconds.Text, out _);
        SaveButton.IsEnabled = valid;
        ValidationText.Text = valid ? "" : "이동시간은 1부터 86,400까지의 정수로 입력해 주세요.";
        return valid;
    }
    private void Seconds_Changed(object sender, TextChangedEventArgs e) => Validate();
    private void Step_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((string)((Button)sender).Tag).Split(':');
        var input = parts[0] switch { "plain" => PlainSeconds, "ctrl" => CtrlSeconds, _ => ShiftSeconds };
        var value = int.TryParse(input.Text, out var parsed) ? parsed : 1;
        input.Text = Math.Clamp((long)value + int.Parse(parts[1]), 1, 86400).ToString();
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        try { _save(int.Parse(PlainSeconds.Text), int.Parse(CtrlSeconds.Text), int.Parse(ShiftSeconds.Text)); DialogResult = true; }
        catch (Exception ex) { ValidationText.Text = "설정 저장 실패: " + ex.Message; }
    }
}
