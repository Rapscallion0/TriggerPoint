using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public partial class ConfirmationDialog : Window, IConfirmationDialogService
{
    public bool Confirmed { get; private set; } = false;
    public bool DoNotAskAgain => DoNotAskAgainCheck.IsChecked == true;

    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(string message, string title = "TriggerPoint Confirmation", string confirmButtonText = "Confirm", string cancelButtonText = "Cancel", bool showDoNotAskAgain = false) : this()
    {
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ConfirmBtn.Content = confirmButtonText;
        CancelBtn.Content = $"{cancelButtonText} (Esc)";
        if (showDoNotAskAgain)
        {
            DoNotAskAgainCheck.Visibility = Visibility.Visible;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnOwnerOrActiveScreen();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CenterOnOwnerOrActiveScreen();
        Activate();
        Focus();
        ConfirmBtn.Focus();
        Keyboard.Focus(ConfirmBtn);

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    private void CenterOnOwnerOrActiveScreen()
    {
        // Measure visual tree to determine true content bounds for accurate centering
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double winWidth = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        if (winWidth <= 0) winWidth = MinWidth > 0 ? MinWidth : 440;
        if (winHeight <= 0) winHeight = MinHeight > 0 ? MinHeight : 200;

        if (Owner != null && Owner.IsLoaded && Owner.WindowState == WindowState.Normal)
        {
            Left = Owner.Left + (Owner.ActualWidth - winWidth) / 2.0;
            Top = Owner.Top + (Owner.ActualHeight - winHeight) / 2.0;
            return;
        }

        IntPtr hMonitor = IntPtr.Zero;
        if (Owner != null && Owner.IsLoaded && Owner.WindowState != WindowState.Minimized)
        {
            var ownerHwnd = new System.Windows.Interop.WindowInteropHelper(Owner).Handle;
            if (ownerHwnd != IntPtr.Zero)
            {
                hMonitor = NativeMethods.MonitorFromWindow(ownerHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            }
        }

        if (hMonitor == IntPtr.Zero && NativeMethods.GetCursorPos(out var pt))
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

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Confirmed = true;
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }

    public async Task<bool> ShowConfirmationAsync(
        string message, 
        string title = "TriggerPoint Confirmation", 
        string confirmButtonText = "Confirm", 
        string cancelButtonText = "Cancel")
    {
        if (Application.Current == null) return false;

        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dlg = new ConfirmationDialog(message, title, confirmButtonText, cancelButtonText);
            var result = dlg.ShowDialog();
            return result == true && dlg.Confirmed;
        });
    }
}
