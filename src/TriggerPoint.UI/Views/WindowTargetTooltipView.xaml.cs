using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class WindowTargetTooltipView : Window
{
    private IntPtr _hwnd = IntPtr.Zero;

    public WindowTargetTooltipView()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        int exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    public void SetPrompt(string prompt)
    {
        TooltipTitleText.Text = string.IsNullOrWhiteSpace(prompt)
            ? "Click application window to select"
            : prompt;
    }

    public void MoveNearCursor(NativeMethods.POINT cursorPt)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
        }

        // Identify active monitor for clamping
        var hMon = NativeMethods.MonitorFromPoint(cursorPt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var workArea = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        uint monitorDpi = 96;

        if (hMon != IntPtr.Zero)
        {
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                workArea = mi.rcWork;
            }
            NativeMethods.GetMonitorDpi(hMon, out monitorDpi, out _);
        }

        double scale = monitorDpi > 0 ? monitorDpi / 96.0 : 1.0;
        double wDip = ActualWidth > 0 ? ActualWidth : 360;
        double hDip = ActualHeight > 0 ? ActualHeight : 60;

        int wPx = (int)Math.Round(wDip * scale);
        int hPx = (int)Math.Round(hDip * scale);

        // Position 24px right and 24px down from cursor
        int targetX = cursorPt.X + 24;
        int targetY = cursorPt.Y + 24;

        if (targetX + wPx > workArea.Right - 16)
        {
            targetX = cursorPt.X - wPx - 20;
        }
        if (targetY + hPx > workArea.Bottom - 16)
        {
            targetY = cursorPt.Y - hPx - 20;
        }

        targetX = Math.Max(workArea.Left + 16, Math.Min(targetX, workArea.Right - wPx - 16));
        targetY = Math.Max(workArea.Top + 16, Math.Min(targetY, workArea.Bottom - hPx - 16));

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                targetX,
                targetY,
                0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }
}
