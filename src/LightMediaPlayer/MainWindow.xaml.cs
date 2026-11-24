using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace LightMediaPlayer;

public partial class MainWindow : Window
{
    private readonly string[] _arguments;
    private readonly PlayerSettings _settings = PlayerSettings.Load();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _startTitleTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _startTitleShown;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private LibVLC? _vlc;
    private VlcPlayer? _player;
    private Media? _media;
    private LibVLCSharp.WPF.VideoView? _videoView;
    private string _aspectRatio = string.Empty;
    private string? _path;
    private int _generation;
    private bool _ready, _closing, _canClose, _seeking, _stopped = true, _ended, _fullscreen, _software;
    private bool _muted;
    private bool _hasPlayableStreams;
    private int _lastSubtitle = -1;
    private long _pendingResume;
    private bool _pendingPause;
    private string? _externalSubtitle;
    private WindowState _beforeFullscreen;
    private Rect _normalBounds;
    private PlayerWindowFrame? _frame;

    public MainWindow(string[] arguments)
    {
        _arguments = arguments;
        InitializeComponent();
        FullscreenProgress.DataContext = SeekBar;
        StartTitle.DataContext = MediaTitle;
        _startTitleTimer.Tick += (_, _) => HideStartTitle();
        PlaylistList.ItemsSource = _queue;
        SizeChanged += (_, _) => { if (_playlistOpen && !_fullscreen) UpdatePlaylistLayout(); };
        _osdTimer.Tick += (_, _) => { OsdBubble.Visibility = Visibility.Collapsed; _osdTimer.Stop(); };
        _pointerIdle.Tick += (_, _) =>
        {
            _pointerIdle.Stop();
            if (_fullscreen && VideoOverlay.IsMouseOver)
            {
                VideoOverlay.Cursor = Cursors.None;
                VideoOverlay.ForceCursor = true;
            }
        };
        SourceInitialized += (_, _) =>
        {
            _frame = new PlayerWindowFrame(this, TitleBar, TitleButtons, () => _fullscreen);
            _magnet = new WindowMagnet(this, () => !_fullscreen);
        };
        Activated += (_, _) => { if (!_arguments.Contains("--self-test-report")) RefreshSeekPreferences(); };
        VideoOverlay.Loaded += (_, _) =>
        {
            var overlayWindow = Window.GetWindow(VideoOverlay);
            if (overlayWindow != null && overlayWindow != this)
            { overlayWindow.PreviewKeyDown -= Window_KeyDown; overlayWindow.PreviewKeyDown += Window_KeyDown; }
        };
        Width = Math.Min(_settings.Width, SystemParameters.VirtualScreenWidth);
        Height = Math.Min(_settings.Height, SystemParameters.VirtualScreenHeight);
        if (_settings.Left is double left && _settings.Top is double top &&
            left + Width > SystemParameters.VirtualScreenLeft + 80 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
            top >= SystemParameters.VirtualScreenTop && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
        { Left = left; Top = top; WindowStartupLocation = WindowStartupLocation.Manual; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_settings.Maximized) WindowState = WindowState.Maximized;
        VolumeBar.Value = _settings.Volume;
        _muted = _settings.Muted;
        RefreshVolume();
        _timer.Tick += (_, _) => Tick();
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        UpdateButtons();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("재생 엔진을 준비하고 있습니다…");
            var runtime = await Task.Run(NativeRuntime.Prepare);
            if (_closing) return;
            Core.Initialize(runtime);
            _vlc = new LibVLC("--no-video-title-show", "--no-snapshot-preview", "--no-osd", "--file-caching=500");
            _vlc.Log += (_, log) => { if (log.Level >= LogLevel.Warning) AppLog.Write($"VLC {log.Module}: {log.Message}"); };
            _ready = true;
            _timer.Start();
            SetStatus("준비됨 · 파일을 열거나 끌어 놓으세요");
            AppLog.Write("READY " + Environment.ProcessId);
            if (_arguments.Contains("--self-test-report"))
            {
                if (_arguments.Contains("--resize-only")) await RunResizeTestAsync();
                else await RunSelfTestAsync();
                return;
            }
            await OpenFilesAsync(_arguments.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray());
        }
        catch (Exception ex) { ShowError("재생 엔진을 시작할 수 없습니다.", ex); }
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
        StatusText.Foreground = new SolidColorBrush(error ? Color.FromRgb(255, 151, 145) : Color.FromRgb(139, 154, 169));
    }

