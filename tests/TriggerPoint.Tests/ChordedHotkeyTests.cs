using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ChordedHotkeyTests
{
    [Fact]
    public void ShortcutBinding_SingleStroke_PropertiesCorrect()
    {
        var binding = new ShortcutBinding(ModifierKeys.Control, 75, "K");

        Assert.False(binding.IsChord);
        Assert.Equal("Ctrl + K", binding.DisplayText);
        Assert.Equal("Ctrl + K", binding.PrimaryDisplayText);
        Assert.Equal(string.Empty, binding.ChordDisplayText);
        Assert.Equal(binding, binding.GetLeaderBinding());
    }

    [Fact]
    public void ShortcutBinding_Chord_PropertiesCorrect()
    {
        var chord = new ShortcutBinding(
            ModifierKeys.Control, 75, "K",
            ModifierKeys.None, 87, "W");

        Assert.True(chord.IsChord);
        Assert.Equal("Ctrl + K, W", chord.DisplayText);
        Assert.Equal("Ctrl + K", chord.PrimaryDisplayText);
        Assert.Equal("W", chord.ChordDisplayText);

        var leader = chord.GetLeaderBinding();
        Assert.False(leader.IsChord);
        Assert.Equal(ModifierKeys.Control, leader.Modifiers);
        Assert.Equal(75, leader.VirtualKey);
        Assert.Equal("K", leader.KeyName);
    }

    [Fact]
    public void ShortcutBinding_ChordWithModifiers_DisplaysAccurateSequence()
    {
        var chordWithMod = new ShortcutBinding(
            ModifierKeys.Control, 75, "K",
            ModifierKeys.Control, 87, "W");

        Assert.True(chordWithMod.IsChord);
        Assert.Equal("Ctrl + K, Ctrl + W", chordWithMod.DisplayText);
        Assert.Equal("Ctrl + W", chordWithMod.ChordDisplayText);
    }

    [Fact]
    public void ShortcutBinding_ChordEquality_DistinguishesChordsSharingLeader()
    {
        var chord1 = new ShortcutBinding(ModifierKeys.Control, 75, "K", ModifierKeys.None, 87, "W");
        var chord2 = new ShortcutBinding(ModifierKeys.Control, 75, "K", ModifierKeys.None, 84, "T");
        var chord3 = new ShortcutBinding(ModifierKeys.Control, 75, "K", ModifierKeys.None, 87, "W");

        Assert.NotEqual(chord1, chord2);
        Assert.Equal(chord1, chord3);
        Assert.Equal(chord1.GetHashCode(), chord3.GetHashCode());
    }

    [Fact]
    public void HotkeyRegistryValidator_AllowsMultipleActionsSharingLeader_WithDistinctChords()
    {
        var folderId = Guid.NewGuid();
        var folder = new TriggerItem { Id = folderId, Name = "Shortcuts", ActionType = ActionType.Folder };

        var item1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Close Window",
            Hotkey = new ShortcutBinding(ModifierKeys.Control, 75, "K", ModifierKeys.None, 87, "W")
        };

        var item2 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Close Tab",
            Hotkey = new ShortcutBinding(ModifierKeys.Control, 75, "K", ModifierKeys.None, 84, "T")
        };

        var items = new List<TriggerItem> { folder, item1, item2 };
        var conflicts = HotkeyRegistryValidator.ValidateTier1Conflicts(items);

        // Neither item should be in conflict since second stroke is different
        Assert.Empty(conflicts);
        Assert.Equal(HotkeyConflictStatus.None, item1.ConflictStatus);
        Assert.Equal(HotkeyConflictStatus.None, item2.ConflictStatus);
    }

    [Fact]
    public void HotkeyRegistryValidator_DetectsExactChordCollision()
    {
        var folderId = Guid.NewGuid();
        var folder = new TriggerItem { Id = folderId, Name = "Shortcuts", ActionType = ActionType.Folder };

        var item1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Format Code",
            Hotkey = new ShortcutBinding(ModifierKeys.Control, 75, "K", ModifierKeys.Control, 68, "D")
        };

        var item2 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Duplicate Format Code",
            Hotkey = new ShortcutBinding(ModifierKeys.Control, 75, "K", ModifierKeys.Control, 68, "D")
        };

        var items = new List<TriggerItem> { folder, item1, item2 };
        var conflicts = HotkeyRegistryValidator.ValidateTier1Conflicts(items);

        Assert.Equal(2, conflicts.Count);
        Assert.True(conflicts.ContainsKey(item1.Id));
        Assert.True(conflicts.ContainsKey(item2.Id));
    }
}
