using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class SearchQueryParserTests
{
    [Fact]
    public void Parse_ImplicitAnd_ExtractsMultiplePositiveTokensInSingleClause()
    {
        var plan = SearchQueryParser.Parse("rocky mavericks volleyball");

        Assert.False(plan.IsEmpty);
        Assert.Single(plan.Clauses);
        Assert.Empty(plan.ExcludedTokens);

        var tokens = plan.Clauses[0].PositiveTokens;
        Assert.Equal(3, tokens.Count);
        Assert.Equal("rocky", tokens[0].Text);
        Assert.Equal("mavericks", tokens[1].Text);
        Assert.Equal("volleyball", tokens[2].Text);
    }

    [Fact]
    public void Parse_LogicalOr_ExtractsMultipleClauses()
    {
        var plan1 = SearchQueryParser.Parse("invoice OR receipt");
        Assert.Equal(2, plan1.Clauses.Count);
        Assert.Equal("invoice", plan1.Clauses[0].PositiveTokens[0].Text);
        Assert.Equal("receipt", plan1.Clauses[1].PositiveTokens[0].Text);

        var plan2 = SearchQueryParser.Parse("report | memo");
        Assert.Equal(2, plan2.Clauses.Count);
        Assert.Equal("report", plan2.Clauses[0].PositiveTokens[0].Text);
        Assert.Equal("memo", plan2.Clauses[1].PositiveTokens[0].Text);
    }

    [Fact]
    public void Parse_Exclusions_ExtractsExcludedTokens()
    {
        var plan = SearchQueryParser.Parse("invoice -draft NOT paid");

        Assert.Single(plan.Clauses);
        Assert.Equal("invoice", plan.Clauses[0].PositiveTokens[0].Text);

        Assert.Equal(2, plan.ExcludedTokens.Count);
        Assert.Equal("draft", plan.ExcludedTokens[0].Text);
        Assert.True(plan.ExcludedTokens[0].IsNegated);
        Assert.Equal("paid", plan.ExcludedTokens[1].Text);
        Assert.True(plan.ExcludedTokens[1].IsNegated);
    }

    [Fact]
    public void Parse_ExactPhrase_PreservesExactMatchWithSpaces()
    {
        var plan = SearchQueryParser.Parse("\"Rocky Mountain\" Mavericks");

        Assert.Single(plan.Clauses);
        var tokens = plan.Clauses[0].PositiveTokens;
        Assert.Equal(2, tokens.Count);
        Assert.Equal("Rocky Mountain", tokens[0].Text);
        Assert.True(tokens[0].IsExactPhrase);

        Assert.Equal("Mavericks", tokens[1].Text);
        Assert.False(tokens[1].IsExactPhrase);
    }

    [Fact]
    public void Parse_PrefixAnchor_SetsIsPrefixAnchor()
    {
        var plan = SearchQueryParser.Parse("^calc");

        Assert.Single(plan.Clauses);
        var token = plan.Clauses[0].PositiveTokens[0];
        Assert.Equal("calc", token.Text);
        Assert.True(token.IsPrefixAnchor);
    }

    [Fact]
    public void Matches_RejectsSequentialScatteredCharacters_UserReportedDateBug()
    {
        // Bug reported by user: searching for 'date' matched
        // "Hello Andrew, On behalf of the Rocky Mountain Mavericks"
        // because d...a...t...e appeared scattered across unrelated words.
        var clip = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = "Hello Andrew, On behalf of the Rocky Mountain Mavericks",
            PreviewText = "Hello Andrew, On behalf of the Rocky Mountain Mavericks",
            CapturedAt = DateTime.UtcNow
        };

        var match = FuzzyMatcher.MatchClipboard(clip, "date");
        Assert.Null(match);
    }

    [Fact]
    public void Matches_MatchesContiguousTokenAndPhrase()
    {
        var clip = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PreviewText = "Quarterly Financial Report",
            PlainText = "Please check the due date for the invoice.",
            CapturedAt = DateTime.UtcNow
        };

        var matchDate = FuzzyMatcher.MatchClipboard(clip, "date");
        Assert.NotNull(matchDate);
        Assert.True(matchDate.IsMatch);
        Assert.False(matchDate.IsMatchInTitle);
        Assert.NotNull(matchDate.ContextExcerpt);

        var matchExact = FuzzyMatcher.MatchClipboard(clip, "\"due date\"");
        Assert.NotNull(matchExact);
        Assert.True(matchExact.IsMatch);
        Assert.False(matchExact.IsMatchInTitle);

        var matchNegative = FuzzyMatcher.MatchClipboard(clip, "financial -invoice");
        Assert.Null(matchNegative);

        var matchNegativePass = FuzzyMatcher.MatchClipboard(clip, "financial -draft");
        Assert.NotNull(matchNegativePass);
        Assert.True(matchNegativePass.IsMatch);
    }

    [Fact]
    public void ExtractContextExcerpt_GeneratesFocusedExcerptWithRelativeIndices()
    {
        var text = "0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ The quick brown fox jumps over the lazy dog. Extra padding and information at the end of the line.";
        var matchIndex = text.IndexOf("quick brown", StringComparison.OrdinalIgnoreCase);
        var matchLength = "quick brown".Length;
        var matchedIndices = new List<int>();
        for (int i = 0; i < matchLength; i++) matchedIndices.Add(matchIndex + i);

        var (excerpt, relIndices) = SearchQueryParser.ExtractContextExcerpt(text, matchedIndices, maxCharsBefore: 15, maxCharsAfter: 20);

        Assert.NotNull(excerpt);
        Assert.Contains("quick brown", excerpt);
        Assert.NotEmpty(relIndices);
        Assert.Equal(matchLength, relIndices.Count);

        // Verify the slice matches the expected term
        var sliced = excerpt.Substring(relIndices[0], relIndices.Count);
        Assert.Equal("quick brown", sliced);
    }

    [Fact]
    public void FuzzyMatcher_MatchClipboard_PopulatesContextExcerptForDeepMatches()
    {
        var longBody = new string('x', 200) + " Important Invoice #998822 " + new string('y', 200);
        var clip = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PlainText = longBody,
            PreviewText = "Title of long snippet",
            CapturedAt = DateTime.UtcNow
        };

        var result = FuzzyMatcher.MatchClipboard(clip, "998822");

        Assert.NotNull(result);
        Assert.True(result.IsMatch);
        Assert.False(result.IsMatchInTitle);
        Assert.NotNull(result.ContextExcerpt);
        Assert.Contains("998822", result.ContextExcerpt);
        Assert.NotNull(result.ContextExcerptMatchedIndices);
        Assert.NotEmpty(result.ContextExcerptMatchedIndices);
    }

    [Fact]
    public void FuzzyMatcher_TriggerItem_MatchesWithSearchOperators()
    {
        var item = new TriggerItem
        {
            Name = "Open Chrome Browser",
            Description = "Launches Google Chrome window"
        };

        // AND match
        var matchAnd = FuzzyMatcher.Match(item, "open chrome");
        Assert.True(matchAnd.IsMatch);

        // OR match
        var matchOr = FuzzyMatcher.Match(item, "firefox OR chrome");
        Assert.True(matchOr.IsMatch);

        // NOT match
        var matchNot = FuzzyMatcher.Match(item, "chrome -firefox");
        Assert.True(matchNot.IsMatch);

        var matchExcluded = FuzzyMatcher.Match(item, "chrome -browser");
        Assert.False(matchExcluded.IsMatch);
    }

    [Fact]
    public void ExtractContextExcerpt_MultiLineCrlf_PreservesExactMatchHighlightWithoutIndexDrift()
    {
        // 50 lines of text with Windows \r\n linebreaks preceding the match
        var lines = new List<string>();
        for (int i = 0; i < 50; i++)
        {
            lines.Add($"Header line number {i} with text and data.");
        }
        lines.Add("Section 51: DATED at Okotoks, Alberta, this 21st day of October.");
        lines.Add("Ending line with extra footer information.");
        var multiLineText = string.Join("\r\n", lines);

        var query = SearchQueryParser.Parse("date");
        var match = query.Match(multiLineText);

        Assert.NotNull(match);
        Assert.NotEmpty(match.MatchedIndices);

        var (excerpt, relIndices) = SearchQueryParser.ExtractContextExcerpt(
            multiLineText,
            match.MatchedIndices,
            maxCharsBefore: 25,
            maxCharsAfter: 35);

        Assert.NotNull(excerpt);
        Assert.NotEmpty(relIndices);

        // Verify the highlighted slice in the excerpt matches "DATE" exactly with 0 index drift
        var highlightedSlice = excerpt.Substring(relIndices[0], "date".Length);
        Assert.Equal("DATE", highlightedSlice);
    }

    [Fact]
    public void TieredRanking_WholeWordAndWordStartOutranksMidWordSubstring()
    {
        var clipWholeWord = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PreviewText = "Meeting Notes",
            PlainText = "Please set a date for the upcoming presentation.",
            CapturedAt = DateTime.UtcNow
        };

        var clipWordStart = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PreviewText = "Contract Document",
            PlainText = "This agreement is DATED at Okotoks.",
            CapturedAt = DateTime.UtcNow
        };

        var clipMidWord = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PreviewText = "Bylaws",
            PlainText = "All provisions are mandated by the board of directors.",
            CapturedAt = DateTime.UtcNow
        };

        var matchWhole = FuzzyMatcher.MatchClipboard(clipWholeWord, "date");
        var matchWordStart = FuzzyMatcher.MatchClipboard(clipWordStart, "date");
        var matchMidWord = FuzzyMatcher.MatchClipboard(clipMidWord, "date");

        Assert.NotNull(matchWhole);
        Assert.NotNull(matchWordStart);
        Assert.NotNull(matchMidWord);

        // Whole word and word start rank higher than mid-word substring
        Assert.True(matchWhole.Score > matchMidWord.Score, "Whole word match 'date' must score higher than 'mandated'");
        Assert.True(matchWordStart.Score > matchMidWord.Score, "Word-start match 'DATED' must score higher than 'mandated'");
    }

    [Fact]
    public void Quotes_StrictWordBoundary_ExcludesMidWordMatches()
    {
        var clipMidWord = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PreviewText = "Bylaws",
            PlainText = "All provisions are mandated by the board.",
            CapturedAt = DateTime.UtcNow
        };

        var clipWholeWord = new ClipboardHistoryItem
        {
            Type = ClipboardItemType.PlainText,
            PreviewText = "Bylaws",
            PlainText = "The meeting date is established by the board.",
            CapturedAt = DateTime.UtcNow
        };

        // Searching without quotes allows mid-word matches
        var matchUnquoted = FuzzyMatcher.MatchClipboard(clipMidWord, "date");
        Assert.NotNull(matchUnquoted);

        // Searching with quotes enforces strict word boundaries
        var matchQuotedMidWord = FuzzyMatcher.MatchClipboard(clipMidWord, "\"date\"");
        Assert.Null(matchQuotedMidWord);

        var matchQuotedWholeWord = FuzzyMatcher.MatchClipboard(clipWholeWord, "\"date\"");
        Assert.NotNull(matchQuotedWholeWord);
    }
}
