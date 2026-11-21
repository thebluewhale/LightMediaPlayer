using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace LightMediaPlayer;

internal static class NativeRuntime
{
    public static string Prepare()
    {
        // A user can substitute a compatible LibVLC build without rebuilding the app.
        var external = Path.Combine(AppContext.BaseDirectory, "libvlc");
        if (File.Exists(Path.Combine(external, "libvlc.dll"))) return external;
        var assembly = Assembly.GetExecutingAssembly();
        using var hashStream = assembly.GetManifestResourceStream("native.sha256")!;
        using var reader = new StreamReader(hashStream);
        var hash = reader.ReadToEnd().Trim();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightMediaPlayer", "runtime", hash);
        // Only extraction is serialized. Playback processes are never serialized.
        using var mutex = new Mutex(false, @"Local\LightMediaPlayer.Extract." + hash);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromMinutes(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("재생 엔진 준비 시간이 초과되었습니다. 잠시 후 다시 실행해 주세요.");
            var marker = Path.Combine(root, ".complete");
            if (!File.Exists(marker) || !File.Exists(Path.Combine(root, "libvlc.dll")))
            {
                Directory.CreateDirectory(root);
                using var payload = assembly.GetManifestResourceStream("native.zip")!;
                using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
                zip.ExtractToDirectory(root, overwriteFiles: true);
                File.WriteAllText(marker, hash);
            }
            return root;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
}
