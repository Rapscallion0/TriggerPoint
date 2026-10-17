using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class AbbreviationCollisionTests
{
    [Fact]
    public void ExactDuplicates_CollidesWhenBothCaseSensitive()
    {
        var item1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Signature 1",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";sig",
            AbbreviationCaseSensitive = true
        };

        var item2 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Signature 2",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";sig",
            AbbreviationCaseSensitive = true
        };

        var allItems = new List<TriggerItem> { item1, item2 };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Equal(2, conflicts.Count);
        Assert.True(item1.HasAbbreviationConflict);
        Assert.True(item2.HasAbbreviationConflict);
        Assert.Equal(AbbreviationConflictType.ExactDuplicate, item1.AbbreviationConflicts[0].Type);
        Assert.Equal(item2.Id, item1.AbbreviationConflicts[0].ConflictingItemId);
    }

    [Fact]
    public void CaseInsensitiveCollision_DetectedWhenCasingDiffers()
    {
        var item1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Lowercase BRB",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = "brb",
            AbbreviationCaseSensitive = false
        };

        var item2 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Uppercase BRB",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = "BRB",
            AbbreviationCaseSensitive = true
        };

        var allItems = new List<TriggerItem> { item1, item2 };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Equal(2, conflicts.Count);
        Assert.Equal(AbbreviationConflictType.ExactDuplicate, item1.AbbreviationConflicts[0].Type);
        Assert.Equal(AbbreviationConflictType.ExactDuplicate, item2.AbbreviationConflicts[0].Type);
    }

    [Fact]
    public void CaseSensitiveDifferingCasing_NoCollisionWhenBothCaseSensitive()
    {
        var item1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Lowercase sig",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";sig",
            AbbreviationCaseSensitive = true
        };

        var item2 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Uppercase SIG",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";SIG",
            AbbreviationCaseSensitive = true
        };

        var allItems = new List<TriggerItem> { item1, item2 };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Empty(conflicts);
        Assert.False(item1.HasAbbreviationConflict);
        Assert.False(item2.HasAbbreviationConflict);
    }

    [Fact]
    public void PrefixShadow_ImmediatePrefixShadowsLongerSnippet()
    {
        var itemA = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Quick Date",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";d",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var itemB = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Full Date Time",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";datetime",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var allItems = new List<TriggerItem> { itemA, itemB };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Equal(2, conflicts.Count);
        Assert.Equal(AbbreviationConflictType.PrefixShadow, itemA.AbbreviationConflicts[0].Type);
        Assert.Equal(AbbreviationConflictType.PrefixShadow, itemB.AbbreviationConflicts[0].Type);
        Assert.Contains(";d", itemA.AbbreviationConflicts[0].Message);
        Assert.Contains(";datetime", itemA.AbbreviationConflicts[0].Message);
    }

    [Fact]
    public void DelimiterSnippets_DoNotShadowEachOther()
    {
        var itemA = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Short Delimited",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";d",
            AbbreviationMode = AbbreviationTriggerMode.Delimiter
        };

        var itemB = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Long Delimited",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";date",
            AbbreviationMode = AbbreviationTriggerMode.Delimiter
        };

        var allItems = new List<TriggerItem> { itemA, itemB };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Empty(conflicts);
        Assert.False(itemA.HasAbbreviationConflict);
        Assert.False(itemB.HasAbbreviationConflict);
    }

    [Fact]
    public void ImmediatePrefix_ShadowsDelimiterLongerSnippet()
    {
        var itemA = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Immediate Short",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";d",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var itemB = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Delimiter Long",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";date",
            AbbreviationMode = AbbreviationTriggerMode.Delimiter
        };

        var allItems = new List<TriggerItem> { itemA, itemB };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Equal(2, conflicts.Count);
        Assert.Equal(AbbreviationConflictType.PrefixShadow, itemA.AbbreviationConflicts[0].Type);
        Assert.Equal(AbbreviationConflictType.PrefixShadow, itemB.AbbreviationConflicts[0].Type);
    }

    [Fact]
    public void DelimiterPrefix_DoesNotShadowImmediateLongerSnippet()
    {
        var itemA = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Delimiter Short",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";d",
            AbbreviationMode = AbbreviationTriggerMode.Delimiter
        };

        var itemB = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Immediate Long",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";date",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var allItems = new List<TriggerItem> { itemA, itemB };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Empty(conflicts);
        Assert.False(itemA.HasAbbreviationConflict);
        Assert.False(itemB.HasAbbreviationConflict);
    }

    [Fact]
    public void DisabledOrNonSnippetItems_AreIgnoredInValidation()
    {
        var itemA = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Disabled Snippet",
            ActionType = ActionType.Snippet,
            IsEnabled = false,
            Abbreviation = ";test",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var itemB = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Active Snippet",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";test",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var shellItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Shell Command",
            ActionType = ActionType.Shell,
            IsEnabled = true,
            Abbreviation = ";test"
        };

        var allItems = new List<TriggerItem> { itemA, itemB, shellItem };
        var conflicts = AbbreviationCollisionValidator.ValidateAll(allItems);

        Assert.Empty(conflicts);
        Assert.False(itemB.HasAbbreviationConflict);
    }

    [Fact]
    public void ValidateSingle_ReturnsLiveDiagnosticsAgainstExistingItems()
    {
        var existing = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Existing Snippet",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";sig",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var candidate = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "New Snippet",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = ";sig",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        var allItems = new List<TriggerItem> { existing };
        var diags = AbbreviationCollisionValidator.ValidateSingle(candidate, allItems);

        Assert.Single(diags);
        Assert.Equal(AbbreviationConflictType.ExactDuplicate, diags[0].Type);
        Assert.Equal(existing.Id, diags[0].ConflictingItemId);
        Assert.Equal("Existing Snippet", diags[0].ConflictingItemName);
    }
}
