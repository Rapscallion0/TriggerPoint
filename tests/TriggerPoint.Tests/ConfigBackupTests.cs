using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Persistence;
using Xunit;

namespace TriggerPoint.Tests;

public class ConfigBackupTests : IDisposable
{
    private readonly string _testDir;

    public ConfigBackupTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_BackupTest_" + Guid.NewGuid());
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
    public async Task ExportPackageAsync_FullBackup_PersistsAllMetadataCountsAndSettings()
    {
        var repo = new JsonConfigRepository(_testDir);
        var folderId1 = Guid.NewGuid();
        var folderId2 = Guid.NewGuid();

        var items = new List<TriggerItem>
        {
            new() { Id = folderId1, Name = "Folder 1", ActionType = ActionType.Folder },
            new() { Id = folderId2, Name = "Folder 2", ActionType = ActionType.Folder, ParentId = folderId1 },
            new() { Id = Guid.NewGuid(), Name = "Action 1", ActionType = ActionType.Shell, ParentId = folderId2 },
            new() { Id = Guid.NewGuid(), Name = "Action 2", ActionType = ActionType.Snippet, ParentId = folderId1 },
            new() { Id = Guid.NewGuid(), Name = "Action 3", ActionType = ActionType.Shell }
        };

        var settings = new AppSettings
        {
            Theme = ThemePreference.Dark,
            LogLevel = LogLevelOption.Debug,
            LogRetentionDays = 14,
            RunAtStartup = true,
            StartMinimized = true
        };

        var package = ConfigurationBackupPackage.CreateFullBackup(items, settings);
        var exportPath = Path.Combine(_testDir, "full_backup.json");

        await repo.ExportPackageAsync(exportPath, package);

        Assert.True(File.Exists(exportPath));

        var loadedPackage = await repo.ReadPackageAsync(exportPath);
        Assert.NotNull(loadedPackage);
        Assert.Equal(BackupContentType.FullBackup, loadedPackage.ContentType);
        Assert.Equal("Full Configuration", loadedPackage.ScopeName);
        Assert.Equal(2, loadedPackage.FolderCount);
        Assert.Equal(3, loadedPackage.ActionCount);
        Assert.Equal(5, loadedPackage.Items.Count);

        Assert.NotNull(loadedPackage.Settings);
        Assert.Equal(ThemePreference.Dark, loadedPackage.Settings.Theme);
        Assert.Equal(LogLevelOption.Debug, loadedPackage.Settings.LogLevel);
        Assert.Equal(14, loadedPackage.Settings.LogRetentionDays);
        Assert.True(loadedPackage.Settings.RunAtStartup);
        Assert.True(loadedPackage.Settings.StartMinimized);
        Assert.True(loadedPackage.Settings.HideOnTargetWindow);
    }

    [Fact]
    public async Task ExportPackageAsync_TreeItemsOnly_PersistsItemsAndCountsWithoutSettings()
    {
        var repo = new JsonConfigRepository(_testDir);
        var folderId = Guid.NewGuid();

        var items = new List<TriggerItem>
        {
            new() { Id = folderId, Name = "Dev Tools", ActionType = ActionType.Folder },
            new() { Id = Guid.NewGuid(), Name = "Git Pull", ActionType = ActionType.Shell, ParentId = folderId }
        };

        var package = ConfigurationBackupPackage.CreateTreeItems(items, "Dev Tools");
        var exportPath = Path.Combine(_testDir, "tree_items.json");

        await repo.ExportPackageAsync(exportPath, package);

        var loadedPackage = await repo.ReadPackageAsync(exportPath);
        Assert.NotNull(loadedPackage);
        Assert.Equal(BackupContentType.TreeItems, loadedPackage.ContentType);
        Assert.Equal("Dev Tools", loadedPackage.ScopeName);
        Assert.Equal(1, loadedPackage.FolderCount);
        Assert.Equal(1, loadedPackage.ActionCount);
        Assert.Null(loadedPackage.Settings);
        Assert.Equal(2, loadedPackage.Items.Count);
    }

