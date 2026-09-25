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
    [InlineData("20% of 850", "170")]
    [InlineData("100 + 15%", "115")]
    [InlineData("100 - 20%", "80")]
    [InlineData("100 to 125 as %", "+25%")]
    public void QuickCalculator_PercentageCalculations_WorkAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    [InlineData("100c to f", "212°F")]
    [InlineData("32f to c", "0°C")]
    [InlineData("100 cm to in", "39.3701 in")]
    [InlineData("1 km to mi", "0.6214 mi")]
    [InlineData("1 kg to lbs", "2.2046 lbs")]
    public void QuickCalculator_PhysicalUnitConversions_WorkAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Fact]
    public void QuickCalculator_RelativeDateAndProgrammerMath_WorkAccurately()
    {
        var dateRes = QuickCalculatorService.TryEvaluate("now + 3d");
        Assert.NotNull(dateRes);
        Assert.Contains(DateTime.Now.AddDays(3).ToString("yyyy-MM-dd"), dateRes.Description);

        var hexRes = QuickCalculatorService.TryEvaluate("0xFF + 16");
        Assert.NotNull(hexRes);
        Assert.Equal("271 (0x10F)", hexRes.FormattedResult);

        var shiftRes = QuickCalculatorService.TryEvaluate("1 << 8");
        Assert.NotNull(shiftRes);
        Assert.Equal("256 (0x100)", shiftRes.FormattedResult);

        var calcPrefixRes = QuickCalculatorService.TryEvaluate("@calc 50 * 4");
        Assert.NotNull(calcPrefixRes);
        Assert.Equal("200", calcPrefixRes.FormattedResult);
    }

    [Theory]
    [InlineData("now")]
    [InlineData("= now")]
    [InlineData("today")]
    [InlineData("= today")]
    [InlineData("tomorrow")]
    [InlineData("yesterday")]
    public void QuickCalculator_DateKeywords_EvaluateOnTheFly(string input)
    {
        var res = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(res);
        Assert.NotEmpty(res.FormattedResult);
        Assert.NotEmpty(res.Description);
    }

    [Fact]
    public void QuickCalculator_MultiUnitDateMath_EvaluatesAccurately()
    {
        var res = QuickCalculatorService.TryEvaluate("now + 2 days, 3 hours and 26 minutes");
        Assert.NotNull(res);
        var expected = DateTime.Now.AddDays(2).AddHours(3).AddMinutes(26);
        Assert.Contains(expected.ToString("yyyy-MM-dd"), res.Description);

        var daysRes = QuickCalculatorService.TryEvaluate("today + 14 days");
        Assert.NotNull(daysRes);
        var expectedDate = DateTime.Today.AddDays(14);
        Assert.Contains(expectedDate.ToString("yyyy-MM-dd"), daysRes.Description);
    }

    [Theory]
    [InlineData("days to Dec 25")]
    [InlineData("days until Dec 25")]
    [InlineData("days since Dec 25")]
    [InlineData("days from Dec 25")]
    public void QuickCalculator_CountdownAndCountup_EvaluatesAccurately(string query)
    {
        var res = QuickCalculatorService.TryEvaluate(query);
        Assert.NotNull(res);
        Assert.Contains("day", res.FormattedResult);
        Assert.NotNull(res.Alternatives);
        Assert.True(res.Alternatives.Count > 10);

        // Verify common units are present (Days is common, Business Days is not)
        Assert.Contains(res.Alternatives, a => a.Label == "Days" && a.IsCommon);
        Assert.Contains(res.Alternatives, a => a.Label == "Hours" && a.IsCommon);
        Assert.Contains(res.Alternatives, a => a.Label == "Business Days" && !a.IsCommon);

        // Verify physics, space, and pop culture units with descriptions
        Assert.Contains(res.Alternatives, a => a.Label == "Speed of Light Distance" && a.Category == "Physics");
        Assert.Contains(res.Alternatives, a => a.Label == "ISS Earth Orbits" && a.Category == "Space");
        Assert.Contains(res.Alternatives, a => a.Label == "Cisco Hold Music" && a.Category == "PopCulture");

        // Ensure every alternative measurement has concept title, conversational sentence, and description
        Assert.All(res.Alternatives, a => Assert.False(string.IsNullOrWhiteSpace(a.Description)));
        Assert.All(res.Alternatives, a => Assert.False(string.IsNullOrWhiteSpace(a.ConceptTitle)));
        Assert.All(res.Alternatives, a => Assert.False(string.IsNullOrWhiteSpace(a.ConversationalSentence)));
        Assert.NotNull(res.HierarchicalBreakdown);
        Assert.NotNull(res.CumulativeBreakdown);
    }

    [Theory]
    [InlineData("minutes to Dec 25", "minute")]
    [InlineData("minute to Dec 25", "minute")]
    [InlineData("seconds until Dec 25", "second")]
    [InlineData("second until Dec 25", "second")]
    [InlineData("hours to Dec 25", "hour")]
    [InlineData("hour to Dec 25", "hour")]
    [InlineData("weeks to Dec 25", "week")]
    [InlineData("week to Dec 25", "week")]
    [InlineData("days to Dec 25", "day")]
    [InlineData("day to Dec 25", "day")]
    [InlineData("business day to Dec 25", "business day")]
    [InlineData("business days to Dec 25", "business day")]
    [InlineData("minutes since Dec 25", "minute")]
    [InlineData("minute since Dec 25", "minute")]
    [InlineData("seconds since Dec 25", "second")]
    [InlineData("second since Dec 25", "second")]
    public void QuickCalculator_MinutesSecondsHoursWeeks_CountdownAndCountup(string query, string expectedUnitSubstring)
    {
        var res = QuickCalculatorService.TryEvaluate(query);
        Assert.NotNull(res);
        Assert.Contains(expectedUnitSubstring, res.FormattedResult, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(res.Alternatives);
        Assert.True(res.Alternatives.Count > 10);
        Assert.NotNull(res.HierarchicalBreakdown);
        Assert.NotNull(res.CumulativeBreakdown);
    }


    [Fact]
    public void QuickCalculator_BetweenCalculations_HandleStandardAndOvernightRanges()
    {
        var dayRes = QuickCalculatorService.TryEvaluate("hours between 9am and 5pm");
        Assert.NotNull(dayRes);
        Assert.Equal("8 hours", dayRes.FormattedResult);

        var overnightRes = QuickCalculatorService.TryEvaluate("hours between 10pm and 6am");
        Assert.NotNull(overnightRes);
        Assert.Equal("8 hours", overnightRes.FormattedResult);

        var minsRes = QuickCalculatorService.TryEvaluate("minutes between 1:00pm and 1:45pm");
        Assert.NotNull(minsRes);
        Assert.Equal("45 minutes", minsRes.FormattedResult);
    }

    [Fact]
    public void QuickCalculator_OrdinalDayOfYearOrMonth_EvaluatesAccurately()
    {
        // 25th day of 2020 should be January 25, 2020
        var dayOfYearRes = QuickCalculatorService.TryEvaluate("25th day of 2020");
        Assert.NotNull(dayOfYearRes);
        Assert.Contains("January 25, 2020", dayOfYearRes.FormattedResult);
        Assert.Contains("2020-01-25", dayOfYearRes.Description);

        // 9th day of next June should have month June and day 9
        var dayOfMonthRes = QuickCalculatorService.TryEvaluate("9th day of next June");
        Assert.NotNull(dayOfMonthRes);
        Assert.Contains("June 9", dayOfMonthRes.FormattedResult);
        Assert.Contains("-06-09", dayOfMonthRes.Description);
    }

    [Fact]
    public void QuickCalculator_OrdinalWeekdayInMonth_EvaluatesAccurately()
    {
        var res = QuickCalculatorService.TryEvaluate("second friday next June");
        Assert.NotNull(res);
        Assert.Contains("Friday", res.FormattedResult);
        Assert.Contains("June", res.FormattedResult);
        Assert.Contains("-06-", res.Description);

        var lastFridayRes = QuickCalculatorService.TryEvaluate("last friday of this month");
        Assert.NotNull(lastFridayRes);
        Assert.Contains("Friday", lastFridayRes.FormattedResult);
    }

    [Fact]
    public void QuickCalculator_RelativeNthWeekday_EvaluatesAccurately()
    {
        var futureRes = QuickCalculatorService.TryEvaluate("3rd friday from now");
        Assert.NotNull(futureRes);
        Assert.Contains("Friday", futureRes.FormattedResult);

        // Plural weekday: "2 fridays from now"
        var pluralWkday = QuickCalculatorService.TryEvaluate("2 fridays from now");
        Assert.NotNull(pluralWkday);
        Assert.Contains("Friday", pluralWkday.FormattedResult);

        // Cardinal word: "two fridays from now"
        var wordWkday = QuickCalculatorService.TryEvaluate("two fridays from now");
        Assert.NotNull(wordWkday);
        Assert.Contains("Friday", wordWkday.FormattedResult);

        // Past with cardinal / plural
        var pastRes = QuickCalculatorService.TryEvaluate("2 mondays in the past");
        Assert.NotNull(pastRes);
        Assert.Contains("Monday", pastRes.FormattedResult);
    }

    [Fact]
    public void QuickCalculator_NaturalOffsets_SupportCardinalWordsAndUnits()
    {
        var daysRes = QuickCalculatorService.TryEvaluate("two days from now");
        Assert.NotNull(daysRes);

        var weeksRes = QuickCalculatorService.TryEvaluate("three weeks ago");
        Assert.NotNull(weeksRes);
    }

    [Fact]
    public void QuickCalculator_PointInTime_OptionA_IncludesDateFormatAlternativesAndNaturalFraming()
    {
        var res = QuickCalculatorService.TryEvaluate("next friday");
        Assert.NotNull(res);
        Assert.Contains("Friday", res.FormattedResult);
        Assert.NotNull(res.Alternatives);

        // Option A: Check for "Date Formats" category
        var dateFormats = res.Alternatives.Where(a => a.Category == "Date Formats").ToList();
        Assert.NotEmpty(dateFormats);
        Assert.Contains(dateFormats, a => a.Label == "ISO 8601");
        Assert.Contains(dateFormats, a => a.Label == "Short Date");
        Assert.Contains(dateFormats, a => a.Label == "Full Date");
        Assert.Contains(dateFormats, a => a.Label == "Unix Timestamp");
        Assert.Contains(dateFormats, a => a.Label == "Day of Year");

        // Natural conversational sentence starts with "From now until Next Friday"
        var daysCard = res.Alternatives.FirstOrDefault(a => a.Label == "Days");
        Assert.NotNull(daysCard);
        Assert.NotNull(daysCard.ConversationalSentence);
        Assert.StartsWith("From now until Next Friday", daysCard.ConversationalSentence, StringComparison.OrdinalIgnoreCase);

        // Check past query framing
        var pastRes = QuickCalculatorService.TryEvaluate("last friday");
        Assert.NotNull(pastRes);
        var pastDaysCard = pastRes.Alternatives?.FirstOrDefault(a => a.Label == "Days");
        Assert.NotNull(pastDaysCard?.ConversationalSentence);
        Assert.StartsWith("Since Last Friday", pastDaysCard.ConversationalSentence, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("next friday")]
    [InlineData("last friday")]
    [InlineData("this tuesday")]
    public void QuickCalculator_RelativeWeekday_EvaluatesAccurately(string query)
    {
        var res = QuickCalculatorService.TryEvaluate(query);
        Assert.NotNull(res);
        Assert.NotEmpty(res.FormattedResult);
    }

    [Theory]
    [InlineData("Valentine's Day")]
    [InlineData("Boxing Day")]
    [InlineData("Easter")]
    [InlineData("Thanksgiving")]
    public void QuickCalculator_Holidays_EvaluateAccurately(string holiday)
    {
        var res = QuickCalculatorService.TryEvaluate(holiday);
        Assert.NotNull(res);
        Assert.NotEmpty(res.FormattedResult);
        Assert.Contains(holiday, res.Description, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(res.Alternatives);
        Assert.True(res.Alternatives.Count > 10);
    }

    [Theory]
    [InlineData("sin(90)", "1")]
    [InlineData("cos(0)", "1")]
    [InlineData("sqrt(144)", "12")]
    [InlineData("cbrt(27)", "3")]
    [InlineData("log(100)", "2")]
    [InlineData("255 to hex", "0xFF")]
    [InlineData("0xFF to dec", "255")]
    [InlineData("42 to bin", "0b00101010")]
    public void QuickCalculator_ScientificAndProgrammerFunctions_WorkAccurately(string input, string expectedResult)
    {
        var res = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(res);
        Assert.Equal(expectedResult, res.FormattedResult);
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
