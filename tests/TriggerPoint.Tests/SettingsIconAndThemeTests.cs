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
    public void VectorGeometries_EyeAndRevert_ExistAndAreValid()
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

                var eyeVisible = Application.Current.FindResource("EyeVisibleGeometry") as Geometry;
                Assert.NotNull(eyeVisible);
                Assert.False(eyeVisible.Bounds.IsEmpty);
                Assert.True(eyeVisible.Bounds.Width > 0);

                var eyeSlash = Application.Current.FindResource("EyeSlashGeometry") as Geometry;
                Assert.NotNull(eyeSlash);
                Assert.False(eyeSlash.Bounds.IsEmpty);
                Assert.True(eyeSlash.Bounds.Width > 0);

                var revertUndo = Application.Current.FindResource("RevertUndoGeometry") as Geometry;
                Assert.NotNull(revertUndo);
                Assert.False(revertUndo.Bounds.IsEmpty);
                Assert.True(revertUndo.Bounds.Width > 0);
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
            throw new InvalidOperationException($"VectorGeometries validation failed: {caughtEx.Message}", caughtEx);
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

    [Fact]
    public void RichTextEditorGeometries_ExistInThemeResources_AndAreValid()
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

                string[] geometryKeys =
                [
                    "ClearFormatGeometry",
                    "BulletListGeometry",
                    "NumberedListGeometry",
                    "AlignLeftGeometry",
                    "AlignCenterGeometry",
                    "AlignRightGeometry",
                    "HighlighterPenGeometry"
                ];

                foreach (var key in geometryKeys)
                {
                    var geom = Application.Current.FindResource(key) as Geometry;
                    Assert.NotNull(geom);
                    Assert.False(geom.Bounds.IsEmpty, $"Geometry {key} bounds should not be empty");
                    Assert.True(geom.Bounds.Width > 0, $"Geometry {key} width should be > 0");
                    Assert.True(geom.Bounds.Height > 0, $"Geometry {key} height should be > 0");
                }
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
            throw new InvalidOperationException($"RichTextEditorGeometries test failed: {caughtEx.Message}", caughtEx);
        }
    }
}

