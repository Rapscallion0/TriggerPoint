using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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

public partial class ModernMessageDialog : Window
{
    public bool UserConfirmed { get; private set; }
    public FolderDeleteChoice FolderChoice { get; private set; } = FolderDeleteChoice.Cancel;

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

        Loaded += (s, e) => PrimaryBtn.Focus();
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

    private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
    {
        FolderChoice = FolderDeleteChoice.DeleteAll;
        UserConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void AlternateBtn_Click(object sender, RoutedEventArgs e)
    {
        FolderChoice = FolderDeleteChoice.MoveToRoot;
        UserConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void SecondaryBtn_Click(object sender, RoutedEventArgs e)
    {
        FolderChoice = FolderDeleteChoice.Cancel;
        UserConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FolderChoice = FolderDeleteChoice.Cancel;
            UserConfirmed = false;
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (FolderChoice == FolderDeleteChoice.Cancel)
            {
                FolderChoice = FolderDeleteChoice.DeleteAll;
            }
            UserConfirmed = true;
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }
}
