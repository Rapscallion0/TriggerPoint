using System;
using System.Collections.Generic;
using System.Linq;

namespace TriggerPoint.Core.Services;

public sealed record SearchMatchResult(
    double Score,
    IReadOnlyList<int> MatchedIndices,
    int FirstMatchIndex,
    int MatchLength);

public sealed class SearchToken
{
    public string Text { get; }
    public bool IsExactPhrase { get; }
    public bool IsPrefixAnchor { get; }
    public bool IsNegated { get; }

    public SearchToken(string text, bool isExactPhrase = false, bool isPrefixAnchor = false, bool isNegated = false)
    {
        Text = text ?? string.Empty;
        IsExactPhrase = isExactPhrase;
        IsPrefixAnchor = isPrefixAnchor;
        IsNegated = isNegated;
    }

    public override string ToString()
    {
        string prefix = IsNegated ? "-" : (IsPrefixAnchor ? "^" : "");
        return IsExactPhrase ? $"{prefix}\"{Text}\"" : $"{prefix}{Text}";
    }
}

public sealed class SearchClause
{
    public IReadOnlyList<SearchToken> PositiveTokens { get; }

    public SearchClause(IEnumerable<SearchToken> tokens)
    {
        PositiveTokens = tokens.ToList();
    }
}

public sealed class SearchQuery
{
    public string RawQuery { get; }
    public IReadOnlyList<SearchClause> Clauses { get; }
    public IReadOnlyList<SearchToken> ExcludedTokens { get; }
    public bool IsEmpty => Clauses.Count == 0 && ExcludedTokens.Count == 0;

    public SearchQuery(string rawQuery, IEnumerable<SearchClause> clauses, IEnumerable<SearchToken> excludedTokens)
    {
        RawQuery = rawQuery ?? string.Empty;
        Clauses = clauses.ToList();
        ExcludedTokens = excludedTokens.ToList();
    }

    public SearchMatchResult? Match(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return IsEmpty ? new SearchMatchResult(1.0, Array.Empty<int>(), 0, 0) : null;
        }

        if (IsEmpty)
        {
            return new SearchMatchResult(1.0, Array.Empty<int>(), 0, 0);
        }

