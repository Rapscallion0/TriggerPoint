using System;
using System.Windows;
using System.Windows.Interop;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class WindowTargetHighlightView : Window
{
    private IntPtr _hwnd = IntPtr.Zero;

    public WindowTargetHighlightView()
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

    public void MoveToPhysicalRect(NativeMethods.RECT rect, uint monitorDpi)
    {
        if (rect.Width <= 10 || rect.Height <= 10)
        {
            HideHighlight();
            return;
        }

        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
        }

        double scale = monitorDpi > 0 ? monitorDpi / 96.0 : 1.0;
        if (scale <= 0) scale = 1.0;

        // Set WPF DIP dimensions so WPF allocates the proper DirectX composition surface
        Width = rect.Width / scale;
        Height = rect.Height / scale;
        Left = rect.Left / scale;
        Top = rect.Top / scale;

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            Show();
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    public void HideHighlight()
    {
        if (Visibility != Visibility.Collapsed)
        {
            Visibility = Visibility.Collapsed;
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Hide();
        }
    }
}
