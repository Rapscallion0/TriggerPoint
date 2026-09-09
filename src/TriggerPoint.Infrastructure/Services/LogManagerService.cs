using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public interface ILogManagerService
{
    LoggingLevelSwitch LevelSwitch { get; }
    string LogDirectory { get; }
    void UpdateLogLevel(LogLevelOption level);
    void OpenLogDirectory();
}

public class LogManagerService : ILogManagerService
{
    private static readonly ILogger Logger = Log.ForContext<LogManagerService>();
    public LoggingLevelSwitch LevelSwitch { get; }
    public string LogDirectory { get; }

    public LogManagerService(LoggingLevelSwitch levelSwitch, string logDirectory)
    {
        LevelSwitch = levelSwitch ?? throw new ArgumentNullException(nameof(levelSwitch));
        LogDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
    }

    public void UpdateLogLevel(LogLevelOption level)
    {
        var logEventLevel = ToLogEventLevel(level);
        LevelSwitch.MinimumLevel = logEventLevel;
        Logger.Information("Log level dynamically updated to: {Level}", level);
    }

    public void OpenLogDirectory()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open log directory: {LogDirectory}", LogDirectory);
        }
    }

    public static LogEventLevel ToLogEventLevel(LogLevelOption option) => option switch
    {
        LogLevelOption.Verbose => LogEventLevel.Verbose,
        LogLevelOption.Debug => LogEventLevel.Debug,
        LogLevelOption.Information => LogEventLevel.Information,
        LogLevelOption.Warning => LogEventLevel.Warning,
        LogLevelOption.Error => LogEventLevel.Error,
        LogLevelOption.Fatal => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };
}
