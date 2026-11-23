using System.IO;
using System.Windows;

namespace LightMediaPlayer;

public partial class App : Application
{
    private ExplorerOpenServer? _explorer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Deliberately no application-wide mutex / IPC: every activation is independent.
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write(args.Exception.ToString());
            MessageBox.Show("오류가 발생했습니다.\n" + args.Exception.Message, "Light Media Player", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        if (e.Args.FirstOrDefault() == "--shell-open")
        {
            _explorer = new ExplorerOpenServer();
            // Show and activate on the STA during Explorer's Open call.
            _explorer.Start(paths => Dispatcher.Invoke(() => ShowPlayer(paths)));
            return;
        }
        ShowPlayer(e.Args);
    }

    private void ShowPlayer(string[] paths)
    {
        var window = new MainWindow(paths);
        MainWindow = window;
        window.Show();
        if (window.Activate()) window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _explorer?.Dispose();
        base.OnExit(e);
    }
}

internal static class AppLog
{
    private static readonly object Sync = new();
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightMediaPlayer", "logs");
    public static readonly string FilePath = Path.Combine(DirectoryPath, $"player-{Environment.ProcessId}.log");
    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000) File.WriteAllText(FilePath, "");
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
