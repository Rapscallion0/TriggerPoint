using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Persistence;
using Xunit;

namespace TriggerPoint.Tests;

public class RecycleBinTests : IDisposable
{
    private readonly string _testDir;

    public RecycleBinTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_RecycleBinTest_" + Guid.NewGuid());
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
    public async Task MoveToRecycleBin_StoresItemWithOriginalLocation()
    {
        var repo = new JsonConfigRepository(_testDir);
        var folderId = Guid.NewGuid();
        var allItems = new List<TriggerItem>
        {
            new TriggerItem { Id = folderId, Name = "Tools", ActionType = ActionType.Folder }
        };

        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Notepad",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "notepad.exe" }
        };

        await repo.MoveToRecycleBinAsync(item, allItems);

        var bin = (await repo.LoadRecycleBinAsync()).ToList();
        Assert.Single(bin);
        Assert.Equal(item.Id, bin[0].Item.Id);
        Assert.Equal("Tools", bin[0].OriginalPath);
        Assert.Equal(folderId, bin[0].Item.ParentId);
    }

    [Fact]
    public async Task MoveToRecycleBin_BatchMovesMultipleItems()
    {
        var repo = new JsonConfigRepository(_testDir);
        var folder = new TriggerItem { Id = Guid.NewGuid(), Name = "Dev", ActionType = ActionType.Folder };
        var child1 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folder.Id, Name = "Git", ActionType = ActionType.Shell };
        var child2 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folder.Id, Name = "VSCode", ActionType = ActionType.Shell };

        var allItems = new List<TriggerItem> { folder, child1, child2 };
        await repo.MoveToRecycleBinAsync(new[] { folder, child1, child2 }, allItems);

        var bin = (await repo.LoadRecycleBinAsync()).ToList();
        Assert.Equal(3, bin.Count);
        Assert.Contains(bin, x => x.Item.Id == folder.Id && x.OriginalPath == "Root");
        Assert.Contains(bin, x => x.Item.Id == child1.Id && x.OriginalPath == "Dev");
        Assert.Contains(bin, x => x.Item.Id == child2.Id && x.OriginalPath == "Dev");
    }

    [Fact]
    public async Task RestoreFromRecycleBin_RemovesFromBinAndReturnsItem()
    {
        var repo = new JsonConfigRepository(_testDir);
        var item = new TriggerItem { Id = Guid.NewGuid(), Name = "Calc", ActionType = ActionType.Shell };
        await repo.MoveToRecycleBinAsync(item, new[] { item });

        var binBefore = await repo.LoadRecycleBinAsync();
        Assert.Single(binBefore);

        var restored = await repo.RestoreFromRecycleBinAsync(item.Id);
        Assert.NotNull(restored);
        Assert.Equal(item.Id, restored.Id);
        Assert.Equal("Calc", restored.Name);

        var binAfter = await repo.LoadRecycleBinAsync();
        Assert.Empty(binAfter);
    }

    [Fact]
    public async Task PermanentlyDeleteFromRecycleBin_RemovesOnlyTargetItem()
    {
        var repo = new JsonConfigRepository(_testDir);
        var item1 = new TriggerItem { Id = Guid.NewGuid(), Name = "Item 1", ActionType = ActionType.Shell };
        var item2 = new TriggerItem { Id = Guid.NewGuid(), Name = "Item 2", ActionType = ActionType.Shell };

        await repo.MoveToRecycleBinAsync(new[] { item1, item2 }, new[] { item1, item2 });

        await repo.PermanentlyDeleteFromRecycleBinAsync(item1.Id);

        var bin = (await repo.LoadRecycleBinAsync()).ToList();
        Assert.Single(bin);
        Assert.Equal(item2.Id, bin[0].Item.Id);
    }

    [Fact]
    public async Task EmptyRecycleBin_DeletesAllRecycledItems()
    {
        var repo = new JsonConfigRepository(_testDir);
        var items = Enumerable.Range(1, 5)
            .Select(i => new TriggerItem { Id = Guid.NewGuid(), Name = $"Item {i}", ActionType = ActionType.Shell })
            .ToList();

        await repo.MoveToRecycleBinAsync(items, items);
        Assert.Equal(5, (await repo.LoadRecycleBinAsync()).Count);

        await repo.EmptyRecycleBinAsync();
        Assert.Empty(await repo.LoadRecycleBinAsync());
    }

    [Fact]
    public async Task PurgeRecycleBin_PurgesExpiredItemsAndPreservesRecent()
    {
        var repo = new JsonConfigRepository(_testDir);
        var oldItem = new RecycleBinItem
        {
            Id = Guid.NewGuid(),
            DeletedAtUtc = DateTime.UtcNow.AddDays(-35),
            OriginalPath = "Root",
            Item = new TriggerItem { Id = Guid.NewGuid(), Name = "Old Item", ActionType = ActionType.Shell }
        };
        var recentItem = new RecycleBinItem
        {
            Id = Guid.NewGuid(),
            DeletedAtUtc = DateTime.UtcNow.AddDays(-5),
            OriginalPath = "Root",
            Item = new TriggerItem { Id = Guid.NewGuid(), Name = "Recent Item", ActionType = ActionType.Shell }
        };

        await repo.SaveRecycleBinAsync(new[] { oldItem, recentItem });

        // Purge with 30 days retention
        await repo.PurgeRecycleBinAsync(30);

        var remaining = (await repo.LoadRecycleBinAsync()).ToList();
        Assert.Single(remaining);
        Assert.Equal(recentItem.Id, remaining[0].Id);
    }

    [Fact]
    public async Task PurgeRecycleBin_NeverDeletesWhenRetentionDaysIsZero()
    {
        var repo = new JsonConfigRepository(_testDir);
        var veryOldItem = new RecycleBinItem
        {
            Id = Guid.NewGuid(),
            DeletedAtUtc = DateTime.UtcNow.AddDays(-365),
            OriginalPath = "Root",
            Item = new TriggerItem { Id = Guid.NewGuid(), Name = "Ancient Item", ActionType = ActionType.Shell }
        };

        await repo.SaveRecycleBinAsync(new[] { veryOldItem });

        // 0 days = never delete
        await repo.PurgeRecycleBinAsync(0);

        var remaining = (await repo.LoadRecycleBinAsync()).ToList();
        Assert.Single(remaining);
    }

    [Fact]
    public void ValidateApplicationHotkeys_DetectsCollisionBetweenAppShortcuts()
    {
        var hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T");

        var result = HotkeyRegistryValidator.ValidateApplicationHotkeys(hotkey, hotkey, Enumerable.Empty<TriggerItem>());
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("conflicts with", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateApplicationHotkeys_DetectsCollisionWithExistingTriggerItem()
    {
        var hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T");
        var existingItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Terminal Launcher",
            Hotkey = hotkey
        };

        var result = HotkeyRegistryValidator.ValidateApplicationHotkeys(
            hotkey, 
            new ShortcutBinding(ModifierKeys.Alt, 32, "Space"), 
            new[] { existingItem });

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Terminal Launcher", result.ErrorMessage);
    }

    [Fact]
    public void ValidateApplicationHotkeys_ReturnsValidWhenNoCollisions()
    {
        var openSettingsHotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T");
        var commandPaletteHotkey = new ShortcutBinding(ModifierKeys.Alt, 32, "Space");

        var existingItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Notepad",
            Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Shift, 78, "N")
        };

        var result = HotkeyRegistryValidator.ValidateApplicationHotkeys(
            openSettingsHotkey,
            commandPaletteHotkey,
            new[] { existingItem });

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task IsRecycleBinExpanded_DefaultsToFalse_AndPersistsRoundtrip()
    {
        var repo = new JsonConfigRepository(_testDir);
        var initialSettings = await repo.LoadSettingsAsync();
        Assert.False(initialSettings.IsRecycleBinExpanded);

        initialSettings.IsRecycleBinExpanded = true;
        await repo.SaveSettingsAsync(initialSettings);

        var loadedSettings = await repo.LoadSettingsAsync();
        Assert.True(loadedSettings.IsRecycleBinExpanded);

        loadedSettings.IsRecycleBinExpanded = false;
        await repo.SaveSettingsAsync(loadedSettings);

        var reloadedSettings = await repo.LoadSettingsAsync();
        Assert.False(reloadedSettings.IsRecycleBinExpanded);
    }

    [Fact]
    public async Task FolderExpansionState_PersistsThroughRepository()
    {
        var repo = new JsonConfigRepository(_testDir);
        var folder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "My Folder",
            ActionType = ActionType.Folder,
            IsExpanded = false
        };

        await repo.SaveAsync(new[] { folder });

        var loadedItems = (await repo.LoadAsync()).ToList();
        Assert.Single(loadedItems);
        Assert.False(loadedItems[0].IsExpanded);

        loadedItems[0].IsExpanded = true;
        await repo.SaveAsync(loadedItems);

        var reloadedItems = (await repo.LoadAsync()).ToList();
        Assert.Single(reloadedItems);
        Assert.True(reloadedItems[0].IsExpanded);
    }
}
