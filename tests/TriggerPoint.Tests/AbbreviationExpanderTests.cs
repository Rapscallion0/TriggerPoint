using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class AbbreviationExpanderTests
{
    [Fact]
    public void AbbreviationTrie_ImmediateMode_MatchesExactSuffix()
    {
        var trie = new AbbreviationTrie();
        var item1 = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Email Signature",
            ActionType = ActionType.Snippet,
            Abbreviation = "sig",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        trie.Insert("sig", item1);

        // Substrings before full match do not trigger
        Assert.Null(trie.MatchSuffix("s", AbbreviationTriggerMode.Immediate));
        Assert.Null(trie.MatchSuffix("si", AbbreviationTriggerMode.Immediate));

        // Exact match
        var match = trie.MatchSuffix("sig", AbbreviationTriggerMode.Immediate);
        Assert.NotNull(match);
        Assert.Equal("Email Signature", match!.Name);

        // Matching suffix preceded by other keystrokes
        var matchWithPrefix = trie.MatchSuffix("hello sig", AbbreviationTriggerMode.Immediate);
        Assert.NotNull(matchWithPrefix);
        Assert.Equal("Email Signature", matchWithPrefix!.Name);
    }

    [Fact]
    public void AbbreviationTrie_DelimiterMode_MatchesOnlyRequestedMode()
    {
        var trie = new AbbreviationTrie();
        var itemDelimiter = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Address Macro",
            ActionType = ActionType.Snippet,
            Abbreviation = "addr",
            AbbreviationMode = AbbreviationTriggerMode.Delimiter
        };

        trie.Insert("addr", itemDelimiter);

        // Should NOT match in Immediate mode
        Assert.Null(trie.MatchSuffix("addr", AbbreviationTriggerMode.Immediate));

        // Matches in Delimiter mode
        var match = trie.MatchSuffix("addr", AbbreviationTriggerMode.Delimiter);
        Assert.NotNull(match);
        Assert.Equal("Address Macro", match!.Name);
    }

    [Fact]
    public void AbbreviationTrie_Clear_RemovesAllEntries()
    {
        var trie = new AbbreviationTrie();
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            ActionType = ActionType.Snippet,
            Abbreviation = "test",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        trie.Insert("test", item);
        Assert.NotNull(trie.MatchSuffix("test", AbbreviationTriggerMode.Immediate));

        trie.Clear();
        Assert.Null(trie.MatchSuffix("test", AbbreviationTriggerMode.Immediate));
    }

    [Fact]
    public void TriggerItem_Clone_PreservesAbbreviationProperties()
    {
        var original = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Snippet Item",
            ActionType = ActionType.Snippet,
            Abbreviation = ";email",
            AbbreviationMode = AbbreviationTriggerMode.Delimiter
        };

        var clone = original.Clone();

        Assert.Equal(original.Abbreviation, clone.Abbreviation);
        Assert.Equal(original.AbbreviationMode, clone.AbbreviationMode);
    }

    [Fact]
    public void AppSettings_AbbreviationDefaults_AreSensible()
    {
        var settings = new AppSettings();

        Assert.True(settings.EnableAbbreviationExpander);
        Assert.True(settings.AbbreviationSuppressInFullScreenGames);
        Assert.NotNull(settings.AbbreviationGlobalExcludedProcesses);
        Assert.Contains("mstsc.exe", settings.AbbreviationGlobalExcludedProcesses);
        Assert.Contains("vmconnect.exe", settings.AbbreviationGlobalExcludedProcesses);
    }

    [Fact]
    public void UpdateSnippets_DynamicallyUpdatesTrieWhenModeChanges()
    {
        var snippetService = new MockSnippetService();
        var contextService = new MockContextFilterService();
        var settings = new AppSettings { EnableAbbreviationExpander = false };
        var service = new AbbreviationExpanderService(snippetService, contextService, () => settings);

        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Dynamic Item",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = "brb",
            AbbreviationMode = AbbreviationTriggerMode.Delimiter
        };

        service.UpdateSnippets([item]);

        // Initially Delimiter
        Assert.Null(service.MatchSuffix("brb", AbbreviationTriggerMode.Immediate));
        Assert.NotNull(service.MatchSuffix("brb", AbbreviationTriggerMode.Delimiter));

        // Switch to Immediate
        item.AbbreviationMode = AbbreviationTriggerMode.Immediate;
        service.UpdateSnippets([item]);

        Assert.NotNull(service.MatchSuffix("brb", AbbreviationTriggerMode.Immediate));
        Assert.Null(service.MatchSuffix("brb", AbbreviationTriggerMode.Delimiter));
    }

    [Fact]
    public void UpdateSnippets_DynamicallyUpdatesKeyword()
    {
        var snippetService = new MockSnippetService();
        var contextService = new MockContextFilterService();
        var settings = new AppSettings { EnableAbbreviationExpander = false };
        var service = new AbbreviationExpanderService(snippetService, contextService, () => settings);

        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Keyword Item",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = "brb",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        service.UpdateSnippets([item]);
        Assert.NotNull(service.MatchSuffix("brb", AbbreviationTriggerMode.Immediate));

        // Change keyword to "afk"
        item.Abbreviation = "afk";
        service.UpdateSnippets([item]);

        Assert.Null(service.MatchSuffix("brb", AbbreviationTriggerMode.Immediate));
        Assert.NotNull(service.MatchSuffix("afk", AbbreviationTriggerMode.Immediate));
    }

    [Fact]
    public void UpdateSnippets_DisabledSnippet_IsNotIndexed()
    {
        var snippetService = new MockSnippetService();
        var contextService = new MockContextFilterService();
        var settings = new AppSettings { EnableAbbreviationExpander = false };
        var service = new AbbreviationExpanderService(snippetService, contextService, () => settings);

        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Disabled Item",
            ActionType = ActionType.Snippet,
            IsEnabled = true,
            Abbreviation = "hello",
            AbbreviationMode = AbbreviationTriggerMode.Immediate
        };

        service.UpdateSnippets([item]);
        Assert.NotNull(service.MatchSuffix("hello", AbbreviationTriggerMode.Immediate));

        item.IsEnabled = false;
        service.UpdateSnippets([item]);
        Assert.Null(service.MatchSuffix("hello", AbbreviationTriggerMode.Immediate));
    }

    private class MockSnippetService : ISnippetService
    {
        public Task InjectSnippetAsync(string template, IntPtr targetHwnd, SnippetContentType contentType = SnippetContentType.PlainText, string? rtfContent = null)
            => Task.CompletedTask;
    }

    private class MockContextFilterService : IContextFilterService
    {
        public IntPtr LastExternalForegroundHwnd { get; set; } = IntPtr.Zero;
        public IntPtr GetForegroundWindowHandle() => IntPtr.Zero;
        public string? GetForegroundProcessName() => "notepad";
        public string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null) => null;
        public bool ShouldExecute(TriggerItem item) => true;
        public bool ShouldExecute(TriggerItem item, IReadOnlyList<TriggerItem>? allItems) => true;
        public void SetAllItemsProvider(Func<IReadOnlyList<TriggerItem>>? provider) { }
        public List<TriggerItem> GetInheritanceChain(TriggerItem item, IReadOnlyList<TriggerItem> allItems) => [];
    }
}
