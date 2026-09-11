using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public sealed record FuzzyMatchResult(
    TriggerItem Item,
    double Score,
    IReadOnlyList<int> MatchedIndices)
{
    public bool IsMatch => Score > 0;
}

public static class FuzzyMatcher
{
    private const double ExactMatchBonus = 1000.0;
    private const double PrefixMatchBonus = 150.0;
    private const double WordStartBonus = 50.0;
    private const double ConsecutiveMatchBonus = 30.0;
    private const double BaseMatchScore = 10.0;

    public static FuzzyMatchResult Match(TriggerItem item, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            // Empty pattern matches everything; boost by telemetry
            var telemetryScore = CalculateTelemetryBoost(item.UsageStats);
            return new FuzzyMatchResult(item, 1.0 + telemetryScore, []);
        }

        var candidate = item.Name;
        if (string.IsNullOrEmpty(candidate))
        {
            return new FuzzyMatchResult(item, 0, []);
        }

        // Exact match
        if (string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase))
        {
            var allIndices = new List<int>(candidate.Length);
            for (int i = 0; i < candidate.Length; i++) allIndices.Add(i);
            var score = ExactMatchBonus + CalculateTelemetryBoost(item.UsageStats);
            return new FuzzyMatchResult(item, score, allIndices);
        }

        // Substring match
        var subIdx = candidate.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (subIdx >= 0)
        {
            var indices = new List<int>(pattern.Length);
            for (int i = 0; i < pattern.Length; i++) indices.Add(subIdx + i);

            double score = 200.0 + (pattern.Length * 10);
            if (subIdx == 0) score += PrefixMatchBonus;
            score += CalculateTelemetryBoost(item.UsageStats);

            return new FuzzyMatchResult(item, score, indices);
        }

        // Sequential fuzzy match
        var patternChars = pattern.AsSpan();
        var candidateChars = candidate.AsSpan();

        int pIdx = 0;
        int cIdx = 0;
        double matchScore = 0;
        var matchedIndices = new List<int>(pattern.Length);
        int consecutiveCount = 0;

        while (pIdx < patternChars.Length && cIdx < candidateChars.Length)
        {
            char pChar = char.ToLowerInvariant(patternChars[pIdx]);
            char cChar = char.ToLowerInvariant(candidateChars[cIdx]);

            if (pChar == cChar)
            {
                matchedIndices.Add(cIdx);
                double charScore = BaseMatchScore;

                // Word start bonus
                if (cIdx == 0 || IsWordBoundary(candidateChars, cIdx))
                {
                    charScore += WordStartBonus;
                }

                // Consecutive match bonus
                if (consecutiveCount > 0)
                {
                    charScore += consecutiveCount * ConsecutiveMatchBonus;
                }
                consecutiveCount++;

                matchScore += charScore;
                pIdx++;
            }
            else
            {
                consecutiveCount = 0;
            }

            cIdx++;
        }

        // If not all pattern characters were matched, no match
        if (pIdx < patternChars.Length)
        {
            return new FuzzyMatchResult(item, 0, []);
        }

        // Penalize spread / distance
        int span = matchedIndices[^1] - matchedIndices[0] + 1;
        double penalty = (span - pattern.Length) * 2.0;
        matchScore = Math.Max(1.0, matchScore - penalty);

        // Telemetry boost
        matchScore += CalculateTelemetryBoost(item.UsageStats);

        return new FuzzyMatchResult(item, matchScore, matchedIndices);
    }

    public static IReadOnlyList<FuzzyMatchResult> FilterAndRank(
        IEnumerable<TriggerItem> items, 
        string pattern,
        CommandPaletteSortMode sortMode = CommandPaletteSortMode.Smart)
    {
        var results = new List<FuzzyMatchResult>();
        foreach (var item in items)
        {
            var res = Match(item, pattern);
            if (res.IsMatch)
            {
                results.Add(res);
            }
        }

        switch (sortMode)
        {
            case CommandPaletteSortMode.Alphabetical:
                results.Sort((a, b) => string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase));
                break;

            case CommandPaletteSortMode.MostFrequent:
                results.Sort((a, b) =>
                {
                    int cmp = b.Item.UsageStats.LaunchCount.CompareTo(a.Item.UsageStats.LaunchCount);
                    return cmp != 0 ? cmp : string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase);
                });
                break;

            case CommandPaletteSortMode.Recent:
                results.Sort((a, b) =>
                {
                    var aTime = a.Item.UsageStats.LastExecutedUtc ?? DateTime.MinValue;
                    var bTime = b.Item.UsageStats.LastExecutedUtc ?? DateTime.MinValue;
                    int cmp = bTime.CompareTo(aTime);
                    if (cmp != 0) return cmp;
                    int countCmp = b.Item.UsageStats.LaunchCount.CompareTo(a.Item.UsageStats.LaunchCount);
                    return countCmp != 0 ? countCmp : string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase);
                });
                break;

            case CommandPaletteSortMode.ActionTree:
                // Preserves incoming Action Tree sequence
                break;

            case CommandPaletteSortMode.Smart:
            default:
                results.Sort((a, b) =>
                {
                    int cmp = b.Score.CompareTo(a.Score);
                    return cmp != 0 ? cmp : string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase);
                });
                break;
        }

        return results;
    }

    public static (IReadOnlyList<FuzzyMatchResult> Recent, IReadOnlyList<FuzzyMatchResult> Alphabetical) PartitionEmptySearch(
        IEnumerable<TriggerItem> items,
        int maxRecent = 5)
    {
        var allList = items.Select(x => Match(x, string.Empty)).ToList();

        // Recent items: has executed or launched at least once
        var recent = allList
            .Where(x => x.Item.UsageStats.LaunchCount > 0 || x.Item.UsageStats.LastExecutedUtc.HasValue)
            .OrderByDescending(x => x.Item.UsageStats.LastExecutedUtc ?? DateTime.MinValue)
            .ThenByDescending(x => x.Item.UsageStats.LaunchCount)
            .Take(maxRecent)
            .ToList();

        // Alphabetical: full catalog sorted A-Z
        var alphabetical = allList
            .OrderBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (recent, alphabetical);
    }


    private static bool IsWordBoundary(ReadOnlySpan<char> span, int index)
    {
        if (index == 0) return true;
        char prev = span[index - 1];
        char curr = span[index];

        // Delimiters: space, dash, underscore, dot, slash
        if (prev == ' ' || prev == '-' || prev == '_' || prev == '.' || prev == '/' || prev == '\\')
            return true;

        // CamelCase transition (e.g. "TriggerPoint" -> P is word start)
        if (char.IsLower(prev) && char.IsUpper(curr))
            return true;

        return false;
    }

    public static double CalculateTelemetryBoost(UsageStats? stats)
    {
        if (stats == null || stats.LaunchCount <= 0) return 0;

        // Logarithmic launch count boost (e.g. 10 launches = ~23 pts, 100 = ~46 pts)
        double countBoost = Math.Min(100.0, Math.Log(stats.LaunchCount + 1) * 20.0);

        // Recency boost (within 24 hours = 20 pts, within 7 days = 10 pts)
        double recencyBoost = 0;
        if (stats.LastExecutedUtc.HasValue)
        {
            var age = DateTime.UtcNow - stats.LastExecutedUtc.Value;
            if (age.TotalHours < 24)
            {
                recencyBoost = 25.0 * (1.0 - (age.TotalHours / 24.0));
            }
            else if (age.TotalDays < 7)
            {
                recencyBoost = 10.0 * (1.0 - (age.TotalDays / 7.0));
            }
        }

        return countBoost + recencyBoost;
    }
}
