using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public record ResolvedMenuQuickKey(
    TriggerItem Item,
    string? Key,
    bool IsAutoAssigned);

public static class MenuQuickKeyResolver
{
    /// <summary>
    /// Sequential quick-key sequence: 1-9, followed immediately by A-Z (no 0).
    /// </summary>
    public static readonly IReadOnlyList<string> KeySequence =
    [
        "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "A", "B", "C", "D", "E", "F", "G", "H", "I",
        "J", "K", "L", "M", "N", "O", "P", "Q", "R",
        "S", "T", "U", "V", "W", "X", "Y", "Z"
    ];

    public static List<ResolvedMenuQuickKey> ResolveKeys(
        IReadOnlyList<TriggerItem> items, 
        FolderAutoNumberMode mode)
    {
        if (items == null || items.Count == 0)
        {
            return [];
        }

        return mode switch
        {
            FolderAutoNumberMode.StrictPositional => ResolveStrictPositional(items),
            FolderAutoNumberMode.SmartFill => ResolveSmartFill(items),
            _ => ResolveManual(items)
        };
    }

    private static List<ResolvedMenuQuickKey> ResolveManual(IReadOnlyList<TriggerItem> items)
    {
        return items.Select(item =>
        {
            var key = string.IsNullOrWhiteSpace(item.AcceleratorKey)
                ? null
                : item.AcceleratorKey.Trim().ToUpperInvariant();
            return new ResolvedMenuQuickKey(item, key, IsAutoAssigned: false);
        }).ToList();
    }

    private static List<ResolvedMenuQuickKey> ResolveStrictPositional(IReadOnlyList<TriggerItem> items)
    {
        var result = new List<ResolvedMenuQuickKey>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            string? key = i < KeySequence.Count ? KeySequence[i] : null;
            result.Add(new ResolvedMenuQuickKey(items[i], key, IsAutoAssigned: !string.IsNullOrEmpty(key)));
        }
        return result;
    }

    private static List<ResolvedMenuQuickKey> ResolveSmartFill(IReadOnlyList<TriggerItem> items)
    {
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: collect user-explicit keys
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.AcceleratorKey))
            {
                usedKeys.Add(item.AcceleratorKey.Trim().ToUpperInvariant());
            }
        }

        int sequenceIdx = 0;
        var result = new List<ResolvedMenuQuickKey>(items.Count);

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.AcceleratorKey))
            {
                result.Add(new ResolvedMenuQuickKey(
                    item, 
                    item.AcceleratorKey.Trim().ToUpperInvariant(), 
                    IsAutoAssigned: false));
            }
            else
            {
                // Find next available key in sequence that hasn't been claimed
                while (sequenceIdx < KeySequence.Count && usedKeys.Contains(KeySequence[sequenceIdx]))
                {
                    sequenceIdx++;
                }

                if (sequenceIdx < KeySequence.Count)
                {
                    var assignedKey = KeySequence[sequenceIdx];
                    usedKeys.Add(assignedKey);
                    sequenceIdx++;
                    result.Add(new ResolvedMenuQuickKey(item, assignedKey, IsAutoAssigned: true));
                }
                else
                {
                    result.Add(new ResolvedMenuQuickKey(item, null, IsAutoAssigned: false));
                }
            }
        }

        return result;
    }
}
