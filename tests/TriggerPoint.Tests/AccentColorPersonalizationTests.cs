using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Theme;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class AccentColorPersonalizationTests
{
    private static void EnsureApplicationAndThemeResources()
    {
        if (Application.Current == null)
        {
            new Application();
        }

        if (!Application.Current!.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString?.Contains("ThemeResources.xaml") == true))
        {
            Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/TriggerPoint;component/Theme/ThemeResources.xaml", UriKind.Absolute)
            });
        }
    }

    [Theory]
    [InlineData(AccentColorChoice.Indigo, AppTheme.Dark, 99, 102, 241)]
    [InlineData(AccentColorChoice.Indigo, AppTheme.Light, 79, 70, 229)]
    [InlineData(AccentColorChoice.ElectricViolet, AppTheme.Dark, 167, 139, 250)]
    [InlineData(AccentColorChoice.ElectricViolet, AppTheme.Light, 124, 58, 237)]
    [InlineData(AccentColorChoice.CyberBlue, AppTheme.Dark, 56, 189, 248)]
    [InlineData(AccentColorChoice.CyberBlue, AppTheme.Light, 2, 132, 199)]
    [InlineData(AccentColorChoice.EmeraldGreen, AppTheme.Dark, 52, 211, 153)]
    [InlineData(AccentColorChoice.EmeraldGreen, AppTheme.Light, 5, 150, 105)]
    [InlineData(AccentColorChoice.SunsetOrange, AppTheme.Dark, 251, 146, 60)]
    [InlineData(AccentColorChoice.SunsetOrange, AppTheme.Light, 234, 88, 12)]
    [InlineData(AccentColorChoice.RoseCrimson, AppTheme.Dark, 251, 113, 133)]
    [InlineData(AccentColorChoice.RoseCrimson, AppTheme.Light, 225, 29, 72)]
    public void GetAccentColors_ReturnsExpectedBaseColors(AccentColorChoice choice, AppTheme theme, byte expectedR, byte expectedG, byte expectedB)
    {
        var (baseColor, hoverColor, subtleColor) = ThemeManager.GetAccentColors(choice, theme);

        Assert.Equal(expectedR, baseColor.R);
        Assert.Equal(expectedG, baseColor.G);
        Assert.Equal(expectedB, baseColor.B);

        // Subtle alpha check
        if (theme == AppTheme.Dark)
        {
            Assert.Equal(40, subtleColor.A);
        }
        else
        {
            Assert.Equal(30, subtleColor.A);
        }
    }

    [Fact]
    public void GetAccentColors_WindowsSystem_ResolvesValidColors()
    {
        var (darkBase, darkHover, darkSubtle) = ThemeManager.GetAccentColors(AccentColorChoice.WindowsSystem, AppTheme.Dark);
        var (lightBase, lightHover, lightSubtle) = ThemeManager.GetAccentColors(AccentColorChoice.WindowsSystem, AppTheme.Light);

        Assert.True(darkBase.A == 255);
        Assert.True(lightBase.A == 255);
        Assert.Equal(40, darkSubtle.A);
        Assert.Equal(30, lightSubtle.A);
    }

    [Fact]
    public void AppSettings_SerializesAndDeserializesAccentColor()
    {
        var settings = new AppSettings
        {
            AccentColor = AccentColorChoice.SunsetOrange
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(AccentColorChoice.SunsetOrange, restored.AccentColor);
    }

    [Fact]
    public void AppSettings_SerializesAndDeserializesCustomAccentColor()
    {
        var settings = new AppSettings
        {
            AccentColor = AccentColorChoice.Custom,
            CustomAccentHex = "#D946EF"
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(AccentColorChoice.Custom, restored.AccentColor);
        Assert.Equal("#D946EF", restored.CustomAccentHex);
    }

    [Fact]
    public void GetAccentColors_Custom_ParsesValidHex()
    {
        // Teal #14B8A6: R=20, G=184, B=166
        var (darkBase, darkHover, darkSubtle) = ThemeManager.GetAccentColors(AccentColorChoice.Custom, AppTheme.Dark, "#14B8A6");
        var (lightBase, lightHover, lightSubtle) = ThemeManager.GetAccentColors(AccentColorChoice.Custom, AppTheme.Light, "#14B8A6");

        Assert.Equal(20, darkBase.R);
        Assert.Equal(184, darkBase.G);
        Assert.Equal(166, darkBase.B);
        Assert.Equal(40, darkSubtle.A);

        Assert.Equal(20, lightBase.R);
        Assert.Equal(184, lightBase.G);
        Assert.Equal(166, lightBase.B);
        Assert.Equal(30, lightSubtle.A);
    }

    [Fact]
    public void GetAccentColors_Custom_InvalidHex_FallsBackGracefully()
    {
        var (fallbackBase, _, _) = ThemeManager.GetAccentColors(AccentColorChoice.Custom, AppTheme.Dark, "invalid_color");
        // Fallback is Indigo #6366F1: 99, 102, 241
        Assert.Equal(99, fallbackBase.R);
        Assert.Equal(102, fallbackBase.G);
        Assert.Equal(241, fallbackBase.B);
    }

    [Fact]
    public void ApplyAccentColor_UpdatesDynamicResources()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                ThemeManager.ApplyPreference(ThemePreference.Dark);
                ThemeManager.ApplyAccentColor(AccentColorChoice.CyberBlue);

                var accentBrush = Application.Current.FindResource("AccentBrush") as SolidColorBrush;
                Assert.NotNull(accentBrush);
                Assert.Equal(56, accentBrush.Color.R);
                Assert.Equal(189, accentBrush.Color.G);
                Assert.Equal(248, accentBrush.Color.B);

                // Switch to Electric Violet
                ThemeManager.ApplyAccentColor(AccentColorChoice.ElectricViolet);
                accentBrush = Application.Current.FindResource("AccentBrush") as SolidColorBrush;
                Assert.NotNull(accentBrush);
                Assert.Equal(167, accentBrush.Color.R);
                Assert.Equal(139, accentBrush.Color.G);
                Assert.Equal(250, accentBrush.Color.B);

                // Switch to Custom (#14B8A6: 20, 184, 166)
                ThemeManager.ApplyAccentColor(AccentColorChoice.Custom, "#14B8A6");
                accentBrush = Application.Current.FindResource("AccentBrush") as SolidColorBrush;
                Assert.NotNull(accentBrush);
                Assert.Equal(20, accentBrush.Color.R);
                Assert.Equal(184, accentBrush.Color.G);
                Assert.Equal(166, accentBrush.Color.B);

                // Revert to Indigo default
                ThemeManager.ApplyAccentColor(AccentColorChoice.Indigo);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caughtEx != null)
        {
            throw caughtEx;
        }
    }

    [Theory]
    [InlineData("accent", true)]
    [InlineData("color", true)]
    [InlineData("swatch", true)]
    [InlineData("indigo", true)]
    [InlineData("violet", true)]
    [InlineData("xyznotfound", false)]
    public void ApplicationSettings_AppearanceCategory_MatchesAccentKeywords(string query, bool expectedMatch)
    {
        bool matched = ApplicationSettingsWindow.MatchesCategory(ApplicationSettingsWindow.SettingsCategory.Appearance, query);
        Assert.Equal(expectedMatch, matched);
    }
}
