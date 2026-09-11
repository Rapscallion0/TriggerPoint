using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class MenuQuickKeyResolverTests
{
    [Fact]
    public void KeySequence_Contains1To9AndAToZ_Excludes0()
    {
        Assert.DoesNotContain("0", MenuQuickKeyResolver.KeySequence);
        Assert.Equal(35, MenuQuickKeyResolver.KeySequence.Count); // 9 digits + 26 letters

        // Verify first 9 are 1..9
        for (int i = 1; i <= 9; i++)
        {
            Assert.Equal(i.ToString(), MenuQuickKeyResolver.KeySequence[i - 1]);
        }

        // Verify next is A, not 0
        Assert.Equal("A", MenuQuickKeyResolver.KeySequence[9]);
        Assert.Equal("Z", MenuQuickKeyResolver.KeySequence[34]);
    }

    [Fact]
    public void ResolveKeys_StrictPositional_AssignsRowIndexSequence()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "First", AcceleratorKey = "Z" }, // Has custom key Z
            new() { Name = "Second" },
            new() { Name = "Third", AcceleratorKey = "1" },  // Has custom key 1
            new() { Name = "Tenth" },
        };

        // Fill up to 10 items
        for (int i = 4; i < 10; i++)
        {
            items.Insert(i - 1, new TriggerItem { Name = $"Item {i}" });
        }

        var resolved = MenuQuickKeyResolver.ResolveKeys(items, FolderAutoNumberMode.StrictPositional);

        // Position 0 must be "1" even though item had custom key "Z"
        Assert.Equal("1", resolved[0].Key);
        Assert.True(resolved[0].IsAutoAssigned);

        Assert.Equal("2", resolved[1].Key);
        Assert.Equal("3", resolved[2].Key);

        // Position 9 (10th item) must be "A", not "0"
        Assert.Equal("A", resolved[9].Key);
        Assert.True(resolved[9].IsAutoAssigned);
    }

    [Fact]
    public void ResolveKeys_SmartFill_PreservesCustomKeysAndFillsWithoutCollisions()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "Item A", AcceleratorKey = "" }, // Should get 1
            new() { Name = "Item B", AcceleratorKey = "2" }, // Explicit 2
            new() { Name = "Item C", AcceleratorKey = "T" }, // Explicit T
            new() { Name = "Item D", AcceleratorKey = "" }, // Should get 3 (skips 2)
        };

        var resolved = MenuQuickKeyResolver.ResolveKeys(items, FolderAutoNumberMode.SmartFill);

        Assert.Equal("1", resolved[0].Key);
        Assert.True(resolved[0].IsAutoAssigned);

        Assert.Equal("2", resolved[1].Key);
        Assert.False(resolved[1].IsAutoAssigned);

        Assert.Equal("T", resolved[2].Key);
        Assert.False(resolved[2].IsAutoAssigned);

        // Skips "2" because Item B already claimed it
        Assert.Equal("3", resolved[3].Key);
        Assert.True(resolved[3].IsAutoAssigned);
    }

    [Fact]
    public void ResolveKeys_Off_ReturnsManualOnly()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "Item 1", AcceleratorKey = "" },
            new() { Name = "Item 2", AcceleratorKey = "K" }
        };

        var resolved = MenuQuickKeyResolver.ResolveKeys(items, FolderAutoNumberMode.Off);

        Assert.Null(resolved[0].Key);
        Assert.False(resolved[0].IsAutoAssigned);

        Assert.Equal("K", resolved[1].Key);
        Assert.False(resolved[1].IsAutoAssigned);
    }
}
