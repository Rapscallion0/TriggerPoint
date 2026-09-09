using TriggerPoint.Core.Models;
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
}

