using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LightMediaPlayer;

// Borderless, opaque HWND with native move/size behavior. Unlike WindowChrome,
// this does not toggle WS_VISIBLE or rebuild a window region during updates.
internal sealed class PlayerWindowFrame : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _caption;
    private readonly FrameworkElement _captionButtons;
    private readonly Func<bool> _fullscreen;
    private readonly HwndSource _source;

    internal PlayerWindowFrame(Window window, FrameworkElement caption, FrameworkElement captionButtons, Func<bool> fullscreen)
    {
        _window = window;
        _caption = caption;
        _captionButtons = captionButtons;
        _fullscreen = fullscreen;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        _source.AddHook(Hook);
        Refresh();
    }

    internal void Refresh() => SetWindowPos(_source.Handle, 0, 0, 0, 0, 0, 0x0037); // frame changed, no move/size/activation/z-order

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case 0x0083: // WM_NCCALCSIZE: all of the window is our opaque client area.
                handled = true;
                return 0;
            case 0x0084: // WM_NCHITTEST: retain native resize, caption drag and double click.
                GetWindowRect(hwnd, out var rect);
                var x = (short)(lParam.ToInt64() & 0xffff);
                var y = (short)((lParam.ToInt64() >> 16) & 0xffff);
                var border = (int)Math.Ceiling(6 * GetDpiForWindow(hwnd) / 96.0);
                var hit = HitTestEdges(rect, x, y, border,
                    !_fullscreen() && _window.WindowState == WindowState.Normal &&
                    _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip);
                if (hit == 1 && !_fullscreen())
                {
                    var screen = new Point(x, y);
                    if (ContainsScreenPoint(_caption, screen) && !ContainsScreenPoint(_captionButtons, screen)) hit = 2;
                }
                handled = true;
                return hit;
            case 0x0024: // WM_GETMINMAXINFO: use this monitor's work area, or full monitor in fullscreen.
                var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) break;
                var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                var area = _fullscreen() ? monitor.Monitor : monitor.Work;
                info.MaxPosition = new PointI(area.Left - monitor.Monitor.Left, area.Top - monitor.Monitor.Top);
                info.MaxSize = new PointI(area.Right - area.Left, area.Bottom - area.Top);
                var scale = GetDpiForWindow(hwnd) / 96.0;
                info.MinTrackSize = new PointI((int)Math.Ceiling(_window.MinWidth * scale), (int)Math.Ceiling(_window.MinHeight * scale));
                if (double.IsFinite(_window.MaxWidth)) info.MaxTrackSize.X = (int)Math.Ceiling(_window.MaxWidth * scale);
                if (double.IsFinite(_window.MaxHeight)) info.MaxTrackSize.Y = (int)Math.Ceiling(_window.MaxHeight * scale);
                Marshal.StructureToPtr(info, lParam, false);
                handled = true;
                return 0;
            case 0x0086: // WM_NCACTIVATE: update activation without drawing a native caption.
                handled = true;
                return DefWindowProc(hwnd, (uint)message, wParam, -1);
        }
        return 0;
    }

    private static bool ContainsScreenPoint(FrameworkElement element, Point point) => element.IsVisible &&
        new Rect(0, 0, element.ActualWidth, element.ActualHeight).Contains(element.PointFromScreen(point));

    internal static int HitTestEdges(RectI rect, int x, int y, int border, bool resizable)
    {
        if (!resizable) return 1; // HTCLIENT
        var left = x < rect.Left + border;
        var right = x >= rect.Right - border;
        var top = y < rect.Top + border;
        var bottom = y >= rect.Bottom - border;
        if (top) return left ? 13 : right ? 14 : 12;
        if (bottom) return left ? 16 : right ? 17 : 15;
        return left ? 10 : right ? 11 : 1;
    }

    public void Dispose() => _source.RemoveHook(Hook);

    [StructLayout(LayoutKind.Sequential)] internal struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct PointI { public int X, Y; public PointI(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo { public PointI Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public RectI Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectI rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
}
