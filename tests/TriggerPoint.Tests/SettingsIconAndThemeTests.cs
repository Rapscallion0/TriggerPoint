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
                var darkIcon = Application.Current!.Resources["AppIconSource"] as ImageSource;
                Assert.NotNull(darkIcon);

                // Light Theme Test
                ThemeManager.ApplyTheme(AppTheme.Light);
                var lightIcon = Application.Current!.Resources["AppIconSource"] as ImageSource;
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

    [Fact]
    public void TextBlock_DefaultForeground_ResolvesTextPrimaryBrush_InDarkAndLightModes()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                if (!Application.Current!.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString?.Contains("ThemeResources.xaml") == true))
                {
                    Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/TriggerPoint;component/Theme/ThemeResources.xaml", UriKind.Absolute)
                    });
                }

                // 1. Dark Theme Test
                ThemeManager.ApplyTheme(AppTheme.Dark);
                var winDark = new Window();
                var spDark = new System.Windows.Controls.StackPanel();
                var tbDark = new System.Windows.Controls.TextBlock { Text = "🌐 Browser Tab & URL Rules" };
                var accentStyle = Application.Current.TryFindResource("AccentButtonStyle") as Style;
                var btnDark = new System.Windows.Controls.Button { Style = accentStyle, Content = "💾 Save" };
                spDark.Children.Add(tbDark);
                spDark.Children.Add(btnDark);
                winDark.Content = spDark;
                winDark.Show();
                winDark.UpdateLayout();
                btnDark.ApplyTemplate();

                Assert.IsType<SolidColorBrush>(tbDark.Foreground);
                Assert.Equal(Color.FromRgb(242, 243, 245), ((SolidColorBrush)tbDark.Foreground).Color);

                var btnDarkTb = (DependencyObject?)FindVisualChild<System.Windows.Controls.TextBlock>(btnDark) ?? FindVisualChild<System.Windows.Controls.AccessText>(btnDark);
                Assert.NotNull(btnDarkTb);
                var brushDark = (SolidColorBrush)(btnDarkTb is System.Windows.Controls.TextBlock tbD ? tbD.Foreground : ((System.Windows.Controls.AccessText)btnDarkTb).Foreground);
                Assert.Equal(Colors.White, brushDark.Color);

                // 2. Light Theme Test
                ThemeManager.ApplyTheme(AppTheme.Light);
                winDark.UpdateLayout();
                btnDark.ApplyTemplate();

                Assert.IsType<SolidColorBrush>(tbDark.Foreground);
                Assert.Equal(Color.FromRgb(15, 23, 42), ((SolidColorBrush)tbDark.Foreground).Color);

                var btnLightTb = (DependencyObject?)FindVisualChild<System.Windows.Controls.TextBlock>(btnDark) ?? FindVisualChild<System.Windows.Controls.AccessText>(btnDark);
                Assert.NotNull(btnLightTb);
                var brushLight = (SolidColorBrush)(btnLightTb is System.Windows.Controls.TextBlock tbL ? tbL.Foreground : ((System.Windows.Controls.AccessText)btnLightTb).Foreground);
                Assert.Equal(Colors.White, brushLight.Color);

                winDark.Close();
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
            throw new InvalidOperationException($"TextBlock default foreground test failed: {caughtEx.Message}", caughtEx);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}


