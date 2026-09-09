using System;
using System.IO;
using System.Threading.Tasks;
using Serilog.Events;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class LogManagerAndSettingsTests : IDisposable
{
    private readonly string _testDir;

    public LogManagerAndSettingsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_LogTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void ToLogEventLevel_MapsAllLevelsCorrectly()
    {
        Assert.Equal(LogEventLevel.Verbose, LogManagerService.ToLogEventLevel(LogLevelOption.Verbose));
        Assert.Equal(LogEventLevel.Debug, LogManagerService.ToLogEventLevel(LogLevelOption.Debug));
        Assert.Equal(LogEventLevel.Information, LogManagerService.ToLogEventLevel(LogLevelOption.Information));
        Assert.Equal(LogEventLevel.Warning, LogManagerService.ToLogEventLevel(LogLevelOption.Warning));
        Assert.Equal(LogEventLevel.Error, LogManagerService.ToLogEventLevel(LogLevelOption.Error));
        Assert.Equal(LogEventLevel.Fatal, LogManagerService.ToLogEventLevel(LogLevelOption.Fatal));
    }

    [Fact]
    public void InputStruct_SizeCheck()
    {
        int size = System.Runtime.InteropServices.Marshal.SizeOf<TriggerPoint.Infrastructure.Win32.NativeMethods.INPUT>();
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, size);
    }

    [Fact]
    public void UpdateLogLevel_ChangesMinimumLevelSwitchDynamically()
    {
        var levelSwitch = new Serilog.Core.LoggingLevelSwitch(LogEventLevel.Information);
        var logManager = new LogManagerService(levelSwitch, _testDir);

        Assert.Equal(LogEventLevel.Information, levelSwitch.MinimumLevel);

        logManager.UpdateLogLevel(LogLevelOption.Debug);
        Assert.Equal(LogEventLevel.Debug, levelSwitch.MinimumLevel);

        logManager.UpdateLogLevel(LogLevelOption.Warning);
        Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);
    }

    [Fact]
    public void AppSettings_NormalizeClampsRetentionDays()
    {
        var settings = new AppSettings { LogRetentionDays = 0 };
        settings.Normalize();
        Assert.Equal(1, settings.LogRetentionDays);

        settings.LogRetentionDays = 120;
        settings.Normalize();
        Assert.Equal(90, settings.LogRetentionDays);
    }

    [Fact]
    public void AppSettings_LogSplitThresholdMb_DefaultAndNormalization()
    {
        var settings = new AppSettings();
        Assert.Equal(100, settings.LogSplitThresholdMb);

        settings.LogSplitThresholdMb = 2;
        settings.Normalize();
        Assert.Equal(10, settings.LogSplitThresholdMb);

        settings.LogSplitThresholdMb = 5000;
        settings.Normalize();
        Assert.Equal(1024, settings.LogSplitThresholdMb);
    }

    [Fact]
    public async Task JsonConfigRepository_PersistsLogSplitThresholdMb()
    {
        var repo = new JsonConfigRepository(_testDir);
        var settings = new AppSettings
        {
            LogSplitThresholdMb = 250
        };

        await repo.SaveSettingsAsync(settings);
        var loaded = await repo.LoadSettingsAsync();

        Assert.Equal(250, loaded.LogSplitThresholdMb);
    }

    [Fact]
    public async Task JsonConfigRepository_PersistsToastPlacement()
    {
        var defaultSettings = new AppSettings();
        Assert.Equal(ToastMonitorPlacement.PrimaryMonitor, defaultSettings.ToastPlacement);

        var repo = new JsonConfigRepository(_testDir);
        var settings = new AppSettings
        {
            ToastPlacement = ToastMonitorPlacement.ActiveMonitor
        };

        await repo.SaveSettingsAsync(settings);
        var loaded = await repo.LoadSettingsAsync();

        Assert.Equal(ToastMonitorPlacement.ActiveMonitor, loaded.ToastPlacement);
    }
}
