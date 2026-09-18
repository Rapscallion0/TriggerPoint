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
    private readonly IUpdateService _updateService;
    private AppSettings _currentSettings = new();
    private List<TriggerItem> _allItems = [];

    internal enum SettingsCategory
    {
        Appearance,
        Shortcuts,
        System,
        Logging,
        Updates,
        Data
    }

    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;

    public bool TreeDataChanged { get; private set; }

    public ApplicationSettingsWindow(IConfigRepository repository, ILogManagerService logManagerService, IUpdateService? updateService = null)
    {
        InitializeComponent();
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logManagerService = logManagerService ?? throw new ArgumentNullException(nameof(logManagerService));
        _updateService = updateService ?? new GitHubUpdateService(repository);

        Loaded += async (s, e) =>
        {
            SelectCategory(SettingsCategory.Appearance);
            await LoadCurrentSettingsAsync();
        };

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
        double winWidth = Width > 0 ? Width : 780;
        double winHeight = Height > 0 ? Height : 620;

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

            // Populate Theme & Visuals
            ThemeCombo.SelectedIndex = _currentSettings.Theme switch
            {
                ThemePreference.System => 0,
                ThemePreference.Dark => 1,
                ThemePreference.Light => 2,
                _ => 0
            };
            EnableBackdropEffectsCheck.IsChecked = _currentSettings.EnableBackdropEffects;
            EnableUiAnimationsCheck.IsChecked = _currentSettings.EnableUiAnimations;

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
            CheatSheetHotkeyRecorder.Binding = _currentSettings.CheatSheetHotkey;
            OpenSettingsHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            CommandPaletteHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            CheatSheetHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            CheckHotkeyConflicts();

            // Populate Recycle Bin retention
            bool neverDelete = _currentSettings.RecycleBinRetentionDays == 0;
            NeverDeleteRecycleCheck.IsChecked = neverDelete;
            RecycleRetentionDaysInput.IsEnabled = !neverDelete;
            RecycleRetentionDaysInput.Text = neverDelete ? "0" : _currentSettings.RecycleBinRetentionDays.ToString();
            await RefreshRecycleBinStatusAsync();

            UpdateBackupCardUI();

            // Populate Update Settings
            PopulateUpdateSettingsUI();

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
        var cheatSheetHotkey = CheatSheetHotkeyRecorder.Binding;

        var validation = HotkeyRegistryValidator.ValidateApplicationHotkeys(openHotkey, cmdHotkey, _allItems, cheatSheetHotkey);
        if (!validation.IsValid)
        {
            ShortcutConflictWarningText.Text = validation.ErrorMessage;
            ShortcutConflictWarningBorder.Visibility = Visibility.Visible;
            ShortcutNavBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ShortcutConflictWarningBorder.Visibility = Visibility.Collapsed;
            ShortcutNavBadge.Visibility = Visibility.Collapsed;
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
            var cheatSheetHotkey = CheatSheetHotkeyRecorder.Binding;

            var validation = HotkeyRegistryValidator.ValidateApplicationHotkeys(openSettingsHotkey, cmdPaletteHotkey, _allItems, cheatSheetHotkey);
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
            _currentSettings.CheatSheetHotkey = cheatSheetHotkey;
            _currentSettings.EnableBackdropEffects = EnableBackdropEffectsCheck.IsChecked == true;
            _currentSettings.EnableUiAnimations = EnableUiAnimationsCheck.IsChecked == true;
            _currentSettings.RecycleBinRetentionDays = recycleDays;

            // Update settings
            _currentSettings.UpdateFrequency = UpdateFrequencyCombo.SelectedIndex switch
            {
                0 => UpdateCheckFrequency.OnStartup,
                1 => UpdateCheckFrequency.Daily,
                2 => UpdateCheckFrequency.Weekly,
                3 => UpdateCheckFrequency.Monthly,
                4 => UpdateCheckFrequency.ManualOnly,
                _ => UpdateCheckFrequency.Daily
            };
            _currentSettings.SilentInstallUpdates = SilentInstallUpdatesCheck.IsChecked == true;
            _currentSettings.IncludePreReleases = IncludePreReleasesCheck.IsChecked == true;

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
        if ((Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control && e.Key == Key.F)
        {
            SearchSettingsBox.Focus();
            SearchSettingsBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrWhiteSpace(SearchSettingsBox.Text))
            {
                SearchSettingsBox.Text = string.Empty;
                e.Handled = true;
                return;
            }

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

    private void PopulateUpdateSettingsUI()
    {
        var currentVer = _updateService.GetCurrentVersion();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        bool isSystem = (!string.IsNullOrEmpty(progFiles) && baseDir.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(progFilesX86) && baseDir.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase));
        string scope = isSystem ? "System" : "User";

        CurrentVersionInfoText.Text = $"v{currentVer} ({scope}-wide)";

        // Last Checked Text
        if (_currentSettings.LastUpdateCheckUtc.HasValue)
        {
            var localTime = _currentSettings.LastUpdateCheckUtc.Value.ToLocalTime();
            var dateStr = localTime.Date == DateTime.Today
                ? $"Today at {localTime:h:mm tt}"
                : (localTime.Date == DateTime.Today.AddDays(-1)
                    ? $"Yesterday at {localTime:h:mm tt}"
                    : localTime.ToString("MMM dd, yyyy h:mm tt"));
            LastCheckedInfoText.Text = dateStr;
        }
        else
        {
            LastCheckedInfoText.Text = "Never checked";
        }

        // Latest Version Found Text
        if (!string.IsNullOrWhiteSpace(_currentSettings.LastVersionFound))
        {
            int cmp = GitHubUpdateService.CompareVersions(_currentSettings.LastVersionFound, currentVer);
            if (cmp > 0)
            {
                LastVersionFoundText.Text = $"v{_currentSettings.LastVersionFound} (Update available)";
                LastVersionFoundText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                UpdateNavBadge.Visibility = Visibility.Visible;
            }
            else
            {
                LastVersionFoundText.Text = $"v{_currentSettings.LastVersionFound} (Up to date)";
                LastVersionFoundText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                UpdateNavBadge.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            LastVersionFoundText.Text = "None recorded yet";
            LastVersionFoundText.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            UpdateNavBadge.Visibility = Visibility.Collapsed;
        }

        // Update Frequency
        UpdateFrequencyCombo.SelectedIndex = _currentSettings.UpdateFrequency switch
        {
            UpdateCheckFrequency.OnStartup => 0,
            UpdateCheckFrequency.Daily => 1,
            UpdateCheckFrequency.Weekly => 2,
            UpdateCheckFrequency.Monthly => 3,
            UpdateCheckFrequency.ManualOnly => 4,
            _ => 1
        };

        SilentInstallUpdatesCheck.IsChecked = _currentSettings.SilentInstallUpdates;
        IncludePreReleasesCheck.IsChecked = _currentSettings.IncludePreReleases;

        // Ignored version banner
        RefreshIgnoredVersionUI();
    }

    private void RefreshIgnoredVersionUI()
    {
        if (!string.IsNullOrWhiteSpace(_currentSettings.IgnoredUpdateVersion))
        {
            IgnoredVersionBorder.Visibility = Visibility.Visible;
            IgnoredVersionText.Text = $"v{_currentSettings.IgnoredUpdateVersion}";
        }
        else
        {
            IgnoredVersionBorder.Visibility = Visibility.Collapsed;
        }
    }

    private async void CheckForUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesBtn.IsEnabled = false;
        UpdateCheckingPanel.Visibility = Visibility.Visible;
        UpdateCheckingStatusText.Text = "Checking GitHub Releases for updates...";
        SettingsStatusText.Text = "Checking for updates...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(isManualCheck: true);

            // Reload settings so last checked / version found are fresh
            _currentSettings = await _repository.LoadSettingsAsync();
            PopulateUpdateSettingsUI();

            UpdateCheckingPanel.Visibility = Visibility.Collapsed;
            CheckForUpdatesBtn.IsEnabled = true;

            if (result.IsUpdateAvailable)
            {
                SettingsStatusText.Text = $"Update v{result.LatestUpdate?.Version} available!";
                var updateDlg = new UpdateAvailableDialog(result, _updateService, _repository)
                {
                    Owner = this
                };
                updateDlg.ShowDialog();

                // Refresh settings after dialog closes (user may have chosen to ignore or update)
                _currentSettings = await _repository.LoadSettingsAsync();
                PopulateUpdateSettingsUI();
            }
            else if (result.IsSuccess)
            {
                SettingsStatusText.Text = "TriggerPoint is up to date.";
                ModernMessageDialog.ShowAlert(
                    this,
                    "TriggerPoint is Up to Date",
                    $"You are running TriggerPoint v{_updateService.GetCurrentVersion()}.\nNo newer releases were found on GitHub.",
                    ModernDialogType.Info);
            }
            else
            {
                SettingsStatusText.Text = "Update check failed.";
                ModernMessageDialog.ShowAlert(
                    this,
                    "Update Check Failed",
                    result.ErrorMessage ?? "Could not retrieve releases from GitHub. Check your internet connection.",
                    ModernDialogType.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error during manual update check.");
            UpdateCheckingPanel.Visibility = Visibility.Collapsed;
            CheckForUpdatesBtn.IsEnabled = true;
            SettingsStatusText.Text = "Update check failed.";
            ModernMessageDialog.ShowAlert(this, "Error", $"An unexpected error occurred: {ex.Message}", ModernDialogType.Error);
        }
    }

    private async void ResetIgnoredVersionBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _currentSettings.IgnoredUpdateVersion = null;
            await _repository.SaveSettingsAsync(_currentSettings);
            RefreshIgnoredVersionUI();
            SettingsStatusText.Text = "Ignored version reset.";
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to clear ignored update version.");
        }
    }

    #region Category Navigation & Search Engine

    private void NavCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            SettingsCategory category = btn.Name switch
            {
                nameof(NavAppearanceBtn) => SettingsCategory.Appearance,
                nameof(NavShortcutsBtn) => SettingsCategory.Shortcuts,
                nameof(NavSystemBtn) => SettingsCategory.System,
                nameof(NavLoggingBtn) => SettingsCategory.Logging,
                nameof(NavUpdatesBtn) => SettingsCategory.Updates,
                nameof(NavDataBtn) => SettingsCategory.Data,
                _ => SettingsCategory.Appearance
            };

            if (!string.IsNullOrEmpty(SearchSettingsBox.Text))
            {
                SearchSettingsBox.Text = string.Empty;
            }

            SelectCategory(category);
        }
    }

    private void SelectCategory(SettingsCategory category)
    {
        _selectedCategory = category;

        // Update sidebar nav tags for accent styling
        NavAppearanceBtn.Tag = category == SettingsCategory.Appearance ? "Selected" : null;
        NavShortcutsBtn.Tag = category == SettingsCategory.Shortcuts ? "Selected" : null;
        NavSystemBtn.Tag = category == SettingsCategory.System ? "Selected" : null;
        NavLoggingBtn.Tag = category == SettingsCategory.Logging ? "Selected" : null;
        NavUpdatesBtn.Tag = category == SettingsCategory.Updates ? "Selected" : null;
        NavDataBtn.Tag = category == SettingsCategory.Data ? "Selected" : null;

        // Toggle category panels
        CategoryAppearancePanel.Visibility = category == SettingsCategory.Appearance ? Visibility.Visible : Visibility.Collapsed;
        CategoryShortcutsPanel.Visibility = category == SettingsCategory.Shortcuts ? Visibility.Visible : Visibility.Collapsed;
        CategorySystemPanel.Visibility = category == SettingsCategory.System ? Visibility.Visible : Visibility.Collapsed;
        CategoryLoggingPanel.Visibility = category == SettingsCategory.Logging ? Visibility.Visible : Visibility.Collapsed;
        CategoryUpdatesPanel.Visibility = category == SettingsCategory.Updates ? Visibility.Visible : Visibility.Collapsed;
        CategoryDataPanel.Visibility = category == SettingsCategory.Data ? Visibility.Visible : Visibility.Collapsed;

        NoSearchResultsPanel.Visibility = Visibility.Collapsed;

        // Update breadcrumb and contextual subtitle
        switch (category)
        {
            case SettingsCategory.Appearance:
                CategoryBreadcrumbText.Text = "Settings > Appearance & Theme";
                CategorySubtitleText.Text = "Configure visual preferences, translucent materials, and interface micro-animations";
                break;
            case SettingsCategory.Shortcuts:
                CategoryBreadcrumbText.Text = "Settings > Shortcuts & Hotkeys";
                CategorySubtitleText.Text = "System-wide hotkeys to bring up TriggerPoint, the quick Command Palette, or the Cheat Sheet HUD";
                break;
            case SettingsCategory.System:
                CategoryBreadcrumbText.Text = "Settings > System Integration";
                CategorySubtitleText.Text = "Configure Windows startup, background launch behavior, and notification anchors";
                break;
            case SettingsCategory.Logging:
                CategoryBreadcrumbText.Text = "Settings > Diagnostics & Logging";
                CategorySubtitleText.Text = "Dynamic runtime log level and automatic daily rolling file retention";
                break;
            case SettingsCategory.Updates:
                CategoryBreadcrumbText.Text = "Settings > Updates & Maintenance";
                CategorySubtitleText.Text = "Check for newer versions and configure automatic update intervals";
                break;
            case SettingsCategory.Data:
                CategoryBreadcrumbText.Text = "Settings > Backup & Data Management";
                CategorySubtitleText.Text = "Export and import your TriggerPoint actions, folders, and application settings";
                break;
        }
    }

    private void SearchSettingsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchSettingsBox.Text?.Trim() ?? string.Empty;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchBtn.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

        if (string.IsNullOrEmpty(query))
        {
            SelectCategory(_selectedCategory);
        }
        else
        {
            FilterSettings(query);
        }
    }

    private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        SearchSettingsBox.Text = string.Empty;
        SearchSettingsBox.Focus();
    }

    private void SearchSettingsBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchSettingsBox.Text = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            NavAppearanceBtn.Focus();
            e.Handled = true;
        }
    }

    internal static readonly Dictionary<SettingsCategory, string[]> CategoryKeywords = new()
    {
        [SettingsCategory.Appearance] = ["appearance", "theme", "dark", "light", "system default", "mica", "acrylic", "material", "transparency", "translucent", "animation", "animations", "micro-transitions", "visual", "look"],
        [SettingsCategory.Shortcuts] = ["shortcut", "shortcuts", "hotkey", "hotkeys", "global", "action manager", "command palette", "cheat sheet", "hud", "overlay", "conflict", "key", "recorder"],
        [SettingsCategory.System] = ["system", "startup", "login", "windows", "minimized", "tray", "system tray", "hide window", "crosshair", "targeting", "toast", "toasts", "notification", "notifications", "monitor", "display", "active monitor", "primary monitor", "screen", "location", "placement", "validate", "path", "revert", "confirm", "unsaved"],
        [SettingsCategory.Logging] = ["serilog", "log", "logs", "logging", "diagnostics", "minimum log level", "retention", "days", "split", "threshold", "mb", "folder", "open logs", "verbose", "debug", "information", "warning", "error", "fatal"],
        [SettingsCategory.Updates] = ["update", "updates", "maintenance", "check for updates", "version", "latest", "frequency", "startup", "daily", "weekly", "monthly", "manual", "silent install", "restart", "pre-release", "beta", "preview", "ignored", "ignore", "release"],
        [SettingsCategory.Data] = ["backup", "data", "management", "recycle", "recycle bin", "retention", "purge", "empty", "delete", "telemetry", "export", "import", "restore", "json", "actions", "folders"]
    };

    internal static bool MatchesCategory(SettingsCategory cat, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        string lowerQuery = query.Trim().ToLowerInvariant();
        if (CategoryKeywords.TryGetValue(cat, out var words))
        {
            foreach (var word in words)
            {
                if (word.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                    lowerQuery.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void FilterSettings(string query)
    {
        // Clear selection tag on sidebar buttons while search is active
        NavAppearanceBtn.Tag = null;
        NavShortcutsBtn.Tag = null;
        NavSystemBtn.Tag = null;
        NavLoggingBtn.Tag = null;
        NavUpdatesBtn.Tag = null;
        NavDataBtn.Tag = null;

        int matchCount = 0;

        bool matchApp = MatchesCategory(SettingsCategory.Appearance, query);
        bool matchShort = MatchesCategory(SettingsCategory.Shortcuts, query);
        bool matchSys = MatchesCategory(SettingsCategory.System, query);
        bool matchLog = MatchesCategory(SettingsCategory.Logging, query);
        bool matchUpd = MatchesCategory(SettingsCategory.Updates, query);
        bool matchData = MatchesCategory(SettingsCategory.Data, query);

        CategoryAppearancePanel.Visibility = matchApp ? Visibility.Visible : Visibility.Collapsed;
        CategoryShortcutsPanel.Visibility = matchShort ? Visibility.Visible : Visibility.Collapsed;
        CategorySystemPanel.Visibility = matchSys ? Visibility.Visible : Visibility.Collapsed;
        CategoryLoggingPanel.Visibility = matchLog ? Visibility.Visible : Visibility.Collapsed;
        CategoryUpdatesPanel.Visibility = matchUpd ? Visibility.Visible : Visibility.Collapsed;
        CategoryDataPanel.Visibility = matchData ? Visibility.Visible : Visibility.Collapsed;

        if (matchApp) matchCount++;
        if (matchShort) matchCount++;
        if (matchSys) matchCount++;
        if (matchLog) matchCount++;
        if (matchUpd) matchCount++;
        if (matchData) matchCount++;

        if (matchCount == 0)
        {
            NoSearchResultsPanel.Visibility = Visibility.Visible;
            NoSearchQueryText.Text = $"No settings match \"{query}\"";
            CategoryBreadcrumbText.Text = "Search Results (0 matches)";
            CategorySubtitleText.Text = "Try a different keyword or clear the search filter";
        }
        else
        {
            NoSearchResultsPanel.Visibility = Visibility.Collapsed;
            CategoryBreadcrumbText.Text = $"Search Results for \"{query}\" ({matchCount} {(matchCount == 1 ? "category" : "categories")})";
            CategorySubtitleText.Text = "Showing matching settings categories below";
        }
    }

    #endregion
}
