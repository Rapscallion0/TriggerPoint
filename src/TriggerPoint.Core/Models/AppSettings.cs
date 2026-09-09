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
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool ShowSuccessToasts { get; set; } = true;
    public ToastMonitorPlacement ToastPlacement { get; set; } = ToastMonitorPlacement.PrimaryMonitor;
    public bool ValidateShortcutsOnStartup { get; set; } = true;
    public int LogSplitThresholdMb { get; set; } = 100;

    public void Normalize()
    {
        if (LogRetentionDays < 1) LogRetentionDays = 1;
        if (LogRetentionDays > 90) LogRetentionDays = 90;
        if (RecycleBinRetentionDays < 0) RecycleBinRetentionDays = 0;
        if (LogSplitThresholdMb < 10) LogSplitThresholdMb = 10;
        if (LogSplitThresholdMb > 1024) LogSplitThresholdMb = 1024;
    }
}
