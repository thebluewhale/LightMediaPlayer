using System.IO;
using System.Text.Json;

namespace LightMediaPlayer;

internal sealed class PlayerSettings
{
    public int Volume { get; set; } = 75;
    public bool Muted { get; set; }
    public double Width { get; set; } = 1040;
    public double Height { get; set; } = 680;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Maximized { get; set; }
    public int SeekSeconds { get; set; } = 5;
    public int CtrlSeekSeconds { get; set; } = 60;
    public int ShiftSeekSeconds { get; set; } = 300;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightMediaPlayer", "settings.json");
    public static PlayerSettings Load(string? path = null)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path ?? SettingsPath)) ?? new();
            result.Volume = Math.Clamp(result.Volume, 0, 100);
            result.SeekSeconds = ValidSeek(result.SeekSeconds, 5);
            result.CtrlSeekSeconds = ValidSeek(result.CtrlSeekSeconds, 60);
            result.ShiftSeekSeconds = ValidSeek(result.ShiftSeekSeconds, 300);
            result.Width = double.IsFinite(result.Width) ? Math.Clamp(result.Width, 680, 7680) : 1040;
            result.Height = double.IsFinite(result.Height) ? Math.Clamp(result.Height, 400, 4320) : 680;
            if (result.Left is double l && !double.IsFinite(l)) result.Left = null;
            if (result.Top is double t && !double.IsFinite(t)) result.Top = null;
            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    private static int ValidSeek(int value, int fallback) => value is >= 1 and <= 86400 ? value : fallback;

    // Merge only the fields being changed. An older player window must not overwrite
    // preferences saved by another process when it later closes.
    public static PlayerSettings Update(Action<PlayerSettings> change, string? path = null)
    {
        path ??= SettingsPath;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\LightMediaPlayer.Settings." + key);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("설정 파일을 사용 중입니다. 잠시 후 다시 시도해 주세요.");
            var settings = Load(path);
            change(settings);
            settings.Save(path, throwOnError: true);
            return settings;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
    public void Save(string? path = null, bool throwOnError = false)
    {
        path ??= SettingsPath;
        var temporary = path + "." + Environment.ProcessId + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            // Atomic replacement prevents partially written settings across concurrent players.
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("Settings: " + ex.Message); if (throwOnError) throw; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
}
