using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

internal sealed class ScreenCaptureOverlayWindow : Window
{
    private readonly WindowTargetingOverlay _controller;
    private readonly System.Windows.Forms.Screen _screen;

    public ScreenCaptureOverlayWindow(System.Windows.Forms.Screen screen, WindowTargetingOverlay controller)
    {
        _screen = screen;
        _controller = controller;

        Title = "TriggerPoint Screen Target Overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // #01000000 (hit-testable, transparent)
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;

        PreviewMouseMove += (s, e) => _controller.NotifyMouseMove();
        PreviewMouseLeftButtonDown += (s, e) => _controller.NotifyLeftClick();
        PreviewMouseRightButtonDown += (s, e) => _controller.NotifyCancel();
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) _controller.NotifyCancel();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                _screen.Bounds.Left,
                _screen.Bounds.Top,
                _screen.Bounds.Width,
                _screen.Bounds.Height,
                NativeMethods.SWP_SHOWWINDOW);
        }
    }
}

public partial class WindowTargetingOverlay : Window
{
    private readonly Window? _ownerWindow;
    private readonly string _basePrompt;
    private IntPtr _overlayHwnd = IntPtr.Zero;
    private IntPtr _ownerHwnd = IntPtr.Zero;
    private IntPtr _highlightHwnd = IntPtr.Zero;
    private IntPtr _tooltipHwnd = IntPtr.Zero;

    private WindowTargetHighlightView? _highlightView;
    private WindowTargetTooltipView? _tooltipView;
    private readonly List<ScreenCaptureOverlayWindow> _screenOverlays = [];

    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private NativeMethods.HookProc? _keyboardHookProc;

    private IntPtr _currentHoverHwnd = IntPtr.Zero;
    private DateTime _lastScanTime = DateTime.MinValue;

    public IntPtr TargetHwnd { get; private set; } = IntPtr.Zero;
    public NativeMethods.POINT TargetCursorPt { get; private set; }

    public WindowTargetingOverlay(Window? owner, string basePrompt)
    {
        InitializeComponent();
        _ownerWindow = owner;
        _basePrompt = string.IsNullOrWhiteSpace(basePrompt) 
            ? "Click application window to select" 
            : basePrompt;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _overlayHwnd = new WindowInteropHelper(this).Handle;
        if (_ownerWindow != null)
        {
            _ownerHwnd = new WindowInteropHelper(_ownerWindow).Handle;
        }

        _highlightView = new WindowTargetHighlightView();
        _highlightView.Show();
        _highlightHwnd = new WindowInteropHelper(_highlightView).Handle;
        _highlightView.HideHighlight();

        _tooltipView = new WindowTargetTooltipView();
        _tooltipView.SetPrompt(_basePrompt);
        _tooltipView.Show();
        _tooltipHwnd = new WindowInteropHelper(_tooltipView).Handle;

        // Spawn a dedicated full-coverage transparent overlay window for each monitor
        var screens = System.Windows.Forms.Screen.AllScreens;
        foreach (var screen in screens)
        {
            var overlay = new ScreenCaptureOverlayWindow(screen, this);
            overlay.Show();
            _screenOverlays.Add(overlay);
        }

        // Install system-wide keyboard hook for Escape key to ensure Esc cancels regardless of which window has focus
        _keyboardHookProc = KeyboardHookCallback;
        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, moduleHandle, 0);

        if (NativeMethods.GetCursorPos(out var pt))
        {
            _tooltipView.MoveNearCursor(pt);
            UpdateHoverTarget(pt);
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (kbd.vkCode == NativeMethods.VK_ESCAPE)
                {
                    Dispatcher.BeginInvoke(() => NotifyCancel());
                    return (IntPtr)1; // Consume Escape key
                }
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    public void NotifyMouseMove()
    {
        if (NativeMethods.GetCursorPos(out var pt))
        {
            _tooltipView?.MoveNearCursor(pt);

            if ((DateTime.UtcNow - _lastScanTime).TotalMilliseconds >= 25)
            {
                _lastScanTime = DateTime.UtcNow;
                UpdateHoverTarget(pt);
            }
        }
    }

    public void NotifyLeftClick()
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
            IntPtr rawHwnd = NativeMethods.WindowFromPoint(TargetCursorPt);
            if (rawHwnd != IntPtr.Zero && rawHwnd != _overlayHwnd && rawHwnd != _ownerHwnd && rawHwnd != _highlightHwnd && rawHwnd != _tooltipHwnd)
            {
                IntPtr root = NativeMethods.GetAncestor(rawHwnd, NativeMethods.GA_ROOT);
                IntPtr candidate = root != IntPtr.Zero ? root : rawHwnd;
                if (!NativeMethods.IsWindowCloaked(candidate))
                {
                    NativeMethods.GetWindowThreadProcessId(candidate, out uint cPid);
                    if (cPid != (uint)Environment.ProcessId)
                    {
                        TargetHwnd = candidate;
                    }
                }
            }
        }

