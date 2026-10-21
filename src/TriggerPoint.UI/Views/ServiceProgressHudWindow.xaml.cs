using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class ServiceProgressHudWindow : Window, IToastProgressHandle
{
    private readonly Action? _onCancel;
    private DispatcherTimer? _autoCloseTimer;
    private bool _isClosing;
    private Storyboard? _spinStoryboard;

    public ServiceProgressHudWindow(
        string title, 
        string message, 
        Action? onCancel = null, 
        string cancelButtonText = "Cancel")
    {
        InitializeComponent();

        _onCancel = onCancel;
        TitleText.Text = title;
        MessageText.Text = message;

        if (onCancel != null)
        {
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.Content = cancelButtonText;
        }
        else
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }

        Opacity = 0.0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Reposition();

        // Start spinner animation
        _spinStoryboard = TryFindResource("SpinStoryboard") as Storyboard;
        _spinStoryboard?.Begin(this, isControllable: true);

        // Smooth fade-in
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void Reposition()
    {
        // Measure visual tree to determine true content bounds for accurate centering
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double winWidth = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        if (winWidth <= 0) winWidth = MinWidth > 0 ? MinWidth : 400;
        if (winHeight <= 0) winHeight = MinHeight > 0 ? MinHeight : 100;

        IntPtr hMonitor = IntPtr.Zero;
        if (NativeMethods.GetCursorPos(out var pt))
        {
            hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        }

        double workLeft, workTop, workWidth, workHeight;
        var primaryWorkArea = SystemParameters.WorkArea;

        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (hMonitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            bool isPrimary = (monitorInfo.dwFlags & 1) != 0;
            if (isPrimary)
            {
                // Native WPF WorkArea is in DIPs, excludes taskbar, and is perfectly accurate for the primary display
                workLeft = primaryWorkArea.Left;
                workTop = primaryWorkArea.Top;
                workWidth = primaryWorkArea.Width;
                workHeight = primaryWorkArea.Height;
            }
            else
            {
                NativeMethods.GetMonitorDpi(hMonitor, out uint dpiX, out _);
                double scale = dpiX > 0 ? (dpiX / 96.0) : 1.0;
                workLeft = monitorInfo.rcWork.Left / scale;
                workTop = monitorInfo.rcWork.Top / scale;
                workWidth = (monitorInfo.rcWork.Right - monitorInfo.rcWork.Left) / scale;
                workHeight = (monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top) / scale;
            }
        }
        else
        {
            workLeft = primaryWorkArea.Left;
            workTop = primaryWorkArea.Top;
            workWidth = primaryWorkArea.Width;
            workHeight = primaryWorkArea.Height;
        }

        double targetLeft = workLeft + Math.Max(0, (workWidth - winWidth) / 2.0);
        double targetTop = workTop + Math.Max(0, (workHeight - winHeight) / 2.0);

        if (targetLeft < workLeft) targetLeft = workLeft;
        if (targetLeft + winWidth > workLeft + workWidth) targetLeft = workLeft + Math.Max(0, workWidth - winWidth);
        if (targetTop < workTop) targetTop = workTop;
        if (targetTop + winHeight > workTop + workHeight) targetTop = workTop + Math.Max(0, workHeight - winHeight);

        Left = targetLeft;
        Top = targetTop;
    }

    public void ReportSuccess(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ReportSuccess(message));
            return;
        }

        if (_isClosing) return;

        // Stop spinner & hide it
        _spinStoryboard?.Stop(this);
        SpinnerTransform.Angle = 0;
        SpinnerContainer.Visibility = Visibility.Collapsed;

        // Switch to success styling
        var successBrush = Application.Current.TryFindResource("SuccessBrush") as Brush ?? Brushes.MediumSeaGreen;
        var successSubtle = Application.Current.TryFindResource("SnippetSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x20, 0x10, 0xB9, 0x81));

        StatusGlyph.Text = "✔";
        StatusGlyph.Foreground = successBrush;
        StatusGlyph.Visibility = Visibility.Visible;
        IconBadge.Background = successSubtle;
        HudContainer.BorderBrush = successBrush;
        CancelButton.Visibility = Visibility.Collapsed;

        MessageText.Text = message;

        // Auto-close after 1.2s
        StartAutoCloseTimer(TimeSpan.FromSeconds(1.2));
    }

    public void ReportError(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ReportError(message));
            return;
        }

        if (_isClosing) return;

        // Stop spinner & hide it
        _spinStoryboard?.Stop(this);
        SpinnerTransform.Angle = 0;
        SpinnerContainer.Visibility = Visibility.Collapsed;

        var errorBrush = Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.IndianRed;
        var errorSubtle = Application.Current.TryFindResource("ErrorSubtleBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x20, 0xEF, 0x44, 0x44));

        StatusGlyph.Text = "✕";
        StatusGlyph.Foreground = errorBrush;
        StatusGlyph.Visibility = Visibility.Visible;
        IconBadge.Background = errorSubtle;
        HudContainer.BorderBrush = errorBrush;
        CancelButton.Visibility = Visibility.Collapsed;

        MessageText.Text = message;

        // Auto-close after 2.5s for errors
        StartAutoCloseTimer(TimeSpan.FromSeconds(2.5));
    }

    private void StartAutoCloseTimer(TimeSpan duration)
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = new DispatcherTimer { Interval = duration };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer?.Stop();
            Dismiss();
        };
        _autoCloseTimer.Start();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _onCancel?.Invoke();
        Dismiss();
    }

    public void Dismiss()
    {
        if (_isClosing) return;
        _isClosing = true;
        _autoCloseTimer?.Stop();

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s, e) =>
        {
            try { Close(); } catch { }
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    public void Dispose()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Dispose);
            return;
        }

        // If not already in an auto-closing success/error state, dismiss smoothly
        if (_autoCloseTimer == null || !_autoCloseTimer.IsEnabled)
        {
            Dismiss();
        }
    }
}
