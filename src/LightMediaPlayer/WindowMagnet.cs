using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LightMediaPlayer;

internal sealed class WindowMagnet : IDisposable
{
    private readonly Window _window;
    private readonly HwndSource _source;
    private readonly Func<bool> _enabled;
    public WindowMagnet(Window window, Func<bool> enabled)
    {
        _window = window;
        _enabled = enabled;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        _source.AddHook(Hook);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WM_MOVING modifies the proposed drag rectangle, preserving its dimensions.
        // It does not snap programmatic moves, resizing, maximizing or fullscreen.
        if (message != 0x0216 || !_enabled() || _window.WindowState != WindowState.Normal) return 0;
        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref rect, 2), ref info)) return 0;
        var threshold = (int)Math.Round(16 * GetDpiForWindow(hwnd) / 96.0);
        rect = Snap(rect, info.Work, threshold);
        Marshal.StructureToPtr(rect, lParam, false);
        handled = true;
        return 1;
    }
    internal static NativeRect Snap(NativeRect rect, NativeRect work, int threshold)
    {
        var dxLeft = work.Left - rect.Left;
        var dxRight = work.Right - rect.Right;
        var dyTop = work.Top - rect.Top;
        var dyBottom = work.Bottom - rect.Bottom;
        var dx = Math.Abs(dxLeft) <= Math.Abs(dxRight) ? dxLeft : dxRight;
        var dy = Math.Abs(dyTop) <= Math.Abs(dyBottom) ? dyTop : dyBottom;
        if (Math.Abs(dx) <= threshold) { rect.Left += dx; rect.Right += dx; }
        if (Math.Abs(dy) <= threshold) { rect.Top += dy; rect.Bottom += dy; }
        return rect;
    }
    internal static double SpaceToRight(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var rect);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref rect, 2), ref info)) return 0;
        return Math.Max(0, info.Work.Right - rect.Right) * 96.0 / GetDpiForWindow(hwnd);
    }
    public void Dispose() => _source.RemoveHook(Hook);
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect { public int Left, Top, Right, Bottom; public NativeRect(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
}
