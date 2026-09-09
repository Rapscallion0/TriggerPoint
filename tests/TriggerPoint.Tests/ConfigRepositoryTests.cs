using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Persistence;
using Xunit;

namespace TriggerPoint.Tests;

public class ConfigRepositoryTests : IDisposable
{
    private readonly string _testDir;

    public ConfigRepositoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_Test_" + Guid.NewGuid());
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
    public async Task LoadAsync_CreatesDefaultItemsIfNoConfigFile()
    {
        var repo = new JsonConfigRepository(_testDir);
        var items = await repo.LoadAsync();

        Assert.NotEmpty(items);
        Assert.True(File.Exists(repo.ConfigFilePath));
        Assert.Contains(items, x => x.Name == "Calculator");
    }

    [Fact]
    public async Task SaveAsync_PerformsAtomicWriteAndGeneratesBackup()
    {
        var repo = new JsonConfigRepository(_testDir);
        var items = (await repo.LoadAsync()).ToList();

        // Add a new custom item
        items.Add(new TriggerItem
        {
            Name = "Custom Test Tool",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "notepad.exe" }
        });

        await repo.SaveAsync(items);

        // Verify primary file has new item
        var reloaded = await repo.LoadAsync();
        Assert.Contains(reloaded, x => x.Name == "Custom Test Tool");

        // Verify .bak file exists
        Assert.True(File.Exists(repo.BackupFilePath));
    }

    [Fact]
    public async Task LoadAsync_RecoversFromBackupIfPrimaryIsCorrupted()
    {
        var repo = new JsonConfigRepository(_testDir);
        var originalItems = (await repo.LoadAsync()).ToList();

        // Add custom item and save to create .bak
        originalItems.Add(new TriggerItem
        {
            Name = "Critical Backup Item",
            ActionType = ActionType.Snippet,
            Payload = new ActionPayload { SnippetTemplate = "backup test" }
        });
        // Save once to update primary, then save again so it rotates into .bak
        await repo.SaveAsync(originalItems);
        await repo.SaveAsync(originalItems);
        Assert.True(File.Exists(repo.BackupFilePath));

        // Intentionally corrupt primary file
        await File.WriteAllTextAsync(repo.ConfigFilePath, "CORRUPTED_JSON_DATA_!!!");

        // Load again; should auto-recover from .bak
        var recoveredItems = await repo.LoadAsync();
        Assert.Contains(recoveredItems, x => x.Name == "Critical Backup Item");

        // Primary file should now be restored with valid JSON
        var text = await File.ReadAllTextAsync(repo.ConfigFilePath);
        Assert.DoesNotContain("CORRUPTED", text);
    }

    [Fact]
    public async Task LoadSettingsAsync_ReturnsDefaultSettingsIfFileDoesNotExist()
    {
        var repo = new JsonConfigRepository(_testDir);
        var settings = await repo.LoadSettingsAsync();

        Assert.NotNull(settings);
        Assert.Equal(LogLevelOption.Information, settings.LogLevel);
        Assert.Equal(7, settings.LogRetentionDays);
        Assert.True(File.Exists(repo.AppSettingsFilePath));
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsSettingsAndClampsValues()
    {
        var repo = new JsonConfigRepository(_testDir);
        var settings = new AppSettings
        {
            LogLevel = LogLevelOption.Debug,
            LogRetentionDays = 150, // Should clamp to 90
            Theme = ThemePreference.Dark,
            RunAtStartup = true,
            StartMinimized = true
        };

        await repo.SaveSettingsAsync(settings);

        var reloaded = await repo.LoadSettingsAsync();
        Assert.Equal(LogLevelOption.Debug, reloaded.LogLevel);
        Assert.Equal(90, reloaded.LogRetentionDays);
        Assert.Equal(ThemePreference.Dark, reloaded.Theme);
        Assert.True(reloaded.RunAtStartup);
        Assert.True(reloaded.StartMinimized);
    }
}
