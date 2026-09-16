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
        CenterOnActiveScreen();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
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

    private void CenterOnActiveScreen()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo)) return;

        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double workLeft = monitorInfo.rcWork.Left / dpiScale;
        double workTop = monitorInfo.rcWork.Top / dpiScale;
        double workWidth = (monitorInfo.rcWork.Right - monitorInfo.rcWork.Left) / dpiScale;
        double workHeight = (monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top) / dpiScale;

        double winWidth = ActualWidth > 0 ? ActualWidth : Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : 180;

        Left = workLeft + Math.Max(0, (workWidth - winWidth) / 2.0);
        Top = workTop + Math.Max(0, (workHeight - winHeight) / 2.0);
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