        DialogResult = TargetHwnd != IntPtr.Zero;
        Close();
    }

    public void NotifyCancel()
    {
        DialogResult = false;
        Close();
    }

    private void UpdateHoverTarget(NativeMethods.POINT pt)
    {
        IntPtr foundHwnd = FindWindowUnderPoint(pt);
        if (foundHwnd == _currentHoverHwnd && foundHwnd != IntPtr.Zero) return;

        _currentHoverHwnd = foundHwnd;

        if (foundHwnd != IntPtr.Zero && NativeMethods.GetWindowExtendedBounds(foundHwnd, out var rect))
        {
            try
            {
                var hMon = NativeMethods.MonitorFromWindow(foundHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMon == IntPtr.Zero)
                {
                    hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
                }

                NativeMethods.GetMonitorDpi(hMon, out uint dpi, out _);
                _highlightView?.MoveToPhysicalRect(rect, dpi);

                string title = GetWindowDisplayTitle(foundHwnd);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    if (title.Length > 45) title = title[..42] + "...";
                    _tooltipView?.SetPrompt($"Click to select: {title}");
                }
                else
                {
                    _tooltipView?.SetPrompt(_basePrompt);
                }
                return;
            }
            catch
            {
                // Fall through to hide highlight on conversion edge cases
            }
        }

        _highlightView?.HideHighlight();
        _tooltipView?.SetPrompt(_basePrompt);
    }

    private IntPtr FindWindowUnderPoint(NativeMethods.POINT pt)
    {
        IntPtr target = IntPtr.Zero;
        uint currentPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (hWnd == _overlayHwnd || hWnd == _ownerHwnd || hWnd == _highlightHwnd || hWnd == _tooltipHwnd) return true;
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (NativeMethods.IsIconic(hWnd)) return true;
            if (NativeMethods.IsWindowCloaked(hWnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == currentPid) return true; // skip ALL TriggerPoint windows across all monitors

            if (!NativeMethods.GetWindowExtendedBounds(hWnd, out var r)) return true;
            if (pt.X < r.Left || pt.X > r.Right || pt.Y < r.Top || pt.Y > r.Bottom) return true;

            // Skip desktop wallpaper and shell root windows
            string className = NativeMethods.GetWindowClassName(hWnd);
            if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // If an owned popup or child tool window was hit, roll up to owner application window if it contains point
            IntPtr owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
            if (owner != IntPtr.Zero &&
                NativeMethods.IsWindowVisible(owner) &&
                !NativeMethods.IsIconic(owner) &&
                !NativeMethods.IsWindowCloaked(owner) &&
                NativeMethods.GetWindowExtendedBounds(owner, out var ownerRect) &&
                pt.X >= ownerRect.Left && pt.X <= ownerRect.Right && pt.Y >= ownerRect.Top && pt.Y <= ownerRect.Bottom)
            {
                target = owner;
            }
            else
            {
                target = hWnd;
            }

            return false; // Found topmost visible, non-cloaked window in Z-order under point
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
                string t = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
        }

        string proc = NativeMethods.GetProcessNameForWindow(hWnd);
        if (!string.IsNullOrWhiteSpace(proc))
        {
            return proc;
        }

        return string.Empty;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        try
        {
            _highlightView?.Close();
            _highlightView = null;
        }
        catch { }

        try
        {
            _tooltipView?.Close();
            _tooltipView = null;
        }
        catch { }

        foreach (var overlay in _screenOverlays)
        {
            try
            {
                overlay.Close();
            }
            catch { }
        }
        _screenOverlays.Clear();

        base.OnClosed(e);
    }

    /// <summary>
    /// Helper to show targeting overlay and capture target window and cursor coordinates.
    /// Returns true if user clicked a valid window, false if cancelled via Esc or right-click.
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
