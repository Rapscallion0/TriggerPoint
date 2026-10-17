using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IAppPathsService? _pathsService;
    private AppSettings _currentSettings = new();
    private List<TriggerItem> _allItems = [];
    private bool _savedExplorerMenuEnabled;
    private ThemePreference _initialTheme;
    private AccentColorChoice _initialAccent;
    private string? _initialCustomHex;
    private string? _currentCustomHex;
    private bool _isAppearanceLoaded;
    private bool _settingsSaved;

    public enum SettingsCategory
    {
        Appearance,
        Shortcuts,
        System,
        Logging,
        Updates,
        Data,
        Productivity,
        Clipboard
    }

    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;
    private readonly IProductivityService? _productivityService;
    private readonly IClipboardHistoryService? _clipboardService;

    public bool TreeDataChanged { get; private set; }

    public ApplicationSettingsWindow(
        IConfigRepository repository,
        ILogManagerService logManagerService,
        IUpdateService? updateService = null,
        SettingsCategory initialCategory = SettingsCategory.Appearance,
        IAppPathsService? pathsService = null,
        IProductivityService? productivityService = null,
        IClipboardHistoryService? clipboardService = null)
    {
        InitializeComponent();
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logManagerService = logManagerService ?? throw new ArgumentNullException(nameof(logManagerService));
        _pathsService = pathsService ?? (Application.Current as App)?.ServiceProvider?.GetService<IAppPathsService>();
        _updateService = updateService ?? new GitHubUpdateService(repository, _pathsService);
        _productivityService = productivityService ?? (Application.Current as App)?.ServiceProvider?.GetService<IProductivityService>();
        _clipboardService = clipboardService ?? (Application.Current as App)?.ClipboardHistoryService;
        _selectedCategory = initialCategory;

        Loaded += async (s, e) =>
        {
            SelectCategory(initialCategory);
            await LoadCurrentSettingsAsync();
            if (HourlyChartCanvas != null)
            {
                HourlyChartCanvas.SizeChanged += (sender, args) => RefreshProductivityUi();
            }
        };

        ThemeManager.ThemeChanged += (s, theme) =>
        {
            ThemeManager.ApplyWindowIcons(this);
        };

        Closed += (s, e) =>
        {
            if (!_settingsSaved)
            {
                ThemeManager.ApplyPreference(_initialTheme);
                ThemeManager.ApplyAccentColor(_initialAccent, _initialCustomHex);
            }
        };
    }

    public ApplicationSettingsWindow(
        IConfigRepository repository,
        ILogManagerService logManagerService,
        IUpdateService? updateService,
        IAppPathsService? pathsService)
        : this(repository, logManagerService, updateService, SettingsCategory.Appearance, pathsService)
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnOwnerOrActiveMonitor();
        ThemeManager.ApplyWindowIcons(this);
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                NativeMethods.BringWindowToTop(handle);
                NativeMethods.SetForegroundWindow(handle);
            }
        }
        catch { }
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
            _initialTheme = _currentSettings.Theme;
            _initialAccent = _currentSettings.AccentColor;
            _initialCustomHex = _currentSettings.CustomAccentHex;
            _currentCustomHex = _currentSettings.CustomAccentHex;
            if (!string.IsNullOrWhiteSpace(_currentCustomHex))
            {
                UpdateCustomColorDisplay(_currentCustomHex);
            }

            ThemeCombo.SelectedIndex = _currentSettings.Theme switch
            {
                ThemePreference.System => 0,
                ThemePreference.Dark => 1,
                ThemePreference.Light => 2,
                _ => 0
            };
            SelectAccentColorSwatch(_currentSettings.AccentColor);
            _isAppearanceLoaded = true;

            EnableBackdropEffectsCheck.IsChecked = _currentSettings.EnableBackdropEffects;
            EnableUiAnimationsCheck.IsChecked = _currentSettings.EnableUiAnimations;

            // Populate Startup & Minimized
            RunAtStartupCheck.IsChecked = IsRunAtStartupConfigured() || _currentSettings.RunAtStartup;
            RunAsAdminStartupCheck.IsChecked = _currentSettings.RunAsAdminAtStartup;
            RunAsAdminStartupCheck.IsEnabled = RunAtStartupCheck.IsChecked == true;
            RunAtStartupCheck.Checked += (s, e) => RunAsAdminStartupCheck.IsEnabled = true;
            RunAtStartupCheck.Unchecked += (s, e) => { RunAsAdminStartupCheck.IsEnabled = false; RunAsAdminStartupCheck.IsChecked = false; };
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

            // Populate Explorer Context Menu
            _savedExplorerMenuEnabled = ExplorerContextMenuHelper.IsRegistered();
            EnableExplorerMenuCheck.IsChecked = _savedExplorerMenuEnabled;
            UpdateExplorerMenuStatusBadge();

            // Populate Global Shortcuts
            OpenSettingsHotkeyRecorder.Binding = _currentSettings.OpenSettingsHotkey;
            CommandPaletteHotkeyRecorder.Binding = _currentSettings.CommandPaletteHotkey;
            CheatSheetHotkeyRecorder.Binding = _currentSettings.CheatSheetHotkey;
            GlobalClipboardHotkeyRecorder.Binding = _currentSettings.ClipboardHotkey;
            ShowSnippetKeywordsInCommandPaletteCheck.IsChecked = _currentSettings.ShowSnippetKeywordsInCommandPalette;
            OpenSettingsHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            CommandPaletteHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            CheatSheetHotkeyRecorder.BindingRecorded += (s, b) => CheckHotkeyConflicts();
            GlobalClipboardHotkeyRecorder.BindingRecorded += (s, b) =>
            {
                if (ClipboardHotkeyRecorder != null)
                {
                    ClipboardHotkeyRecorder.Binding = b;
                }
                CheckHotkeyConflicts();
            };
            CheckHotkeyConflicts();

            // Populate Abbreviation Expander settings
            EnableAbbreviationExpanderCheck.IsChecked = _currentSettings.EnableAbbreviationExpander;
            AbbreviationCaseSensitiveCheck.IsChecked = _currentSettings.AbbreviationCaseSensitive;
            AbbreviationSuppressInFullScreenGamesCheck.IsChecked = _currentSettings.AbbreviationSuppressInFullScreenGames;
            AbbreviationGlobalExcludedProcessesBox.Text = _currentSettings.AbbreviationGlobalExcludedProcesses != null
                ? string.Join(", ", _currentSettings.AbbreviationGlobalExcludedProcesses)
                : string.Empty;
            EnableAbbreviationSoundFeedbackCheck.IsChecked = _currentSettings.EnableAbbreviationSoundFeedback;
            AbbreviationSoundProfileCombo.SelectedIndex = _currentSettings.AbbreviationSound switch
            {
                AbbreviationSoundProfile.SoftChime => 1,
                AbbreviationSoundProfile.ModernBlip => 2,
                _ => 0
            };
            EnableAbbreviationVisualFeedbackCheck.IsChecked = _currentSettings.EnableAbbreviationVisualFeedback;

            // Populate Clipboard History settings
            EnableClipboardHistoryCheck.IsChecked = _currentSettings.EnableClipboardHistory;
            ClipboardHotkeyRecorder.Binding = _currentSettings.ClipboardHotkey;
            ClipboardHotkeyRecorder.BindingRecorded += (s, b) =>
            {
                if (GlobalClipboardHotkeyRecorder != null)
                {
                    GlobalClipboardHotkeyRecorder.Binding = b;
                }
                CheckHotkeyConflicts();
            };
            SelectComboByTag(ClipboardMaxItemsCombo, _currentSettings.ClipboardMaxItems);
            SelectComboByTag(ClipboardRetentionDaysCombo, _currentSettings.ClipboardRetentionDays);
            SelectComboByTag(ClipboardMaxItemSizeCombo, _currentSettings.ClipboardMaxItemSizeMb);
            RefreshClipboardStorageText();

            // Populate Recycle Bin retention
            bool neverDelete = _currentSettings.RecycleBinRetentionDays == 0;
            NeverDeleteRecycleCheck.IsChecked = neverDelete;
            RecycleRetentionDaysInput.IsEnabled = !neverDelete;
            RecycleRetentionDaysInput.Text = neverDelete ? "0" : _currentSettings.RecycleBinRetentionDays.ToString();
            await RefreshRecycleBinStatusAsync();

            // Populate Portable mode specifics
            if (_pathsService?.IsPortable == true)
            {
                PortableBadge.Visibility = Visibility.Visible;
                PortableStartupAdvisoryBorder.Visibility = Visibility.Visible;
                CleanupHostIntegrationOnExitCheck.Visibility = Visibility.Visible;
            }
            else
            {
                PortableBadge.Visibility = Visibility.Collapsed;
                PortableStartupAdvisoryBorder.Visibility = Visibility.Collapsed;
                CleanupHostIntegrationOnExitCheck.Visibility = Visibility.Collapsed;
            }
            CleanupHostIntegrationOnExitCheck.IsChecked = _currentSettings.CleanupHostIntegrationOnExit;

            if (_pathsService != null)
            {
                DataDirectoryPathText.Text = _pathsService.BaseDataDirectory;
                if (_pathsService.IsPortable)
                {
                    StorageModeTitleText.Text = "Storage Mode: Portable";
                    DataStorageModeBadgeText.Text = "Portable (data/)";
                }
                else
                {
                    StorageModeTitleText.Text = "Storage Mode: Standard";
                    DataStorageModeBadgeText.Text = "Installed (%APPDATA%)";
                }
            }

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
        var clipHotkey = GlobalClipboardHotkeyRecorder?.Binding ?? ClipboardHotkeyRecorder.Binding;

        var validation = HotkeyRegistryValidator.ValidateApplicationHotkeys(openHotkey, cmdHotkey, _allItems, cheatSheetHotkey, clipHotkey);
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

    private void SelectAccentColorSwatch(AccentColorChoice choice)
    {
        switch (choice)
        {
            case AccentColorChoice.ElectricViolet:
                AccentRadioElectricViolet.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Collapsed;
                break;
            case AccentColorChoice.CyberBlue:
                AccentRadioCyberBlue.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Collapsed;
                break;
            case AccentColorChoice.EmeraldGreen:
                AccentRadioEmeraldGreen.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Collapsed;
                break;
            case AccentColorChoice.SunsetOrange:
                AccentRadioSunsetOrange.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Collapsed;
                break;
            case AccentColorChoice.RoseCrimson:
                AccentRadioRoseCrimson.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Collapsed;
                break;
            case AccentColorChoice.WindowsSystem:
                AccentRadioWindowsSystem.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Collapsed;
                break;
            case AccentColorChoice.Custom:
                AccentRadioCustom.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(_currentCustomHex))
                {
                    UpdateCustomColorDisplay(_currentCustomHex);
                }
                break;
            default:
                AccentRadioIndigo.IsChecked = true;
                CustomColorPanel.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tagStr } && Enum.TryParse<AccentColorChoice>(tagStr, out var choice))
        {
            CustomColorPanel.Visibility = choice == AccentColorChoice.Custom ? Visibility.Visible : Visibility.Collapsed;
            if (choice == AccentColorChoice.Custom)
            {
                if (string.IsNullOrWhiteSpace(_currentCustomHex))
                {
                    _currentCustomHex = "#8B5CF6";
                    UpdateCustomColorDisplay(_currentCustomHex);
                }
                ThemeManager.ApplyAccentColor(AccentColorChoice.Custom, _currentCustomHex);
            }
            else
            {
                ThemeManager.ApplyAccentColor(choice);
            }
        }
    }

    private void CustomColorHexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isAppearanceLoaded) return;
        string text = CustomColorHexInput.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (text.Length == 7 && int.TryParse(text[1..], System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            _currentCustomHex = text.ToUpperInvariant();
            var col = ThemeManager.ParseHexColor(_currentCustomHex, Color.FromRgb(139, 92, 246));
            var brush = new SolidColorBrush(col);
            CustomAccentSwatchBorder.Background = brush;
            CustomColorPreviewBox.Background = brush;

            if (AccentRadioCustom.IsChecked == true)
            {
                ThemeManager.ApplyAccentColor(AccentColorChoice.Custom, _currentCustomHex);
            }
        }
    }

    private void PickCustomColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerDialog(_currentCustomHex ?? "#8B5CF6", _currentSettings.CustomPaletteColors)
        {
            Owner = this
        };

        if (picker.ShowDialog() == true)
        {
            _currentSettings.CustomPaletteColors = picker.GetCustomColors();
            SetCustomHex(picker.SelectedHex);
        }
    }

    private void CustomPresetDot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hexTag })
        {
            SetCustomHex(hexTag);
        }
    }

    private void SetCustomHex(string hex)
    {
        _currentCustomHex = hex.ToUpperInvariant();
        UpdateCustomColorDisplay(_currentCustomHex);
        if (AccentRadioCustom.IsChecked != true)
        {
            AccentRadioCustom.IsChecked = true;
            CustomColorPanel.Visibility = Visibility.Visible;
        }
        ThemeManager.ApplyAccentColor(AccentColorChoice.Custom, _currentCustomHex);
    }

    private void UpdateCustomColorDisplay(string hex)
    {
        var col = ThemeManager.ParseHexColor(hex, Color.FromRgb(139, 92, 246));
        var brush = new SolidColorBrush(col);
        CustomAccentSwatchBorder.Background = brush;
        CustomColorPreviewBox.Background = brush;
        if (CustomColorHexInput.Text != hex)
        {
            CustomColorHexInput.Text = hex;
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isAppearanceLoaded) return;
        var themePref = ThemeCombo.SelectedIndex switch
        {
            0 => ThemePreference.System,
            1 => ThemePreference.Dark,
            2 => ThemePreference.Light,
            _ => ThemePreference.System
        };
        ThemeManager.ApplyPreference(themePref);
    }

    private AccentColorChoice GetSelectedAccentChoice()
    {
        if (AccentRadioCustom.IsChecked == true) return AccentColorChoice.Custom;
        if (AccentRadioElectricViolet.IsChecked == true) return AccentColorChoice.ElectricViolet;
        if (AccentRadioCyberBlue.IsChecked == true) return AccentColorChoice.CyberBlue;
        if (AccentRadioEmeraldGreen.IsChecked == true) return AccentColorChoice.EmeraldGreen;
        if (AccentRadioSunsetOrange.IsChecked == true) return AccentColorChoice.SunsetOrange;
        if (AccentRadioRoseCrimson.IsChecked == true) return AccentColorChoice.RoseCrimson;
        if (AccentRadioWindowsSystem.IsChecked == true) return AccentColorChoice.WindowsSystem;
        return AccentColorChoice.Indigo;
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
            var clipHotkey = GlobalClipboardHotkeyRecorder?.Binding ?? ClipboardHotkeyRecorder.Binding;

            var validation = HotkeyRegistryValidator.ValidateApplicationHotkeys(openSettingsHotkey, cmdPaletteHotkey, _allItems, cheatSheetHotkey, clipHotkey);
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
            var accentChoice = GetSelectedAccentChoice();
            _currentSettings.AccentColor = accentChoice;
            _currentSettings.CustomAccentHex = _currentCustomHex;
            bool runAsAdmin = RunAsAdminStartupCheck.IsChecked == true;
            _currentSettings.RunAtStartup = runStartup;
            _currentSettings.RunAsAdminAtStartup = runAsAdmin;
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

            bool enableExplorerMenu = EnableExplorerMenuCheck.IsChecked == true;
            _currentSettings.EnableExplorerContextMenu = enableExplorerMenu;
            _currentSettings.CleanupHostIntegrationOnExit = CleanupHostIntegrationOnExitCheck.IsChecked == true;
            if (enableExplorerMenu)
            {
                ExplorerContextMenuHelper.Register(isPortable: _pathsService?.IsPortable ?? false);
            }
            else
            {
                ExplorerContextMenuHelper.Unregister();
            }
            _savedExplorerMenuEnabled = ExplorerContextMenuHelper.IsRegistered();
            UpdateExplorerMenuStatusBadge();

            _currentSettings.OpenSettingsHotkey = openSettingsHotkey;
            _currentSettings.CommandPaletteHotkey = cmdPaletteHotkey;
            _currentSettings.CheatSheetHotkey = cheatSheetHotkey;
            _currentSettings.ShowSnippetKeywordsInCommandPalette = ShowSnippetKeywordsInCommandPaletteCheck.IsChecked == true;
            _currentSettings.EnableBackdropEffects = EnableBackdropEffectsCheck.IsChecked == true;
            _currentSettings.EnableUiAnimations = EnableUiAnimationsCheck.IsChecked == true;
            _currentSettings.RecycleBinRetentionDays = recycleDays;

            // Abbreviation Expander settings
            _currentSettings.EnableAbbreviationExpander = EnableAbbreviationExpanderCheck.IsChecked == true;
            _currentSettings.AbbreviationCaseSensitive = AbbreviationCaseSensitiveCheck.IsChecked == true;
            _currentSettings.AbbreviationSuppressInFullScreenGames = AbbreviationSuppressInFullScreenGamesCheck.IsChecked == true;
            _currentSettings.AbbreviationGlobalExcludedProcesses = AbbreviationGlobalExcludedProcessesBox.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _currentSettings.EnableAbbreviationSoundFeedback = EnableAbbreviationSoundFeedbackCheck.IsChecked == true;
            _currentSettings.AbbreviationSound = AbbreviationSoundProfileCombo.SelectedIndex switch
            {
                1 => AbbreviationSoundProfile.SoftChime,
                2 => AbbreviationSoundProfile.ModernBlip,
                _ => AbbreviationSoundProfile.SubtleClick
            };
            _currentSettings.EnableAbbreviationVisualFeedback = EnableAbbreviationVisualFeedbackCheck.IsChecked == true;

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

            // Clipboard history settings
            _currentSettings.EnableClipboardHistory = EnableClipboardHistoryCheck.IsChecked == true;
            _currentSettings.ClipboardHotkey = clipHotkey;
            if (ClipboardMaxItemsCombo.SelectedItem is ComboBoxItem maxItem && int.TryParse(maxItem.Tag?.ToString(), out int maxVal))
            {
                _currentSettings.ClipboardMaxItems = maxVal;
            }
            if (ClipboardRetentionDaysCombo.SelectedItem is ComboBoxItem retItem && int.TryParse(retItem.Tag?.ToString(), out int retVal))
            {
                _currentSettings.ClipboardRetentionDays = retVal;
            }
            if (ClipboardMaxItemSizeCombo.SelectedItem is ComboBoxItem sizeItem && int.TryParse(sizeItem.Tag?.ToString(), out int sizeVal))
            {
                _currentSettings.ClipboardMaxItemSizeMb = sizeVal;
            }
            _clipboardService?.ReloadSettings();

            // 1. Persist to appsettings.json
            await _repository.SaveSettingsAsync(_currentSettings);

            // 2. Dynamically apply log level at runtime without app restart
            _logManagerService.UpdateLogLevel(level);

            // 3. Apply theme and accent preference immediately
            ThemeManager.ApplyPreference(themePref);
            ThemeManager.ApplyAccentColor(accentChoice, _currentCustomHex);
            _settingsSaved = true;

            // 4. Configure Windows Startup Registry Key / Task Scheduler
            SetRunAtStartup(runStartup, runAsAdmin, startMinimized);

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

    private void EnableExplorerMenuCheck_Click(object sender, RoutedEventArgs e)
    {
        UpdateExplorerMenuStatusBadge();
    }

    private void UpdateExplorerMenuStatusBadge()
    {
        if (ExplorerMenuStatusText == null || ExplorerMenuStatusBadge == null) return;
        bool isChecked = EnableExplorerMenuCheck.IsChecked == true;

        if (isChecked == _savedExplorerMenuEnabled)
        {
            if (_savedExplorerMenuEnabled)
            {
                ExplorerMenuStatusText.Text = "Enabled";
                ExplorerMenuStatusBadge.Background = (System.Windows.Media.Brush)FindResource("SuccessSubtleBrush");
                ExplorerMenuStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            }
            else
            {
                ExplorerMenuStatusText.Text = "Disabled";
                ExplorerMenuStatusBadge.Background = (System.Windows.Media.Brush)FindResource("ErrorSubtleBrush");
                ExplorerMenuStatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            }
        }
        else
        {
            ExplorerMenuStatusText.Text = isChecked ? "Enable after save" : "Disable after save";
            ExplorerMenuStatusBadge.Background = (System.Windows.Media.Brush)FindResource("WarningSubtleBrush");
            ExplorerMenuStatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        }
    }

    private void OpenLogsFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        _logManagerService.OpenLogDirectory();
    }

    private void OpenDataFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var path = _pathsService?.BaseDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TriggerPoint");
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to open data folder in explorer.");
        }
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
                    SetRunAtStartup(_currentSettings.RunAtStartup, _currentSettings.RunAsAdminAtStartup, _currentSettings.StartMinimized);
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
                SetRunAtStartup(_currentSettings.RunAtStartup, _currentSettings.RunAsAdminAtStartup, _currentSettings.StartMinimized);
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
        if (!_settingsSaved)
        {
            ThemeManager.ApplyPreference(_initialTheme);
            ThemeManager.ApplyAccentColor(_initialAccent, _initialCustomHex);
        }
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
            using var hkcuKey = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            var valHkcu = hkcuKey?.GetValue(AppRegistryValueName) as string;
            if (!string.IsNullOrWhiteSpace(valHkcu)) return true;

            using var hklmKey = Registry.LocalMachine.OpenSubKey(StartupRegistryKey, false);
            var valHklm = hklmKey?.GetValue(AppRegistryValueName) as string;
            return !string.IsNullOrWhiteSpace(valHklm);
        }
        catch
        {
            return false;
        }
    }

    private static void SetRunAtStartup(bool enable, bool runAsAdmin, bool startMinimized)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            string args = startMinimized ? " --minimized" : "";

            if (enable && runAsAdmin)
            {
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    ConfigureTaskSchedulerStartup(true, exePath, args);
                }

                // Delete standard current user Run key to avoid duplicate startup
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
                key?.DeleteValue(AppRegistryValueName, false);
            }
            else
            {
                ConfigureTaskSchedulerStartup(false, exePath ?? string.Empty, args);

                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
                if (key != null)
                {
                    if (enable)
                    {
                        if (!string.IsNullOrWhiteSpace(exePath))
                        {
                            key.SetValue(AppRegistryValueName, $"\"{exePath}\"{args}");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppRegistryValueName, false);
                    }
                }
            }

            // Also clean duplicate/unwanted entry in HKLM if present and writable
            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(StartupRegistryKey, true);
                hklmKey?.DeleteValue(AppRegistryValueName, false);
            }
            catch { /* Ignore HKLM write failure if non-elevated */ }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to update Windows startup configuration.");
        }
    }

    private static void ConfigureTaskSchedulerStartup(bool enable, string exePath, string args)
    {
        try
        {
            if (enable)
            {
                string cmdArgs = $"/Create /TN \"TriggerPoint\" /TR \"\\\"{exePath}\\\"{args}\" /SC ONLOGON /RL HIGHEST /F";
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = cmdArgs,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                if (!TriggerPoint.Infrastructure.Services.WindowsServiceManager.IsRunningElevated())
                {
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                }

                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/Delete /TN \"TriggerPoint\" /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to configure Task Scheduler startup for TriggerPoint.");
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

                UpdateAvailableActionCard.Visibility = Visibility.Visible;
                UpdateActionTitleText.Text = $"TriggerPoint v{_currentSettings.LastVersionFound} is available";
                CheckForUpdatesBtn.Content = "⚡ Check Again";
            }
            else
            {
                LastVersionFoundText.Text = $"v{_currentSettings.LastVersionFound} (Up to date)";
                LastVersionFoundText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                UpdateNavBadge.Visibility = Visibility.Collapsed;

                UpdateAvailableActionCard.Visibility = Visibility.Collapsed;
                CheckForUpdatesBtn.Content = "⚡ Check for Updates";
            }
        }
        else
        {
            LastVersionFoundText.Text = "None recorded yet";
            LastVersionFoundText.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            UpdateNavBadge.Visibility = Visibility.Collapsed;

            UpdateAvailableActionCard.Visibility = Visibility.Collapsed;
            CheckForUpdatesBtn.Content = "⚡ Check for Updates";
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
                App.LatestAvailableUpdate = result;
                SettingsStatusText.Text = $"Update v{result.LatestUpdate?.Version} available!";
                var updateDlg = new UpdateAvailableDialog(result, _updateService, _repository, _pathsService)
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
                App.LatestAvailableUpdate = result;
                SettingsStatusText.Text = "TriggerPoint is up to date.";
                bool viewNotes = ModernMessageDialog.ShowConfirm(
                    this,
                    "TriggerPoint is Up to Date",
                    $"You are running TriggerPoint v{_updateService.GetCurrentVersion()}.\nNo newer releases were found on GitHub.\n\nWould you like to view the latest release notes?",
                    primaryText: "📋 View Release Notes",
                    secondaryText: "Close");

                if (viewNotes && result.LatestUpdate != null)
                {
                    var viewDlg = new UpdateAvailableDialog(result, _updateService, _repository, _pathsService, isViewOnly: true)
                    {
                        Owner = this
                    };
                    viewDlg.ShowDialog();
                }
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

    private void ViewReleaseNotesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.LatestAvailableUpdate?.LatestUpdate != null)
        {
            var dlg = new UpdateAvailableDialog(App.LatestAvailableUpdate, _updateService, _repository, _pathsService, isViewOnly: true)
            {
                Owner = this
            };
            dlg.ShowDialog();
            return;
        }

        FetchAndShowReleaseNotes();
    }

    private async void FetchAndShowReleaseNotes()
    {
        UpdateCheckingPanel.Visibility = Visibility.Visible;
        UpdateCheckingStatusText.Text = "Fetching release notes from GitHub...";
        try
        {
            var result = await _updateService.CheckForUpdatesAsync(isManualCheck: true);
            App.LatestAvailableUpdate = result;
            UpdateCheckingPanel.Visibility = Visibility.Collapsed;

            if (result.LatestUpdate != null)
            {
                var dlg = new UpdateAvailableDialog(result, _updateService, _repository, _pathsService, isViewOnly: true)
                {
                    Owner = this
                };
                dlg.ShowDialog();
            }
            else
            {
                ModernMessageDialog.ShowAlert(this, "Release Notes Unavailable", "Could not retrieve release notes from GitHub. Check your internet connection.", ModernDialogType.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load release notes.");
            UpdateCheckingPanel.Visibility = Visibility.Collapsed;
            ModernMessageDialog.ShowAlert(this, "Error", $"Failed to fetch release notes: {ex.Message}", ModernDialogType.Error);
        }
    }

    private void ViewGitHubReleasesBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/Rapscallion0/TriggerPoint/releases",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to open GitHub releases URL.");
        }
    }

    private async void InstallUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.LatestAvailableUpdate?.IsUpdateAvailable == true && App.LatestAvailableUpdate.LatestUpdate != null)
        {
            var updateDlg = new UpdateAvailableDialog(App.LatestAvailableUpdate, _updateService, _repository, _pathsService)
            {
                Owner = this
            };
            updateDlg.ShowDialog();

            _currentSettings = await _repository.LoadSettingsAsync();
            PopulateUpdateSettingsUI();
            return;
        }

        // Fallback to fresh check if cached update result is not in memory
        CheckForUpdatesBtn_Click(sender, e);
    }

    private void WhatsNewBtn_Click(object sender, RoutedEventArgs e)
    {
        ViewReleaseNotesBtn_Click(sender, e);
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
                nameof(NavProductivityBtn) => SettingsCategory.Productivity,
                nameof(NavClipboardBtn) => SettingsCategory.Clipboard,
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
        NavProductivityBtn.Tag = category == SettingsCategory.Productivity ? "Selected" : null;
        NavClipboardBtn.Tag = category == SettingsCategory.Clipboard ? "Selected" : null;

        // Toggle category panels
        CategoryAppearancePanel.Visibility = category == SettingsCategory.Appearance ? Visibility.Visible : Visibility.Collapsed;
        CategoryShortcutsPanel.Visibility = category == SettingsCategory.Shortcuts ? Visibility.Visible : Visibility.Collapsed;
        CategorySystemPanel.Visibility = category == SettingsCategory.System ? Visibility.Visible : Visibility.Collapsed;
        CategoryLoggingPanel.Visibility = category == SettingsCategory.Logging ? Visibility.Visible : Visibility.Collapsed;
        CategoryUpdatesPanel.Visibility = category == SettingsCategory.Updates ? Visibility.Visible : Visibility.Collapsed;
        CategoryDataPanel.Visibility = category == SettingsCategory.Data ? Visibility.Visible : Visibility.Collapsed;
        CategoryProductivityPanel.Visibility = category == SettingsCategory.Productivity ? Visibility.Visible : Visibility.Collapsed;
        CategoryClipboardPanel.Visibility = category == SettingsCategory.Clipboard ? Visibility.Visible : Visibility.Collapsed;

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
            case SettingsCategory.Productivity:
                CategoryBreadcrumbText.Text = "Settings > Productivity & Statistics";
                CategorySubtitleText.Text = "Keystrokes saved, time reclaimed, and automation productivity insights";
                RefreshProductivityUi();
                break;
            case SettingsCategory.Clipboard:
                CategoryBreadcrumbText.Text = "Settings > Clipboard History";
                CategorySubtitleText.Text = "Smart clipboard capture, storage retention, and unformatted paste";
                RefreshClipboardStorageText();
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
        [SettingsCategory.Appearance] = ["appearance", "theme", "dark", "light", "system default", "mica", "acrylic", "material", "transparency", "translucent", "animation", "animations", "micro-transitions", "visual", "look", "accent", "color", "swatch", "indigo", "violet", "blue", "green", "orange", "crimson", "windows accent"],
        [SettingsCategory.Shortcuts] = ["shortcut", "shortcuts", "hotkey", "hotkeys", "global", "action manager", "command palette", "cheat sheet", "hud", "overlay", "conflict", "key", "recorder", "abbreviation", "abbreviations", "expander", "expansion", "text expansion", "auto-expand", "snippet expander"],
        [SettingsCategory.System] = ["system", "startup", "login", "windows", "minimized", "tray", "system tray", "hide window", "crosshair", "targeting", "toast", "toasts", "notification", "notifications", "monitor", "display", "active monitor", "primary monitor", "screen", "location", "placement", "validate", "path", "revert", "confirm", "unsaved", "explorer", "context menu", "right-click", "add to triggerpoint", "shell"],
        [SettingsCategory.Logging] = ["serilog", "log", "logs", "logging", "diagnostics", "minimum log level", "retention", "days", "split", "threshold", "mb", "folder", "open logs", "verbose", "debug", "information", "warning", "error", "fatal"],
        [SettingsCategory.Updates] = ["update", "updates", "maintenance", "check for updates", "version", "latest", "frequency", "startup", "daily", "weekly", "monthly", "manual", "silent install", "restart", "pre-release", "beta", "preview", "ignored", "ignore", "release"],
        [SettingsCategory.Data] = ["backup", "data", "management", "recycle", "recycle bin", "retention", "purge", "empty", "delete", "telemetry", "export", "import", "restore", "json", "actions", "folders"],
        [SettingsCategory.Productivity] = ["productivity", "savings", "stats", "statistics", "keystrokes", "hours", "saved", "roi", "wpm", "speed", "analytics", "dashboard", "expansions", "volume"],
        [SettingsCategory.Clipboard] = ["clipboard", "clip", "history", "paste", "copy", "retention", "images", "purge", "storage", "clips", "unformatted"]
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
        NavProductivityBtn.Tag = null;
        NavClipboardBtn.Tag = null;

        int matchCount = 0;

        bool matchApp = MatchesCategory(SettingsCategory.Appearance, query);
        bool matchShort = MatchesCategory(SettingsCategory.Shortcuts, query);
        bool matchSys = MatchesCategory(SettingsCategory.System, query);
        bool matchLog = MatchesCategory(SettingsCategory.Logging, query);
        bool matchUpd = MatchesCategory(SettingsCategory.Updates, query);
        bool matchData = MatchesCategory(SettingsCategory.Data, query);
        bool matchProd = MatchesCategory(SettingsCategory.Productivity, query);
        bool matchClip = MatchesCategory(SettingsCategory.Clipboard, query);

        CategoryAppearancePanel.Visibility = matchApp ? Visibility.Visible : Visibility.Collapsed;
        CategoryShortcutsPanel.Visibility = matchShort ? Visibility.Visible : Visibility.Collapsed;
        CategorySystemPanel.Visibility = matchSys ? Visibility.Visible : Visibility.Collapsed;
        CategoryLoggingPanel.Visibility = matchLog ? Visibility.Visible : Visibility.Collapsed;
        CategoryUpdatesPanel.Visibility = matchUpd ? Visibility.Visible : Visibility.Collapsed;
        CategoryDataPanel.Visibility = matchData ? Visibility.Visible : Visibility.Collapsed;
        CategoryProductivityPanel.Visibility = matchProd ? Visibility.Visible : Visibility.Collapsed;
        CategoryClipboardPanel.Visibility = matchClip ? Visibility.Visible : Visibility.Collapsed;

        if (matchApp) matchCount++;
        if (matchShort) matchCount++;
        if (matchSys) matchCount++;
        if (matchLog) matchCount++;
        if (matchUpd) matchCount++;
        if (matchData) matchCount++;
        if (matchProd)
        {
            matchCount++;
            RefreshProductivityUi();
        }
        if (matchClip)
        {
            matchCount++;
            RefreshClipboardStorageText();
        }

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

    #region Productivity & Savings Dashboard

    private void ProductivityHorizon_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshProductivityUi();
    }

    private void ProductivityWpmBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateConfiguredWpm();
    }

    private void ProductivityWpmBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UpdateConfiguredWpm();
        }
    }

    private void UpdateConfiguredWpm()
    {
        if (int.TryParse(ProductivityWpmBox.Text?.Trim(), out int wpm) && wpm >= 10 && wpm <= 200)
        {
            _productivityService?.SetTypingSpeedWpm(wpm);
        }
        else
        {
            ProductivityWpmBox.Text = (_productivityService?.GetTypingSpeedWpm() ?? 40).ToString();
        }
        RefreshProductivityUi();
    }

    private void RefreshProductivityUi()
    {
        if (CategoryProductivityPanel == null || CategoryProductivityPanel.Visibility != Visibility.Visible) return;

        var window = ProductivityTimeWindow.Today;
        if (Horizon7DaysRadio?.IsChecked == true) window = ProductivityTimeWindow.Last7Days;
        else if (Horizon30DaysRadio?.IsChecked == true) window = ProductivityTimeWindow.Last30Days;
        else if (Horizon1YearRadio?.IsChecked == true) window = ProductivityTimeWindow.LastYear;
        else if (HorizonAllTimeRadio?.IsChecked == true) window = ProductivityTimeWindow.AllTime;

        int wpm = _productivityService?.GetTypingSpeedWpm() ?? 40;
        if (ProductivityWpmBox != null && !ProductivityWpmBox.IsFocused)
        {
            ProductivityWpmBox.Text = wpm.ToString();
        }

        var metrics = _productivityService?.GetMetrics(window, wpm) ?? new ProductivityDashboardMetrics();

        if (MetricKeystrokesSavedText != null) MetricKeystrokesSavedText.Text = metrics.TotalKeystrokesSaved.ToString("N0");
        if (MetricHoursSavedText != null) MetricHoursSavedText.Text = $"{metrics.HoursSaved:0.#} hrs";
        if (MetricTypingWpmSubtext != null) MetricTypingWpmSubtext.Text = $"@ {wpm} WPM";
        if (MetricSnippetsExpandedText != null) MetricSnippetsExpandedText.Text = metrics.TotalSnippetsExpanded.ToString("N0");
        if (MetricHotkeysFiredText != null) MetricHotkeysFiredText.Text = metrics.TotalHotkeysTriggered.ToString("N0");

        if (TopSnippetsByCountItemsControl != null)
        {
            TopSnippetsByCountItemsControl.ItemsSource = metrics.TopSnippetsByCount;
            if (NoSnippetsByCountText != null)
            {
                NoSnippetsByCountText.Visibility = metrics.TopSnippetsByCount.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        if (TopSnippetsByVolumeItemsControl != null)
        {
            TopSnippetsByVolumeItemsControl.ItemsSource = metrics.TopSnippetsByVolume;
            if (NoSnippetsByVolumeText != null)
            {
                NoSnippetsByVolumeText.Visibility = metrics.TopSnippetsByVolume.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        DrawHourlyChart(metrics.HourlyDistribution);
    }

    private void DrawHourlyChart(int[] hourlyCounts)
    {
        if (HourlyChartCanvas == null) return;
        HourlyChartCanvas.Children.Clear();

        double canvasWidth = HourlyChartCanvas.ActualWidth > 0 ? HourlyChartCanvas.ActualWidth : 480;
        double canvasHeight = HourlyChartCanvas.ActualHeight > 0 ? HourlyChartCanvas.ActualHeight : 64;

        int max = hourlyCounts.Length > 0 ? hourlyCounts.Max() : 0;
        int peakHour = -1;
        if (max > 0)
        {
            peakHour = Array.IndexOf(hourlyCounts, max);
        }

        if (PeakHourSummaryText != null)
        {
            if (max > 0 && peakHour >= 0)
            {
                string peakHourFormatted = DateTime.Today.AddHours(peakHour).ToString("h tt");
                PeakHourSummaryText.Text = $"Peak hour: {peakHourFormatted} ({max:N0} actions)";
            }
            else
            {
                PeakHourSummaryText.Text = "No activity in this period";
            }
        }

        double barSpacing = 2.0;
        double totalSpacing = barSpacing * 23;
        double barWidth = Math.Max(2.0, (canvasWidth - totalSpacing) / 24.0);

        for (int h = 0; h < 24; h++)
        {
            int count = h < hourlyCounts.Length ? hourlyCounts[h] : 0;
            double barHeight = max > 0 ? Math.Max(3.0, (count / (double)max) * (canvasHeight - 6.0)) : 3.0;
            double x = h * (barWidth + barSpacing);
            double y = canvasHeight - barHeight;

            bool isPeak = count > 0 && count == max;
            var brush = isPeak
                ? (TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue)
                : (count > 0 
                    ? (TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray)
                    : (TryFindResource("BorderBrush") as Brush ?? Brushes.DarkGray));

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 2,
                RadiusY = 2,
                Fill = brush,
                ToolTip = $"{DateTime.Today.AddHours(h):h tt} - {count:N0} actions"
            };

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            HourlyChartCanvas.Children.Add(rect);
        }
    }

    private async void ResetProductivityStatsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ModernMessageDialog.ShowConfirm(this, "Reset Productivity Statistics",
            "Are you sure you want to reset all lifetime and time-windowed productivity statistics?\n\nThis will clear all recorded keystrokes saved, counts, and history and cannot be undone.",
            "Reset Statistics", "Cancel", isDestructive: true))
        {
            if (_productivityService != null)
            {
                await _productivityService.ResetMetricsAsync();
            }
            RefreshProductivityUi();
            SettingsStatusText.Text = "Productivity statistics reset.";
        }
    }

    private void PreviewSoundBtn_Click(object sender, RoutedEventArgs e)
    {
        var profile = AbbreviationSoundProfileCombo.SelectedIndex switch
        {
            1 => AbbreviationSoundProfile.SoftChime,
            2 => AbbreviationSoundProfile.ModernBlip,
            _ => AbbreviationSoundProfile.SubtleClick
        };
        PlayFeedbackSound(profile);
    }

    public static void PlayFeedbackSound(AbbreviationSoundProfile profile)
    {
        Task.Run(() =>
        {
            try
            {
                switch (profile)
                {
                    case AbbreviationSoundProfile.SubtleClick:
                        Console.Beep(1200, 20);
                        break;
                    case AbbreviationSoundProfile.SoftChime:
                        Console.Beep(880, 30);
                        Console.Beep(1320, 40);
                        break;
                    case AbbreviationSoundProfile.ModernBlip:
                        Console.Beep(1760, 25);
                        break;
                    case AbbreviationSoundProfile.None:
                    default:
                        break;
                }
            }
            catch
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
        });
    }
    #endregion

    #region Clipboard History Helpers

    private static void SelectComboByTag(ComboBox combo, int value)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int tag) && tag == value)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void OpenClipsFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pathsService != null && Directory.Exists(_pathsService.ClipboardClipsDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _pathsService.ClipboardClipsDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void PurgeClipboardBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboardService == null) return;
        var confirm = ModernMessageDialog.ShowConfirm(this, "Purge Clipboard History",
            "Are you sure you want to purge unpinned clipboard history?\n\nPinned clips and images will be preserved.",
            primaryText: "Purge",
            secondaryText: "Cancel",
            isDestructive: true);
        if (confirm)
        {
            _clipboardService.PurgeHistory(olderThanDays: 0, keepPinned: true);
            RefreshClipboardStorageText();
            SettingsStatusText.Text = "Clipboard history purged.";
        }
    }

    private void RefreshClipboardStorageText()
    {
        if (ClipboardStorageUsedText == null) return;
        if (_clipboardService != null)
        {
            long bytes = _clipboardService.GetTotalStorageBytes();
            ClipboardStorageUsedText.Text = FormatStorageBytes(bytes);
        }
        else
        {
            ClipboardStorageUsedText.Text = "0 KB";
        }
    }

    private static string FormatStorageBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    #endregion
}
