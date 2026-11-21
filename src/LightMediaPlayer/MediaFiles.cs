using System.IO;
using System.Runtime.InteropServices;

namespace LightMediaPlayer;

internal static class MediaFiles
{
    public const string RequestedExtensions = "3g2 3gp 3gp2 3gpp aac ac3 aif aiff amr amv ape asf ass asx avi cda cue divx dmskm dpg dpl dsf dts dtshd dvr-ms eac3 evo f4v flac flv idx ifo k3g lmp4 m1a m1v m2a m2t m2ts m2v m3u m3u8 m4a m4b m4p m4v mka mkv mod mov mp2 mp2v mp3 mp4 mpa mpc mpd mpeg mpg mpl mpls mpv2 mqv mts mxf nsr nsv ogg ogm ogv opus pbf pls psb qt ra ram rm rmvb rpm rt sbv skm smi srt ssa ssf sub sup tak tp tpr ts tta ttml usf vob vtt wav wax webm wm wma wmp wmv wmx wtv wv wvx wspf xss";
    public static readonly HashSet<string> Subtitles = Set("ass idx psb rt sbv smi srt ssa ssf sub sup ttml usf vtt");
    public static readonly HashSet<string> Audio = Set("aac ac3 aif aiff amr ape dsf dts dtshd eac3 flac m1a m2a m4a m4b mka mp2 mp3 mpa mpc ogg opus ra tak tta wav wma wv");
    public static readonly HashSet<string> Playable = Set("3g2 3gp 3gp2 3gpp aac ac3 aif aiff amr amv ape asf avi divx dpg dsf dts dtshd dvr-ms eac3 evo f4v flac flv k3g lmp4 m1a m1v m2a m2t m2ts m2v m4a m4b m4p m4v mka mkv mod mov mp2 mp2v mp3 mp4 mpa mpc mpeg mpg mpv2 mqv mts mxf nsv ogg ogm ogv opus qt ra rm rmvb skm tak tp ts tta vob wav webm wm wma wmv wtv wv");
    private static HashSet<string> Set(string text) => text.Split(' ').Select(x => "." + x).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static readonly IComparer<string> NaturalComparer = Comparer<string>.Create(NaturalCompare);
    public static string OpenFilter => "미디어 파일|" + string.Join(';', RequestedExtensions.Split(' ').Distinct().Select(x => "*." + x)) + ";*.xspf|모든 파일|*.*";
    public static string SubtitleFilter => "자막 파일|" + string.Join(';', Subtitles.Select(x => "*" + x)) + "|모든 파일|*.*";
    public static string? FindSubtitle(string path)
    {
        var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        return new[] { ".srt", ".ass", ".smi", ".ssa", ".vtt", ".idx", ".sub" }.Select(x => stem + x).FirstOrDefault(File.Exists);
    }
    private static int NaturalCompare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);
}
