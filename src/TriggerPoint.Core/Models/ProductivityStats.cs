using System;
using System.Collections.Generic;
using System.Linq;

namespace TriggerPoint.Core.Models;

public enum ProductivityTimeWindow
{
    Today = 0,
    Last7Days = 1,
    Last30Days = 2,
    LastYear = 3,
    AllTime = 4
}

public sealed class ItemProductivityRecord
{
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public long LaunchCount { get; set; }
    public long KeystrokesSaved { get; set; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DailyProductivityRecord
{
    public string DateKey { get; set; } = string.Empty; // yyyy-MM-dd
    public long KeystrokesSaved { get; set; }
    public long SnippetsExpanded { get; set; }
    public long HotkeysTriggered { get; set; }
    public long TotalActions { get; set; }
    public Dictionary<int, int> HourlyDistribution { get; set; } = new();
    public Dictionary<Guid, ItemProductivityRecord> ItemStats { get; set; } = new();
}

public sealed class ProductivityDataStore
{
    public int Version { get; set; } = 1;
    public int ConfiguredTypingSpeedWpm { get; set; } = 40;
    public Dictionary<string, DailyProductivityRecord> DailyRecords { get; set; } = new();
}

public sealed class TopSnippetStat
{
    public Guid ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
    public long KeystrokesSaved { get; set; }
}

public sealed class ProductivityDashboardMetrics
{
    public ProductivityTimeWindow Window { get; set; }
    public long TotalKeystrokesSaved { get; set; }
    public double HoursSaved { get; set; }
    public long TotalSnippetsExpanded { get; set; }
    public long TotalHotkeysTriggered { get; set; }
    public long TotalActionsExecuted { get; set; }
    public List<TopSnippetStat> TopSnippetsByCount { get; set; } = new();
    public List<TopSnippetStat> TopSnippetsByVolume { get; set; } = new();
    public int[] HourlyDistribution { get; set; } = new int[24];
}