        // 1. Check for negative exclusions
        foreach (var excluded in ExcludedTokens)
        {
            if (string.IsNullOrWhiteSpace(excluded.Text)) continue;
            if (text.IndexOf(excluded.Text, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null; // Excluded term found, reject match
            }
        }

        // If there are only negative exclusions and none matched, candidate qualifies
        if (Clauses.Count == 0)
        {
            return new SearchMatchResult(100.0, Array.Empty<int>(), 0, 0);
        }

        SearchMatchResult? bestResult = null;

        // 2. Evaluate OR clauses: at least one clause must match completely (AND logic within clause)
        foreach (var clause in Clauses)
        {
            if (clause.PositiveTokens.Count == 0) continue;

            bool allTokensMatched = true;
            double clauseScore = 0.0;
            var matchedIndicesSet = new SortedSet<int>();
            int firstMatchIndex = int.MaxValue;
            int totalMatchLength = 0;

            foreach (var token in clause.PositiveTokens)
            {
                if (string.IsNullOrEmpty(token.Text)) continue;

                int bestTokenIdx = -1;
                double bestQualityScore = -1.0;
                int searchStart = 0;
                var tokenIndices = new List<int>();

                while (searchStart < text.Length)
                {
                    int occ = text.IndexOf(token.Text, searchStart, StringComparison.OrdinalIgnoreCase);
                    if (occ < 0) break;

                    bool isPrefix = occ == 0;
                    bool isStart = isPrefix || IsWordStart(text, occ);
                    bool isEnd = IsWordEnd(text, occ + token.Text.Length);

                    // Exact phrase / quotes: enforce word boundary
                    if (token.IsExactPhrase)
                    {
                        if (!isStart || !isEnd)
                        {
                            searchStart = occ + token.Text.Length;
                            continue;
                        }
                    }
                    else if (token.IsPrefixAnchor)
                    {
                        if (occ != 0)
                        {
                            break;
                        }
                    }

                    // Tiered quality score for this occurrence
                    double occQuality = 10.0; // Baseline mid-word match
                    if (text.Length == token.Text.Length && occ == 0)
                    {
                        occQuality = 500.0; // Exact entire string match
                    }
                    else if (isStart && isEnd)
                    {
                        occQuality = 250.0; // Whole word match
                    }
                    else if (isStart)
                    {
                        occQuality = 120.0; // Word-start match (e.g. 'dated', 'date_picker')
                    }

                    if (occQuality > bestQualityScore)
                    {
                        bestQualityScore = occQuality;
                        bestTokenIdx = occ;
                    }

                    tokenIndices.Add(occ);
                    searchStart = occ + token.Text.Length;
                }

                if (bestTokenIdx < 0)
                {
                    allTokensMatched = false;
                    break;
                }

                if (bestTokenIdx < firstMatchIndex)
                {
                    firstMatchIndex = bestTokenIdx;
                }

                totalMatchLength += token.Text.Length;

                double tokenScore = (token.Text.Length * 10.0) + bestQualityScore;
                clauseScore += tokenScore;

                foreach (var occ in tokenIndices)
                {
                    for (int i = 0; i < token.Text.Length; i++)
                    {
                        matchedIndicesSet.Add(occ + i);
                    }
                }
            }

            if (allTokensMatched)
            {
                // Multi-token completeness bonus
                if (clause.PositiveTokens.Count > 1)
                {
                    clauseScore += clause.PositiveTokens.Count * 25.0;
                }

                var result = new SearchMatchResult(
                    clauseScore,
                    matchedIndicesSet.ToList(),
                    firstMatchIndex == int.MaxValue ? 0 : firstMatchIndex,
                    totalMatchLength);

                if (bestResult == null || result.Score > bestResult.Score)
                {
                    bestResult = result;
                }
            }
        }

        return bestResult;
    }

    private static bool IsWordStart(string text, int index)
    {
        if (index <= 0) return true;
        char prev = text[index - 1];
        return char.IsWhiteSpace(prev) || char.IsPunctuation(prev) || prev == '_' || prev == '-' || prev == '.' || prev == '/' || prev == '\\' || prev == '(' || prev == '[' || prev == '{';
    }

    private static bool IsWordEnd(string text, int endIndex)
    {
        if (endIndex >= text.Length) return true;
        char next = text[endIndex];
        return char.IsWhiteSpace(next) || char.IsPunctuation(next) || next == '_' || next == '-' || next == '.' || next == '/' || next == '\\' || next == ')' || next == ']' || next == '}';
    }
}

public static class SearchQueryParser
{
    public static SearchQuery Parse(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return new SearchQuery(string.Empty, Array.Empty<SearchClause>(), Array.Empty<SearchToken>());
        }

        string trimmed = rawQuery.Trim();

        // 1. Extract excluded terms first (e.g. -term or NOT term)
        var excludedTokens = new List<SearchToken>();
        var orClausesRaw = SplitOrClauses(trimmed);
        var clauses = new List<SearchClause>();

        foreach (var clauseStr in orClausesRaw)
        {
            var tokens = TokenizeClause(clauseStr, out var clauseExcluded);
            excludedTokens.AddRange(clauseExcluded);
            if (tokens.Count > 0)
            {
                clauses.Add(new SearchClause(tokens));
            }
        }

