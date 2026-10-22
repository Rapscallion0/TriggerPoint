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

public sealed record ClipboardMatchResult(
    ClipboardHistoryItem Item,
    double Score,
    IReadOnlyList<int> MatchedIndices,
    bool IsMatchInTitle,
    string? ContextExcerpt = null,
    IReadOnlyList<int>? ContextExcerptMatchedIndices = null)
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

    public static (double Score, IReadOnlyList<int> MatchedIndices)? MatchText(string candidate, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return (1.0, Array.Empty<int>());
        }

        if (string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        // Exact match
        if (string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase))
        {
            var allIndices = new List<int>(candidate.Length);
            for (int i = 0; i < candidate.Length; i++) allIndices.Add(i);
            return (ExactMatchBonus, allIndices);
        }

        // Substring match
        var subIdx = candidate.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (subIdx >= 0)
        {
            var indices = new List<int>(pattern.Length);
            for (int i = 0; i < pattern.Length; i++) indices.Add(subIdx + i);

            double score = 200.0 + (pattern.Length * 10);
            if (subIdx == 0) score += PrefixMatchBonus;

            return (score, indices);
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
            return null;
        }

        // Penalize spread / distance
        int span = matchedIndices[^1] - matchedIndices[0] + 1;
        double penalty = (span - pattern.Length) * 2.5;
        matchScore -= penalty;
        if (matchScore <= 0 || span > Math.Max(pattern.Length * 3, 24))
        {
            return null;
        }

        return (matchScore, matchedIndices);
    }

    public static ClipboardMatchResult? MatchClipboard(ClipboardHistoryItem clip, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new ClipboardMatchResult(clip, 1.0, Array.Empty<int>(), true);
        }

        var query = SearchQueryParser.Parse(pattern);

        // Check exclusions across all text properties of the item first
        if (query.ExcludedTokens.Count > 0)
        {
            foreach (var excluded in query.ExcludedTokens)
            {
                if (string.IsNullOrWhiteSpace(excluded.Text)) continue;
                if ((!string.IsNullOrEmpty(clip.DisplayTitle) && clip.DisplayTitle.IndexOf(excluded.Text, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(clip.PlainText) && clip.PlainText.IndexOf(excluded.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return null; // Excluded term matched anywhere on item
                }
            }
        }

        // 1. Try Title first
        var titleMatch = query.Match(clip.DisplayTitle);
        if (titleMatch != null)
        {
            return new ClipboardMatchResult(clip, titleMatch.Score + 500.0, titleMatch.MatchedIndices, true);
        }

        // 2. Try PlainText body
        if (!string.IsNullOrWhiteSpace(clip.PlainText))
        {
            var textMatch = query.Match(clip.PlainText);
            if (textMatch != null)
            {
                var (excerpt, excerptIndices) = SearchQueryParser.ExtractContextExcerpt(
                    clip.PlainText,
                    textMatch.MatchedIndices,
                    maxCharsBefore: 30,
                    maxCharsAfter: 45);

                return new ClipboardMatchResult(
                    clip,
                    textMatch.Score,
                    textMatch.MatchedIndices,
                    false,
                    excerpt,
                    excerptIndices);
            }
        }

        return null;
    }

    public static FuzzyMatchResult Match(TriggerItem item, string pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern) &&
            (pattern.Contains(' ') || pattern.Contains('|') || pattern.Contains('"') || pattern.Contains('-') || pattern.Contains("NOT", StringComparison.OrdinalIgnoreCase)))
        {
            var query = SearchQueryParser.Parse(pattern);
            if (query.ExcludedTokens.Count > 0)
            {
                foreach (var excluded in query.ExcludedTokens)
                {
                    if (string.IsNullOrWhiteSpace(excluded.Text)) continue;
                    if ((!string.IsNullOrEmpty(item.Name) && item.Name.IndexOf(excluded.Text, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(item.Description) && item.Description.IndexOf(excluded.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return new FuzzyMatchResult(item, 0, []);
                    }
                }
            }
        }

        var nameMatch = MatchText(item.Name, pattern);
        var abbrMatch = (!string.IsNullOrWhiteSpace(pattern) && !string.IsNullOrWhiteSpace(item.Abbreviation))
            ? MatchText(item.Abbreviation, pattern)
            : null;

        if (nameMatch == null && !string.IsNullOrWhiteSpace(pattern) &&
            (pattern.Contains(' ') || pattern.Contains('|') || pattern.Contains('"') || pattern.Contains('-') || pattern.Contains("NOT", StringComparison.OrdinalIgnoreCase)))
        {
            var query = SearchQueryParser.Parse(pattern);
            var qMatch = query.Match(item.Name);
            if (qMatch != null)
            {
                nameMatch = (qMatch.Score, qMatch.MatchedIndices);
            }
        }

        if (nameMatch == null && abbrMatch == null)
        {
            return new FuzzyMatchResult(item, 0, []);
        }

        double score;
        IReadOnlyList<int> matchedIndices;

        if (abbrMatch != null && (nameMatch == null || abbrMatch.Value.Score >= nameMatch.Value.Score))
        {
            // Give abbreviation matches a strong bonus (+100.0) so typing a trigger keyword ranks high
            score = abbrMatch.Value.Score + 100.0;
            matchedIndices = nameMatch?.MatchedIndices ?? [];
        }
        else if (nameMatch != null)
        {
            score = nameMatch.Value.Score;
            matchedIndices = nameMatch.Value.MatchedIndices;
            if (abbrMatch != null)
            {
                score += 20.0; // Bonus when both name and keyword match
            }
        }
        else
        {
            score = 0;
            matchedIndices = [];
        }

        score += CalculateTelemetryBoost(item.UsageStats);
        return new FuzzyMatchResult(item, score, matchedIndices);
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
