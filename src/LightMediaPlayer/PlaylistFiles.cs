using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LightMediaPlayer;

internal static class PlaylistFiles
{
    public static string[]? Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".m3u" or ".m3u8" or ".pls" or ".asx" or ".wax" or ".wvx" or ".xspf")) return null;
        if (new FileInfo(path).Length > 4_000_000) throw new IOException("재생목록 파일이 너무 큽니다.");
        var text = File.ReadAllText(path);
        if (text.Contains('#') && text.Contains("#EXT-X-", StringComparison.Ordinal)) return null; // HLS goes to LibVLC.
        IEnumerable<string> entries;
        if (ext is ".m3u" or ".m3u8") entries = text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('#'));
        else if (ext == ".pls") entries = text.Split('\n').Select(s => Regex.Match(s.Trim(), @"^File(\d+)=(.*)$", RegexOptions.IgnoreCase)).Where(m => m.Success).OrderBy(m => int.Parse(m.Groups[1].Value)).Select(m => m.Groups[2].Value);
        else
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var doc = XDocument.Load(reader);
            entries = ext == ".xspf" ? doc.Descendants().Where(e => e.Name.LocalName == "location").Select(e => e.Value)
                : doc.Descendants().Where(e => e.Name.LocalName.Equals("ref", StringComparison.OrdinalIgnoreCase)).Select(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase))?.Value ?? "");
        }
        var result = new List<string>();
        foreach (var entry in entries.Take(10000))
        {
            try
            {
                var value = entry.Trim();
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) { if (!uri.IsFile) continue; value = uri.LocalPath; }
                var file = Path.GetFullPath(value, Path.GetDirectoryName(path)!);
                // Do not recursively open nested playlists; this also prevents cycles.
                if (File.Exists(file) && MediaFiles.Playable.Contains(Path.GetExtension(file))) result.Add(file);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
