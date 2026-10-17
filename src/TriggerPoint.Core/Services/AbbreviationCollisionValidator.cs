using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static class AbbreviationCollisionValidator
{
    /// <summary>
    /// Validates all enabled snippet abbreviations across the collection and populates
    /// their AbbreviationConflicts list.
    /// </summary>
    public static Dictionary<Guid, List<AbbreviationConflictDiagnostic>> ValidateAll(
        IEnumerable<TriggerItem> allItems, 
        bool defaultCaseSensitive = true)
    {
        var itemsList = allItems.ToList();
        var snippetItems = itemsList
            .Where(x => x.ActionType == ActionType.Snippet && 
                        x.IsEnabled && 
                        !string.IsNullOrWhiteSpace(x.Abbreviation))
            .ToList();

        var result = new Dictionary<Guid, List<AbbreviationConflictDiagnostic>>();

        foreach (var item in itemsList)
        {
            item.AbbreviationConflicts.Clear();
        }

        for (int i = 0; i < snippetItems.Count; i++)
        {
            var itemA = snippetItems[i];
            for (int j = i + 1; j < snippetItems.Count; j++)
            {
                var itemB = snippetItems[j];
                CheckConflictPair(itemA, itemB, defaultCaseSensitive, result);
            }
        }

        return result;
    }

    /// <summary>
    /// Validates a single candidate item against all other items (useful for live UI validation).
    /// </summary>
    public static List<AbbreviationConflictDiagnostic> ValidateSingle(
        TriggerItem candidate, 
        IEnumerable<TriggerItem> allItems, 
        bool defaultCaseSensitive = true)
    {
        var diagnostics = new List<AbbreviationConflictDiagnostic>();

        if (candidate.ActionType != ActionType.Snippet || 
            string.IsNullOrWhiteSpace(candidate.Abbreviation))
        {
            return diagnostics;
        }

        var otherSnippets = allItems
            .Where(x => x.Id != candidate.Id && 
                        x.ActionType == ActionType.Snippet && 
                        x.IsEnabled && 
                        !string.IsNullOrWhiteSpace(x.Abbreviation));

        foreach (var other in otherSnippets)
        {
            var tempDict = new Dictionary<Guid, List<AbbreviationConflictDiagnostic>>();
            CheckConflictPair(candidate, other, defaultCaseSensitive, tempDict);
            if (tempDict.TryGetValue(candidate.Id, out var candidateDiagnostics))
            {
                diagnostics.AddRange(candidateDiagnostics);
            }
        }

        return diagnostics;
    }

    private static void CheckConflictPair(
        TriggerItem a, 
        TriggerItem b, 
        bool defaultCaseSensitive,
        Dictionary<Guid, List<AbbreviationConflictDiagnostic>> diagnosticsMap)
    {
        string abbrA = (a.Abbreviation ?? string.Empty).Trim();
        string abbrB = (b.Abbreviation ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(abbrA) || string.IsNullOrEmpty(abbrB)) return;

        bool caseA = a.AbbreviationCaseSensitive ?? defaultCaseSensitive;
        bool caseB = b.AbbreviationCaseSensitive ?? defaultCaseSensitive;

        // 1. Exact collision check
        bool exactMatch;
        if (caseA && caseB)
        {
            exactMatch = string.Equals(abbrA, abbrB, StringComparison.Ordinal);
        }
        else
        {
            exactMatch = string.Equals(abbrA, abbrB, StringComparison.OrdinalIgnoreCase);
        }

        if (exactMatch)
        {
            string nameA = string.IsNullOrWhiteSpace(a.Name) ? "Untitled Snippet" : a.Name;
            string nameB = string.IsNullOrWhiteSpace(b.Name) ? "Untitled Snippet" : b.Name;

            AddDiagnostic(diagnosticsMap, a, new AbbreviationConflictDiagnostic
            {
                Type = AbbreviationConflictType.ExactDuplicate,
                ConflictingItemId = b.Id,
                ConflictingItemName = nameB,
                ConflictingAbbreviation = abbrB,
                Message = $"Exact collision: '{abbrA}' is already used by '{nameB}'."
            });

            AddDiagnostic(diagnosticsMap, b, new AbbreviationConflictDiagnostic
            {
                Type = AbbreviationConflictType.ExactDuplicate,
                ConflictingItemId = a.Id,
                ConflictingItemName = nameA,
                ConflictingAbbreviation = abbrA,
                Message = $"Exact collision: '{abbrB}' is already used by '{nameA}'."
            });
            return;
        }

        // 2. Prefix shadow check: Immediate mode triggers before a longer abbreviation can be finished
        // Does A shadow B?
        if (a.AbbreviationMode == AbbreviationTriggerMode.Immediate && abbrA.Length < abbrB.Length)
        {
            bool aIsPrefixOfB = caseA
                ? abbrB.StartsWith(abbrA, StringComparison.Ordinal)
                : abbrB.StartsWith(abbrA, StringComparison.OrdinalIgnoreCase);

            if (aIsPrefixOfB)
            {
                string nameA = string.IsNullOrWhiteSpace(a.Name) ? "Untitled Snippet" : a.Name;
                string nameB = string.IsNullOrWhiteSpace(b.Name) ? "Untitled Snippet" : b.Name;

                AddDiagnostic(diagnosticsMap, a, new AbbreviationConflictDiagnostic
                {
                    Type = AbbreviationConflictType.PrefixShadow,
                    ConflictingItemId = b.Id,
                    ConflictingItemName = nameB,
                    ConflictingAbbreviation = abbrB,
                    Message = $"Prefix Shadow: Immediate abbreviation '{abbrA}' will trigger before '{nameB}' ('{abbrB}') can be typed."
                });

                AddDiagnostic(diagnosticsMap, b, new AbbreviationConflictDiagnostic
                {
                    Type = AbbreviationConflictType.PrefixShadow,
                    ConflictingItemId = a.Id,
                    ConflictingItemName = nameA,
                    ConflictingAbbreviation = abbrA,
                    Message = $"Prefix Shadowed: Cannot be triggered because '{nameA}' ('{abbrA}') triggers immediately on prefix."
                });
            }
        }

        // Does B shadow A?
        if (b.AbbreviationMode == AbbreviationTriggerMode.Immediate && abbrB.Length < abbrA.Length)
        {
            bool bIsPrefixOfA = caseB
                ? abbrA.StartsWith(abbrB, StringComparison.Ordinal)
                : abbrA.StartsWith(abbrB, StringComparison.OrdinalIgnoreCase);

            if (bIsPrefixOfA)
            {
                string nameA = string.IsNullOrWhiteSpace(a.Name) ? "Untitled Snippet" : a.Name;
                string nameB = string.IsNullOrWhiteSpace(b.Name) ? "Untitled Snippet" : b.Name;

                AddDiagnostic(diagnosticsMap, b, new AbbreviationConflictDiagnostic
                {
                    Type = AbbreviationConflictType.PrefixShadow,
                    ConflictingItemId = a.Id,
                    ConflictingItemName = nameA,
                    ConflictingAbbreviation = abbrA,
                    Message = $"Prefix Shadow: Immediate abbreviation '{abbrB}' will trigger before '{nameA}' ('{abbrA}') can be typed."
                });

                AddDiagnostic(diagnosticsMap, a, new AbbreviationConflictDiagnostic
                {
                    Type = AbbreviationConflictType.PrefixShadow,
                    ConflictingItemId = b.Id,
                    ConflictingItemName = nameB,
                    ConflictingAbbreviation = abbrB,
                    Message = $"Prefix Shadowed: Cannot be triggered because '{nameB}' ('{abbrB}') triggers immediately on prefix."
                });
            }
        }
    }

    private static void AddDiagnostic(
        Dictionary<Guid, List<AbbreviationConflictDiagnostic>> diagnosticsMap,
        TriggerItem item,
        AbbreviationConflictDiagnostic diagnostic)
    {
        if (!diagnosticsMap.TryGetValue(item.Id, out var list))
        {
            list = [];
            diagnosticsMap[item.Id] = list;
        }
        list.Add(diagnostic);
        item.AbbreviationConflicts.Add(diagnostic);
    }
}
