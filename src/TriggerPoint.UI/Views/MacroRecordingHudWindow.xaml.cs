using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class MacroRecordingHudWindow : Window
{
    private readonly IMacroService _macroService;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();
    private int _eventCount = 0;

    public MacroPayload? RecordedMacro { get; private set; }

    public MacroRecordingHudWindow(IMacroService macroService)
    {
        InitializeComponent();
        _macroService = macroService;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += Timer_Tick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionTopRight();

        if (TryFindResource("BlinkStoryboard") is Storyboard sb)
        {
            sb.Begin(this);
        }

        _macroService.EventCaptured += MacroService_EventCaptured;
        _macroService.StartRecording();
        _stopwatch.Start();
        _timer.Start();
    }

    private void PositionTopRight()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo)) return;

        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double workRight = monitorInfo.rcWork.Right / dpiScale;
        double workTop = monitorInfo.rcWork.Top / dpiScale;

        Left = workRight - Width - 32;
        Top = workTop + 32;
    }

    private void MacroService_EventCaptured(MacroEvent evt)
    {
        _eventCount++;
        Dispatcher.InvokeAsync(() =>
        {
            UpdateStatsText();
        }, DispatcherPriority.Background);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateStatsText();
    }

    private void UpdateStatsText()
    {
        var elapsed = _stopwatch.Elapsed;
        StatsTextBlock.Text = $"{_eventCount} events • {elapsed:mm\\:ss}";
    }

    private void StopRecordingAndClose()
    {
        _timer.Stop();
        _stopwatch.Stop();
        _macroService.EventCaptured -= MacroService_EventCaptured;

        RecordedMacro = _macroService.StopRecording();
        DialogResult = true;
        Close();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopRecordingAndClose();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            StopRecordingAndClose();
            e.Handled = true;
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _macroService.EventCaptured -= MacroService_EventCaptured;
        if (_macroService.IsRecording)
        {
            RecordedMacro ??= _macroService.StopRecording();
        }
        base.OnClosed(e);
    }
}
