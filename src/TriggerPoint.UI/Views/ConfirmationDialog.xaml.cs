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
        double winWidth = ActualWidth > 0 ? ActualWidth : Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : 200;
        if (winWidth <= 0) winWidth = 440;
        if (winHeight <= 0) winHeight = 200;

        IntPtr hMonitor = IntPtr.Zero;
        double workLeft, workTop, workWidth, workHeight;
        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        if (Owner != null && Owner.IsLoaded && Owner.WindowState != WindowState.Minimized)
        {
            var ownerHwnd = new System.Windows.Interop.WindowInteropHelper(Owner).Handle;
            if (ownerHwnd != IntPtr.Zero)
            {
                hMonitor = NativeMethods.MonitorFromWindow(ownerHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            }
        }

        if (hMonitor == IntPtr.Zero)
        {
            if (NativeMethods.GetCursorPos(out var pt))
            {
                hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            }
        }

        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (hMonitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            workLeft = monitorInfo.rcWork.Left / dpiScale;
            workTop = monitorInfo.rcWork.Top / dpiScale;
            workWidth = (monitorInfo.rcWork.Right - monitorInfo.rcWork.Left) / dpiScale;
            workHeight = (monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top) / dpiScale;
        }
        else
        {
            workLeft = SystemParameters.WorkArea.Left;
            workTop = SystemParameters.WorkArea.Top;
            workWidth = SystemParameters.WorkArea.Width;
            workHeight = SystemParameters.WorkArea.Height;
        }

        double targetLeft;
        double targetTop;

        if (Owner != null && Owner.IsLoaded && Owner.WindowState == WindowState.Normal)
        {
            targetLeft = Owner.Left + (Owner.ActualWidth - winWidth) / 2.0;
            targetTop = Owner.Top + (Owner.ActualHeight - winHeight) / 2.0;
        }
        else
        {
            targetLeft = workLeft + (workWidth - winWidth) / 2.0;
            targetTop = workTop + (workHeight - winHeight) / 2.0;
        }

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