    private void ShowError(string message, Exception? error = null)
    {
        var details = error == null ? message : message + " " + error.Message;
        AppLog.Write(details);
        SetStatus(details, true);
        EmptyState.Visibility = Visibility.Visible;
        EmptyTitle.Text = "파일을 재생할 수 없습니다";
        EmptyDetail.Text = details;
        EmptyOpenButton.Visibility = Visibility.Visible;
    }

    private void Post(int generation, Action action)
    {
        if (_closing || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() => { if (!_closing && generation == _generation) action(); });
    }

    private async Task OpenPathAsync(string input, bool software = false, long resume = 0, bool pause = false, bool retainSubtitle = false, PlaylistEntry? entry = null)
    {
        if (!_ready || _closing) return;
        string path;
        try { path = Path.GetFullPath(input.Trim('"')); }
        catch (Exception ex) { SetStatus("잘못된 파일 경로: " + ex.Message, true); return; }
        if (!File.Exists(path)) { SetStatus("파일을 찾을 수 없습니다: " + path, true); return; }
        if (MediaFiles.Subtitles.Contains(Path.GetExtension(path))) { await LoadSubtitleAsync(path); return; }
        try
        {
            var entries = PlaylistFiles.Read(path);
            if (entries != null)
            {
                if (entries.Length == 0) { SetStatus("목록에 재생할 로컬 미디어 파일이 없습니다.", true); return; }
                ReplaceQueue(entries);
                await OpenPathAsync(entries[0], software, resume, pause, retainSubtitle, _queue[0]);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        { SetStatus("재생목록을 읽지 못했습니다: " + ex.Message, true); return; }
        await _operations.WaitAsync();
        try
        {
            if (_closing) return;
            ++_generation;
            await ReleasePlayerAsync();
            if (_closing) return;
            _path = path;
            _software = software;
            _pendingResume = resume;
            _pendingPause = pause;
            _stopped = false;
            _ended = false;
            _lastSubtitle = -1;
            _hasPlayableStreams = false;
            _seeking = false;
            _seekTarget = null;
            if (!retainSubtitle) _externalSubtitle = null;
            entry ??= retainSubtitle && _currentEntry?.Path == path ? _currentEntry : _queue.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                // Opening a media file never discovers or queues its siblings.
                // Additional entries come only from an explicit user selection/import.
                ReplaceQueue([path]);
                entry = _queue[0];
            }
            _currentEntry = entry;
            RefreshQueue();
            FileText.Text = Path.GetFileName(path);
            FileText.ToolTip = path;
            MediaTitle.Text = Path.GetFileNameWithoutExtension(path);
            if (MediaTitle.Text.Length == 0) MediaTitle.Text = FileText.Text;
            Title = MediaTitle.Text + " — Light Media Player";
            SeekBar.Value = 0;
            TimeText.Text = "00:00 / 00:00";
            SetStatus(software ? "소프트웨어 디코딩으로 여는 중…" : "파일을 여는 중…");
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitle.Text = "불러오는 중";
            EmptyDetail.Text = Path.GetFileName(path);
            EmptyOpenButton.Visibility = Visibility.Collapsed;
            _media = new Media(_vlc!, path, FromType.FromPath);
            _media.AddOption(software ? ":avcodec-hw=none" : ":avcodec-hw=any");
            _media.AddOption(":sub-autodetect-file");
            var subtitle = _externalSubtitle ?? MediaFiles.FindSubtitle(path);
            if (subtitle != null)
            {
                var normalized = await NormalizeSubtitleAsync(subtitle);
                _media.AddSlave(MediaSlaveType.Subtitle, 4, new Uri(normalized));
            }
            _player = new VlcPlayer(_vlc!) { EnableHardwareDecoding = !software, EnableKeyInput = false, EnableMouseInput = false };
            var current = _player;
            var generation = _generation;
            current.Opening += (_, _) => Post(generation, () => SetStatus("파일을 여는 중…"));
            current.Buffering += (_, args) => Post(generation, () => { if (args.Cache < 100) SetStatus($"버퍼링 {args.Cache:0}%"); });
            current.Playing += (_, _) => Post(generation, OnPlaying);
            current.Paused += (_, _) => Post(generation, () => { SetStatus("일시정지"); ShowOsd("일시정지 · " + FormatOsdTime(current.Time)); UpdateButtons(); });
            current.Stopped += (_, _) => Post(generation, () => { _stopped = true; UpdateButtons(); });
            current.EndReached += (_, _) => Post(generation, async () =>
            {
                if (!_hasPlayableStreams && !HasPlayableStreams()) { HandlePlaybackError(generation); return; }
                HideStartTitle();
                _ended = true; SetStatus("재생 완료"); UpdateButtons(); AppLog.Write("ENDED");
                if (NavigationTarget(1, wrap: true) == null) return;
                await Task.Delay(TimeSpan.FromSeconds(2));
                // Stop, replay, another open or closing invalidates the completed player.
                // Resolve the next entry after the wait so playlist edits take effect.
                if (!_closing && generation == _generation && _ended)
                    await MoveFileAsync(1, wrap: true);
            });
            current.EncounteredError += (_, _) => Post(generation, () => HandlePlaybackError(generation));
            if (_videoView == null)
            {
                // The native video host paints its system background before it has
                // video. Keep the welcome screen in the main WPF window until now.
                VideoSurface.Children.Remove(VideoOverlay);
                _videoView = new LibVLCSharp.WPF.VideoView { Content = VideoOverlay };
                VideoSurface.Children.Add(_videoView);
                VideoSurface.UpdateLayout();
            }
            _videoView.MediaPlayer = current;
            current.Volume = (int)VolumeBar.Value;
            current.Mute = _muted;
            current.Scale = 0; // Fit the selected display ratio within the video surface.
            current.AspectRatio = _aspectRatio;
            if (_videoFilter != NoFilter) ApplyVideoFilter(current);
            if (!current.Play(_media)) HandlePlaybackError(generation);
            UpdateButtons();
            AppLog.Write("OPEN " + path);
        }
        catch (Exception ex) { _stopped = true; ShowError("미디어를 열지 못했습니다.", ex); UpdateButtons(); }
        finally { _operations.Release(); }
    }

    private void OnPlaying()
    {
        _stopped = false;
        _ended = false;
        _player!.Volume = (int)VolumeBar.Value;
        _player.Mute = _muted;
        SetStatus(_software ? "재생 중 · 소프트웨어 디코딩" : "재생 중");
        ShowOsd("재생 · " + FormatOsdTime(_player.Time));
        UpdateButtons();
        AppLog.Write("PLAYING " + _path);
    }

    private int _fallbackGeneration = -1;
    private async void HandlePlaybackError(int generation)
    {
        if (_closing || generation != _generation || _fallbackGeneration == generation) return;
        _fallbackGeneration = generation;
        if (!_software && _path != null)
        {
            var resume = Math.Max(0, _player?.Time ?? 0);
            SetStatus("재생 오류 · 소프트웨어 디코딩으로 다시 시도합니다");
            await OpenPathAsync(_path, true, resume, retainSubtitle: true);
        }
        else
        {
            _stopped = true;
            HideStartTitle();
            ShowError("파일이 손상되었거나 지원하지 않는 형식입니다. DRM 파일과 일부 전용 형식은 재생할 수 없습니다.");
            UpdateButtons();
        }
    }

    private async Task ReleasePlayerAsync()
    {
        HideStartTitle();
        _startTitleShown = false;
        if (_videoView != null) _videoView.MediaPlayer = null;
        var player = _player;
        var media = _media;
        _player = null;
        _media = null;
        // Stop can block while VLC joins decoder threads. Never block the WPF dispatcher.
        await Task.Run(() => { if (player != null) { player.Stop(); player.Dispose(); } media?.Dispose(); });
    }

    private async Task TogglePlaybackAsync()
    {
        if (!_ready || _closing) return;
        if (_path == null) { OpenDialog(); return; }
        if (_stopped || _ended) { await OpenPathAsync(_path, _software, retainSubtitle: true); return; }
        if (_player != null) _player.SetPause(_player.IsPlaying);
    }

    private async Task StopPlaybackAsync()
    {
        if (_player == null) return;
        await _operations.WaitAsync();
        try
        {
            ++_generation;
            await ReleasePlayerAsync();
            _stopped = true;
            _ended = false;
            _pendingResume = 0;
            _pendingPause = false;
            SeekBar.Value = 0;
            SeekBar.IsEnabled = false;
            TimeText.Text = "00:00 / 00:00";
            SetStatus("정지 · 재생하면 처음부터 시작합니다");
            ShowOsd("정지 · 00:00:00");
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitle.Text = "재생이 정지되었습니다";
            EmptyDetail.Text = Path.GetFileName(_path);
            EmptyOpenButton.Visibility = Visibility.Collapsed;
            UpdateButtons();
            AppLog.Write("STOPPED");
        }
        finally { _operations.Release(); }
    }

    private void Tick()
    {
        if (_player == null || _closing) return;
        var length = Math.Max(0, _player.Length);
        var time = Math.Max(0, _player.Time);
        _hasPlayableStreams |= HasPlayableStreams();
        if (_pendingResume > 0 && _player.IsSeekable && length > 0)
        { _player.Time = Math.Min(_pendingResume, length - 1); _pendingResume = 0; }
        if (_pendingPause && _player.IsPlaying && _pendingResume == 0)
        { _pendingPause = false; _player.SetPause(true); }
        SeekBar.IsEnabled = _player.IsSeekable && length > 0 && !_stopped && !_ended;
        SeekBar.Maximum = Math.Max(1, length);
        if (!_seeking) { SeekBar.Value = Math.Min(time, SeekBar.Maximum); TimeText.Text = $"{FormatTime(time)} / {FormatTime(length)}"; }
        if (_player.IsPlaying || _player.State == VLCState.Paused)
        {
            var audioOnly = !_player.VideoTrackDescription.Any(t => t.Id >= 0);
            EmptyState.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
            if (audioOnly) { EmptyTitle.Text = "음악 재생"; EmptyDetail.Text = Path.GetFileName(_path); EmptyOpenButton.Visibility = Visibility.Collapsed; }
            else if (!_stopped && !_ended)
            {
                if (!_startTitleShown)
                {
                    _startTitleShown = true;
                    StartTitleBubble.Visibility = Visibility.Visible;
                    _startTitleTimer.Start();
                }
                if (_startTitleTimer.IsEnabled)
                    StartDuration.Text = length > 0 ? $"({FormatOsdTime(length)})" : "(--:--:--)";
            }
        }
    }

    private void HideStartTitle()
    {
        _startTitleTimer.Stop();
        StartTitleBubble.Visibility = Visibility.Collapsed;
    }

    private bool HasPlayableStreams() => _media?.Tracks.Any(t => t.TrackType is TrackType.Video or TrackType.Audio) == true;

    internal static string FormatTime(long milliseconds)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    private void UpdateButtons()
    {
        var playing = _player?.IsPlaying == true && !_ended && !_stopped;
        PlayButton.Content = playing ? "\uE769" : "\uE768";
        PlayButton.ToolTip = playing ? "일시정지 (Space)" : "재생 (Space)";
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, playing ? "일시정지" : "재생");
        StopButton.IsEnabled = _path != null && !_stopped;
        PreviousButton.IsEnabled = NavigationTarget(-1) != null;
        NextButton.IsEnabled = NavigationTarget(1) != null;
        if (PlaylistPrevious != null) PlaylistPrevious.IsEnabled = PreviousButton.IsEnabled;
        if (PlaylistNext != null) PlaylistNext.IsEnabled = NextButton.IsEnabled;
    }

