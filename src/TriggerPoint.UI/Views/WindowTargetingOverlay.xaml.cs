using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class WindowTargetingOverlay : Window
{
    private readonly Window? _ownerWindow;
    private readonly string _basePrompt;
    private IntPtr _overlayHwnd = IntPtr.Zero;
    private IntPtr _ownerHwnd = IntPtr.Zero;

    private IntPtr _currentHoverHwnd = IntPtr.Zero;
    private DateTime _lastScanTime = DateTime.MinValue;

    public IntPtr TargetHwnd { get; private set; } = IntPtr.Zero;
    public NativeMethods.POINT TargetCursorPt { get; private set; }

    public WindowTargetingOverlay(Window? owner, string basePrompt)
    {
        InitializeComponent();
        _ownerWindow = owner;
        _basePrompt = string.IsNullOrWhiteSpace(basePrompt) 
            ? "Click application window to select (Esc to cancel)" 
            : basePrompt;

        TooltipPromptText.Text = _basePrompt;

        // Cover the full virtual desktop (multi-monitor support)
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _overlayHwnd = new WindowInteropHelper(this).Handle;
        if (_ownerWindow != null)
        {
            _ownerHwnd = new WindowInteropHelper(_ownerWindow).Handle;
        }

        Activate();
        Focus();

        // Initial tooltip placement near cursor
        if (NativeMethods.GetCursorPos(out var pt))
        {
            UpdateHoverTarget(pt);
            UpdateTooltipPosition(PointFromScreen(new Point(pt.X, pt.Y)));
        }
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var mousePos = e.GetPosition(OverlayCanvas);
        UpdateTooltipPosition(mousePos);

        // Throttle window scanning to ~30ms intervals to minimize CPU usage while keeping feedback fluid
        if ((DateTime.UtcNow - _lastScanTime).TotalMilliseconds >= 30)
        {
            _lastScanTime = DateTime.UtcNow;
            if (NativeMethods.GetCursorPos(out var pt))
            {
                UpdateHoverTarget(pt);
            }
        }
    }

    private void UpdateTooltipPosition(Point mousePos)
    {
        double tipLeft = mousePos.X + 20;
        double tipTop = mousePos.Y + 20;

        double tooltipWidth = FloatingTooltip.ActualWidth > 0 ? FloatingTooltip.ActualWidth : 260;
        double tooltipHeight = FloatingTooltip.ActualHeight > 0 ? FloatingTooltip.ActualHeight : 34;

        if (tipLeft + tooltipWidth > ActualWidth - 16)
        {
            tipLeft = mousePos.X - tooltipWidth - 20;
        }
        if (tipTop + tooltipHeight > ActualHeight - 16)
        {
            tipTop = mousePos.Y - tooltipHeight - 20;
        }

        Canvas.SetLeft(FloatingTooltip, Math.Max(8, tipLeft));
        Canvas.SetTop(FloatingTooltip, Math.Max(8, tipTop));
    }

    private void UpdateHoverTarget(NativeMethods.POINT pt)
    {
        IntPtr foundHwnd = FindWindowUnderPoint(pt);
        if (foundHwnd == _currentHoverHwnd && foundHwnd != IntPtr.Zero) return;

        _currentHoverHwnd = foundHwnd;

        if (foundHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(foundHwnd, out var rect))
        {
            try
            {
                Point topLeft = PointFromScreen(new Point(rect.Left, rect.Top));
                Point bottomRight = PointFromScreen(new Point(rect.Right, rect.Bottom));

                double w = Math.Max(0, bottomRight.X - topLeft.X);
                double h = Math.Max(0, bottomRight.Y - topLeft.Y);

                if (w > 10 && h > 10)
                {
                    Canvas.SetLeft(HoverHighlightBorder, topLeft.X);
                    Canvas.SetTop(HoverHighlightBorder, topLeft.Y);
                    HoverHighlightBorder.Width = w;
                    HoverHighlightBorder.Height = h;
                    HoverHighlightBorder.Visibility = Visibility.Visible;

                    // Display window title in tooltip if available
                    string title = GetWindowDisplayTitle(foundHwnd);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        if (title.Length > 36) title = title[..33] + "...";
                        TooltipPromptText.Text = $"Click to select: {title} (Esc to cancel)";
                    }
                    else
                    {
                        TooltipPromptText.Text = _basePrompt;
                    }
                    return;
                }
            }
            catch
            {
                // Fall through to hide highlight on conversion edge cases
            }
        }

        HoverHighlightBorder.Visibility = Visibility.Collapsed;
        TooltipPromptText.Text = _basePrompt;
    }

    private IntPtr FindWindowUnderPoint(NativeMethods.POINT pt)
    {
        IntPtr target = IntPtr.Zero;
        uint currentPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (hWnd == _overlayHwnd || hWnd == _ownerHwnd) return true; // skip self and owner
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == currentPid) return true; // skip TriggerPoint application windows

            if (NativeMethods.GetWindowRect(hWnd, out var r))
            {
                if (pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom)
                {
                    target = hWnd;
                    return false; // Found topmost window in Z-order under point
                }
            }
            return true;
        }, IntPtr.Zero);

        return target;
    }

    private static string GetWindowDisplayTitle(IntPtr hWnd)
    {
        int len = NativeMethods.GetWindowTextLength(hWnd);
        if (len > 0)
        {
            var sb = new StringBuilder(len + 1);
            if (NativeMethods.GetWindowText(hWnd, sb, sb.Capacity) > 0)
            {
                return sb.ToString().Trim();
            }
        }
        return string.Empty;
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (NativeMethods.GetCursorPos(out var pt))
        {
            TargetCursorPt = pt;
        }

        TargetHwnd = _currentHoverHwnd != IntPtr.Zero 
            ? _currentHoverHwnd 
            : FindWindowUnderPoint(TargetCursorPt);

        if (TargetHwnd == IntPtr.Zero)
        {
            // Fallback to WindowFromPoint
            IntPtr rawHwnd = NativeMethods.WindowFromPoint(TargetCursorPt);
            if (rawHwnd != IntPtr.Zero && rawHwnd != _overlayHwnd)
            {
                IntPtr root = NativeMethods.GetAncestor(rawHwnd, NativeMethods.GA_ROOT);
                TargetHwnd = root != IntPtr.Zero ? root : rawHwnd;
            }
        }

        DialogResult = TargetHwnd != IntPtr.Zero;
        Close();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
        Close();
        e.Handled = true;
    }

    /// <summary>
    /// Launches the global targeting overlay, optionally hiding the owner window during selection.
    /// </summary>
    public static bool TryTargetWindow(
        Window? owner, 
        string promptMessage, 
        bool hideOwnerDuringTargeting, 
        out IntPtr targetHwnd, 
        out NativeMethods.POINT cursorPt)
    {
        targetHwnd = IntPtr.Zero;
        cursorPt = default;

        bool ownerWasVisible = owner?.IsVisible ?? false;
        if (hideOwnerDuringTargeting && owner != null && ownerWasVisible)
        {
            owner.Visibility = Visibility.Hidden;
        }

        try
        {
            var overlay = new WindowTargetingOverlay(owner, promptMessage);
            bool? result = overlay.ShowDialog();

            if (result == true && overlay.TargetHwnd != IntPtr.Zero)
            {
                targetHwnd = overlay.TargetHwnd;
                cursorPt = overlay.TargetCursorPt;
                return true;
            }

            return false;
        }
        finally
        {
            if (hideOwnerDuringTargeting && owner != null && ownerWasVisible)
            {
                owner.Visibility = Visibility.Visible;
                owner.Activate();
                owner.Focus();
            }
        }
    }
}
