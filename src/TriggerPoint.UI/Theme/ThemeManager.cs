using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TriggerPoint.Core.Models;

namespace TriggerPoint.UI.Theme;

public enum AppTheme
{
    Dark,
    Light
}

public static class ThemeManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public static ThemePreference CurrentPreference { get; private set; } = ThemePreference.System;

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static void Initialize(ThemePreference preference = ThemePreference.System)
    {
        CurrentPreference = preference;
        ApplyPreference(preference);

        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (CurrentPreference == ThemePreference.System && 
                (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color))
            {
                var newTheme = DetectSystemTheme();
                if (newTheme != CurrentTheme)
                {
                    CurrentTheme = newTheme;
                    ApplyTheme(newTheme);
                    ThemeChanged?.Invoke(null, newTheme);
                }
            }
        };
    }

    public static void ApplyPreference(ThemePreference preference)
    {
        CurrentPreference = preference;
        var themeToApply = preference switch
        {
            ThemePreference.Dark => AppTheme.Dark,
            ThemePreference.Light => AppTheme.Light,
            _ => DetectSystemTheme()
        };

        CurrentTheme = themeToApply;
        ApplyTheme(themeToApply);
        ThemeChanged?.Invoke(null, themeToApply);
    }

    public static AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(RegistryValueName) is int value)
            {
                return value == 1 ? AppTheme.Light : AppTheme.Dark;
            }
        }
        catch { }

        return AppTheme.Dark;
    }

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        var res = Application.Current.Resources;

        if (theme == AppTheme.Dark)
        {
            res["BgPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(26, 27, 34));       // #1A1B22
            res["BgSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(34, 35, 45));     // #22232D
            res["BgTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(43, 44, 56));      // #2B2C38
            res["BgInputBrush"] = new SolidColorBrush(Color.FromRgb(20, 21, 26));         // #14151A
            res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(55, 56, 70));          // #373846
            res["BorderSubtleBrush"] = new SolidColorBrush(Color.FromRgb(40, 41, 52));    // #282934
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(242, 243, 245));  // #F2F3F5
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(160, 163, 175));// #A0A3AF
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(115, 118, 130));    // #737682
            res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(99, 102, 241));        // #6366F1 Indigo
            res["AccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(129, 132, 245));   // #8184F5
            res["AccentSubtleBrush"] = new SolidColorBrush(Color.FromArgb(40, 99, 102, 241));
            res["ItemHoverBrush"] = new SolidColorBrush(Color.FromRgb(42, 43, 55));
            res["ItemSelectedBrush"] = new SolidColorBrush(Color.FromRgb(51, 53, 68));
            res["WarningBrush"] = new SolidColorBrush(Color.FromRgb(245, 158, 11));       // Amber
            res["WarningSubtleBrush"] = new SolidColorBrush(Color.FromArgb(45, 245, 158, 11));
            res["ErrorBrush"] = new SolidColorBrush(Color.FromRgb(239, 68, 68));          // Red
            res["SuccessBrush"] = new SolidColorBrush(Color.FromRgb(16, 185, 129));       // Emerald
        }
        else
        {
            res["BgPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(248, 249, 250));    // #F8F9FA
            res["BgSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));  // #FFFFFF
            res["BgTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(241, 243, 245));   // #F1F3F5
            res["BgInputBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));      // #FFFFFF
            res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));       // #E2E8F0
            res["BorderSubtleBrush"] = new SolidColorBrush(Color.FromRgb(238, 242, 246)); // #EEF2F6
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));     // #0F172A
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(71, 85, 105));  // #475569
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(148, 163, 184));    // #94A3B8
            res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(79, 70, 229));         // #4F46E5
            res["AccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(99, 102, 241));   // #6366F1
            res["AccentSubtleBrush"] = new SolidColorBrush(Color.FromArgb(30, 79, 70, 229));
            res["ItemHoverBrush"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            res["ItemSelectedBrush"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            res["WarningBrush"] = new SolidColorBrush(Color.FromRgb(217, 119, 6));        // Amber dark
            res["WarningSubtleBrush"] = new SolidColorBrush(Color.FromArgb(35, 217, 119, 6));
            res["ErrorBrush"] = new SolidColorBrush(Color.FromRgb(220, 38, 38));          // Red
            res["SuccessBrush"] = new SolidColorBrush(Color.FromRgb(5, 150, 105));        // Emerald
        }
    }
}
