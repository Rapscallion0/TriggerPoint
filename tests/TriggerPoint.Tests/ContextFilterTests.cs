using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ContextFilterTests
{
    [Fact]
    public void ContextFilter_EmptyLists_AllowsAnyProcess()
    {
        var filter = new ContextFilter();
        Assert.True(filter.IsActiveForProcess("notepad.exe"));
        Assert.True(filter.IsActiveForProcess("code"));
        Assert.True(filter.IsActiveForProcess(null));
    }

    [Fact]
    public void ContextFilter_AllowedProcesses_MatchesWithOrWithoutExe()
    {
        var filter = new ContextFilter
        {
            AllowedProcesses = ["code.exe", "devenv"]
        };

        Assert.True(filter.IsActiveForProcess("Code.exe"));
        Assert.True(filter.IsActiveForProcess("code"));
        Assert.True(filter.IsActiveForProcess("DEVENV.EXE"));
        Assert.True(filter.IsActiveForProcess("devenv"));
        Assert.False(filter.IsActiveForProcess("notepad.exe"));
        Assert.False(filter.IsActiveForProcess("chrome"));
    }

    [Fact]
    public void ContextFilter_ExcludedProcesses_OverridesAllowed()
    {
        var filter = new ContextFilter
        {
            AllowedProcesses = ["chrome.exe", "msedge.exe"],
            ExcludedProcesses = ["chrome"]
        };

        Assert.False(filter.IsActiveForProcess("chrome.exe"));
        Assert.True(filter.IsActiveForProcess("msedge.exe"));
    }

    [Theory]
    [InlineData("https://github.com/torvalds/linux", "*github.com*", true)]
    [InlineData("http://localhost:3000/dashboard", "localhost:*", true)]
    [InlineData("https://google.com/search?q=test", "*github.com*", false)]
    [InlineData("https://jira.corp.internal/browse/TP-1", "*jira.*", true)]
    public void ContextFilter_MatchesWildcard_CorrectlyEvaluates(string url, string pattern, bool expected)
    {
        bool result = ContextFilter.MatchesWildcard(url, pattern);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ContextFilter_AllowedUrls_MatchesActiveBrowserUrl()
    {
        var filter = new ContextFilter
        {
            AllowedProcesses = ["chrome.exe"],
            AllowedUrls = ["*github.com*"]
        };

        // GitHub tab in Chrome -> Active
        Assert.True(filter.IsActive("chrome.exe", "https://github.com/dotnet/wpf"));

        // YouTube tab in Chrome -> Not active
        Assert.False(filter.IsActive("chrome.exe", "https://youtube.com/watch?v=123"));

        // Notepad -> Not active (process is not chrome)
        Assert.False(filter.IsActive("notepad.exe", null));
    }

    [Fact]
    public void ContextFilter_ExcludedUrls_SuppressesOnMatchedUrl()
    {
        var filter = new ContextFilter
        {
            AllowedProcesses = ["msedge.exe"],
            ExcludedUrls = ["*youtube.com*", "*netflix.com*"]
        };

        // Allowed on normal pages
        Assert.True(filter.IsActive("msedge.exe", "https://stackoverflow.com"));

        // Suppressed on excluded video pages
        Assert.False(filter.IsActive("msedge.exe", "https://youtube.com/feed/subscriptions"));
        Assert.False(filter.IsActive("msedge.exe", "https://www.netflix.com/browse"));
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("chrome.exe", true)]
    [InlineData("CHROME.EXE", true)]
    [InlineData("msedge", true)]
    [InlineData("msedge.exe", true)]
    [InlineData("firefox", true)]
    [InlineData("firefox.exe", true)]
    [InlineData("brave", true)]
    [InlineData("brave.exe", true)]
    [InlineData("opera", true)]
    [InlineData("opera.exe", true)]
    [InlineData("vivaldi", true)]
    [InlineData("vivaldi.exe", true)]
    [InlineData("arc", true)]
    [InlineData("arc.exe", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("devenv.exe", false)]
    [InlineData("excel.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContextFilter_IsKnownBrowser_IdentifiesBrowsersCorrectly(string? processName, bool expected)
    {
        Assert.Equal(expected, ContextFilter.IsKnownBrowser(processName));
    }

    [Theory]
    [InlineData("mail.google.com/mail/u/0/#inbox", true)]
    [InlineData("https://mail.google.com/mail/u/0/#inbox", true)]
    [InlineData("https://mail.google.com/mail/u/0/#inbox/FMfcgzQX12345", true)]
    [InlineData("https://mail.google.com/mail/u/0/#search/invoice/FMfcgzQX12345", true)]
    [InlineData("mail.google.com/mail/u/0/#all/FMfcgzQX12345", true)]
    [InlineData("mail.google.com/mail/u/0/#sent/FMfcgzQX12345", true)]
    [InlineData("mail.google.com/mail/u/0/#label/Work/FMfcgzQX12345", true)]
    [InlineData("https://mail.google.com/mail/u/0/?ui=2&view=btop", true)]
    [InlineData("https://mail.google.com/mail/u/1/#inbox", false)]
    [InlineData("https://calendar.google.com/calendar/u/0/r", false)]
    [InlineData("https://www.google.com/search?q=gmail", false)]
    public void ContextFilter_GmailInboxRule_MatchesAcrossGmailViewsAndInlineReplies(string activeUrl, bool expected)
    {
        var filter = new ContextFilter
        {
            AllowedUrls = ["mail.google.com/mail/u/0/#inbox"]
        };

        Assert.Equal(expected, filter.IsActive("chrome.exe", activeUrl, "Re: Follow up - user@gmail.com - Gmail - Google Chrome"));
    }

    [Fact]
    public void ContextFilter_DualUrlAndTabTitle_MatchesEitherUrlOrTabTitleFallback()
    {
        var filterWithTitleRule = new ContextFilter
        {
            AllowedUrls = ["*Gmail*"]
        };

        // Matches via tab title even if URL is null (e.g. PWA / app mode without address bar)
        Assert.True(filterWithTitleRule.IsActive("chrome.exe", null, "Inbox (3) - user@gmail.com - Gmail - Google Chrome"));

        // Matches via tab title when both URL and title are provided
        Assert.True(filterWithTitleRule.IsActive("chrome.exe", "mail.google.com/mail/u/0/#inbox", "Re: Thread - user@gmail.com - Gmail - Google Chrome"));

        // Does not match unrelated tab
        Assert.False(filterWithTitleRule.IsActive("chrome.exe", "github.com/org/repo", "GitHub - Google Chrome"));
    }

    [Fact]
    public void ContextFilter_ExcludedUrls_SuppressesWhenEitherUrlOrTabTitleMatches()
    {
        var filter = new ContextFilter
        {
            ExcludedUrls = ["*YouTube*"]
        };

        Assert.False(filter.IsActive("chrome.exe", "https://www.youtube.com/watch?v=1", "Video - YouTube - Google Chrome"));
        Assert.False(filter.IsActive("chrome.exe", null, "Music - YouTube - Google Chrome"));
        Assert.True(filter.IsActive("chrome.exe", "https://mail.google.com/mail/u/0/#inbox", "Inbox - Gmail - Google Chrome"));
    }

    [Fact]
    public void ContextFilter_IsActiveForProcess_OnlyChecksProcessRules()
    {
        var filter = new ContextFilter
        {
            AllowedProcesses = ["chrome.exe"],
            AllowedUrls = ["mail.google.com/mail/u/0/#inbox"]
        };

        // Process-only check passes for chrome.exe
        Assert.True(filter.IsActiveForProcess("chrome.exe"));
        Assert.False(filter.IsActiveForProcess("notepad.exe"));
    }

    [Theory]
    [InlineData("mail.google.com/mail/u/0/#inbox", true)]
    [InlineData("https://mail.google.com/mail/u/0/#inbox", true)]
    [InlineData("localhost:3000/api", true)]
    [InlineData("person@company.com", false)]
    [InlineData("Hi team, please see the inline response below.", false)]
    [InlineData("Subject with spaces.txt", false)]
    [InlineData("", false)]
    public void ContextFilterService_IsLikelyBrowserUrl_RejectsInlineEmailFieldsAndAcceptsUrls(string candidate, bool expected)
    {
        Assert.Equal(expected, ContextFilterService.IsLikelyBrowserUrl(candidate));
    }

    [Theory]
    [InlineData("https://mail.google.com/mail/u/0/#inbox", "mail.google.com/mail/u/0/*")]
    [InlineData("mail.google.com/mail/u/0/#search/test", "mail.google.com/mail/u/0/*")]
    [InlineData("https://github.com/Rapscallion0/TriggerPoint?tab=readme", "github.com/Rapscallion0/TriggerPoint/*")]
    [InlineData("https://example.com/", "example.com*")]
    public void ContextFilter_ExtractBaseWebAppUrlPattern_StripsHashAndQuery(string rawUrl, string expectedPattern)
    {
        Assert.Equal(expectedPattern, ContextFilter.ExtractBaseWebAppUrlPattern(rawUrl));
    }

    [Theory]
    [InlineData("Inbox (4) - user@gmail.com - Gmail - Google Chrome", "*Gmail*")]
    [InlineData("Pull Requests - GitHub — Mozilla Firefox", "*GitHub*")]
    [InlineData("Dashboard - Brave", "*Dashboard*")]
    public void ContextFilter_ExtractTabTitleKeywordPattern_ExtractsCleanAppKeyword(string rawTitle, string expectedKeywordPattern)
    {
        Assert.Equal(expectedKeywordPattern, ContextFilter.ExtractTabTitleKeywordPattern(rawTitle));
    }
}