        return new SearchQuery(trimmed, clauses, excludedTokens);
    }

    private static List<string> SplitOrClauses(string input)
    {
        var result = new List<string>();
        int start = 0;
        bool inQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes)
            {
                // Check for " OR "
                if (i <= input.Length - 4 &&
                    (i == 0 || char.IsWhiteSpace(input[i - 1])) &&
                    (input.Substring(i, 2).Equals("OR", StringComparison.OrdinalIgnoreCase)) &&
                    (i + 2 < input.Length && char.IsWhiteSpace(input[i + 2])))
                {
                    result.Add(input.Substring(start, i - start).Trim());
                    i += 2; // skip "OR"
                    start = i + 1;
                    continue;
                }

                // Check for " | "
                if (input[i] == '|' &&
                    (i == 0 || char.IsWhiteSpace(input[i - 1])) &&
                    (i + 1 == input.Length || char.IsWhiteSpace(input[i + 1])))
                {
                    result.Add(input.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
        }

        if (start < input.Length)
        {
            string remaining = input.Substring(start).Trim();
            if (!string.IsNullOrEmpty(remaining))
            {
                result.Add(remaining);
            }
        }

        if (result.Count == 0 && !string.IsNullOrWhiteSpace(input))
        {
            result.Add(input.Trim());
        }

        return result;
    }

    private static List<SearchToken> TokenizeClause(string clauseText, out List<SearchToken> excludedTokens)
    {
        var positiveTokens = new List<SearchToken>();
        excludedTokens = new List<SearchToken>();

        int i = 0;
        while (i < clauseText.Length)
        {
            // Skip leading whitespace
            while (i < clauseText.Length && char.IsWhiteSpace(clauseText[i])) i++;
            if (i >= clauseText.Length) break;

            // Check for NOT prefix
            if (i <= clauseText.Length - 4 &&
                clauseText.Substring(i, 3).Equals("NOT", StringComparison.OrdinalIgnoreCase) &&
                char.IsWhiteSpace(clauseText[i + 3]))
            {
                i += 4;
                while (i < clauseText.Length && char.IsWhiteSpace(clauseText[i])) i++;
                if (i >= clauseText.Length) break;

                string notTerm = ReadWordOrQuoted(clauseText, ref i, out bool isQuoted);
                if (!string.IsNullOrEmpty(notTerm))
                {
                    excludedTokens.Add(new SearchToken(notTerm, isExactPhrase: isQuoted, isNegated: true));
                }
                continue;
            }

            // Check for -term (negation)
            if (clauseText[i] == '-' && i + 1 < clauseText.Length && !char.IsWhiteSpace(clauseText[i + 1]))
            {
                i++; // skip '-'
                string notTerm = ReadWordOrQuoted(clauseText, ref i, out bool isQuoted);
                if (!string.IsNullOrEmpty(notTerm))
                {
                    excludedTokens.Add(new SearchToken(notTerm, isExactPhrase: isQuoted, isNegated: true));
                }
                continue;
            }

            // Check for ^term (prefix anchor)
            if (clauseText[i] == '^' && i + 1 < clauseText.Length && !char.IsWhiteSpace(clauseText[i + 1]))
            {
                i++; // skip '^'
                string anchorTerm = ReadWordOrQuoted(clauseText, ref i, out bool isQuoted);
                if (!string.IsNullOrEmpty(anchorTerm))
                {
                    positiveTokens.Add(new SearchToken(anchorTerm, isExactPhrase: isQuoted, isPrefixAnchor: true));
                }
                continue;
            }

            // Standard term or quoted phrase
            string term = ReadWordOrQuoted(clauseText, ref i, out bool wasQuoted);
            if (!string.IsNullOrEmpty(term))
            {
                positiveTokens.Add(new SearchToken(term, isExactPhrase: wasQuoted));
            }
        }

        return positiveTokens;
    }

    private static string ReadWordOrQuoted(string text, ref int index, out bool isQuoted)
    {
        isQuoted = false;
        if (index >= text.Length) return string.Empty;

        if (text[index] == '"')
        {
            isQuoted = true;
            index++; // skip opening quote
            int start = index;
            while (index < text.Length && text[index] != '"')
            {
                index++;
            }

            string phrase = text.Substring(start, index - start);
            if (index < text.Length && text[index] == '"')
            {
                index++; // skip closing quote
            }
            return phrase.Trim();
        }

        int wordStart = index;
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return text.Substring(wordStart, index - wordStart).Trim();
    }

    /// <summary>
    /// Extracts a context snippet around the first matched index in a long text string.
    /// Returns the excerpt text and the relative matched indices inside that excerpt for highlighting.
    /// </summary>
    public static (string Excerpt, IReadOnlyList<int> ExcerptMatchedIndices) ExtractContextExcerpt(
        string fullText,
        IReadOnlyList<int>? matchedIndices,
        int maxCharsBefore = 35,
        int maxCharsAfter = 45)
    {
        if (string.IsNullOrEmpty(fullText))
        {
            return (string.Empty, Array.Empty<int>());
        }

        // Replace newlines and tabs 1:1 with single spaces to maintain 100% exact index parity
        char[] chars = fullText.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\r' || chars[i] == '\n' || chars[i] == '\t')
            {
                chars[i] = ' ';
            }
        }
        string singleLine = new string(chars);

        if (matchedIndices == null || matchedIndices.Count == 0)
        {
            string shortSnippet = singleLine.Length > (maxCharsBefore + maxCharsAfter)
                ? singleLine.Substring(0, maxCharsBefore + maxCharsAfter).Trim() + "..."
                : singleLine.Trim();
            return (shortSnippet, Array.Empty<int>());
        }

        int firstMatch = matchedIndices[0];
        int lastMatch = matchedIndices[^1];

        // Bounds check
        if (firstMatch < 0 || firstMatch >= singleLine.Length)
        {
            string shortSnippet = singleLine.Length > (maxCharsBefore + maxCharsAfter)
                ? singleLine.Substring(0, maxCharsBefore + maxCharsAfter).Trim() + "..."
                : singleLine.Trim();
            return (shortSnippet, Array.Empty<int>());
        }

        int start = Math.Max(0, firstMatch - maxCharsBefore);
        int end = Math.Min(singleLine.Length, Math.Max(lastMatch + 1, firstMatch + 1) + maxCharsAfter);

        // Adjust to word boundaries if possible
        if (start > 0)
        {
            int spaceIdx = singleLine.IndexOf(' ', start);
            if (spaceIdx >= 0 && spaceIdx < firstMatch)
            {
                start = spaceIdx + 1;
            }
        }

        if (end < singleLine.Length)
        {
            int spaceIdx = singleLine.LastIndexOf(' ', end - 1, end - (lastMatch + 1));
            if (spaceIdx > lastMatch && spaceIdx < singleLine.Length)
            {
                end = spaceIdx;
            }
        }

        // Trim whitespace while tracking bounds to maintain exact 1:1 offsets
        while (start < firstMatch && char.IsWhiteSpace(singleLine[start]))
        {
            start++;
        }
        while (end > (lastMatch + 1) && char.IsWhiteSpace(singleLine[end - 1]))
        {
            end--;
        }

        if (start >= end)
        {
            start = Math.Max(0, firstMatch - 10);
            end = Math.Min(singleLine.Length, lastMatch + 11);
        }

        string rawSubstring = singleLine.Substring(start, end - start);
        bool hasLeadingEllipsis = start > 0;
        bool hasTrailingEllipsis = end < singleLine.Length;

        string excerpt = (hasLeadingEllipsis ? "..." : "") + rawSubstring + (hasTrailingEllipsis ? "..." : "");
        int prefixOffset = hasLeadingEllipsis ? 3 : 0;

        // Calculate relative indices in the excerpt
        var relativeIndices = new List<int>();
        var matchSet = new HashSet<int>(matchedIndices);

        for (int i = 0; i < rawSubstring.Length; i++)
        {
            int originalIdx = start + i;
            if (matchSet.Contains(originalIdx))
            {
                relativeIndices.Add(prefixOffset + i);
            }
        }

        return (excerpt, relativeIndices);
    }
}
