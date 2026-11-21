using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private async Task RunSoundMenuTestsAsync(Action<string, bool, object?> check, Func<Func<bool>, int, Task<bool>> until)
    {
        void Check(string name, bool ok) => check(name, ok, null);
        var volume = VolumeBar.Value;
        var menu = BuildVideoMenu();
        var sound = (DelayedSubmenuItem)menu.Items[3];
        Check("sound_menu_four_actions", sound.Items.Cast<MenuItem>().Select(item => (string)item.Header).SequenceEqual(new[] { "음소거", "음량 높이기", "음량 낮추기", "오디오 트랙 선택" }));
        var mute = (MenuItem)sound.Items[0];
        mute.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_mute", await until(() => _player!.Mute && _muted, 3000) && OsdText.Text == "음소거 · 0%");
        Check("sound_menu_mute_checked", ((MenuItem)((MenuItem)BuildVideoMenu().Items[3]).Items[0]).IsChecked);
        var up = (MenuItem)sound.Items[1];
        var down = (MenuItem)sound.Items[2];
        up.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_volume_up_keeps_mute", await until(() => _player!.Volume == (int)volume + 5, 3000) && VolumeBar.Value == volume + 5 && _player!.Mute && OsdText.Text == $"{(int)volume + 5}%");
        down.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_volume_down", await until(() => _player!.Volume == (int)volume, 3000) && VolumeBar.Value == volume && OsdText.Text == $"{(int)volume}%");
        mute.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_unmute", await until(() => !_player!.Mute && !_muted, 3000) && !((MenuItem)((MenuItem)BuildVideoMenu().Items[3]).Items[0]).IsChecked);
        VolumeBar.Value = 98;
        up.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_volume_upper_limit", await until(() => _player!.Volume == 100, 3000) && !((MenuItem)((MenuItem)BuildVideoMenu().Items[3]).Items[1]).IsEnabled);
        VolumeBar.Value = 2;
        down.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_volume_lower_limit", await until(() => _player!.Volume == 0, 3000) && !((MenuItem)((MenuItem)BuildVideoMenu().Items[3]).Items[2]).IsEnabled);
        VolumeBar.Value = volume;

        var tracks = (DelayedSubmenuItem)sound.Items[3];
        var descriptions = _player!.AudioTrackDescription.ToArray();
        Check("sound_menu_track_list", tracks.Items.Count == descriptions.Length && tracks.Items.Cast<MenuItem>().Count(item => item.IsChecked) == 1);
        menu.PlacementTarget = TracksButton;
        menu.IsOpen = true;
        await until(() => sound.IsLoaded, 3000);
        sound.IsSubmenuOpen = true;
        Check("sound_menu_blocks_early_open", !sound.IsSubmenuOpen);
        using (var hover = new HoverIntent(sound.OpenFromInput))
        {
            hover.Start();
            await Task.Delay(500);
            Check("sound_menu_waits_one_second", !sound.IsSubmenuOpen);
            Check("sound_menu_delayed_open", await until(() => sound.IsSubmenuOpen, 1500));
        }
        sound.Reset();
        sound.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = PreviewMouseLeftButtonDownEvent, Source = sound });
        Check("sound_menu_click_opens", sound.IsSubmenuOpen);
        await until(() => tracks.IsLoaded, 3000);
        tracks.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = PreviewMouseLeftButtonDownEvent, Source = tracks });
        Check("sound_track_submenu_click_opens", tracks.IsSubmenuOpen);
        var disabledIndex = Array.FindIndex(descriptions, track => track.Id == -1);
        ((MenuItem)tracks.Items[disabledIndex]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_disable_track", await until(() => _player.AudioTrack == -1, 3000));
        var selectedIndex = Array.FindLastIndex(descriptions, track => track.Id >= 0);
        ((MenuItem)tracks.Items[selectedIndex]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("sound_menu_select_track", await until(() => _player.AudioTrack == descriptions[selectedIndex].Id, 3000));
        var refreshedTracks = (MenuItem)((MenuItem)BuildVideoMenu().Items[3]).Items[3];
        Check("sound_menu_selected_track_checked", ((MenuItem)refreshedTracks.Items[selectedIndex]).IsChecked);
        menu.IsOpen = false;
        Check("sound_menu_close_resets_nested", await until(() => !sound.IsSubmenuOpen && !tracks.IsSubmenuOpen, 2000));
    }
}
