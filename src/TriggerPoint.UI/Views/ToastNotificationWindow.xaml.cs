using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public enum ToastType
{
    Success,
    Error,
    Warning,
    Information
}

public partial class ToastNotificationWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly ToastMonitorPlacement _placement;
    private readonly double _verticalOffset;
    private bool _isClosing;

    public bool IsClosing => _isClosing;

    public ToastNotificationWindow(
        ToastType type, 
        string title, 
        string message, 
        ToastMonitorPlacement placement = ToastMonitorPlacement.PrimaryMonitor,
        double verticalOffset = 0.0)
    {
        _placement = placement;
        _verticalOffset = verticalOffset;

        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        ApplyToastTypeStyle(type);

        _timer.Interval = type == ToastType.Error ? TimeSpan.FromSeconds(5.0) : TimeSpan.FromSeconds(3.2);
        _timer.Tick += (s, e) => Dismiss();
    }

    private void ApplyToastTypeStyle(ToastType type)
    {
        string brushKey = type switch
        {
            ToastType.Success => "SuccessBrush",
            ToastType.Error => "ErrorBrush",
            ToastType.Warning => "WarningBrush",
            _ => "AccentBrush"
        };

        var brush = Application.Current.TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        ToastContainer.BorderBrush = brush;
        IconText.Foreground = brush;

        IconText.Text = type switch
        {
            ToastType.Success => "✔",
            ToastType.Error => "✕",
            ToastType.Warning => "⚠",
            _ => "ℹ"
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Reposition();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Reposition();
        AnimateIn();
        _timer.Start();
    }

    public void Reposition()
    {
        if (_placement == ToastMonitorPlacement.ActiveMonitor)
        {
            PositionOnActiveScreen(_verticalOffset);
        }
        else
        {
            PositionOnPrimaryScreen(_verticalOffset);
        }
    }

    private void PositionOnPrimaryScreen(double verticalOffset)
    {
        // SystemParameters.WorkArea is in WPF DIPs for the Primary Monitor and cleanly excludes the taskbar
        var workArea = SystemParameters.WorkArea;
        double winWidth = ActualWidth > 0 ? ActualWidth : 340;
        double winHeight = ActualHeight > 0 ? ActualHeight : 80;

        Left = workArea.Right - winWidth - 16;
        Top = workArea.Bottom - winHeight - 16 - verticalOffset;
    }

    private void PositionOnActiveScreen(double verticalOffset)
    {
        if (!NativeMethods.GetCursorPos(out var pt))
        {
            PositionOnPrimaryScreen(verticalOffset);
            return;
        }

        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            PositionOnPrimaryScreen(verticalOffset);
            return;
        }

        double dpiScale = 1.0;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            dpiScale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        }
        catch
        {
            dpiScale = 1.0;
        }

        double workRight = monitorInfo.rcWork.Right / dpiScale;
        double workBottom = monitorInfo.rcWork.Bottom / dpiScale;

        double winWidth = ActualWidth > 0 ? ActualWidth : 340;
        double winHeight = ActualHeight > 0 ? ActualHeight : 80;

        Left = workRight - winWidth - 16;
        Top = workBottom - winHeight - 16 - verticalOffset;
    }

    private void AnimateIn()
    {
        Opacity = 0.0;
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void Dismiss()
    {
        if (_isClosing) return;
        _isClosing = true;
        _timer.Stop();

        var fadeOut = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s, e) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void DismissBtn_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Dismiss();
    }
}
