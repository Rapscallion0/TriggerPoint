using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public enum ModernDialogType
{
    Question,
    Warning,
    Error,
    Info
}

public enum FolderDeleteChoice
{
    Cancel,
    DeleteAll,
    MoveToRoot
}

public enum ImportChoice
{
    Cancel,
    Replace,
    Merge
}

public enum ConvertJsChoice
{
    Cancel,
    Recompile,
    KeepScript
}

public enum SavePromptChoice
{
    Cancel,
    Save,
    Discard
}

public partial class ModernMessageDialog : Window
{
    public bool UserConfirmed { get; private set; }
    public FolderDeleteChoice FolderChoice { get; private set; } = FolderDeleteChoice.Cancel;
    public ImportChoice ImportUserChoice { get; private set; } = ImportChoice.Cancel;
    public ConvertJsChoice ConvertJsUserChoice { get; private set; } = ConvertJsChoice.Cancel;
    public SavePromptChoice SaveChoice { get; private set; } = SavePromptChoice.Cancel;

    public ModernMessageDialog(
        string title, 
        string message, 
        string primaryButtonText = "Confirm", 
        string secondaryButtonText = "Cancel",
        ModernDialogType dialogType = ModernDialogType.Question,
        bool isDestructive = false)
    {
        InitializeComponent();

        DialogTitleText.Text = title;
        DialogMessageText.Text = message;
        PrimaryBtn.Content = primaryButtonText;
        SecondaryBtn.Content = secondaryButtonText;

        if (isDestructive)
        {
            PrimaryBtn.Background = (Brush)Application.Current.FindResource("ErrorBrush");
        }

        DialogIconText.Text = dialogType switch
        {
            ModernDialogType.Question => "❓",
            ModernDialogType.Warning => "⚠",
            ModernDialogType.Error => "❌",
            ModernDialogType.Info => "ℹ",
            _ => "ℹ"
        };

        Loaded += (s, e) =>
        {
            CenterOnOwnerOrActiveScreen();
            PrimaryBtn.Focus();
        };
    }

