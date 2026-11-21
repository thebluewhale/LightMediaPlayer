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
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightMediaPlayer", "settings.json");
    public static PlayerSettings Load(string? path = null)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path ?? SettingsPath)) ?? new();
            result.Volume = Math.Clamp(result.Volume, 0, 100);
            result.Width = double.IsFinite(result.Width) ? Math.Clamp(result.Width, 680, 7680) : 1040;
            result.Height = double.IsFinite(result.Height) ? Math.Clamp(result.Height, 400, 4320) : 680;
            if (result.Left is double l && !double.IsFinite(l)) result.Left = null;
            if (result.Top is double t && !double.IsFinite(t)) result.Top = null;
            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string? path = null)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("Settings: " + ex.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
}
