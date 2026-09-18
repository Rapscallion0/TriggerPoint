using System;

namespace TriggerPoint.Core.Models;

public enum LogLevelOption
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal
}

public enum ThemePreference
{
    System,
    Dark,
    Light
}

public enum ToastMonitorPlacement
{
    PrimaryMonitor,
    ActiveMonitor
}

public enum WindowStartupPlacement
{
    RememberLast,
    PrimaryDisplay,
    CursorDisplay
}

public enum UpdateCheckFrequency
{
    OnStartup,
    Daily,
    Weekly,
    Monthly,
    ManualOnly
}

public class AppSettings
{
    public LogLevelOption LogLevel { get; set; } = LogLevelOption.Information;
    public int LogRetentionDays { get; set; } = 7;
    public bool RunAtStartup { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool HideOnTargetWindow { get; set; } = true;
    public int RecycleBinRetentionDays { get; set; } = 30;
    public ShortcutBinding? OpenSettingsHotkey { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, 84, "T");
    public ShortcutBinding? CommandPaletteHotkey { get; set; } = new(ModifierKeys.Alt, 32, "Space");
    public ShortcutBinding? CheatSheetHotkey { get; set; } = new(ModifierKeys.Control | ModifierKeys.Shift, 191, "/");
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool EnableBackdropEffects { get; set; } = true;
    public bool EnableUiAnimations { get; set; } = true;
    public bool ShowSuccessToasts { get; set; } = true;
    public ToastMonitorPlacement ToastPlacement { get; set; } = ToastMonitorPlacement.PrimaryMonitor;
    public bool ValidateShortcutsOnStartup { get; set; } = true;
    public int LogSplitThresholdMb { get; set; } = 100;
    public bool IsRecycleBinExpanded { get; set; } = false;
    public bool ShowShortcutsInTree { get; set; } = true;
    public WindowStartupPlacement WindowPlacement { get; set; } = WindowStartupPlacement.RememberLast;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public CommandPaletteSortMode CommandPaletteSortMode { get; set; } = CommandPaletteSortMode.Smart;
    public bool CompactTreeDensity { get; set; } = true;
    public bool ShowDisabledItemsInTree { get; set; } = true;
    public bool ConfirmRevertChanges { get; set; } = true;

    // Update settings
    public UpdateCheckFrequency UpdateFrequency { get; set; } = UpdateCheckFrequency.Daily;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string? LastVersionFound { get; set; }
    public string? IgnoredUpdateVersion { get; set; }
    public bool IncludePreReleases { get; set; } = false;
    public bool SilentInstallUpdates { get; set; } = true;
    public string? LastKnownAppVersion { get; set; }

    public void Normalize()
    {
        if (LogRetentionDays < 1) LogRetentionDays = 1;
        if (LogRetentionDays > 90) LogRetentionDays = 90;
        if (RecycleBinRetentionDays < 0) RecycleBinRetentionDays = 0;
        if (LogSplitThresholdMb < 10) LogSplitThresholdMb = 10;
        if (LogSplitThresholdMb > 1024) LogSplitThresholdMb = 1024;

        // Auto-migrate legacy Win+F1 shortcut (which is intercepted and consumed by Windows Help)
        if (CheatSheetHotkey != null && CheatSheetHotkey.Modifiers == ModifierKeys.Windows && CheatSheetHotkey.VirtualKey == 112)
        {
            CheatSheetHotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Shift, 191, "/");
        }
    }
}
