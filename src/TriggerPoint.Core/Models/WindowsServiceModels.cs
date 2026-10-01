using System;

namespace TriggerPoint.Core.Models;

public enum WindowsServiceStatus
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7
}

public enum ServiceOperation
{
    Toggle = 0,
    Start = 1,
    Stop = 2,
    Restart = 3
}

public sealed class WindowsServiceItem
{
    public string ServiceName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public WindowsServiceStatus Status { get; set; } = WindowsServiceStatus.Unknown;

    public string StatusText => Status switch
    {
        WindowsServiceStatus.Running => "Running",
        WindowsServiceStatus.Stopped => "Stopped",
        WindowsServiceStatus.Paused => "Paused",
        WindowsServiceStatus.StartPending => "Starting...",
        WindowsServiceStatus.StopPending => "Stopping...",
        WindowsServiceStatus.ContinuePending => "Continuing...",
        WindowsServiceStatus.PausePending => "Pausing...",
        _ => "Unknown"
    };

    public string StatusBadgeEmoji => Status switch
    {
        WindowsServiceStatus.Running => "🟢",
        WindowsServiceStatus.Stopped => "⚪",
        WindowsServiceStatus.Paused => "🟡",
        WindowsServiceStatus.StartPending or WindowsServiceStatus.ContinuePending => "🔄",
        WindowsServiceStatus.StopPending or WindowsServiceStatus.PausePending => "⏳",
        _ => "⚪"
    };

    public string StartType { get; init; } = "Automatic";
    public bool CanStop { get; init; } = true;
    public bool CanPauseAndContinue { get; init; } = false;
    public bool CanShutdown { get; init; } = false;
}

public record ServiceOperationResult(bool Success, string Message, WindowsServiceStatus NewStatus);