    private async Task MoveFileAsync(int direction, bool wrap = false)
    {
        if (NavigationTarget(direction, wrap) is { } next) await PlayEntryAsync(next);
    }

    private void SeekTo(long position)
    {
        if (_player?.IsSeekable == true && !_stopped && !_ended)
        {
            var before = EffectiveSeekTime();
            var duration = _player.Length;
            var target = Math.Clamp(position, 0, Math.Max(0, duration - 1));
            _player.Time = target;
            _seekTarget = target;
            _seekRequestedAt = Environment.TickCount64;
            SeekBar.Maximum = Math.Max(1, duration);
            SeekBar.Value = target;
            var delta = target - before;
            var progress = duration > 0 ? $", ({Math.Round(100d * target / duration, MidpointRounding.AwayFromZero)}%)" : string.Empty;
            ShowOsd($"{Math.Abs(delta) / 1000:0}초 {(delta >= 0 ? "앞으로" : "뒤로")}, {FormatOsdTime(target)}{progress}");
        }
    }
    private void SeekAt(MouseEventArgs e)
    {
        var fraction = Math.Clamp((e.GetPosition(SeekBar).X - 6) / Math.Max(1, SeekBar.ActualWidth - 12), 0, 1);
        SeekBar.Value = fraction * SeekBar.Maximum;
        TimeText.Text = $"{FormatTime((long)SeekBar.Value)} / {FormatTime((long)SeekBar.Maximum)}";
    }
    private void Seek_MouseDown(object sender, MouseButtonEventArgs e) { if (!SeekBar.IsEnabled) return; _seeking = true; SeekBar.CaptureMouse(); SeekAt(e); e.Handled = true; }
    private void Seek_MouseMove(object sender, MouseEventArgs e) { if (_seeking) SeekAt(e); }
    private void Seek_MouseUp(object sender, MouseButtonEventArgs e) { if (!_seeking) return; SeekAt(e); _seeking = false; SeekTo((long)SeekBar.Value); SeekBar.ReleaseMouseCapture(); e.Handled = true; }
    private void Seek_LostCapture(object sender, MouseEventArgs e) { if (_seeking) { _seeking = false; SeekTo((long)SeekBar.Value); } }
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_player != null) _player.Volume = (int)e.NewValue; if (MuteButton != null) RefreshVolume(); if (_ready) ShowOsd($"{(int)e.NewValue}%"); }
    private void RefreshVolume() { MuteButton.Content = _muted || VolumeBar.Value == 0 ? "\uE74F" : "\uE767"; MuteButton.ToolTip = $"{(_muted ? "음소거 해제" : "음소거")} (M) · {(int)VolumeBar.Value}%"; VolumeBar.ToolTip = $"음량 {(int)VolumeBar.Value}%"; }
    private void ToggleMute() { _muted = !_muted; if (_player != null) _player.Mute = _muted; RefreshVolume(); ShowOsd(_muted ? "음소거 · 0%" : $"{(int)VolumeBar.Value}%"); }
    private void ChangeVolume(int delta) => VolumeBar.Value = Math.Clamp(VolumeBar.Value + delta, 0, 100);

    private void OpenDialog()
    {
        var dialog = new OpenFileDialog { Title = "미디어 파일 열기", Filter = MediaFiles.OpenFilter, CheckFileExists = true, Multiselect = true };
        if (dialog.ShowDialog(this) == true) _ = OpenFilesAsync(dialog.FileNames);
    }
    private async Task OpenFilesAsync(string[] paths)
    {
        if (paths.Length == 0) return;
        var media = paths.Where(p => !MediaFiles.Subtitles.Contains(Path.GetExtension(p))).ToArray();
        if (media.Length == 1) await OpenPathAsync(media[0]);
        else if (media.Length > 1)
        {
            var entries = ReadPlaylistFiles(media);
            if (entries.Count == 0) { SetStatus("목록에 재생할 로컬 미디어 파일이 없습니다.", true); return; }
            ReplaceQueue(entries);
            await PlayEntryAsync(_queue[0]);
        }
        foreach (var subtitle in paths.Where(p => MediaFiles.Subtitles.Contains(Path.GetExtension(p)))) await LoadSubtitleAsync(subtitle);
    }

    private static async Task<string> NormalizeSubtitleAsync(string path)
    {
        if (!new[] { ".srt", ".smi", ".ssa", ".ass", ".vtt", ".sbv", ".ttml" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return path;
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.Length > 16_000_000) return path;
        string? text = null;
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else
        {
            try { new UTF8Encoding(false, true).GetString(bytes); return path; }
            catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = Encoding.GetEncoding(949).GetString(bytes); }
        }
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightMediaPlayer", "subtitles");
        Directory.CreateDirectory(cache);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var output = Path.Combine(cache, hash + Path.GetExtension(path));
        // Content-addressed cache entries are immutable: a decoder may hold the
        // destination open without delete sharing, so never replace an existing entry.
        if (File.Exists(output)) return output;
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(true));
            try { File.Move(temporary, output); }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && File.Exists(output))
            { /* Another conversion published the same complete content first. */ }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("Subtitle cache cleanup: " + ex.Message); }
        }
        return output;
    }

    private async Task LoadSubtitleAsync(string path)
    {
        if (_player == null || _stopped || _ended) { SetStatus("먼저 영상을 재생한 다음 자막을 열어 주세요.", true); return; }
        try
        {
            var generation = _generation;
            var normalized = await NormalizeSubtitleAsync(path);
            if (generation != _generation || _player == null) return;
            if (_player.AddSlave(MediaSlaveType.Subtitle, new Uri(normalized).AbsoluteUri, true))
            { _externalSubtitle = path; SetStatus("자막 불러옴 · " + Path.GetFileName(path)); }
            else SetStatus("이 자막 형식을 불러올 수 없습니다.", true);
        }
        catch (Exception ex) { SetStatus("자막 오류: " + ex.Message, true); }
    }

    private void SubtitleDialog()
    {
        var dialog = new OpenFileDialog { Title = "자막 파일 열기", Filter = MediaFiles.SubtitleFilter };
        if (dialog.ShowDialog(this) == true) _ = LoadSubtitleAsync(dialog.FileName);
    }
    private void ToggleSubtitles()
    {
        if (_player == null) return;
        if (_player.Spu >= 0) { _lastSubtitle = _player.Spu; _player.SetSpu(-1); SetStatus("자막 숨김"); }
        else
        {
            var id = _player.SpuDescription.Where(t => t.Id >= 0).Select(t => t.Id).FirstOrDefault(-1);
            _player.SetSpu(_lastSubtitle >= 0 ? _lastSubtitle : id);
            SetStatus(id >= 0 ? "자막 표시" : "표시할 자막이 없습니다");
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, bool enabled = true) { var item = new MenuItem { Header = label, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("파일 열기…    Ctrl+O", OpenDialog);
        Add("자막 열기…    Ctrl+Shift+O", SubtitleDialog, _player != null);
        Add("자막 표시 / 숨김    V", ToggleSubtitles, _player != null);
        menu.Items.Add(new Separator());
        var audio = CreateAudioTrackMenu();
        var subtitles = CreateSubtitleTrackMenu();
        menu.Items.Add(audio); menu.Items.Add(subtitles);
        menu.Items.Add(new Separator());
        Add("전체화면 / 창 모드    Enter", ToggleFullscreen);
        Add("소프트웨어 디코딩으로 다시 열기", () => { if (_path != null) _ = OpenPathAsync(_path, true, Math.Max(0, _player?.Time ?? 0), _player?.State == VLCState.Paused, true); }, _path != null);
        Add("로그 폴더 열기", () => { Directory.CreateDirectory(AppLog.DirectoryPath); Process.Start(new ProcessStartInfo(AppLog.DirectoryPath) { UseShellExecute = true }); });
        Add("단축키 안내", () => MessageBox.Show(this, $"Space  재생 / 일시정지\nS  정지\n← / →  {_settings.SeekSeconds}초 이동\nCtrl + ← / →  {_settings.CtrlSeekSeconds}초 이동\nShift + ← / →  {_settings.ShiftSeekSeconds}초 이동\n↑ / ↓  음량 5% 조절\nM  음소거\nEnter / Alt+Enter / F11 / 영상 더블클릭  전체화면 전환\nEsc  전체화면 해제 후 최소화\nCtrl+O  파일 열기\nCtrl+Shift+O  자막 열기\nV  자막 표시 / 숨김\nPage Up / Page Down  이전 / 다음 파일\nHome  처음으로 이동", "단축키"));
        Add("오픈소스 정보", () =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("notices.txt")!;
            using var reader = new StreamReader(stream);
            MessageBox.Show(this, reader.ReadToEnd(), "오픈소스 정보");
        });
        return menu;
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _normalBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _beforeFullscreen = WindowState;
            _fullscreen = true;
            PlaylistToggle.Visibility = Visibility.Collapsed;
            UpdatePlaylistLayout();
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            SetFullscreenBars(true);
            WindowState = WindowState.Maximized;
        }
        else
        {
            _fullscreen = false;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
            SetFullscreenBars(false);
            Left = _normalBounds.Left; Top = _normalBounds.Top; Width = _normalBounds.Width; Height = _normalBounds.Height;
            WindowState = _beforeFullscreen;
            PlaylistToggle.Visibility = Visibility.Visible;
            UpdatePlaylistLayout();
        }
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (PlaylistList.IsKeyboardFocusWithin && key is Key.Up or Key.Down or Key.Home or Key.End) return;
        // Mark handled before an await so ListBox does not also page/select while
        // Page Up/Down is asynchronously opening the adjacent media item.
        e.Handled = true;
        if (ctrl && key == Key.O) { if (shift) SubtitleDialog(); else OpenDialog(); }
        else if (key == Key.Space) await TogglePlaybackAsync();
        else if (key == Key.S) await StopPlaybackAsync();
        else if (key == Key.Left || key == Key.Right) SeekByKey(key == Key.Right ? 1 : -1, ctrl, shift);
        else if (key == Key.Up || key == Key.Down) ChangeVolume(key == Key.Up ? 5 : -5);
        else if (key == Key.M) ToggleMute();
        else if (key == Key.V) ToggleSubtitles();
        else if (key == Key.Home) SeekTo(0);
        else if (key == Key.PageUp) await MoveFileAsync(-1, wrap: true);
        else if (key == Key.PageDown) await MoveFileAsync(1, wrap: true);
        else if (key == Key.F11 || (key == Key.Enter && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Alt))
        {
            if (!e.IsRepeat) ToggleFullscreen();
        }
        else if (key == Key.Escape && _fullscreen)
        {
            ToggleFullscreen();
            WindowState = WindowState.Minimized;
        }
        else { e.Handled = false; return; }
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Window_Drop(object sender, DragEventArgs e) { e.Handled = true; if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await OpenFilesAsync(files); }
    private void Video_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { Focus(); if (e.ClickCount == 2) { ToggleFullscreen(); e.Handled = true; } }
    private void Video_MouseRightButtonUp(object sender, MouseButtonEventArgs e) { var menu = BuildVideoMenu(); menu.PlacementTarget = VideoOverlay; menu.IsOpen = true; e.Handled = true; }
    private void Tracks_Click(object sender, RoutedEventArgs e) { var menu = BuildMenu(); menu.PlacementTarget = TracksButton; menu.IsOpen = true; }
    private async void Play_Click(object sender, RoutedEventArgs e) => await TogglePlaybackAsync();
    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopPlaybackAsync();
    private async void Previous_Click(object sender, RoutedEventArgs e) => await MoveFileAsync(-1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await MoveFileAsync(1);
    private void Open_Click(object sender, RoutedEventArgs e) => OpenDialog();
    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) { if (_fullscreen) ToggleFullscreen(); else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        _osdTimer.Stop();
        ResetFullscreenPointer();
        _magnet?.Dispose();
        _frame?.Dispose();
        CancelSorting();
        ++_generation;
        var bounds = _fullscreen ? _normalBounds : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Width = bounds.Width; _settings.Height = bounds.Height; _settings.Left = bounds.Left; _settings.Top = bounds.Top;
        _settings.Maximized = (_fullscreen ? _beforeFullscreen : WindowState) == WindowState.Maximized;
        _settings.Volume = (int)VolumeBar.Value; _settings.Muted = _muted;
        if (!_arguments.Contains("--self-test-report"))
        {
            try
            {
                PlayerSettings.Update(saved =>
                {
                    saved.Width = _settings.Width; saved.Height = _settings.Height;
                    saved.Left = _settings.Left; saved.Top = _settings.Top; saved.Maximized = _settings.Maximized;
                    saved.Volume = _settings.Volume; saved.Muted = _settings.Muted;
                });
            }
            catch (Exception ex) { AppLog.Write("Settings: " + ex.Message); }
        }
        await _operations.WaitAsync();
        try { await ReleasePlayerAsync(); var vlc = _vlc; _vlc = null; if (vlc != null) await Task.Run(vlc.Dispose); }
        catch (Exception ex) { AppLog.Write(ex.ToString()); }
        finally { _operations.Release(); _canClose = true; Close(); }
    }
}
