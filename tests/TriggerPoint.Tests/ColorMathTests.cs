using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Media;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Theme;
using Xunit;

namespace TriggerPoint.Tests;

public class ColorMathTests
{
    [Theory]
    [InlineData(255, 0, 0, 0.0, 1.0, 0.5)]       // Pure Red
    [InlineData(0, 255, 0, 120.0, 1.0, 0.5)]     // Pure Green
    [InlineData(0, 0, 255, 240.0, 1.0, 0.5)]     // Pure Blue
    [InlineData(255, 255, 0, 60.0, 1.0, 0.5)]     // Pure Yellow
    [InlineData(0, 255, 255, 180.0, 1.0, 0.5)]   // Pure Cyan
    [InlineData(255, 0, 255, 300.0, 1.0, 0.5)]   // Pure Magenta
    [InlineData(255, 255, 255, 0.0, 0.0, 1.0)]   // Pure White
    [InlineData(0, 0, 0, 0.0, 0.0, 0.0)]         // Pure Black
    public void RgbToHsl_CalculatesAccurateValues(byte r, byte g, byte b, double expectedH, double expectedS, double expectedL)
    {
        var (h, s, l) = ColorMath.RgbToHsl(r, g, b);

        Assert.Equal(expectedH, Math.Round(h, 1));
        Assert.Equal(expectedS, Math.Round(s, 2));
        Assert.Equal(expectedL, Math.Round(l, 2));
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.5, 255, 0, 0)]
    [InlineData(120.0, 1.0, 0.5, 0, 255, 0)]
    [InlineData(240.0, 1.0, 0.5, 0, 0, 255)]
    [InlineData(60.0, 1.0, 0.5, 255, 255, 0)]
    [InlineData(180.0, 1.0, 0.5, 0, 255, 255)]
    [InlineData(300.0, 1.0, 0.5, 255, 0, 255)]
    [InlineData(0.0, 0.0, 1.0, 255, 255, 255)]
    [InlineData(0.0, 0.0, 0.0, 0, 0, 0)]
    public void HslToRgb_CalculatesAccurateValues(double h, double s, double l, byte expectedR, byte expectedG, byte expectedB)
    {
        var (r, g, b) = ColorMath.HslToRgb(h, s, l);

        Assert.Equal(expectedR, r);
        Assert.Equal(expectedG, g);
        Assert.Equal(expectedB, b);
    }

    [Fact]
    public void RgbToHsl_And_HslToRgb_RoundTripsConsistently()
    {
        // Test 10 distinct colors across the spectrum
        var testColors = new (byte R, byte G, byte B)[]
        {
            (139, 92, 246),  // Violet
            (20, 184, 166),  // Teal
            (217, 70, 239),  // Fuchsia
            (132, 204, 22),  // Lime
            (245, 158, 11),  // Amber
            (244, 63, 94),   // Ruby
            (99, 102, 241),  // Indigo
            (56, 189, 248),  // Sky Blue
            (52, 211, 153),  // Emerald
            (251, 146, 60)   // Sunset Orange
        };

        foreach (var (r, g, b) in testColors)
        {
            var (h, s, l) = ColorMath.RgbToHsl(r, g, b);
            var (reconstructedR, reconstructedG, reconstructedB) = ColorMath.HslToRgb(h, s, l);

            // Allow maximum rounding difference of 1 due to 8-bit integer quantization
            Assert.True(Math.Abs(r - reconstructedR) <= 1, $"R mismatch for ({r},{g},{b}): got {reconstructedR}");
            Assert.True(Math.Abs(g - reconstructedG) <= 1, $"G mismatch for ({r},{g},{b}): got {reconstructedG}");
            Assert.True(Math.Abs(b - reconstructedB) <= 1, $"B mismatch for ({r},{g},{b}): got {reconstructedB}");
        }
    }

    [Fact]
    public void ParseHex_And_ToHex_WorkCorrectly()
    {
        var fallback = Color.FromRgb(10, 20, 30);

        // Standard with #
        var c1 = ColorMath.ParseHex("#14B8A6", fallback);
        Assert.Equal(20, c1.R);
        Assert.Equal(184, c1.G);
        Assert.Equal(166, c1.B);
        Assert.Equal("#14B8A6", ColorMath.ToHex(c1));

        // Without #
        var c2 = ColorMath.ParseHex("8B5CF6", fallback);
        Assert.Equal(139, c2.R);
        Assert.Equal(92, c2.G);
        Assert.Equal(246, c2.B);

        // Invalid hex
        var c3 = ColorMath.ParseHex("invalid", fallback);
        Assert.Equal(fallback, c3);
    }

    [Fact]
    public void AppSettings_CustomPaletteColors_SerializesAndDeserializes()
    {
        var customPalette = new List<string>
        {
            "#FF0000", "#00FF00", "#0000FF", "#FFFF00"
        };

        var settings = new AppSettings
        {
            CustomPaletteColors = customPalette
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(4, restored.CustomPaletteColors.Count);
        Assert.Equal("#FF0000", restored.CustomPaletteColors[0]);
        Assert.Equal("#FFFF00", restored.CustomPaletteColors[3]);
    }
}
