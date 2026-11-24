using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LightMediaPlayer;

public partial class MainWindow
{
    private readonly DispatcherTimer _pointerIdle = new() { Interval = TimeSpan.FromSeconds(2) };
    private Point? _lastPointer;

    private void SetFullscreenBars(bool fullscreen)
    {
        ResetFullscreenPointer();
        WindowBorder.BorderThickness = new Thickness(fullscreen ? 0 : 1);
        if (fullscreen)
        {
            // The VideoView owns a separate overlay window above its native video HWND.
            // Move the existing controls there so they keep their handlers and playback state.
            WindowLayout.Children.Remove(TitleBar);
            WindowLayout.Children.Remove(ControlBar);
            TitleSlide.Child = TitleBar;
            ControlsSlide.Child = ControlBar;
            FullscreenLayer.Visibility = Visibility.Visible;
            if (VideoOverlay.IsMouseOver) RestartPointerTimer();
        }
        else
        {
            FullscreenLayer.Visibility = Visibility.Collapsed;
            foreach (var slide in new[] { TitleSlide, ControlsSlide })
            {
                ((TranslateTransform)slide.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
                slide.Visibility = Visibility.Hidden;
                slide.Child = null;
            }
            WindowLayout.Children.Add(TitleBar);
            WindowLayout.Children.Add(ControlBar);
        }
    }

    private void FullscreenEdge_Changed(object sender, MouseEventArgs e)
    {
        if (!_fullscreen || _closing) return;
        var edge = (Grid)sender;
        bool top = edge == TitleEdge;
        SlideFullscreenBar(top ? TitleSlide : ControlsSlide, edge.IsMouseOver, top ? -edge.ActualHeight : edge.ActualHeight);
    }

    private static void SlideFullscreenBar(Border slide, bool show, double hiddenOffset)
    {
        if (!show && slide.Visibility == Visibility.Hidden) return;
        var transform = (TranslateTransform)slide.RenderTransform;
        var from = slide.Visibility == Visibility.Hidden ? hiddenOffset : transform.Y;
        var to = show ? 0 : hiddenOffset;
        slide.Visibility = Visibility.Visible;
        slide.IsHitTestVisible = show;
        transform.Y = to;
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        if (!show) animation.Completed += (_, _) => { if (!slide.IsHitTestVisible) slide.Visibility = Visibility.Hidden; };
        transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void ControlsSlide_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        FullscreenProgress.Visibility = (bool)e.NewValue ? Visibility.Hidden : Visibility.Visible;

    private void VideoOverlay_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_fullscreen) RestartPointerTimer();
    }

    private void VideoOverlay_MouseLeave(object sender, MouseEventArgs e) => ResetFullscreenPointer();

    private void VideoOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_fullscreen) return;
        var position = e.GetPosition(VideoOverlay);
        if (position == _lastPointer) return;
        _lastPointer = position;
        RestartPointerTimer();
    }

    private void RestartPointerTimer()
    {
        VideoOverlay.ForceCursor = false;
        VideoOverlay.ClearValue(CursorProperty);
        _pointerIdle.Stop();
        _pointerIdle.Start();
    }

    private void ResetFullscreenPointer()
    {
        _pointerIdle.Stop();
        _lastPointer = null;
        VideoOverlay.ForceCursor = false;
        VideoOverlay.ClearValue(CursorProperty);
    }
}