    private void CenterOnOwnerOrActiveScreen()
    {
        double winWidth = ActualWidth > 0 ? ActualWidth : Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : Height;
        if (winWidth <= 0) winWidth = 460;
        if (winHeight <= 0) winHeight = 220;

        IntPtr hMonitor = IntPtr.Zero;
        double workLeft, workTop, workWidth, workHeight;
        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        if (Owner != null && Owner.IsLoaded && Owner.WindowState != WindowState.Minimized)
        {
            var ownerHwnd = new WindowInteropHelper(Owner).Handle;
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

    public static bool ShowConfirm(
        Window? owner, 
        string title, 
        string message, 
        string primaryText = "Confirm", 
        string secondaryText = "Cancel",
        bool isDestructive = false)
    {
        var dlg = new ModernMessageDialog(title, message, primaryText, secondaryText, ModernDialogType.Question, isDestructive)
        {
            Owner = owner
        };
        dlg.ShowDialog();
        return dlg.UserConfirmed;
    }

    public static FolderDeleteChoice ShowFolderDeleteConfirm(
        Window? owner, 
        string folderName, 
        int childCount)
    {
        var dlg = new ModernMessageDialog(
            "Delete Folder",
            $"Folder '{folderName}' contains {childCount} item{(childCount == 1 ? "" : "s")}. What would you like to do with them?",
            primaryButtonText: $"Delete All ({childCount + 1} Items)",
            secondaryButtonText: "Cancel",
            dialogType: ModernDialogType.Warning,
            isDestructive: true)
        {
            Owner = owner
        };
        dlg.AlternateBtn.Visibility = Visibility.Visible;
        dlg.ShowDialog();
        return dlg.FolderChoice;
    }

    public static ImportChoice ShowImportChoiceDialog(
        Window? owner, 
        string title, 
        string message, 
        string primaryText = "Replace All", 
        string alternateText = "Merge", 
        string secondaryText = "Cancel",
        bool isDestructive = true)
    {
        var dlg = new ModernMessageDialog(
            title, 
            message, 
            primaryButtonText: primaryText, 
            secondaryButtonText: secondaryText, 
            dialogType: ModernDialogType.Question, 
            isDestructive: isDestructive)
        {
            Owner = owner
        };
        dlg.AlternateBtn.Content = alternateText;
        dlg.AlternateBtn.Visibility = Visibility.Visible;
        dlg.ShowDialog();
        return dlg.ImportUserChoice;
    }

    public static ImportChoice ShowImportChoiceDialog(
        Window? owner,
        string targetScopeName,
        int folderCount,
        int actionCount,
        bool canReplace = true)
    {
        string title = "Import Configuration";
        string message = $"The backup package contains {folderCount} folder(s) and {actionCount} action(s).\n\nDestination: {targetScopeName}\n\nHow would you like to import these items?";
        var dlg = new ModernMessageDialog(
            title,
            message,
            primaryButtonText: "Replace All",
            secondaryButtonText: "Cancel",
            dialogType: ModernDialogType.Question,
            isDestructive: true)
        {
            Owner = owner
        };
        dlg.AlternateBtn.Content = "Merge (Append)";
        dlg.AlternateBtn.Visibility = Visibility.Visible;

        if (!canReplace)
        {
            dlg.PrimaryBtn.Visibility = Visibility.Collapsed;
        }

        dlg.ShowDialog();
        return dlg.ImportUserChoice;
    }

    public static ConvertJsChoice ShowConvertJsDialog(Window? owner)
    {
        var dlg = new ModernMessageDialog(
            "Convert to JavaScript",
            "You already have a JavaScript script from a previous conversion.\n\nWhat would you like to do?",
            primaryButtonText: "⚡ Re-compile from Steps",
            secondaryButtonText: "Cancel",
            dialogType: ModernDialogType.Question,
            isDestructive: true)
        {
            Owner = owner
        };
        dlg.AlternateBtn.Content = "← Keep My Script";
        dlg.AlternateBtn.Visibility = Visibility.Visible;
        dlg.ShowDialog();
        return dlg.ConvertJsUserChoice;
    }

    public static void ShowAlert(
        Window? owner, 
        string title, 
        string message, 
        ModernDialogType dialogType = ModernDialogType.Info)
    {
        var dlg = new ModernMessageDialog(title, message, "OK", string.Empty, dialogType)
        {
            Owner = owner
        };
        dlg.SecondaryBtn.Visibility = Visibility.Collapsed;
        dlg.ShowDialog();
    }

    public static SavePromptChoice ShowUnsavedChangesDialog(
        Window? owner, 
        string itemName)
    {
        var dlg = new ModernMessageDialog(
            "Unsaved Changes",
            $"You have unsaved changes to '{itemName}'.\n\nWhat would you like to do before switching?",
            primaryButtonText: "💾 Save Changes",
            secondaryButtonText: "Cancel",
            dialogType: ModernDialogType.Warning)
        {
            Owner = owner
        };
        dlg.AlternateBtn.Content = "Discard Changes";
        dlg.AlternateBtn.Visibility = Visibility.Visible;
        dlg.ShowDialog();
        return dlg.SaveChoice;
    }

    private void SafeClose(bool? result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            // If the window is already closing, closed, or not showing as dialog, fallback to Close()
            try
            {
                Close();
            }
            catch { }
        }
    }

    private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
    {
        FolderChoice = FolderDeleteChoice.DeleteAll;
        ImportUserChoice = ImportChoice.Replace;
        ConvertJsUserChoice = ConvertJsChoice.Recompile;
        SaveChoice = SavePromptChoice.Save;
        UserConfirmed = true;
        SafeClose(true);
    }

    private void AlternateBtn_Click(object sender, RoutedEventArgs e)
    {
        FolderChoice = FolderDeleteChoice.MoveToRoot;
        ImportUserChoice = ImportChoice.Merge;
        ConvertJsUserChoice = ConvertJsChoice.KeepScript;
        SaveChoice = SavePromptChoice.Discard;
        UserConfirmed = true;
        SafeClose(true);
    }

    private void SecondaryBtn_Click(object sender, RoutedEventArgs e)
    {
        FolderChoice = FolderDeleteChoice.Cancel;
        ImportUserChoice = ImportChoice.Cancel;
        ConvertJsUserChoice = ConvertJsChoice.Cancel;
        SaveChoice = SavePromptChoice.Cancel;
        UserConfirmed = false;
        SafeClose(false);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FolderChoice = FolderDeleteChoice.Cancel;
            ImportUserChoice = ImportChoice.Cancel;
            SaveChoice = SavePromptChoice.Cancel;
            UserConfirmed = false;
            SafeClose(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (FolderChoice == FolderDeleteChoice.Cancel)
            {
                FolderChoice = FolderDeleteChoice.DeleteAll;
            }
            if (ImportUserChoice == ImportChoice.Cancel)
            {
                ImportUserChoice = ImportChoice.Replace;
            }
            if (ConvertJsUserChoice == ConvertJsChoice.Cancel)
            {
                ConvertJsUserChoice = ConvertJsChoice.Recompile;
            }
            if (SaveChoice == SavePromptChoice.Cancel)
            {
                SaveChoice = SavePromptChoice.Save;
            }
            UserConfirmed = true;
            SafeClose(true);
            e.Handled = true;
        }
    }
}