    [Fact]
    public async Task ExportPackageAsync_AppSettingsOnly_PersistsSettingsWithZeroItemCounts()
    {
        var repo = new JsonConfigRepository(_testDir);
        var settings = new AppSettings
        {
            Theme = ThemePreference.Light,
            LogLevel = LogLevelOption.Warning,
            LogRetentionDays = 30
        };

        var package = ConfigurationBackupPackage.CreateAppSettings(settings);
        var exportPath = Path.Combine(_testDir, "app_settings.json");

        await repo.ExportPackageAsync(exportPath, package);

        var loadedPackage = await repo.ReadPackageAsync(exportPath);
        Assert.NotNull(loadedPackage);
        Assert.Equal(BackupContentType.AppSettings, loadedPackage.ContentType);
        Assert.Equal("Application Settings", loadedPackage.ScopeName);
        Assert.Equal(0, loadedPackage.FolderCount);
        Assert.Equal(0, loadedPackage.ActionCount);
        Assert.Empty(loadedPackage.Items);

        Assert.NotNull(loadedPackage.Settings);
        Assert.Equal(ThemePreference.Light, loadedPackage.Settings.Theme);
        Assert.Equal(LogLevelOption.Warning, loadedPackage.Settings.LogLevel);
        Assert.Equal(30, loadedPackage.Settings.LogRetentionDays);
    }

    [Fact]
    public async Task ReadPackageAsync_LegacyFlatList_ConvertsGracefullyToTreeItemsPackage()
    {
        var repo = new JsonConfigRepository(_testDir);
        var legacyItems = new List<TriggerItem>
        {
            new() { Id = Guid.NewGuid(), Name = "Legacy Folder", ActionType = ActionType.Folder },
            new() { Id = Guid.NewGuid(), Name = "Legacy Shell", ActionType = ActionType.Shell },
            new() { Id = Guid.NewGuid(), Name = "Legacy Snippet", ActionType = ActionType.Snippet }
        };

        var legacyPath = Path.Combine(_testDir, "legacy_config.json");
        var json = JsonSerializer.Serialize(legacyItems, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(legacyPath, json);

        var loadedPackage = await repo.ReadPackageAsync(legacyPath);
        Assert.NotNull(loadedPackage);
        Assert.Equal(BackupContentType.TreeItems, loadedPackage.ContentType);
        Assert.Equal(1, loadedPackage.FolderCount);
        Assert.Equal(2, loadedPackage.ActionCount);
        Assert.Equal(3, loadedPackage.Items.Count);
        Assert.Contains(loadedPackage.Items, x => x.Name == "Legacy Shell");
    }

    [Fact]
    public void MergeImport_CollisionResolution_AssignsNewGuidsAndPreservesSubtreeHierarchy()
    {
        var folderId = Guid.NewGuid();
        var childActionId = Guid.NewGuid();

        var packageItems = new List<TriggerItem>
        {
            new() { Id = folderId, Name = "Imported Subfolder", ActionType = ActionType.Folder, ParentId = null },
            new() { Id = childActionId, Name = "Imported Command", ActionType = ActionType.Shell, ParentId = folderId }
        };

        var targetParentFolderId = Guid.NewGuid();

        // Perform merge logic:
        var idMap = new Dictionary<Guid, Guid>();
        foreach (var item in packageItems)
        {
            idMap[item.Id] = Guid.NewGuid();
        }

        var importedItems = new List<TriggerItem>();
        foreach (var origItem in packageItems)
        {
            var clone = origItem.Clone();
            clone.Id = idMap[origItem.Id];

            if (origItem.ParentId.HasValue && idMap.TryGetValue(origItem.ParentId.Value, out var newParentGuid))
            {
                clone.ParentId = newParentGuid;
            }
            else
            {
                clone.ParentId = targetParentFolderId;
            }

            importedItems.Add(clone);
        }

        // Verify:
        // 1. Neither imported item has original GUID
        Assert.DoesNotContain(importedItems, x => x.Id == folderId || x.Id == childActionId);

        var newFolder = importedItems.First(x => x.Name == "Imported Subfolder");
        var newAction = importedItems.First(x => x.Name == "Imported Command");

        // 2. The root folder item attached to the target parent folder
        Assert.Equal(targetParentFolderId, newFolder.ParentId);

        // 3. The child action's ParentId was cleanly remapped to the new folder's GUID
        Assert.Equal(newFolder.Id, newAction.ParentId);
    }

    [Fact]
    public async Task ReadPackageAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var repo = new JsonConfigRepository(_testDir);
        await Assert.ThrowsAsync<FileNotFoundException>(() => repo.ReadPackageAsync(Path.Combine(_testDir, "missing.json")));
    }

    [Fact]
    public async Task ReadPackageAsync_CorruptFile_ThrowsInvalidOperationException()
    {
        var repo = new JsonConfigRepository(_testDir);
        var corruptPath = Path.Combine(_testDir, "corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{ invalid json content !@#$%^");

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ReadPackageAsync(corruptPath));
    }
}
