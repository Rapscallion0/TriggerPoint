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
}
