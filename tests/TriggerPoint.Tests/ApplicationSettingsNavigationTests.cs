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
    [InlineData("pin")]
    [InlineData("pinned")]
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

    [Theory]
    [InlineData("clipboard")]
    [InlineData("history")]
    [InlineData("clip")]
    [InlineData("retention")]
    [InlineData("paste")]
    public void MatchesCategory_ClipboardQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Clipboard, query);
        Assert.True(matches);
    }

    [Theory]
    [InlineData("generator")]
    [InlineData("guid")]
    [InlineData("password")]
    [InlineData("passphrase")]
    [InlineData("phrase")]
    [InlineData("leetspeak")]
    public void MatchesCategory_GeneratorQueries_MatchCorrectly(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Generators, query);
        Assert.True(matches);
    }

    [Fact]
    public void CategoryKeywords_CoversAllCategoriesWithSubstantialKeywords()
    {
        var allCategories = Enum.GetValues<ApplicationSettingsWindow.SettingsCategory>();
        Assert.Equal(9, allCategories.Length);

        foreach (var cat in allCategories)
        {
            Assert.True(ApplicationSettingsWindow.CategoryKeywords.ContainsKey(cat));
            var keywords = ApplicationSettingsWindow.CategoryKeywords[cat];
            Assert.NotEmpty(keywords);
            Assert.True(keywords.Length >= 5);
        }
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("tabs")]
    [InlineData("filter")]
    [InlineData("services")]
    [InlineData("snippets")]
    [InlineData("workflows")]
    [InlineData("folders")]
    public void MatchesCategory_TabShortcutQueries_MatchShortcutsCategory(string query)
    {
        bool matches = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Shortcuts, query);
        Assert.True(matches);
    }

    [Fact]
    public void ValidateApplicationHotkeys_DetectsConflictBetweenTabHotkeys()
    {
        var appHotkey = new TriggerPoint.Core.Models.ShortcutBinding(TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Shift, 0x41, "A");
        var snippetHotkey = new TriggerPoint.Core.Models.ShortcutBinding(TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Shift, 0x41, "A"); // duplicate

        var result = TriggerPoint.Core.Services.HotkeyRegistryValidator.ValidateApplicationHotkeys(
            openSettingsHotkey: null,
            commandPaletteHotkey: null,
            allItems: [],
            commandPaletteAppHotkey: appHotkey,
            commandPaletteSnippetHotkey: snippetHotkey);

        Assert.False(result.IsValid);
        Assert.Contains("conflict", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateVirtualApplicationItems_IncludesConfiguredTabShortcuts()
    {
        var settings = new TriggerPoint.Core.Models.AppSettings
        {
            CommandPaletteAppHotkey = new TriggerPoint.Core.Models.ShortcutBinding(TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Alt, 0x41, "A"),
            CommandPaletteSnippetHotkey = new TriggerPoint.Core.Models.ShortcutBinding(TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Alt, 0x53, "S"),
            CommandPaletteWorkflowHotkey = new TriggerPoint.Core.Models.ShortcutBinding(TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Alt, 0x57, "W"),
            CommandPaletteFolderHotkey = new TriggerPoint.Core.Models.ShortcutBinding(TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Alt, 0x46, "F"),
            CommandPaletteServiceHotkey = new TriggerPoint.Core.Models.ShortcutBinding(TriggerPoint.Core.Models.ModifierKeys.Control | TriggerPoint.Core.Models.ModifierKeys.Alt, 0x56, "V"),
        };

        var virtualItems = TriggerPoint.UI.App.CreateVirtualApplicationItems(settings);

        Assert.Contains(virtualItems, x => x.Id == TriggerPoint.UI.App.CommandPaletteAppActionId && x.Hotkey?.DisplayText == "Ctrl + Alt + A");
        Assert.Contains(virtualItems, x => x.Id == TriggerPoint.UI.App.CommandPaletteSnippetActionId && x.Hotkey?.DisplayText == "Ctrl + Alt + S");
        Assert.Contains(virtualItems, x => x.Id == TriggerPoint.UI.App.CommandPaletteWorkflowActionId && x.Hotkey?.DisplayText == "Ctrl + Alt + W");
        Assert.Contains(virtualItems, x => x.Id == TriggerPoint.UI.App.CommandPaletteFolderActionId && x.Hotkey?.DisplayText == "Ctrl + Alt + F");
        Assert.Contains(virtualItems, x => x.Id == TriggerPoint.UI.App.CommandPaletteServiceActionId && x.Hotkey?.DisplayText == "Ctrl + Alt + V");
    }

    [Fact]
    public void ServiceItemViewModel_DoesNotHaveFastSelectionNumberByDefault()
    {
        var svc = new TriggerPoint.Core.Models.WindowsServiceItem
        {
            ServiceName = "wuauserv",
            DisplayName = "Windows Update",
            Status = TriggerPoint.Core.Models.WindowsServiceStatus.Running,
            StartType = "Manual"
        };

        var vm = new PaletteItemViewModel(svc);
        Assert.Null(vm.FastSelectionNumber);
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.HasFastSelection);
        Assert.Empty(vm.FastSelectionBadgeText);
    }

    [Fact]
    public void PaletteItemViewModel_HasFastSelection_IsCollapsedAndTooltipUsesAlt()
    {
        var item = new TriggerPoint.Core.Models.TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Test App",
            ActionType = TriggerPoint.Core.Models.ActionType.Shell
        };
        var vm = new PaletteItemViewModel(item, matchResult: null)
        {
            FastSelectionNumber = 3
        };

        Assert.Equal(System.Windows.Visibility.Collapsed, vm.HasFastSelection);
        Assert.Equal("Fast select (Alt+3)", vm.FastSelectionTooltip);
    }
}
