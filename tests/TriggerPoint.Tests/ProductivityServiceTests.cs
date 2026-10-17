using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ProductivityServiceTests : IDisposable
{
    private readonly string _tempFilePath;

    public ProductivityServiceTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"tp_prod_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }
        catch { }
    }

    [Fact]
    public void RecordSnippetExpansion_CalculatesKeystrokesCorrectly()
    {
        using var service = new ProductivityService(_tempFilePath);
        var id = Guid.NewGuid();

        // Expanded 100 characters from a 4-character abbreviation ";sig"
        service.RecordSnippetExpansion(id, "Email Signature", expandedCharCount: 100, abbreviationLength: 4);

        var metrics = service.GetMetrics(ProductivityTimeWindow.Today);
        Assert.Equal(96, metrics.TotalKeystrokesSaved);
        Assert.Equal(1, metrics.TotalSnippetsExpanded);
        Assert.Equal(1, metrics.TotalActionsExecuted);
        Assert.Single(metrics.TopSnippetsByCount);
        Assert.Equal("Email Signature", metrics.TopSnippetsByCount[0].Name);
        Assert.Equal(1, metrics.TopSnippetsByCount[0].Count);
        Assert.Equal(96, metrics.TopSnippetsByVolume[0].KeystrokesSaved);
    }

    [Fact]
    public void RecordHotkeyTrigger_IncrementsCounters()
    {
        using var service = new ProductivityService(_tempFilePath);
        var id = Guid.NewGuid();

        service.RecordHotkeyTrigger(id, "Open Terminal");
        service.RecordHotkeyTrigger(id, "Open Terminal");

        var metrics = service.GetMetrics(ProductivityTimeWindow.Today);
        Assert.Equal(2, metrics.TotalHotkeysTriggered);
        Assert.Equal(2, metrics.TotalActionsExecuted);
        Assert.Equal(0, metrics.TotalKeystrokesSaved);
    }

    [Fact]
    public void HoursSaved_CalculatedBasedOnTypingSpeedWpm()
    {
        using var service = new ProductivityService(_tempFilePath);
        service.SetTypingSpeedWpm(40); // 40 WPM = 200 chars/min = 12,000 chars/hour

        var id = Guid.NewGuid();
        // 12,000 keystrokes saved = 1.0 hour saved
        service.RecordSnippetExpansion(id, "Big Boilerplate", expandedCharCount: 12004, abbreviationLength: 4);

        var metrics = service.GetMetrics(ProductivityTimeWindow.Today, typingSpeedWpm: 40);
        Assert.Equal(1.0, metrics.HoursSaved);

        // At 80 WPM, 12,000 keystrokes = 0.5 hours saved
        var metricsFast = service.GetMetrics(ProductivityTimeWindow.Today, typingSpeedWpm: 80);
        Assert.Equal(0.5, metricsFast.HoursSaved);
    }

    [Fact]
    public void TopSnippets_SeparatesFrequencyAndVolumeRankings()
    {
        using var service = new ProductivityService(_tempFilePath);

        var snippetA = Guid.NewGuid(); // Triggered 10 times, saves 10 keys each = 100 keys
        var snippetB = Guid.NewGuid(); // Triggered 1 time, saves 5,000 keys = 5,000 keys

        for (int i = 0; i < 10; i++)
        {
            service.RecordSnippetExpansion(snippetA, "Short Shortcut", expandedCharCount: 15, abbreviationLength: 5);
        }

        service.RecordSnippetExpansion(snippetB, "Massive Template", expandedCharCount: 5005, abbreviationLength: 5);

        var metrics = service.GetMetrics(ProductivityTimeWindow.Today);

        // Top by count should have Short Shortcut first (10x vs 1x)
        Assert.Equal("Short Shortcut", metrics.TopSnippetsByCount[0].Name);
        Assert.Equal(10, metrics.TopSnippetsByCount[0].Count);

        // Top by volume should have Massive Template first (5000 keys vs 100 keys)
        Assert.Equal("Massive Template", metrics.TopSnippetsByVolume[0].Name);
        Assert.Equal(5000, metrics.TopSnippetsByVolume[0].KeystrokesSaved);
    }

    [Fact]
    public async Task SaveAndReload_PersistsDataOnDisk()
    {
        var id = Guid.NewGuid();
        {
            using var service1 = new ProductivityService(_tempFilePath);
            service1.RecordSnippetExpansion(id, "Persistent Snippet", expandedCharCount: 50, abbreviationLength: 5);
            await service1.SaveAsync();
        }

        // Verify file exists on disk
        Assert.True(File.Exists(_tempFilePath));

        // Load in a fresh service instance
        {
            using var service2 = new ProductivityService(_tempFilePath);
            var metrics = service2.GetMetrics(ProductivityTimeWindow.Today);
            Assert.Equal(45, metrics.TotalKeystrokesSaved);
            Assert.Equal(1, metrics.TotalSnippetsExpanded);
            Assert.Equal("Persistent Snippet", metrics.TopSnippetsByCount[0].Name);
        }
    }

    [Fact]
    public async Task ResetMetrics_ClearsAllStoredData()
    {
        using var service = new ProductivityService(_tempFilePath);
        var id = Guid.NewGuid();
        service.RecordSnippetExpansion(id, "To Be Cleared", expandedCharCount: 80, abbreviationLength: 4);

        var beforeMetrics = service.GetMetrics(ProductivityTimeWindow.AllTime);
        Assert.Equal(76, beforeMetrics.TotalKeystrokesSaved);

        await service.ResetMetricsAsync();

        var afterMetrics = service.GetMetrics(ProductivityTimeWindow.AllTime);
        Assert.Equal(0, afterMetrics.TotalKeystrokesSaved);
        Assert.Equal(0, afterMetrics.TotalSnippetsExpanded);
        Assert.Empty(afterMetrics.TopSnippetsByCount);
    }

    [Fact]
    public void HourlyDistribution_RecordsCurrentHourBucket()
    {
        using var service = new ProductivityService(_tempFilePath);
        service.RecordHotkeyTrigger(Guid.NewGuid(), "Action A");
        service.RecordHotkeyTrigger(Guid.NewGuid(), "Action B");

        int currentHour = DateTime.Now.Hour;
        var metrics = service.GetMetrics(ProductivityTimeWindow.Today);

        Assert.Equal(2, metrics.HourlyDistribution[currentHour]);
    }
}
