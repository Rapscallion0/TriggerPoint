using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.Infrastructure.Win32;
using TriggerPoint.UI.Theme;

namespace TriggerPoint.UI.Views;

public partial class ApplicationSettingsWindow : Window
{
    private static readonly ILogger Logger = Log.ForContext<ApplicationSettingsWindow>();
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryValueName = "TriggerPoint";

    private readonly IConfigRepository _repository;
    private readonly ILogManagerService _logManagerService;
    private AppSettings _currentSettings = new();
    private List<TriggerItem> _allItems = [];

    public bool TreeDataChanged { get; private set; }

    public ApplicationSettingsWindow(IConfigRepository repository, ILogManagerService logManagerService)
    {
        InitializeComponent();
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logManagerService = logManagerService ?? throw new ArgumentNullException(nameof(logManagerService));

        Loaded += async (s, e) => await LoadCurrentSettingsAsync();

        ThemeManager.ThemeChanged += (s, theme) =>
        {
            ThemeManager.ApplyWindowIcons(this);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnOwnerOrActiveMonitor();
        ThemeManager.ApplyWindowIcons(this);
    }

    private void CenterOnOwnerOrActiveMonitor()
    {
        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double winWidth = Width > 0 ? Width : 600;
        double winHeight = Height > 0 ? Height : 650;

        // 1. If Owner is set and visible, center over Owner
        if (Owner != null && Owner.IsVisible)
        {
            CenterOverWindow(Owner, winWidth, winHeight, dpiScale);
            return;
        }

        // 2. If main window exists in App and is visible, center over it
        if (Application.Current is App app && app.MainWindow != null && app.MainWindow.IsVisible)
        {
            CenterOverWindow(app.MainWindow, winWidth, winHeight, dpiScale);
            return;
        }

        // 3. Otherwise center on the monitor where the cursor currently is (e.g. tray click)
        if (NativeMethods.GetCursorPos(out var pt))
        {
            var hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                double workLeft = mi.rcWork.Left / dpiScale;
                double workTop = mi.rcWork.Top / dpiScale;
                double workWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiScale;
                double workHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiScale;

                Left = workLeft + Math.Max(0, (workWidth - winWidth) / 2.0);
                Top = workTop + Math.Max(0, (workHeight - winHeight) / 2.0);
                return;
            }
        }

        // Fallback: Primary Display
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - winWidth) / 2.0);
        Top = workArea.Top + Math.Max(0, (workArea.Height - winHeight) / 2.0);
    }

    private void CenterOverWindow(Window target, double winWidth, double winHeight, double dpiScale)
    {
        var targetHandle = new System.Windows.Interop.WindowInteropHelper(target).Handle;
        if (targetHandle != IntPtr.Zero)
        {
            var hMon = NativeMethods.MonitorFromWindow(targetHandle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                double workLeft = mi.rcWork.Left / dpiScale;
                double workTop = mi.rcWork.Top / dpiScale;
                double workWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiScale;
                double workHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiScale;

                double targetCenterX = target.Left + (target.ActualWidth > 0 ? target.ActualWidth : target.Width) / 2.0;
                double targetCenterY = target.Top + (target.ActualHeight > 0 ? target.ActualHeight : target.Height) / 2.0;

                double l = targetCenterX - winWidth / 2.0;
                double t = targetCenterY - winHeight / 2.0;

                Left = Math.Clamp(l, workLeft, workLeft + Math.Max(0, workWidth - winWidth));
                Top = Math.Clamp(t, workTop, workTop + Math.Max(0, workHeight - winHeight));
                return;
            }
        }

        Left = target.Left + Math.Max(0, (target.Width - winWidth) / 2.0);
        Top = target.Top + Math.Max(0, (target.Height - winHeight) / 2.0);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private async System.Threading.Tasks.Task LoadCurrentSettingsAsync()
    {
        try
        {
            _currentSettings = await _repository.LoadSettingsAsync();
            _allItems = (await _repository.LoadAsync()).ToList();

            // Populate Log Level
            LogLevelCombo.SelectedIndex = _currentSettings.LogLevel switch
            {
                LogLevelOption.Verbose => 0,
                LogLevelOption.Debug => 1,
                LogLevelOption.Information => 2,
                LogLevelOption.Warning => 3,
                LogLevelOption.Error => 4,
                LogLevelOption.Fatal => 5,
                _ => 2
            };

            // Populate Retention Days
            RetentionDaysCombo.SelectedIndex = _currentSettings.LogRetentionDays switch
            {
                1 => 0,
                3 => 1,
                7 => 2,
                14 => 3,
                30 => 4,
                90 => 5,
                _ => 2
            };
            RetentionDaysText.Text = $"{_currentSettings.LogRetentionDays} days";

            // Populate Log Split Threshold
            LogSplitThresholdCombo.SelectedIndex = _currentSettings.LogSplitThresholdMb switch
            {
                25 => 0,
                50 => 1,
                100 => 2,
                250 => 3,
                500 => 4,
                1000 => 5,
                _ => 2
            };
            LogSplitThresholdText.Text = $"{_currentSettings.LogSplitThresholdMb} MB";

            // Populate Theme
            ThemeCombo.SelectedIndex = _currentSettings.Theme switch
            {
                ThemePreference.System => 0,
                ThemePreference.Dark => 1,
                ThemePreference.Light => 2,
                _ => 0
            };

            // Populate Startup & Minimized
            RunAtStartupCheck.IsChecked = IsRunAtStartupConfigured() || _currentSettings.RunAtStartup;
            StartMinimizedCheck.IsChecked = _currentSettings.StartMinimized;
            HideWindowOnTargetCheck.IsChecked = _currentSettings.HideOnTargetWindow;
            ShowSuccessToastsCheck.IsChecked = _currentSettings.ShowSuccessToasts;
            ToastPlacementCombo.SelectedIndex = _currentSettings.ToastPlacement == ToastMonitorPlacement.ActiveMonitor ? 1 : 0;
            ToastPlacementPanel.IsEnabled = _currentSettings.ShowSuccessToasts;
            ShowSuccessToastsCheck.Checked += (s, e) => ToastPlacementPanel.IsEnabled = true;
            ShowSuccessToastsCheck.Unchecked += (s, e) => ToastPlacementPanel.IsEnabled = false;

            WindowPlacementCombo.SelectedIndex = _currentSettings.WindowPlacement switch
            {
                WindowStartupPlacement.RememberLast => 0,
                WindowStartupPlacement.PrimaryDisplay => 1,
                WindowStartupPlacement.CursorDisplay => 2,
                _ => 0
            };

            ValidateOnStartupCheck.IsChecked = _currentSettings.ValidateShortcutsOnStartup;
            ConfirmRevertChangesCheck.IsChecked = _currentSettings.ConfirmRevertChanges;

            // Populate Global Shortcuts
            OpenSettingsHotkeyRecorder.Binding = _currentSettings.OpenSettingsHotkey;
            CommandPaletteHotkeyRecorder.Binding = _currentSettings.CommandPaletteHotkey;
            OpenSettingsHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            CommandPaletteHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            CheckHotkeyConflicts();

            // Populate Recycle Bin retention
            bool neverDelete = _currentSettings.RecycleBinRetentionDays == 0;
            NeverDeleteRecycleCheck.IsChecked = neverDelete;
            RecycleRetentionDaysInput.IsEnabled = !neverDelete;
            RecycleRetentionDaysInput.Text = neverDelete ? "0" : _currentSettings.RecycleBinRetentionDays.ToString();
            await RefreshRecycleBinStatusAsync();

            UpdateBackupCardUI();

            SettingsStatusText.Text = "Settings loaded.";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load application settings.");
            SettingsStatusText.Text = "Failed to load settings.";
        }
    }

    private void CheckHotkeyConflicts()
    {
        var openHotkey = OpenSettingsHotkeyRecorder.Binding;
        var cmdHotkey = CommandPaletteHotkeyRecorder.Binding;

        var validation = HotkeyRegistryValidator.ValidateApplicationHotkeys(openHotkey, cmdHotkey, _allItems);
        if (!validation.IsValid)
        {
            ShortcutConflictWarningText.Text = validation.ErrorMessage;
            ShortcutConflictWarningBorder.Visibility = Visibility.Visible;
        }
        else
        {
            ShortcutConflictWarningBorder.Visibility = Visibility.Collapsed;
        }
    }

    private async System.Threading.Tasks.Task RefreshRecycleBinStatusAsync()
    {
        try
        {
            var recycled = await _repository.LoadRecycleBinAsync();
            int count = recycled.Count;
            RecycleBinStatusText.Text = $"Recycle Bin: {count} item{(count == 1 ? "" : "s")}";
            EmptyRecycleBinBtn.IsEnabled = count > 0;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load recycle bin status.");
            RecycleBinStatusText.Text = "Recycle Bin: Unknown";
            EmptyRecycleBinBtn.IsEnabled = false;
        }
    }

    private void NeverDeleteRecycleCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (RecycleRetentionDaysInput == null) return;
        bool never = NeverDeleteRecycleCheck.IsChecked == true;
        RecycleRetentionDaysInput.IsEnabled = !never;
        if (never)
        {
            RecycleRetentionDaysInput.Text = "0";
        }
        else if (RecycleRetentionDaysInput.Text == "0" || string.IsNullOrWhiteSpace(RecycleRetentionDaysInput.Text))
        {
            RecycleRetentionDaysInput.Text = "30";
        }
    }

    private void RecycleRetentionDaysInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private async void EmptyRecycleBinBtn_Click(object sender, RoutedEventArgs e)
    {
        bool confirm = ModernMessageDialog.ShowConfirm(this,
            "Empty Recycle Bin",
            "Are you sure you want to permanently delete all items in the Recycle Bin? This action cannot be undone.",
            "Empty Bin", "Cancel");

        if (!confirm) return;

        try
        {
            await _repository.EmptyRecycleBinAsync();
            TreeDataChanged = true;
            await RefreshRecycleBinStatusAsync();
            SettingsStatusText.Text = "Recycle Bin emptied.";
            ModernMessageDialog.ShowAlert(this, "Recycle Bin Emptied", "All items have been permanently deleted.", ModernDialogType.Info);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to empty recycle bin.");
            ModernMessageDialog.ShowAlert(this, "Error", $"Failed to empty recycle bin: {ex.Message}", ModernDialogType.Error);
        }
    }

    private void UpdateBackupCardUI()
    {
        int folderCount = _allItems.Count(x => x.ActionType == ActionType.Folder);
        int actionCount = _allItems.Count(x => x.ActionType != ActionType.Folder);
        BackupTelemetryText.Text = $"Current Data: {folderCount} Folder(s), {actionCount} Action(s) | Theme: {_currentSettings.Theme}, Logging: {_currentSettings.LogLevel}";

        ExportFullBackupMenuItem.Header = $"Complete Backup (Actions & Settings) ({folderCount} folders, {actionCount} actions)";
        ExportTreeItemsMenuItem.Header = $"All Actions & Folders Only ({folderCount} folders, {actionCount} actions)";
    }

    private void RetentionDaysCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RetentionDaysCombo == null || RetentionDaysText == null) return;
        int days = GetSelectedRetentionDays();
        RetentionDaysText.Text = $"{days} days";
    }

    private int GetSelectedRetentionDays() => RetentionDaysCombo.SelectedIndex switch
    {
        0 => 1,
        1 => 3,
        2 => 7,
        3 => 14,
        4 => 30,
        5 => 90,
        _ => 7
    };

    private void LogSplitThresholdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogSplitThresholdCombo == null || LogSplitThresholdText == null) return;
        int mb = GetSelectedLogSplitThreshold();
        LogSplitThresholdText.Text = $"{mb} MB";
    }

    private int GetSelectedLogSplitThreshold() => LogSplitThresholdCombo.SelectedIndex switch
    {
        0 => 25,
        1 => 50,
        2 => 100,
        3 => 250,
        4 => 500,
        5 => 1000,
        _ => 100
    };

    private async void SaveSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openSettingsHotkey = OpenSettingsHotkeyRecorder.Binding;
            var cmdPaletteHotkey = CommandPaletteHotkeyRecorder.Binding;

            var validation = HotkeyRegistryValidator.ValidateApplicationHotkeys(openSettingsHotkey, cmdPaletteHotkey, _allItems);
            if (!validation.IsValid)
            {
                CheckHotkeyConflicts();
                ModernMessageDialog.ShowAlert(this, "Shortcut Conflict", validation.ErrorMessage ?? "A shortcut conflict was detected.", ModernDialogType.Error);
                return;
            }

            int recycleDays = 30;
            if (NeverDeleteRecycleCheck.IsChecked == true)
            {
                recycleDays = 0;
            }
            else if (int.TryParse(RecycleRetentionDaysInput.Text.Trim(), out int parsedDays))
            {
                recycleDays = Math.Clamp(parsedDays, 1, 3650);
            }

            var level = LogLevelCombo.SelectedIndex switch
            {
                0 => LogLevelOption.Verbose,
                1 => LogLevelOption.Debug,
                2 => LogLevelOption.Information,
                3 => LogLevelOption.Warning,
                4 => LogLevelOption.Error,
                5 => LogLevelOption.Fatal,
                _ => LogLevelOption.Information
            };

            var retentionDays = GetSelectedRetentionDays();
            var splitThresholdMb = GetSelectedLogSplitThreshold();
            var themePref = ThemeCombo.SelectedIndex switch
            {
                0 => ThemePreference.System,
                1 => ThemePreference.Dark,
                2 => ThemePreference.Light,
                _ => ThemePreference.System
            };

            bool runStartup = RunAtStartupCheck.IsChecked == true;
            bool startMinimized = StartMinimizedCheck.IsChecked == true;
            bool hideOnTarget = HideWindowOnTargetCheck.IsChecked == true;

            _currentSettings.LogLevel = level;
            _currentSettings.LogRetentionDays = retentionDays;
            _currentSettings.LogSplitThresholdMb = splitThresholdMb;
            _currentSettings.Theme = themePref;
            _currentSettings.RunAtStartup = runStartup;
            _currentSettings.StartMinimized = startMinimized;
            _currentSettings.HideOnTargetWindow = hideOnTarget;
            _currentSettings.ShowSuccessToasts = ShowSuccessToastsCheck.IsChecked == true;
            _currentSettings.ToastPlacement = ToastPlacementCombo.SelectedIndex == 1 
                ? ToastMonitorPlacement.ActiveMonitor 
                : ToastMonitorPlacement.PrimaryMonitor;
            _currentSettings.WindowPlacement = WindowPlacementCombo.SelectedIndex switch
            {
                0 => WindowStartupPlacement.RememberLast,
                1 => WindowStartupPlacement.PrimaryDisplay,
                2 => WindowStartupPlacement.CursorDisplay,
                _ => WindowStartupPlacement.RememberLast
            };
            _currentSettings.ValidateShortcutsOnStartup = ValidateOnStartupCheck.IsChecked == true;
            _currentSettings.ConfirmRevertChanges = ConfirmRevertChangesCheck.IsChecked == true;
            _currentSettings.OpenSettingsHotkey = openSettingsHotkey;
            _currentSettings.CommandPaletteHotkey = cmdPaletteHotkey;
            _currentSettings.RecycleBinRetentionDays = recycleDays;

            // 1. Persist to appsettings.json
            await _repository.SaveSettingsAsync(_currentSettings);

            // 2. Dynamically apply log level at runtime without app restart
            _logManagerService.UpdateLogLevel(level);

            // 3. Apply theme preference immediately
            ThemeManager.ApplyPreference(themePref);

            // 4. Configure Windows Startup Registry Key
            SetRunAtStartup(runStartup, startMinimized);

            Logger.Information("Application settings successfully updated. LogLevel={LogLevel}, RetentionDays={RetentionDays}, SplitThreshold={SplitThreshold}MB, Theme={Theme}, Startup={Startup}, RecycleDays={RecycleDays}",
                level, retentionDays, splitThresholdMb, themePref, runStartup, recycleDays);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save application settings.");
            ModernMessageDialog.ShowAlert(this, "Settings Error", $"Failed to save application settings: {ex.Message}", ModernDialogType.Error);
        }
    }

    private void OpenLogsFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        _logManagerService.OpenLogDirectory();
    }

    private void ExportBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ExportBackupContextMenu != null)
        {
            ExportBackupContextMenu.PlacementTarget = ExportBackupBtn;
            ExportBackupContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ExportBackupContextMenu.IsOpen = true;
        }
    }

    private async void ExportFullBackupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = ConfigurationBackupPackage.CreateFullBackup(_allItems, _currentSettings);
            var dlg = new SaveFileDialog
            {
                Title = "Export Complete Backup",
                Filter = "JSON Backup File (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"triggerpoint-backup-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };

            if (dlg.ShowDialog(this) == true)
            {
                await _repository.ExportPackageAsync(dlg.FileName, package);
                SettingsStatusText.Text = $"Exported complete backup ({package.FolderCount} folders, {package.ActionCount} actions).";
                ModernMessageDialog.ShowAlert(this, "Export Complete",
                    $"Successfully exported complete backup with {package.FolderCount} folder(s), {package.ActionCount} action(s), and Application Settings to:\n{Path.GetFileName(dlg.FileName)}",
                    ModernDialogType.Info);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to export complete backup");
            ModernMessageDialog.ShowAlert(this, "Export Error", $"Failed to export backup: {ex.Message}", ModernDialogType.Error);
        }
    }

    private async void ExportTreeItemsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = ConfigurationBackupPackage.CreateTreeItems(_allItems, "All Items");
            var dlg = new SaveFileDialog
            {
                Title = "Export All Actions & Folders",
                Filter = "JSON Backup File (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"triggerpoint-actions-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };

            if (dlg.ShowDialog(this) == true)
            {
                await _repository.ExportPackageAsync(dlg.FileName, package);
                SettingsStatusText.Text = $"Exported {package.FolderCount} folders and {package.ActionCount} actions.";
                ModernMessageDialog.ShowAlert(this, "Export Complete",
                    $"Successfully exported {package.FolderCount} folder(s) and {package.ActionCount} action(s) to:\n{Path.GetFileName(dlg.FileName)}",
                    ModernDialogType.Info);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to export actions and folders");
            ModernMessageDialog.ShowAlert(this, "Export Error", $"Failed to export items: {ex.Message}", ModernDialogType.Error);
        }
    }

    private async void ExportSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = ConfigurationBackupPackage.CreateAppSettings(_currentSettings);
            var dlg = new SaveFileDialog
            {
                Title = "Export Application Settings",
                Filter = "JSON Backup File (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"triggerpoint-settings-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };

            if (dlg.ShowDialog(this) == true)
            {
                await _repository.ExportPackageAsync(dlg.FileName, package);
                SettingsStatusText.Text = "Exported application settings.";
                ModernMessageDialog.ShowAlert(this, "Export Complete",
                    $"Successfully exported application settings to:\n{Path.GetFileName(dlg.FileName)}",
                    ModernDialogType.Info);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to export application settings");
            ModernMessageDialog.ShowAlert(this, "Export Error", $"Failed to export settings: {ex.Message}", ModernDialogType.Error);
        }
    }

    private async void ImportBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Import / Restore Backup",
                Filter = "JSON Backup File (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog(this) != true) return;

            var package = await _repository.ReadPackageAsync(dlg.FileName);
            if (package == null)
            {
                ModernMessageDialog.ShowAlert(this, "Import Error", "Failed to read backup package from the selected file.", ModernDialogType.Error);
                return;
            }

            if (package.ContentType == BackupContentType.AppSettings)
            {
                if (package.Settings == null)
                {
                    ModernMessageDialog.ShowAlert(this, "Import Error", "No application settings found in the package.", ModernDialogType.Warning);
                    return;
                }

                bool apply = ModernMessageDialog.ShowConfirm(this, "Apply Application Settings",
                    $"The selected file contains Application Settings:\n• Theme: {package.Settings.Theme}\n• Log Level: {package.Settings.LogLevel}\n• Retention: {package.Settings.LogRetentionDays} days\n• Split Threshold: {package.Settings.LogSplitThresholdMb} MB\n• Run at Startup: {package.Settings.RunAtStartup}\n\nApply these settings now?",
                    "Apply Settings", "Cancel");

                if (apply)
                {
                    _currentSettings = package.Settings;
                    await _repository.SaveSettingsAsync(_currentSettings);
                    _logManagerService.UpdateLogLevel(_currentSettings.LogLevel);
                    ThemeManager.ApplyPreference(_currentSettings.Theme);
                    SetRunAtStartup(_currentSettings.RunAtStartup, _currentSettings.StartMinimized);
                    await LoadCurrentSettingsAsync();
                    SettingsStatusText.Text = "Application settings restored.";
                    ModernMessageDialog.ShowAlert(this, "Settings Restored", "Application settings have been successfully updated.", ModernDialogType.Info);
                }
                return;
            }

            // Contains items (TreeItems or FullBackup)
            var choice = ModernMessageDialog.ShowImportChoiceDialog(
                this,
                "All Items",
                package.FolderCount,
                package.ActionCount,
                canReplace: true);

            if (choice == ImportChoice.Cancel) return;

            if (choice == ImportChoice.Replace)
            {
                _allItems = package.Items.Select(x => x.Clone()).ToList();
            }
            else // Merge
            {
                var idMap = new Dictionary<Guid, Guid>();
                foreach (var item in package.Items)
                {
                    idMap[item.Id] = Guid.NewGuid();
                }

                foreach (var origItem in package.Items)
                {
                    var clone = origItem.Clone();
                    clone.Id = idMap[origItem.Id];
                    if (origItem.ParentId.HasValue && idMap.TryGetValue(origItem.ParentId.Value, out var newParentGuid))
                    {
                        clone.ParentId = newParentGuid;
                    }
                    else
                    {
                        clone.ParentId = null;
                    }
                    _allItems.Add(clone);
                }
            }

            await _repository.SaveAsync(_allItems);
            TreeDataChanged = true;

            if (package.ContentType == BackupContentType.FullBackup && package.Settings != null)
            {
                _currentSettings = package.Settings;
                await _repository.SaveSettingsAsync(_currentSettings);
                _logManagerService.UpdateLogLevel(_currentSettings.LogLevel);
                ThemeManager.ApplyPreference(_currentSettings.Theme);
                SetRunAtStartup(_currentSettings.RunAtStartup, _currentSettings.StartMinimized);
                await LoadCurrentSettingsAsync();
            }
            else
            {
                UpdateBackupCardUI();
            }

            SettingsStatusText.Text = $"Restored {package.FolderCount} folder(s) and {package.ActionCount} action(s).";
            ModernMessageDialog.ShowAlert(this, "Restore Complete",
                $"Successfully imported {package.FolderCount} folder(s) and {package.ActionCount} action(s)." +
                (package.ContentType == BackupContentType.FullBackup ? " Application settings were also updated." : ""),
                ModernDialogType.Info);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to import/restore backup");
            ModernMessageDialog.ShowAlert(this, "Import Error", $"Failed to import backup: {ex.Message}", ModernDialogType.Error);
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private static bool IsRunAtStartupConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            var val = key?.GetValue(AppRegistryValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    private static void SetRunAtStartup(bool enable, bool startMinimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    string args = startMinimized ? " --minimized" : "";
                    key.SetValue(AppRegistryValueName, $"\"{exePath}\"{args}");
                }
            }
            else
            {
                key.DeleteValue(AppRegistryValueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to update Windows startup registry key.");
        }
    }
}
