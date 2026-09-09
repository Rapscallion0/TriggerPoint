using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class HotkeyConflictTests
{
    [Fact]
    public void ValidateTier1Conflicts_IdentifiesExactCollisionWithFolderAndAction()
    {
        var folderId = Guid.NewGuid();
        var folder = new TriggerItem
        {
            Id = folderId,
            Name = "Development",
            ActionType = ActionType.Folder
        };

        var item1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Open Terminal",
            Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T")
        };

        var item2 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = null, // Root
            Name = "Open Text Editor",
            Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T")
        };

        var items = new List<TriggerItem> { folder, item1, item2 };
        var conflicts = HotkeyRegistryValidator.ValidateTier1Conflicts(items);

        Assert.Equal(2, conflicts.Count);
        Assert.True(conflicts.ContainsKey(item1.Id));
        Assert.True(conflicts.ContainsKey(item2.Id));

        var conflict1 = conflicts[item1.Id];
        Assert.Equal(HotkeyConflictType.Internal, conflict1.ConflictType);
        Assert.Equal("Open Text Editor", conflict1.ConflictingActionName);
        Assert.Equal("Root", conflict1.ConflictingFolderName);

        var conflict2 = conflicts[item2.Id];
        Assert.Equal(HotkeyConflictType.Internal, conflict2.ConflictType);
        Assert.Equal("Open Terminal", conflict2.ConflictingActionName);
        Assert.Equal("Development", conflict2.ConflictingFolderName);
    }

    [Fact]
    public void CheckPotentialConflict_DetectsDuplicateBeforeSaving()
    {
        var folderId = Guid.NewGuid();
        var folder = new TriggerItem { Id = folderId, Name = "Tools", ActionType = ActionType.Folder };
        var existing = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Calculator",
            Hotkey = new ShortcutBinding(ModifierKeys.Windows | ModifierKeys.Alt, 67, "C")
        };

        var candidate = new TriggerItem { Id = Guid.NewGuid(), Name = "Calendar" };
        var newBinding = new ShortcutBinding(ModifierKeys.Windows | ModifierKeys.Alt, 67, "C");

        var conflict = HotkeyRegistryValidator.CheckPotentialConflict(candidate, newBinding, [folder, existing]);

        Assert.NotNull(conflict);
        Assert.Equal(HotkeyConflictType.Internal, conflict.ConflictType);
        Assert.Equal("Calculator", conflict.ConflictingActionName);
        Assert.Equal("Tools", conflict.ConflictingFolderName);
    }
}
