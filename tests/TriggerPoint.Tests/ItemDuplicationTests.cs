using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ItemDuplicationTests
{
    [Fact]
    public void GenerateDuplicateName_FirstDuplicate_AppendsCopy()
    {
        var existing = new[] { "Launch Terminal", "Open Browser" };
        var result = ItemDuplicationHelper.GenerateDuplicateName("Launch Terminal", existing);
        Assert.Equal("Launch Terminal - Copy", result);
    }

    [Fact]
    public void GenerateDuplicateName_SecondDuplicate_AppendsCopy2()
    {
        var existing = new[] { "Launch Terminal", "Launch Terminal - Copy" };
        var result = ItemDuplicationHelper.GenerateDuplicateName("Launch Terminal", existing);
        Assert.Equal("Launch Terminal - Copy (2)", result);
    }

    [Fact]
    public void GenerateDuplicateName_DuplicatingACopy_IncrementsSequence()
    {
        var existing = new[] { "Launch Terminal", "Launch Terminal - Copy" };
        var result = ItemDuplicationHelper.GenerateDuplicateName("Launch Terminal - Copy", existing);
        Assert.Equal("Launch Terminal - Copy (2)", result);
    }

    [Fact]
    public void GenerateDuplicateName_DuplicatingNumberedCopy_IncrementsSequence()
    {
        var existing = new[] { "Launch Terminal", "Launch Terminal - Copy", "Launch Terminal - Copy (2)" };
        var result = ItemDuplicationHelper.GenerateDuplicateName("Launch Terminal - Copy (2)", existing);
        Assert.Equal("Launch Terminal - Copy (3)", result);
    }

    [Fact]
    public void GenerateDuplicateName_FillsLowestAvailableNumber()
    {
        // 2 is missing between Copy and Copy (3)
        var existing = new[] { "App", "App - Copy", "App - Copy (3)" };
        var result = ItemDuplicationHelper.GenerateDuplicateName("App", existing);
        Assert.Equal("App - Copy (2)", result);
    }

    [Fact]
    public void CreateDuplicate_ClearsHotkeyAndResetsStats()
    {
        var original = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Format Code",
            ActionType = ActionType.Shell,
            OrderIndex = 2,
            Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 70, "F"),
            ConflictStatus = HotkeyConflictStatus.CreateExternal("Ctrl+Alt+F", "OtherApp"),
            UsageStats = new UsageStats { LaunchCount = 42, LastExecutedUtc = DateTime.UtcNow },
            Payload = new ActionPayload
            {
                Command = "dotnet format",
                Arguments = "--verify-no-changes",
                WorkingDirectory = "C:\\Projects",
                RunAsAdmin = true
            },
            ContextFilter = new ContextFilter
            {
                AllowedProcesses = ["code.exe"],
                ExcludedProcesses = ["devenv.exe"],
                AllowedUrls = ["*github.com*"],
                ExcludedUrls = ["*youtube.com*"]
            }
        };

        var items = new List<TriggerItem> { original };
        var duplicate = ItemDuplicationHelper.CreateDuplicate(original, items);

        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.Equal("Format Code - Copy", duplicate.Name);
        Assert.Null(duplicate.Hotkey);
        Assert.False(duplicate.ConflictStatus.HasConflict);
        Assert.Equal(0, duplicate.UsageStats.LaunchCount);

        // Verify deep copied payloads and context
        Assert.Equal("dotnet format", duplicate.Payload.Command);
        Assert.Equal("--verify-no-changes", duplicate.Payload.Arguments);
        Assert.Equal("C:\\Projects", duplicate.Payload.WorkingDirectory);
        Assert.True(duplicate.Payload.RunAsAdmin);
        Assert.Equal(["code.exe"], duplicate.ContextFilter.AllowedProcesses);
        Assert.Equal(["devenv.exe"], duplicate.ContextFilter.ExcludedProcesses);
        Assert.Equal(["*github.com*"], duplicate.ContextFilter.AllowedUrls);
        Assert.Equal(["*youtube.com*"], duplicate.ContextFilter.ExcludedUrls);
    }

    [Fact]
    public void InsertDuplicateIntoList_PlacesImmediatelyAfterOriginal_AndIncrementsSubsequentOrder()
    {
        var parentId = Guid.NewGuid();
        var item1 = new TriggerItem { Id = Guid.NewGuid(), Name = "A", ParentId = parentId, OrderIndex = 0 };
        var item2 = new TriggerItem { Id = Guid.NewGuid(), Name = "B", ParentId = parentId, OrderIndex = 1 };
        var item3 = new TriggerItem { Id = Guid.NewGuid(), Name = "C", ParentId = parentId, OrderIndex = 2 };

        var items = new List<TriggerItem> { item1, item2, item3 };

        // Duplicate item1 ("A")
        var duplicateA = ItemDuplicationHelper.CreateDuplicate(item1, items);
        ItemDuplicationHelper.InsertDuplicateIntoList(items, item1, duplicateA);

        Assert.Equal(0, item1.OrderIndex);
        Assert.Equal(1, duplicateA.OrderIndex);
        Assert.Equal(2, item2.OrderIndex);
        Assert.Equal(3, item3.OrderIndex);

        var sorted = items.OrderBy(x => x.OrderIndex).Select(x => x.Name).ToList();
        Assert.Equal(["A", "A - Copy", "B", "C"], sorted);
    }

    [Fact]
    public void DuplicateItemOrFolder_ActionItem_DuplicatesSingleItem()
    {
        var action = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Open Terminal",
            ActionType = ActionType.Shell,
            OrderIndex = 0
        };
        var items = new List<TriggerItem> { action };

        var cloned = ItemDuplicationHelper.DuplicateItemOrFolder(action, items);

        Assert.Single(cloned);
        Assert.Equal("Open Terminal - Copy", cloned[0].Name);
        Assert.NotEqual(action.Id, cloned[0].Id);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void DuplicateItemOrFolder_EmptyFolder_CreatesClonedFolder()
    {
        var folder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Tools",
            ActionType = ActionType.Folder,
            OrderIndex = 0
        };
        var items = new List<TriggerItem> { folder };

        var cloned = ItemDuplicationHelper.DuplicateItemOrFolder(folder, items);

        Assert.Single(cloned);
        Assert.Equal("Tools - Copy", cloned[0].Name);
        Assert.Equal(ActionType.Folder, cloned[0].ActionType);
        Assert.NotEqual(folder.Id, cloned[0].Id);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void DuplicateItemOrFolder_FolderWithActions_DuplicatesFolderAndAllActionsWithHotkeysCleared()
    {
        var folder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Dev Tools",
            ActionType = ActionType.Folder,
            OrderIndex = 0
        };
        var action1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "VS Code",
            ActionType = ActionType.Shell,
            ParentId = folder.Id,
            OrderIndex = 0,
            Hotkey = new ShortcutBinding(ModifierKeys.Control, 86, "V")
        };
        var action2 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Git Bash",
            ActionType = ActionType.Shell,
            ParentId = folder.Id,
            OrderIndex = 1,
            Hotkey = new ShortcutBinding(ModifierKeys.Control, 71, "G")
        };

        var items = new List<TriggerItem> { folder, action1, action2 };

        var cloned = ItemDuplicationHelper.DuplicateItemOrFolder(folder, items);

        // 1 folder + 2 actions = 3 items in cloned list
        Assert.Equal(3, cloned.Count);

        var clonedFolder = cloned[0];
        Assert.Equal("Dev Tools - Copy", clonedFolder.Name);
        Assert.NotEqual(folder.Id, clonedFolder.Id);

        var clonedAction1 = cloned.First(x => x.Name == "VS Code");
        var clonedAction2 = cloned.First(x => x.Name == "Git Bash");

        Assert.NotEqual(action1.Id, clonedAction1.Id);
        Assert.NotEqual(action2.Id, clonedAction2.Id);

        // Parents must point to the new cloned folder
        Assert.Equal(clonedFolder.Id, clonedAction1.ParentId);
        Assert.Equal(clonedFolder.Id, clonedAction2.ParentId);

        // Hotkeys must be cleared to prevent conflicts
        Assert.Null(clonedAction1.Hotkey);
        Assert.Null(clonedAction2.Hotkey);

        // Total items in collection
        Assert.Equal(6, items.Count);
    }

    [Fact]
    public void DuplicateItemOrFolder_NestedFolderStructure_RecursivelyClonesAllDescendants()
    {
        // Hierarchy:
        // Help
        //  └── V3.3
        //       ├── Usershelp.chm
        //       └── Membershelp.chm
        var rootHelp = new TriggerItem { Id = Guid.NewGuid(), Name = "Help", ActionType = ActionType.Folder, OrderIndex = 0 };
        var subV33 = new TriggerItem { Id = Guid.NewGuid(), Name = "V3.3", ActionType = ActionType.Folder, ParentId = rootHelp.Id, OrderIndex = 0 };
        var doc1 = new TriggerItem { Id = Guid.NewGuid(), Name = "Usershelp.chm", ActionType = ActionType.Shell, ParentId = subV33.Id, OrderIndex = 0 };
        var doc2 = new TriggerItem { Id = Guid.NewGuid(), Name = "Membershelp.chm", ActionType = ActionType.Shell, ParentId = subV33.Id, OrderIndex = 1 };

        var items = new List<TriggerItem> { rootHelp, subV33, doc1, doc2 };

        // Duplicate the top-level "Help" folder
        var cloned = ItemDuplicationHelper.DuplicateItemOrFolder(rootHelp, items);

        // 1 root folder + 1 subfolder + 2 shell actions = 4 cloned items
        Assert.Equal(4, cloned.Count);

        var clonedRoot = cloned[0];
        Assert.Equal("Help - Copy", clonedRoot.Name);
        Assert.Null(clonedRoot.ParentId);
        Assert.NotEqual(rootHelp.Id, clonedRoot.Id);

        var clonedSub = cloned.First(x => x.Name == "V3.3");
        Assert.Equal(clonedRoot.Id, clonedSub.ParentId);
        Assert.NotEqual(subV33.Id, clonedSub.Id);

        var clonedDoc1 = cloned.First(x => x.Name == "Usershelp.chm");
        var clonedDoc2 = cloned.First(x => x.Name == "Membershelp.chm");

        Assert.Equal(clonedSub.Id, clonedDoc1.ParentId);
        Assert.Equal(clonedSub.Id, clonedDoc2.ParentId);
        Assert.NotEqual(doc1.Id, clonedDoc1.Id);
        Assert.NotEqual(doc2.Id, clonedDoc2.Id);

        Assert.Equal(8, items.Count);
    }

    [Fact]
    public void IsExpanded_DefaultIsTrue_CanBeSetToFalse()
    {
        var item = new TriggerItem { Name = "Dev Tools", ActionType = ActionType.Folder };
        Assert.True(item.IsExpanded);

        item.IsExpanded = false;
        Assert.False(item.IsExpanded);
    }

    [Fact]
    public void IsExpanded_PreservedOnClone()
    {
        var original = new TriggerItem
        {
            Name = "Collapsed Folder",
            ActionType = ActionType.Folder,
            IsExpanded = false
        };

        var clone = original.Clone();
        Assert.False(clone.IsExpanded);
    }

    [Fact]
    public void IsExpanded_PreservedOnDuplication()
    {
        var folder = new TriggerItem
        {
            Name = "My Folder",
            ActionType = ActionType.Folder,
            IsExpanded = false
        };
        var items = new List<TriggerItem> { folder };

        var duplicates = ItemDuplicationHelper.DuplicateItemOrFolder(folder, items);
        Assert.Single(duplicates);
        Assert.False(duplicates[0].IsExpanded);
    }

    [Fact]
    public void IsExpanded_JsonSerializationPreserved()
    {
        var item = new TriggerItem
        {
            Name = "Serialized Folder",
            ActionType = ActionType.Folder,
            IsExpanded = false
        };

        string json = System.Text.Json.JsonSerializer.Serialize(item);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<TriggerItem>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.IsExpanded);
    }
}
