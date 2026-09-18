using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class QuickCalculatorAndPowerFeaturesTests
{
    [Theory]
    [InlineData("= 1920 * 1080 / 2", "1,036,800")]
    [InlineData("= 10 + 20 * 3", "70")]
    [InlineData("(45 * 1.15) + 10", "61.75")]
    [InlineData("= 100 - 35.5", "64.5")]
    public void QuickCalculator_EvaluatesMathExpressions_Correctly(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);

        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Fact]
    public void QuickCalculator_DivisionByZero_ReturnsNullOrFailsGracefully()
    {
        var result = QuickCalculatorService.TryEvaluate("= 10 / 0");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("16px to rem", "1rem")]
    [InlineData("32px to rem", "2rem")]
    [InlineData("2rem to px", "32px")]
    [InlineData("1.5rem to px", "24px")]
    public void QuickCalculator_PxAndRemConversions_WorkAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);

        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    [InlineData("1024mb to gb", "1 GB")]
    [InlineData("2gb to mb", "2048 MB")]
    [InlineData("1024kb to mb", "1 MB")]
    [InlineData("2048mb to gb", "2 GB")]
    public void QuickCalculator_DataUnitConversions_WorkAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);

        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    [InlineData("#ffffff to rgb", "rgb(255, 255, 255)")]
    [InlineData("#000000 to rgb", "rgb(0, 0, 0)")]
    [InlineData("#10b981 to rgb", "rgb(16, 185, 129)")]
    public void QuickCalculator_ColorConversions_WorkAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);

        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    [InlineData("Open Chrome")]
    [InlineData("git checkout main")]
    [InlineData("Not a math expression")]
    [InlineData("   ")]
    [InlineData("")]
    public void QuickCalculator_IgnoresRegularSearchQueries(string input)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.Null(result);
    }

    [Fact]
    public void AppSettings_PowerUserOptions_HaveAppropriateDefaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.EnableBackdropEffects);
        Assert.True(settings.EnableUiAnimations);
        Assert.NotNull(settings.CheatSheetHotkey);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, settings.CheatSheetHotkey.Modifiers);
        Assert.Equal(191, settings.CheatSheetHotkey.VirtualKey);
        Assert.Equal("/", settings.CheatSheetHotkey.KeyName);
    }

    [Fact]
    public void AppSettings_Normalize_MigratesLegacyWinF1CheatSheetHotkey()
    {
        var settings = new AppSettings
        {
            CheatSheetHotkey = new ShortcutBinding(ModifierKeys.Windows, 112, "F1")
        };

        settings.Normalize();

        Assert.NotNull(settings.CheatSheetHotkey);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, settings.CheatSheetHotkey.Modifiers);
        Assert.Equal(191, settings.CheatSheetHotkey.VirtualKey);
        Assert.Equal("/", settings.CheatSheetHotkey.KeyName);
    }

    [Fact]
    public void HotkeyRegistryValidator_DetectsCheatSheetConflict()
    {
        var binding = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Shift, 75, "K");
        var settings = new AppSettings
        {
            CheatSheetHotkey = binding
        };

        var items = new List<TriggerItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Conflicting Action",
                Hotkey = binding,
                IsEnabled = true
            }
        };

        var conflicts = HotkeyRegistryValidator.ValidateTier1Conflicts(items, settings);

        Assert.NotEmpty(conflicts);
        Assert.True(conflicts.ContainsKey(items[0].Id));
        Assert.Equal("Cheat Sheet HUD", conflicts[items[0].Id].ConflictingActionName);
    }
}
