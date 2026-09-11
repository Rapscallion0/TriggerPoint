using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void Match_ExactMatchHasHighestScore()
    {
        var itemExact = new TriggerItem { Name = "Visual Studio Code" };
        var itemPartial = new TriggerItem { Name = "Visual Studio Community" };

        var matchExact = FuzzyMatcher.Match(itemExact, "Visual Studio Code");
        var matchPartial = FuzzyMatcher.Match(itemPartial, "Visual Studio Code");

        Assert.True(matchExact.IsMatch);
        Assert.False(matchPartial.IsMatch);
        Assert.True(matchExact.Score >= 1000);
    }

    [Fact]
    public void Match_AcronymMatchYieldsWordStartBonuses()
    {
        var item = new TriggerItem { Name = "Google Chrome" };
        var match = FuzzyMatcher.Match(item, "gc");

        Assert.True(match.IsMatch);
        Assert.Equal(2, match.MatchedIndices.Count);
        // 'G' at 0, 'C' at 7
        Assert.Equal(0, match.MatchedIndices[0]);
        Assert.Equal(7, match.MatchedIndices[1]);
    }

    [Fact]
    public void Match_TelemetryBoostIncreasesRanking()
    {
        var itemUnused = new TriggerItem
        {
            Name = "Terminal",
            UsageStats = new UsageStats { LaunchCount = 0 }
        };

        var itemFrequent = new TriggerItem
        {
            Name = "Terminal",
            UsageStats = new UsageStats
            {
                LaunchCount = 50,
                LastExecutedUtc = DateTime.UtcNow.AddMinutes(-10)
            }
        };

        var matchUnused = FuzzyMatcher.Match(itemUnused, "term");
        var matchFrequent = FuzzyMatcher.Match(itemFrequent, "term");

        Assert.True(matchFrequent.Score > matchUnused.Score);
    }

    [Fact]
    public void FilterAndRank_OrdersResultsByScoreDescending()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "Sublime Text" },
            new() { Name = "Terminal" },
            new() { Name = "Notepad" }
        };

        var results = FuzzyMatcher.FilterAndRank(items, "term");

        Assert.Single(results);
        Assert.Equal("Terminal", results[0].Item.Name);
    }

    [Fact]
    public void FilterAndRank_DeterministicTieBreaker_AlphabeticalOrderOnTiedScores()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "Zebra" },
            new() { Name = "Apple" },
            new() { Name = "Mango" }
        };

        // Empty pattern gives equal base score; should tie-break A-Z
        var results = FuzzyMatcher.FilterAndRank(items, "");

        Assert.Equal(3, results.Count);
        Assert.Equal("Apple", results[0].Item.Name);
        Assert.Equal("Mango", results[1].Item.Name);
        Assert.Equal("Zebra", results[2].Item.Name);
    }

    [Fact]
    public void FilterAndRank_AlphabeticalSortMode_SortsAlphabetically()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "Docker Desktop", UsageStats = new UsageStats { LaunchCount = 100 } },
            new() { Name = "Affinity Photo", UsageStats = new UsageStats { LaunchCount = 0 } }
        };

        var results = FuzzyMatcher.FilterAndRank(items, "", CommandPaletteSortMode.Alphabetical);

        Assert.Equal("Affinity Photo", results[0].Item.Name);
        Assert.Equal("Docker Desktop", results[1].Item.Name);
    }

    [Fact]
    public void FilterAndRank_MostFrequentSortMode_OrdersByLaunchCountDescending()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "Rare Item", UsageStats = new UsageStats { LaunchCount = 2 } },
            new() { Name = "Frequent Item", UsageStats = new UsageStats { LaunchCount = 50 } },
            new() { Name = "Medium Item", UsageStats = new UsageStats { LaunchCount = 15 } }
        };

        var results = FuzzyMatcher.FilterAndRank(items, "", CommandPaletteSortMode.MostFrequent);

        Assert.Equal("Frequent Item", results[0].Item.Name);
        Assert.Equal("Medium Item", results[1].Item.Name);
        Assert.Equal("Rare Item", results[2].Item.Name);
    }

    [Fact]
    public void FilterAndRank_RecentSortMode_OrdersByLastExecutedUtcDescending()
    {
        var now = DateTime.UtcNow;
        var items = new List<TriggerItem>
        {
            new() { Name = "Old Action", UsageStats = new UsageStats { LastExecutedUtc = now.AddDays(-5) } },
            new() { Name = "Fresh Action", UsageStats = new UsageStats { LastExecutedUtc = now.AddMinutes(-5) } },
            new() { Name = "Never Run", UsageStats = new UsageStats() }
        };

        var results = FuzzyMatcher.FilterAndRank(items, "", CommandPaletteSortMode.Recent);

        Assert.Equal("Fresh Action", results[0].Item.Name);
        Assert.Equal("Old Action", results[1].Item.Name);
        Assert.Equal("Never Run", results[2].Item.Name);
    }

    [Fact]
    public void PartitionEmptySearch_SplitsRecentAndAllAlphabetical()
    {
        var now = DateTime.UtcNow;
        var items = new List<TriggerItem>
        {
            new() { Name = "Zebra", UsageStats = new UsageStats { LaunchCount = 5, LastExecutedUtc = now.AddHours(-1) } },
            new() { Name = "Apple", UsageStats = new UsageStats { LaunchCount = 0 } },
            new() { Name = "Banana", UsageStats = new UsageStats { LaunchCount = 20, LastExecutedUtc = now.AddMinutes(-10) } }
        };

        var (recent, alphabetical) = FuzzyMatcher.PartitionEmptySearch(items);

        // Recent items: Banana (10m ago), then Zebra (1h ago)
        Assert.Equal(2, recent.Count);
        Assert.Equal("Banana", recent[0].Item.Name);
        Assert.Equal("Zebra", recent[1].Item.Name);

        // Alphabetical catalog: Apple, Banana, Zebra
        Assert.Equal(3, alphabetical.Count);
        Assert.Equal("Apple", alphabetical[0].Item.Name);
        Assert.Equal("Banana", alphabetical[1].Item.Name);
        Assert.Equal("Zebra", alphabetical[2].Item.Name);
    }

    [Fact]
    public void FilterAndRank_ActionTreeSortMode_PreservesIncomingOrder()
    {
        var items = new List<TriggerItem>
        {
            new() { Name = "Zulu Action" },
            new() { Name = "Alpha Action" },
            new() { Name = "Bravo Action" }
        };

        var results = FuzzyMatcher.FilterAndRank(items, "", CommandPaletteSortMode.ActionTree);

        Assert.Equal(3, results.Count);
        Assert.Equal("Zulu Action", results[0].Item.Name);
        Assert.Equal("Alpha Action", results[1].Item.Name);
        Assert.Equal("Bravo Action", results[2].Item.Name);
    }
}

