using System;
using System.Linq;
using TriggerPoint.Core.Services;
using TriggerPoint.Core.Units;
using Xunit;

namespace TriggerPoint.Tests;

public class UnitConversionTests
{
    [Theory]
    // Length & Distance
    [InlineData("10 in to mm", "254 mm")]
    [InlineData("10 inches to mm", "254 mm")]
    [InlineData("1 inch to mm", "25.4 mm")]
    [InlineData("inches to mm", "25.4 mm")]
    [InlineData("12.7 mm to in", "1/2 in")]
    [InlineData("38.1 mm to in", "1 1/2 in")]
    [InlineData("12.7 mm to in as decimal", "0.5 in")]
    [InlineData("12.7 mm to in as fraction", "1/2 in")]
    [InlineData("1/2 in to mm", "12.7 mm")]
    [InlineData("1 1/2 in to mm", "38.1 mm")]
    [InlineData("10\" to mm", "254 mm")]
    [InlineData("6' to cm", "182.88 cm")]
    [InlineData("100 m to ft", "328.084 ft")]
    [InlineData("3 yd to ft", "9 ft")]
    [InlineData("5 mi to km", "8.0467 km")]
    [InlineData("1000 um to mm", "1 mm")]
    [InlineData("50 mil to mm", "1.27 mm")]
    public void UnitConversion_Length_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Area
    [InlineData("100 sqft to sqm", "9.2903 m²")]
    [InlineData("1 acre to sqft", "43560 ft²")]
    [InlineData("1 ha to acres", "2.4711 ac")]
    [InlineData("1 sqm to sqcm", "10000 cm²")]
    [InlineData("10 sq in to sq cm", "64.516 cm²")]
    public void UnitConversion_Area_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Volume & Liquid Capacity
    [InlineData("1 gal to l", "3.7854 L")]
    [InlineData("2 cups to fl oz", "16 fl oz")]
    [InlineData("1 liter to ml", "1000 ml")]
    [InlineData("3 tbsp to tsp", "9 tsp")]
    [InlineData("350 ml to cups as fraction", "1 1/2 cups")]
    [InlineData("1/2 cup to ml", "118.2941 ml")]
    public void UnitConversion_Volume_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Mass & Weight
    [InlineData("100 g to oz", "3.5274 oz")]
    [InlineData("16 oz to lbs", "1 lbs")]
    [InlineData("1 stone to lbs", "14 lbs")]
    [InlineData("1 metric ton to kg", "1000 kg")]
    [InlineData("500 mg to g", "0.5 g")]
    [InlineData("1/2 lb to oz", "8 oz")]
    public void UnitConversion_Mass_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Temperature (including negative and affine transforms)
    [InlineData("100 c to f", "212°F")]
    [InlineData("32 f to c", "0°C")]
    [InlineData("-40 c to f", "-40°F")]
    [InlineData("-40 f to c", "-40°C")]
    [InlineData("0 c to k", "273.15 K")]
    [InlineData("100 celsius to fahrenheit", "212°F")]
    [InlineData("98.6 fahrenheit to celsius", "37°C")]
    public void UnitConversion_Temperature_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Speed & Velocity
    [InlineData("100 km/h to mph", "62.1371 mph")]
    [InlineData("60 mph to km/h", "96.5606 km/h")]
    [InlineData("10 m/s to km/h", "36 km/h")]
    [InlineData("20 knots to mph", "23.0156 mph")]
    [InlineData("1 mach to m/s", "340.29 m/s")]
    public void UnitConversion_Speed_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Pressure
    [InlineData("1 bar to psi", "14.5038 psi")]
    [InlineData("14.7 psi to bar", "1.0135 bar")]
    [InlineData("1 atm to kpa", "101.325 kPa")]
    [InlineData("760 torr to atm", "1 atm")]
    public void UnitConversion_Pressure_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Energy & Power
    [InlineData("1 kwh to j", "3600000 J")]
    [InlineData("1 cal to j", "4.184 J")]
    [InlineData("1000 btu to kwh", "0.2931 kWh")]
    [InlineData("1 hp to w", "745.6999 W")]
    [InlineData("10 kw to hp", "13.4102 hp")]
    public void UnitConversion_EnergyAndPower_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Time & Angle
    [InlineData("120 min to h", "2 h")]
    [InlineData("2 days to hours", "48 h")]
    [InlineData("180 deg to rad", "3.1416 rad")]
    [InlineData("1 turn to deg", "360°")]
    public void UnitConversion_TimeAndAngle_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Typography & UI
    [InlineData("16px to rem", "1rem")]
    [InlineData("2rem to px", "32px")]
    [InlineData("72pt to px", "96px")]
    [InlineData("1pc to pt", "12pt")]
    public void UnitConversion_Typography_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Theory]
    // Data Transfer Rates
    [InlineData("100 mbps to mb/s", "12.5 MB/s")]
    [InlineData("1 gbps to mbps", "1000 Mbps")]
    [InlineData("8 mbps to B/s", "1000000 B/s")]
    public void UnitConversion_DataTransfer_WorksAccurately(string input, string expectedResult)
    {
        var result = QuickCalculatorService.TryEvaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result.FormattedResult);
    }

    [Fact]
    public void UnitConversion_ConversationalQueries_WorkAccurately()
    {
        var result1 = QuickCalculatorService.TryEvaluate("how many mm in 10 inches");
        Assert.NotNull(result1);
        Assert.Equal("254 mm", result1.FormattedResult);

        var result2 = QuickCalculatorService.TryEvaluate("how many cups in 2 gallons");
        Assert.NotNull(result2);
        Assert.Equal("32 cups", result2.FormattedResult);

        var result3 = QuickCalculatorService.TryEvaluate("convert 5 km to miles");
        Assert.NotNull(result3);
        Assert.Equal("3.1069 mi", result3.FormattedResult);
    }

    [Fact]
    public void UnitConversion_PopulatesRichAlternatives()
    {
        var result = QuickCalculatorService.TryEvaluate("10 inches to mm");
        Assert.NotNull(result);
        Assert.NotNull(result.Alternatives);
        Assert.NotEmpty(result.Alternatives);

        // Verify common alternatives exist for Length
        var labels = result.Alternatives.Select(a => a.Label).ToList();
        Assert.Contains("centimeters", labels);
        Assert.Contains("meters", labels);
        Assert.Contains("feet", labels);

        // Verify fractional precision alternatives exist for Length
        Assert.Contains(result.Alternatives, a => a.Label == "Nearest 1/16\"");

        // Verify fun comparisons exist for Length
        Assert.Contains(result.Alternatives, a => a.Label == "Bananas (for scale)" && a.Category == "PopCulture");
        Assert.Contains(result.Alternatives, a => a.Label == "Smoots" && a.Category == "Historical");
    }

    [Fact]
    public void UnitConversion_ExactFraction_IncludesDecimalInAlternatives()
    {
        var result = QuickCalculatorService.TryEvaluate("12.7 mm to in");
        Assert.NotNull(result);
        Assert.Equal("1/2 in", result.FormattedResult);
        Assert.NotNull(result.Alternatives);

        // Should offer decimal alternative
        var decimalAlt = result.Alternatives.FirstOrDefault(a => a.Label.Contains("decimal"));
        Assert.NotNull(decimalAlt);
        Assert.Equal("0.5 in", decimalAlt.FormattedValue);
    }

    [Fact]
    public void AlternativeMeasurement_DisplayCategory_FormatsProperly()
    {
        var altPop = new AlternativeMeasurement("Test", "123", "Desc", Category: "PopCulture");
        Assert.Equal("Pop Culture", altPop.DisplayCategory);

        var altCommon = new AlternativeMeasurement("Test", "123", "Desc", Category: "Common");
        Assert.Equal("Common", altCommon.DisplayCategory);

        var altFractions = new AlternativeMeasurement("Test", "123", "Desc", Category: "Fractions");
        Assert.Equal("Fractions", altFractions.DisplayCategory);
    }

    [Theory]
    [InlineData("100 kg to lbs", "Domestic House Cats", "Nature")]
    [InlineData("10 gal to L", "Standard Soda Cans", "PopCulture")]
    [InlineData("60 mph to kmh", "Usain Bolt Top Speed", "PopCulture")]
    [InlineData("100 C to F", "Absolute Zero", "Physics")]
    [InlineData("500 Wh to J", "Glazed Donuts", "PopCulture")]
    [InlineData("10 GB to MB", "3.5\" HD Floppy Disks", "Historical")]
    public void UnitConversion_IncludesFunMeasurements_AcrossCategories(string query, string expectedFunLabel, string expectedCategory)
    {
        var result = QuickCalculatorService.TryEvaluate(query);
        Assert.NotNull(result);
        Assert.NotNull(result.Alternatives);

        var funAlt = result.Alternatives.FirstOrDefault(a => a.Label.Contains(expectedFunLabel));
        Assert.NotNull(funAlt);
        Assert.Equal(expectedCategory, funAlt.Category);
    }

    [Fact]
    public void StandardMath_PopulatesProgrammerAndHistoricalAlternatives()
    {
        var result = QuickCalculatorService.TryEvaluate("250 * 40");
        Assert.NotNull(result);
        Assert.Equal("10,000", result.FormattedResult);
        Assert.NotNull(result.Alternatives);

        Assert.Contains(result.Alternatives, a => a.Category == "Programmer" && a.FormattedValue == "0x2710");
        Assert.Contains(result.Alternatives, a => a.Category == "Programmer" && a.Label == "Octal");
    }
}
