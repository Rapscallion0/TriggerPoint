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
        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        if (NativeMethods.GetCursorPos(out var pt))
        {
            var hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                double workLeft = mi.rcWork.Left / dpiScale;
                double workWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiScale;
                double workTop = mi.rcWork.Top / dpiScale;
                double workHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiScale;

                UpdateLayout();
                double w = ActualWidth > 0 ? ActualWidth : 420;

                Left = workLeft + ((workWidth - w) / 2.0);
                Top = workTop + Math.Max(24.0, workHeight * 0.12);
                return;
            }
        }

        // Fallback to Primary Screen
        Left = (SystemParameters.WorkArea.Width - (ActualWidth > 0 ? ActualWidth : 420)) / 2.0;
        Top = Math.Max(24.0, SystemParameters.WorkArea.Height * 0.12);
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
