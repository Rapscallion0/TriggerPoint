using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.UI.Views;

public record BrowserTargetRuleOption(
    string Label,
    string PatternValue,
    string Description,
    bool IsRecommended = false);

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
    public string? SelectedChoiceValue { get; private set; }

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
        // Measure visual tree to determine true content bounds for accurate centering
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double winWidth = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        if (winWidth <= 0) winWidth = MinWidth > 0 ? MinWidth : 420;
        if (winHeight <= 0) winHeight = MinHeight > 0 ? MinHeight : 180;

        if (Owner != null && Owner.IsLoaded && Owner.WindowState == WindowState.Normal)
        {
            Left = Owner.Left + (Owner.ActualWidth - winWidth) / 2.0;
            Top = Owner.Top + (Owner.ActualHeight - winHeight) / 2.0;
            return;
        }

        IntPtr hMonitor = IntPtr.Zero;
        if (Owner != null && Owner.IsLoaded && Owner.WindowState != WindowState.Minimized)
        {
            var ownerHwnd = new WindowInteropHelper(Owner).Handle;
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

    public static string? ShowBrowserTargetChoiceDialog(
        Window? owner,
        string title,
        string message,
        IReadOnlyList<BrowserTargetRuleOption> options,
        string primaryButtonText = "Add Selected Rule")
    {
        if (options == null || options.Count == 0) return null;

        var dlg = new ModernMessageDialog(
            title,
            message,
            primaryButtonText: primaryButtonText,
            secondaryButtonText: "Cancel",
            dialogType: ModernDialogType.Question)
        {
            Owner = owner,
            MaxWidth = 580
        };

        dlg.DialogIconText.Text = "🌐";
        dlg.ChoiceOptionsPanel.Visibility = Visibility.Visible;
        dlg.SelectedChoiceValue = options[0].PatternValue;

        var bgInput = Application.Current.TryFindResource("BgInputBrush") as Brush ?? Brushes.Transparent;
        var borderDefault = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        var accentBrush = Application.Current.TryFindResource("AccentColorBrush") as Brush ?? Brushes.DodgerBlue;
        var textPrimary = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
        var textSecondary = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray;
        var bgTertiary = Application.Current.TryFindResource("BgTertiaryBrush") as Brush ?? Brushes.DarkSlateGray;

        var optionBorders = new List<Border>();
        var radioButtons = new List<RadioButton>();
        bool suppressTextSync = false;

        var editBox = new TextBox
        {
            Text = options[0].PatternValue,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            FontSize = 12.5,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 4, 8, 4)
        };
        if (Application.Current.TryFindResource("ModernTextBoxStyle") is Style modernTbStyle)
        {
            editBox.Style = modernTbStyle;
        }

        editBox.TextChanged += (_, _) =>
        {
            if (suppressTextSync) return;
            dlg.SelectedChoiceValue = editBox.Text.Trim();
            dlg.PrimaryBtn.IsEnabled = !string.IsNullOrWhiteSpace(dlg.SelectedChoiceValue);
        };

        for (int i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            int idx = i;

            var radio = new RadioButton
            {
                GroupName = "BrowserRuleOptionGroup",
                IsChecked = i == 0,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 10, 0)
            };

            var headerStack = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headerStack.Children.Add(new TextBlock
            {
                Text = opt.Label,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Foreground = textPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 2)
            });

            if (opt.IsRecommended)
            {
                var recBadge = new Border
                {
                    Background = accentBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 0, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Recommended",
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White
                    }
                };
                headerStack.Children.Add(recBadge);
            }

            var patternBadge = new Border
            {
                Background = bgTertiary,
                BorderBrush = borderDefault,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = opt.PatternValue,
                    FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                    FontSize = 12,
                    Foreground = textPrimary,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            var descText = new TextBlock
            {
                Text = opt.Description,
                FontSize = 11,
                Foreground = textSecondary,
                TextWrapping = TextWrapping.Wrap
            };

            var contentStack = new StackPanel();
            contentStack.Children.Add(headerStack);
            contentStack.Children.Add(patternBadge);
            contentStack.Children.Add(descText);

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(radio, 0);
            Grid.SetColumn(contentStack, 1);
            rowGrid.Children.Add(radio);
            rowGrid.Children.Add(contentStack);

            var rowBorder = new Border
            {
                Background = bgInput,
                BorderBrush = i == 0 ? accentBrush : borderDefault,
                BorderThickness = new Thickness(i == 0 ? 1.5 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Child = rowGrid
            };

            void SelectOption(bool focusEditor)
            {
                suppressTextSync = true;
                editBox.Text = opt.PatternValue;
                suppressTextSync = false;

                dlg.SelectedChoiceValue = opt.PatternValue;
                dlg.PrimaryBtn.IsEnabled = !string.IsNullOrWhiteSpace(dlg.SelectedChoiceValue);

                for (int j = 0; j < optionBorders.Count; j++)
                {
                    bool isSel = j == idx;
                    radioButtons[j].IsChecked = isSel;
                    optionBorders[j].BorderBrush = isSel ? accentBrush : borderDefault;
                    optionBorders[j].BorderThickness = new Thickness(isSel ? 1.5 : 1);
                }

                if (focusEditor)
                {
                    editBox.Focus();
                    editBox.CaretIndex = editBox.Text.Length;
                }
            }

            radio.Checked += (_, _) => SelectOption(focusEditor: true);
            rowBorder.MouseLeftButtonDown += (_, e) =>
            {
                SelectOption(focusEditor: true);
                if (e.ClickCount == 2 && !string.IsNullOrWhiteSpace(dlg.SelectedChoiceValue))
                {
                    dlg.UserConfirmed = true;
                    dlg.SafeClose(true);
                }
            };

            optionBorders.Add(rowBorder);
            radioButtons.Add(radio);
            dlg.ChoiceOptionsPanel.Children.Add(rowBorder);
        }

        var editSection = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0)
        };
        editSection.Children.Add(new TextBlock
        {
            Text = "Rule Pattern (Editable):",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = textPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        });
        editSection.Children.Add(editBox);
        editSection.Children.Add(new TextBlock
        {
            Text = "Tip: You can edit this pattern or add * wildcards before adding.",
            FontSize = 11,
            Foreground = textSecondary,
            Margin = new Thickness(0, 4, 0, 0)
        });

        dlg.ChoiceOptionsPanel.Children.Add(editSection);
        dlg.Loaded += (_, _) =>
        {
            editBox.Focus();
            editBox.CaretIndex = editBox.Text.Length;
        };

        dlg.ShowDialog();
        return dlg.UserConfirmed && !string.IsNullOrWhiteSpace(dlg.SelectedChoiceValue)
            ? dlg.SelectedChoiceValue.Trim()
            : null;
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
