using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Services;
using TriggerPoint.Core.Units;
using Xunit;

namespace TriggerPoint.Tests;

public class IncompatibleUnitConversionTests
{
    [Theory]
    // First-class fun units queries
    [InlineData("1 inch in bananas", "0.1429 🍌")]
    [InlineData("24 bananas in feet", "14 ft")]
    [InlineData("1 smoot in feet", "5.5833 ft")]
    [InlineData("100 m in smoots", "58.7613 smoot")]
    [InlineData("100 kg in cats", "22.2222 cats")]
    [InlineData("10 cats in kg", "45 kg")]
    [InlineData("1 gb in floppies", "728.1778 floppies")]
    [InlineData("1 bathtub in liters", "150 L")]
    [InlineData("1 fortnight in days", "14 d")]
    public void FunUnit_DirectConversions_WorkAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    [InlineData("24 inches in litres", "Length", "Volume")]
    [InlineData("10 lbs in meters", "Mass", "Length")]
    [InlineData("50 celsius to gallons", "Temperature", "Volume")]
    [InlineData("24 bananas in litres", "Length", "Volume")]
    [InlineData("how many litres in 24 inches", "Length", "Volume")]
    [InlineData("100 km to hours", "Length", "Time")]
    [InlineData("1 gb to kg", "DigitalStorage", "Mass")]
    public void IncompatibleConversion_ReturnsCleverMessageAndHeadline(string input, string fromCat, string toCat)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal($"Cannot convert {fromCat} to {toCat}", result.FormattedResult);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.NotNull(result.Alternatives);
        Assert.NotEmpty(result.Alternatives);
    }

    [Fact]
    public void IncompatibleConversion_DimensionalBridge_ProvidesHelpfulSuggestions()
    {
        // Length -> Volume provides cubic volume bridge
        var lengthToVol = QuickCalculatorService.TryEvaluate("24 inches in litres");
        Assert.NotNull(lengthToVol);
        var bridge = lengthToVol.Alternatives?.FirstOrDefault(a => a.Category == "Suggestion");
        Assert.NotNull(bridge);
        Assert.Contains("Cubic", bridge.Label);
        Assert.Contains("0.3933 L", bridge.FormattedValue);
        Assert.Contains("Tip: Assuming", lengthToVol.Description);

        // Mass -> Volume provides water density bridge
        var massToVol = QuickCalculatorService.TryEvaluate("10 kg in litres");
        Assert.NotNull(massToVol);
        var waterBridge = massToVol.Alternatives?.FirstOrDefault(a => a.Category == "Suggestion");
        Assert.NotNull(waterBridge);
        Assert.Contains("Water Volume", waterBridge.Label);
        Assert.Contains("10 L", waterBridge.FormattedValue);

        // Volume -> Mass provides water mass bridge
        var volToMass = QuickCalculatorService.TryEvaluate("500 ml in grams");
        Assert.NotNull(volToMass);
        var massBridge = volToMass.Alternatives?.FirstOrDefault(a => a.Category == "Suggestion");
        Assert.NotNull(massBridge);
        Assert.Contains("Water Mass", massBridge.Label);
        Assert.Contains("500 g", massBridge.FormattedValue);
    }

    [Fact]
    public void IncompatibleConversion_RandomizedMessages_SelectsFromCategoryPool()
    {
        var distinctMessages = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            string msg = IncompatibleUnitMessageRegistry.GetRandomMessage(UnitCategory.Length, UnitCategory.Volume);
            distinctMessages.Add(msg);
        }

        // Should have hit multiple different witty responses from the category pool
        Assert.True(distinctMessages.Count > 1, "Expected multiple random variations from the witty message pool.");
    }
}
