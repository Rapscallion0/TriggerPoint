using System;
using System.IO;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class AppPathsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AppPathsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_PathsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore test cleanup errors
        }
    }

    [Fact]
    public void Default_WithoutFlagsOrSentinels_ResolvesInstalledMode()
    {
        var fakeAppData = Path.Combine(_tempDir, "AppData");
        var service = new AppPathsService(args: [], baseAppDirectory: _tempDir, customAppDataPath: fakeAppData);

        Assert.False(service.IsPortable);
        Assert.Equal(Path.Combine(fakeAppData, "TriggerPoint"), service.BaseDataDirectory);
        Assert.Equal(Path.Combine(fakeAppData, "TriggerPoint", "triggerpoint.json"), service.ConfigFilePath);
        Assert.Equal(Path.Combine(fakeAppData, "TriggerPoint", "logs"), service.LogsDirectory);
        Assert.Equal(Path.Combine(fakeAppData, "TriggerPoint", "backups"), service.BackupsDirectory);
    }

    [Fact]
    public void PortableFlag_ForcesPortableMode()
    {
        var service = new AppPathsService(args: ["--portable"], baseAppDirectory: _tempDir);

        Assert.True(service.IsPortable);
        Assert.Equal(Path.Combine(_tempDir, "data"), service.BaseDataDirectory);
        Assert.Equal(Path.Combine(_tempDir, "data", "triggerpoint.json"), service.ConfigFilePath);
        Assert.Equal(Path.Combine(_tempDir, "data", "appsettings.json"), service.AppSettingsFilePath);
        Assert.Equal(Path.Combine(_tempDir, "data", "logs"), service.LogsDirectory);
    }

    [Fact]
    public void DataDirArgument_SetsCustomDirectory()
    {
        var customTarget = Path.Combine(_tempDir, "MyCustomStorage");
        var service = new AppPathsService(args: ["--data-dir", customTarget], baseAppDirectory: _tempDir);

        Assert.True(service.IsPortable);
        Assert.Equal(Path.GetFullPath(customTarget), service.BaseDataDirectory);
        Assert.Equal(Path.Combine(customTarget, "triggerpoint.json"), service.ConfigFilePath);
    }

    [Fact]
    public void DataDirArgument_WithEqualsFormat_SetsCustomDirectory()
    {
        var customTarget = Path.Combine(_tempDir, "StorageEquals");
        var service = new AppPathsService(args: [$"--data-dir={customTarget}"], baseAppDirectory: _tempDir);

        Assert.True(service.IsPortable);
        Assert.Equal(Path.GetFullPath(customTarget), service.BaseDataDirectory);
    }

    [Fact]
    public void DataFolderSentinel_AutomaticallyEnablesPortableMode()
    {
        var dataFolder = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(dataFolder);

        var service = new AppPathsService(args: [], baseAppDirectory: _tempDir);

        Assert.True(service.IsPortable);
        Assert.Equal(dataFolder, service.BaseDataDirectory);
    }

    [Fact]
    public void PortableDatFile_AutomaticallyEnablesPortableMode()
    {
        var markerFile = Path.Combine(_tempDir, "portable.dat");
        File.WriteAllText(markerFile, "portable");

        var service = new AppPathsService(args: [], baseAppDirectory: _tempDir);

        Assert.True(service.IsPortable);
        Assert.Equal(Path.Combine(_tempDir, "data"), service.BaseDataDirectory);
    }

    [Fact]
    public void DotPortableFile_AutomaticallyEnablesPortableMode()
    {
        var markerFile = Path.Combine(_tempDir, ".portable");
        File.WriteAllText(markerFile, "");

        var service = new AppPathsService(args: [], baseAppDirectory: _tempDir);

        Assert.True(service.IsPortable);
        Assert.Equal(Path.Combine(_tempDir, "data"), service.BaseDataDirectory);
    }

    [Fact]
    public void EnsureDirectoriesCreated_CreatesRequiredSubfolders()
    {
        var service = new AppPathsService(args: ["--portable"], baseAppDirectory: _tempDir);
        service.EnsureDirectoriesCreated();

        Assert.True(Directory.Exists(service.BaseDataDirectory));
        Assert.True(Directory.Exists(service.BackupsDirectory));
        Assert.True(Directory.Exists(service.LogsDirectory));
    }

    [Fact]
    public void EnvironmentVariables_ArePopulatedInProcess()
    {
        _ = new AppPathsService(args: ["--portable"], baseAppDirectory: _tempDir);

        var appDir = Environment.GetEnvironmentVariable("TRIGGERPOINT_DIR");
        var dataDir = Environment.GetEnvironmentVariable("TRIGGERPOINT_DATA");
        var isPortable = Environment.GetEnvironmentVariable("TRIGGERPOINT_PORTABLE");

        Assert.Equal(_tempDir, appDir);
        Assert.Equal(Path.Combine(_tempDir, "data"), dataDir);
        Assert.Equal("1", isPortable);
    }
}
