using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LightMediaPlayer;

internal sealed class HoverIntent(Action open) : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _wired;
    public void Start()
    {
        if (!_wired) { _timer.Tick += (_, _) => { _timer.Stop(); open(); }; _wired = true; }
        _timer.Stop();
        _timer.Start();
    }
    public void Stop() => _timer.Stop();
    public void Dispose() => Stop();
}

internal sealed class DelayedSubmenuItem : MenuItem
{
    private static readonly CoerceValueCallback? BaseCoerce = IsSubmenuOpenProperty.GetMetadata(typeof(MenuItem)).CoerceValueCallback;
    private readonly HoverIntent _hover;
    private bool _allowOpen;
    static DelayedSubmenuItem()
    {
        // Keep WPF's loaded-state coercion and menu behavior, but block its system
        // hover delay from opening this submenu before our one-second timer.
        IsSubmenuOpenProperty.OverrideMetadata(typeof(DelayedSubmenuItem), new FrameworkPropertyMetadata(false, null, CoerceOpen));
    }
    public DelayedSubmenuItem()
    {
        _hover = new HoverIntent(() => { if (IsMouseOver && IsLoaded && IsEnabled) OpenFromInput(); });
        SubmenuClosed += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) { _allowOpen = false; _hover.Stop(); } };
        Unloaded += (_, _) => Reset();
    }
    private static object CoerceOpen(DependencyObject sender, object value)
    {
        value = BaseCoerce?.Invoke(sender, value) ?? value;
        return (bool)value && ((DelayedSubmenuItem)sender)._allowOpen;
    }
    internal void OpenFromInput()
    {
        if (!HasItems || !IsEnabled) return;
        _hover.Stop();
        _allowOpen = true;
        SetCurrentValue(IsSubmenuOpenProperty, true);
        CoerceValue(IsSubmenuOpenProperty);
    }
    internal void Reset()
    {
        _hover.Stop();
        _allowOpen = false;
        SetCurrentValue(IsSubmenuOpenProperty, false);
    }
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        if (!IsSubmenuOpen) { _allowOpen = false; _hover.Start(); }
        base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover.Stop();
        base.OnMouseLeave(e);
    }
    private bool IsHeader(object source)
    {
        var node = source as DependencyObject;
        while (node != null)
        {
            if (node is MenuItem item) return ReferenceEquals(item, this);
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (HasItems && IsHeader(e.OriginalSource)) { Focus(); OpenFromInput(); e.Handled = true; }
        base.OnPreviewMouseLeftButtonDown(e);
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (HasItems && IsHeader(e.OriginalSource)) e.Handled = true;
        base.OnPreviewMouseLeftButtonUp(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (HasItems && IsHeader(e.OriginalSource) && e.Key is Key.Right or Key.Enter or Key.Space)
        { _hover.Stop(); _allowOpen = true; }
        base.OnPreviewKeyDown(e);
    }
}
