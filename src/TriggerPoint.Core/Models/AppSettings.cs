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

public class AppSettings
{
    public LogLevelOption LogLevel { get; set; } = LogLevelOption.Information;
    public int LogRetentionDays { get; set; } = 7;
    public bool RunAtStartup { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public void Normalize()
    {
        if (LogRetentionDays < 1) LogRetentionDays = 1;
        if (LogRetentionDays > 90) LogRetentionDays = 90;
    }
}
