using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TriggerPoint.UI.Theme;
using Xunit;

namespace TriggerPoint.Tests;

public class SettingsIconAndThemeTests
{
    [Fact]
    public void SettingsGearGeometry_ExistsInThemeResources_AndIsValid()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
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

                var gearGeometry = Application.Current.FindResource("SettingsGearGeometry") as Geometry;
                Assert.NotNull(gearGeometry);
                Assert.False(gearGeometry.Bounds.IsEmpty);
                Assert.True(gearGeometry.Bounds.Width > 0);
                Assert.True(gearGeometry.Bounds.Height > 0);
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
            throw new InvalidOperationException($"SettingsGearGeometry validation failed: {caughtEx.Message}", caughtEx);
        }
    }

    [Fact]
    public void ThemeManager_ApplyTheme_SetsMultiFrameBitmapFrame()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    new Application();
                }

                // Dark Theme Test
                ThemeManager.ApplyTheme(AppTheme.Dark);
                var darkIcon = Application.Current!.Resources["AppIconSource"] as BitmapFrame;
                Assert.NotNull(darkIcon);

                // Light Theme Test
                ThemeManager.ApplyTheme(AppTheme.Light);
                var lightIcon = Application.Current!.Resources["AppIconSource"] as BitmapFrame;
                Assert.NotNull(lightIcon);

                // ApplyWindowIcons on a dummy Window
                var testWindow = new Window();
                ThemeManager.ApplyWindowIcons(testWindow);
                Assert.NotNull(testWindow.Icon);
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
            throw new InvalidOperationException($"ThemeManager icon test failed: {caughtEx.Message}", caughtEx);
        }
    }
}
