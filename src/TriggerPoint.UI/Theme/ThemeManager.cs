using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Win32;

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

    private static System.Drawing.Icon? _currentBigIcon;
    private static System.Drawing.Icon? _currentSmallIcon;

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public static ThemePreference CurrentPreference { get; private set; } = ThemePreference.System;

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static void Initialize(ThemePreference preference = ThemePreference.System)
    {
        CurrentPreference = preference;
        ApplyPreference(preference);
        ApplyTreeDensity(true);

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

    public static void ApplyTreeDensity(bool compact)
    {
        if (Application.Current?.Resources == null) return;
        var res = Application.Current.Resources;
        if (compact)
        {
            res["TreeItemPadding"] = new Thickness(4, 1, 4, 1);
            res["TreeItemMinHeight"] = 20.0;
            res["TreeItemRowMargin"] = new Thickness(0, 0, 0, 0);
        }
        else
        {
            res["TreeItemPadding"] = new Thickness(6, 4, 6, 4);
            res["TreeItemMinHeight"] = 26.0;
            res["TreeItemRowMargin"] = new Thickness(0, 1, 0, 1);
        }
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
        if (Application.Current?.Resources == null) return;
        var res = Application.Current.Resources;

        if (theme == AppTheme.Dark)
        {
            res["BgPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(22, 23, 29));       // #16171D
            res["BgSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(32, 33, 43));     // #20212B
            res["BgTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(40, 42, 54));      // #282A36
            res["CardBgBrush"] = new SolidColorBrush(Color.FromRgb(36, 38, 51));          // #242633 Elevated Card
            res["CardBorderBrush"] = new SolidColorBrush(Color.FromRgb(61, 64, 82));      // #3D4052 Crisp Border
            res["CardHeaderBgBrush"] = new SolidColorBrush(Color.FromRgb(43, 45, 60));    // #2B2D3C
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

            // Semantic Action Types
            res["FolderBrush"] = new SolidColorBrush(Color.FromRgb(245, 158, 11));       // #F59E0B Amber
            res["FolderSubtleBrush"] = new SolidColorBrush(Color.FromArgb(35, 245, 158, 11));
            res["ShellBrush"] = new SolidColorBrush(Color.FromRgb(56, 189, 248));        // #38BDF8 Sky Blue
            res["ShellSubtleBrush"] = new SolidColorBrush(Color.FromArgb(35, 56, 189, 248));
            res["SnippetBrush"] = new SolidColorBrush(Color.FromRgb(52, 211, 153));      // #34D399 Mint Emerald
            res["SnippetSubtleBrush"] = new SolidColorBrush(Color.FromArgb(35, 52, 211, 153));
            res["WorkflowBrush"] = new SolidColorBrush(Color.FromRgb(168, 85, 247));     // #A855F7 Purple
            res["WorkflowSubtleBrush"] = new SolidColorBrush(Color.FromArgb(35, 168, 85, 247));

            // Snippet Token Categories
            res["TokenDateBrush"] = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            res["TokenDateBgBrush"] = new SolidColorBrush(Color.FromArgb(28, 56, 189, 248));
            res["TokenClipBrush"] = new SolidColorBrush(Color.FromRgb(167, 139, 250));    // #A78BFA Violet
            res["TokenClipBgBrush"] = new SolidColorBrush(Color.FromArgb(28, 167, 139, 250));
            res["TokenPromptBrush"] = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            res["TokenPromptBgBrush"] = new SolidColorBrush(Color.FromArgb(28, 52, 211, 153));

            // Context Rules Tags
            res["TagAllowedBgBrush"] = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
            res["TagAllowedBorderBrush"] = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            res["TagAllowedTextBrush"] = new SolidColorBrush(Color.FromRgb(167, 243, 208));  // #A7F3D0
            res["TagExcludedBgBrush"] = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
            res["TagExcludedBorderBrush"] = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            res["TagExcludedTextBrush"] = new SolidColorBrush(Color.FromRgb(254, 202, 202)); // #FECACA

            try
            {
                res["AppIconSource"] = BitmapFrame.Create(
                    new Uri("pack://application:,,,/TriggerPoint;component/Assets/TriggerPoint.ico", UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.Default);
            }
            catch { }
        }
        else
        {
            res["BgPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));    // #F1F5F9 Slate-100 backdrop
            res["BgSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));  // #FFFFFF Pure white
            res["BgTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));   // #F8FAFC Slate-50
            res["CardBgBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));      // #FFFFFF Crisp card
            res["CardBorderBrush"] = new SolidColorBrush(Color.FromRgb(203, 213, 225));  // #CBD5E1 Slate-300
            res["CardHeaderBgBrush"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));// #F8FAFC
            res["BgInputBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));      // #FFFFFF
            res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(215, 222, 232));       // #D7DEE8 Defined border
            res["BorderSubtleBrush"] = new SolidColorBrush(Color.FromRgb(226, 232, 240)); // #E2E8F0
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));     // #0F172A
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(71, 85, 105));  // #475569
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(148, 163, 184));    // #94A3B8
            res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(79, 70, 229));         // #4F46E5
            res["AccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(99, 102, 241));   // #6366F1
            res["AccentSubtleBrush"] = new SolidColorBrush(Color.FromArgb(30, 79, 70, 229));
            res["ItemHoverBrush"] = new SolidColorBrush(Color.FromRgb(235, 240, 246));
            res["ItemSelectedBrush"] = new SolidColorBrush(Color.FromRgb(220, 227, 238));
            res["WarningBrush"] = new SolidColorBrush(Color.FromRgb(217, 119, 6));        // Amber dark
            res["WarningSubtleBrush"] = new SolidColorBrush(Color.FromArgb(35, 217, 119, 6));
            res["ErrorBrush"] = new SolidColorBrush(Color.FromRgb(220, 38, 38));          // Red
            res["SuccessBrush"] = new SolidColorBrush(Color.FromRgb(5, 150, 105));        // Emerald

            // Semantic Action Types
            res["FolderBrush"] = new SolidColorBrush(Color.FromRgb(217, 119, 6));        // #D97706 Amber Dark
            res["FolderSubtleBrush"] = new SolidColorBrush(Color.FromArgb(30, 217, 119, 6));
            res["ShellBrush"] = new SolidColorBrush(Color.FromRgb(2, 132, 199));         // #0284C7 Sky Blue Dark
            res["ShellSubtleBrush"] = new SolidColorBrush(Color.FromArgb(30, 2, 132, 199));
            res["SnippetBrush"] = new SolidColorBrush(Color.FromRgb(5, 150, 105));       // #059669 Emerald Dark
            res["SnippetSubtleBrush"] = new SolidColorBrush(Color.FromArgb(30, 5, 150, 105));
            res["WorkflowBrush"] = new SolidColorBrush(Color.FromRgb(147, 51, 234));     // #9333EA Purple Dark
            res["WorkflowSubtleBrush"] = new SolidColorBrush(Color.FromArgb(30, 147, 51, 234));

            // Snippet Token Categories
            res["TokenDateBrush"] = new SolidColorBrush(Color.FromRgb(2, 132, 199));
            res["TokenDateBgBrush"] = new SolidColorBrush(Color.FromArgb(20, 2, 132, 199));
            res["TokenClipBrush"] = new SolidColorBrush(Color.FromRgb(124, 58, 237));    // #7C3AED Violet Dark
            res["TokenClipBgBrush"] = new SolidColorBrush(Color.FromArgb(20, 124, 58, 237));
            res["TokenPromptBrush"] = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            res["TokenPromptBgBrush"] = new SolidColorBrush(Color.FromArgb(20, 5, 150, 105));

            // Context Rules Tags
            res["TagAllowedBgBrush"] = new SolidColorBrush(Color.FromArgb(28, 5, 150, 105));
            res["TagAllowedBorderBrush"] = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            res["TagAllowedTextBrush"] = new SolidColorBrush(Color.FromRgb(6, 78, 59));      // #064E3B
            res["TagExcludedBgBrush"] = new SolidColorBrush(Color.FromArgb(28, 220, 38, 38));
            res["TagExcludedBorderBrush"] = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            res["TagExcludedTextBrush"] = new SolidColorBrush(Color.FromRgb(127, 29, 29));   // #7F1D1D

            try
            {
                res["AppIconSource"] = BitmapFrame.Create(
                    new Uri("pack://application:,,,/TriggerPoint;component/Assets/TriggerPoint.Light.ico", UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.Default);
            }
            catch { }
        }

        UpdateNativeIcons(theme);

        try
        {
            if (Application.Current != null && Application.Current.Dispatcher != null && Application.Current.Dispatcher.CheckAccess())
            {
                foreach (Window win in Application.Current.Windows)
                {
                    ApplyWindowIcons(win);
                }
            }
        }
        catch { }
    }

    private static void UpdateNativeIcons(AppTheme theme)
    {
        try
        {
            var oldBig = _currentBigIcon;
            var oldSmall = _currentSmallIcon;

            string packUri = theme == AppTheme.Light
                ? "pack://application:,,,/TriggerPoint;component/Assets/TriggerPoint.Light.ico"
                : "pack://application:,,,/TriggerPoint;component/Assets/TriggerPoint.ico";

            var streamInfo = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
            if (streamInfo?.Stream != null)
            {
                using var stream = streamInfo.Stream;
                int bigCx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXICON);
                int bigCy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYICON);
                int smallCx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
                int smallCy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON);

                if (bigCx <= 0) bigCx = 32;
                if (bigCy <= 0) bigCy = 32;
                if (smallCx <= 0) smallCx = 16;
                if (smallCy <= 0) smallCy = 16;

                _currentBigIcon = new System.Drawing.Icon(stream, bigCx, bigCy);

                stream.Seek(0, System.IO.SeekOrigin.Begin);
                _currentSmallIcon = new System.Drawing.Icon(stream, smallCx, smallCy);
            }

            oldBig?.Dispose();
            oldSmall?.Dispose();
        }
        catch
        {
            // Gracefully handle if icon resources cannot be loaded
        }
    }

    public static void ApplyWindowIcons(Window window)
    {
        if (window == null) return;

        try
        {
            if (window.Dispatcher != null && !window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.BeginInvoke(new Action(() => ApplyWindowIcons(window)));
                return;
            }

            if (Application.Current?.Resources["AppIconSource"] is ImageSource iconSource)
            {
                window.Icon = iconSource;
            }

            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            if (_currentBigIcon == null || _currentSmallIcon == null)
            {
                UpdateNativeIcons(CurrentTheme);
            }

            if (_currentBigIcon != null)
            {
                NativeMethods.SendMessage(hwnd, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.ICON_BIG, _currentBigIcon.Handle);
            }

            if (_currentSmallIcon != null)
            {
                NativeMethods.SendMessage(hwnd, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.ICON_SMALL, _currentSmallIcon.Handle);
            }
        }
        catch
        {
            // Graceful fallback
        }
    }
}
