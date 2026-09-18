using System;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class ApplicationSettingsNavigationTests
{
    [Theory]
    [InlineData("anim")]
    [InlineData("theme")]
    [InlineData("Dark Mode")]
    [InlineData("mica")]
    [InlineData("acrylic")]
    [InlineData("transparency")]
    public void MatchesCategory_AppearanceQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Appearance, query);
        Assert.True(matches);
    }

    [Theory]
    [InlineData("hotkey")]
    [InlineData("cheat sheet")]
    [InlineData("palette")]
    [InlineData("conflict")]
    [InlineData("shortcut")]
    public void MatchesCategory_ShortcutQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Shortcuts, query);
        Assert.True(matches);
    }

    [Theory]
    [InlineData("startup")]
    [InlineData("tray")]
    [InlineData("toasts")]
    [InlineData("monitor")]
    [InlineData("crosshair")]
    public void MatchesCategory_SystemQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.System, query);
        Assert.True(matches);
    }

    [Theory]
    [InlineData("serilog")]
    [InlineData("log level")]
    [InlineData("retention")]
    [InlineData("split")]
    [InlineData("folder")]
    public void MatchesCategory_LoggingQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Logging, query);
        Assert.True(matches);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("check for updates")]
    [InlineData("silent install")]
    [InlineData("beta")]
    [InlineData("frequency")]
    public void MatchesCategory_UpdatesQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Updates, query);
        Assert.True(matches);
    }

    [Theory]
    [InlineData("recycle bin")]
    [InlineData("backup")]
    [InlineData("export")]
    [InlineData("import")]
    [InlineData("telemetry")]
    public void MatchesCategory_DataQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Data, query);
        Assert.True(matches);
    }

    [Fact]
    public void MatchesCategory_EmptyOrWhitespaceQuery_MatchesAllCategories()
    {
        foreach (ApplicationSettingsWindow.SettingsCategory cat in Enum.GetValues<ApplicationSettingsWindow.SettingsCategory>())
        {
            Assert.True(ApplicationSettingsWindow.MatchesCategory(cat, string.Empty));
            Assert.True(ApplicationSettingsWindow.MatchesCategory(cat, "   "));
            Assert.True(ApplicationSettingsWindow.MatchesCategory(cat, null!));
        }
    }

    [Fact]
    public void MatchesCategory_NonMatchingQuery_ReturnsFalseForAllCategories()
    {
        const string nonMatching = "zzzz_completely_nonexistent_12345_query";
        foreach (ApplicationSettingsWindow.SettingsCategory cat in Enum.GetValues<ApplicationSettingsWindow.SettingsCategory>())
        {
            Assert.False(ApplicationSettingsWindow.MatchesCategory(cat, nonMatching));
        }
    }

    [Fact]
    public void CategoryKeywords_CoversAllSixCategoriesWithSubstantialKeywords()
    {
        var allCategories = Enum.GetValues<ApplicationSettingsWindow.SettingsCategory>();
        Assert.Equal(6, allCategories.Length);

        foreach (var cat in allCategories)
        {
            Assert.True(ApplicationSettingsWindow.CategoryKeywords.ContainsKey(cat));
            var keywords = ApplicationSettingsWindow.CategoryKeywords[cat];
            Assert.NotEmpty(keywords);
            Assert.True(keywords.Length >= 5);
        }
    }
}
