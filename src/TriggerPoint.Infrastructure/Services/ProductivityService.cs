using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;

namespace TriggerPoint.Infrastructure.Services;

public class ProductivityService : IProductivityService, IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<ProductivityService>();
    private readonly string _storagePath;
    private readonly object _lock = new();
    private ProductivityDataStore _store = new();
    private readonly Timer? _debounceTimer;
    private bool _hasPendingChanges;
    private bool _isDisposed;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProductivityService(IAppPathsService pathsService)
    {
        _storagePath = pathsService.ProductivityFilePath;
        LoadStore();
        _debounceTimer = new Timer(OnDebounceSave, null, Timeout.Infinite, Timeout.Infinite);
    }

    public ProductivityService(string customStoragePath)
    {
        _storagePath = customStoragePath;
        LoadStore();
        _debounceTimer = new Timer(OnDebounceSave, null, Timeout.Infinite, Timeout.Infinite);
    }

    private void LoadStore()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var json = File.ReadAllText(_storagePath);
                var loaded = JsonSerializer.Deserialize<ProductivityDataStore>(json, _jsonOptions);
                if (loaded != null)
                {
                    _store = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load productivity data from {Path}; initializing fresh store.", _storagePath);
        }

        _store = new ProductivityDataStore();
    }

    public void RecordSnippetExpansion(Guid actionId, string actionName, int expandedCharCount, int abbreviationLength)
    {
        int saved = Math.Max(0, expandedCharCount - abbreviationLength);
        lock (_lock)
        {
            var today = GetOrCreateTodayRecord();
            today.SnippetsExpanded++;
            today.KeystrokesSaved += saved;
            today.TotalActions++;

            int hour = DateTime.Now.Hour;
            today.HourlyDistribution[hour] = today.HourlyDistribution.GetValueOrDefault(hour, 0) + 1;

            if (actionId != Guid.Empty)
            {
                if (!today.ItemStats.TryGetValue(actionId, out var itemStat))
                {
                    itemStat = new ItemProductivityRecord
                    {
                        ItemId = actionId,
                        ItemName = actionName
                    };
                    today.ItemStats[actionId] = itemStat;
                }
                itemStat.ItemName = actionName;
                itemStat.LaunchCount++;
                itemStat.KeystrokesSaved += saved;
                itemStat.LastUsedUtc = DateTime.UtcNow;
            }

            ScheduleDebouncedSave();
        }
    }

    public void RecordHotkeyTrigger(Guid actionId, string actionName)
    {
        lock (_lock)
        {
            var today = GetOrCreateTodayRecord();
            today.HotkeysTriggered++;
            today.TotalActions++;

            int hour = DateTime.Now.Hour;
            today.HourlyDistribution[hour] = today.HourlyDistribution.GetValueOrDefault(hour, 0) + 1;

            if (actionId != Guid.Empty)
            {
                if (!today.ItemStats.TryGetValue(actionId, out var itemStat))
                {
                    itemStat = new ItemProductivityRecord
                    {
                        ItemId = actionId,
                        ItemName = actionName
                    };
                    today.ItemStats[actionId] = itemStat;
                }
                itemStat.ItemName = actionName;
                itemStat.LaunchCount++;
                itemStat.LastUsedUtc = DateTime.UtcNow;
            }

            ScheduleDebouncedSave();
        }
    }

    public void RecordActionExecution(Guid actionId, string actionName, ActionType actionType)
    {
        lock (_lock)
        {
            var today = GetOrCreateTodayRecord();
            today.TotalActions++;

            int hour = DateTime.Now.Hour;
            today.HourlyDistribution[hour] = today.HourlyDistribution.GetValueOrDefault(hour, 0) + 1;

            if (actionId != Guid.Empty)
            {
                if (!today.ItemStats.TryGetValue(actionId, out var itemStat))
                {
                    itemStat = new ItemProductivityRecord
                    {
                        ItemId = actionId,
                        ItemName = actionName
                    };
                    today.ItemStats[actionId] = itemStat;
                }
                itemStat.ItemName = actionName;
                itemStat.LaunchCount++;
                itemStat.LastUsedUtc = DateTime.UtcNow;
            }

            ScheduleDebouncedSave();
        }
    }

    public int GetTypingSpeedWpm()
    {
        lock (_lock)
        {
            return _store.ConfiguredTypingSpeedWpm > 0 ? _store.ConfiguredTypingSpeedWpm : 40;
        }
    }

    public void SetTypingSpeedWpm(int wpm)
    {
        lock (_lock)
        {
            _store.ConfiguredTypingSpeedWpm = Math.Clamp(wpm, 10, 200);
            ScheduleDebouncedSave();
        }
    }

    public ProductivityDashboardMetrics GetMetrics(ProductivityTimeWindow window, int? typingSpeedWpm = null)
    {
        lock (_lock)
        {
            int wpm = typingSpeedWpm ?? GetTypingSpeedWpm();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var eligibleRecords = FilterRecordsByWindow(window, today);

            var metrics = new ProductivityDashboardMetrics
            {
                Window = window,
                HourlyDistribution = new int[24]
            };

            var itemMap = new Dictionary<Guid, (string Name, long Count, long Keystrokes)>();

            foreach (var rec in eligibleRecords)
            {
                metrics.TotalKeystrokesSaved += rec.KeystrokesSaved;
                metrics.TotalSnippetsExpanded += rec.SnippetsExpanded;
                metrics.TotalHotkeysTriggered += rec.HotkeysTriggered;
                metrics.TotalActionsExecuted += rec.TotalActions;

                foreach (var (hour, count) in rec.HourlyDistribution)
                {
                    if (hour >= 0 && hour < 24)
                    {
                        metrics.HourlyDistribution[hour] += count;
                    }
                }

                foreach (var (id, itemStat) in rec.ItemStats)
                {
                    if (itemMap.TryGetValue(id, out var existing))
                    {
                        itemMap[id] = (
                            !string.IsNullOrWhiteSpace(itemStat.ItemName) ? itemStat.ItemName : existing.Name,
                            existing.Count + itemStat.LaunchCount,
                            existing.Keystrokes + itemStat.KeystrokesSaved);
                    }
                    else
                    {
                        itemMap[id] = (itemStat.ItemName, itemStat.LaunchCount, itemStat.KeystrokesSaved);
                    }
                }
            }

            // Calculations: 1 word = 5 characters on average.
            // CPM (Characters Per Minute) = WPM * 5.
            // Hours saved = Keystrokes / (CPM * 60)
            double cpm = Math.Max(10, wpm * 5.0);
            metrics.HoursSaved = Math.Round(metrics.TotalKeystrokesSaved / (cpm * 60.0), 1);

            metrics.TopSnippetsByCount = itemMap
                .Where(x => x.Value.Count > 0)
                .OrderByDescending(x => x.Value.Count)
                .Take(5)
                .Select(x => new TopSnippetStat
                {
                    ItemId = x.Key,
                    Name = x.Value.Name,
                    Count = x.Value.Count,
                    KeystrokesSaved = x.Value.Keystrokes
                })
                .ToList();

            metrics.TopSnippetsByVolume = itemMap
                .Where(x => x.Value.Keystrokes > 0)
                .OrderByDescending(x => x.Value.Keystrokes)
                .Take(5)
                .Select(x => new TopSnippetStat
                {
                    ItemId = x.Key,
                    Name = x.Value.Name,
                    Count = x.Value.Count,
                    KeystrokesSaved = x.Value.Keystrokes
                })
                .ToList();

            return metrics;
        }
    }

    private List<DailyProductivityRecord> FilterRecordsByWindow(ProductivityTimeWindow window, DateOnly today)
    {
        var result = new List<DailyProductivityRecord>();

        foreach (var (dateKey, record) in _store.DailyRecords)
        {
            if (!DateOnly.TryParse(dateKey, out var recDate))
            {
                continue;
            }

            bool include = window switch
            {
                ProductivityTimeWindow.Today => recDate == today,
                ProductivityTimeWindow.Last7Days => recDate >= today.AddDays(-6) && recDate <= today,
                ProductivityTimeWindow.Last30Days => recDate >= today.AddDays(-29) && recDate <= today,
                ProductivityTimeWindow.LastYear => recDate >= today.AddDays(-364) && recDate <= today,
                ProductivityTimeWindow.AllTime => true,
                _ => true
            };

            if (include)
            {
                result.Add(record);
            }
        }

        return result;
    }

    private DailyProductivityRecord GetOrCreateTodayRecord()
    {
        string key = DateTime.Today.ToString("yyyy-MM-dd");
        if (!_store.DailyRecords.TryGetValue(key, out var record))
        {
            record = new DailyProductivityRecord { DateKey = key };
            _store.DailyRecords[key] = record;
        }
        return record;
    }

    private void ScheduleDebouncedSave()
    {
        _hasPendingChanges = true;
        _debounceTimer?.Change(10000, Timeout.Infinite); // save within 10 seconds of changes
    }

    private void OnDebounceSave(object? state)
    {
        _ = SaveAsync();
    }

    public async Task SaveAsync()
    {
        string? jsonToSave = null;
        lock (_lock)
        {
            if (!_hasPendingChanges) return;
            try
            {
                jsonToSave = JsonSerializer.Serialize(_store, _jsonOptions);
                _hasPendingChanges = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to serialize productivity data.");
                return;
            }
        }

        if (jsonToSave != null)
        {
            try
            {
                var dir = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllTextAsync(_storagePath, jsonToSave).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to write productivity file {Path}.", _storagePath);
            }
        }
    }

    public async Task ResetMetricsAsync()
    {
        lock (_lock)
        {
            _store.DailyRecords.Clear();
            _hasPendingChanges = true;
        }
        await SaveAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _debounceTimer?.Dispose();
        // Flush remaining changes on shutdown
        if (_hasPendingChanges)
        {
            try
            {
                string json;
                lock (_lock)
                {
                    json = JsonSerializer.Serialize(_store, _jsonOptions);
                    _hasPendingChanges = false;
                }
                var dir = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_storagePath, json);
            }
            catch { }
        }
    }
}
