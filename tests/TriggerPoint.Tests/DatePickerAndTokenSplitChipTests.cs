using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TriggerPoint.UI.Theme;
using Xunit;

namespace TriggerPoint.Tests;

public class DatePickerAndTokenSplitChipTests
{
    private static void EnsureApplicationResources()
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
    }

    [Fact]
    public void ThemeResources_Contains_CalendarItemAndDatePickerStyles()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var calItemStyle = Application.Current!.TryFindResource("ModernCalendarItemStyle") as Style;
                Assert.NotNull(calItemStyle);
                Assert.Equal(typeof(CalendarItem), calItemStyle.TargetType);

                var calStyle = Application.Current!.TryFindResource("ModernCalendarStyle") as Style;
                Assert.NotNull(calStyle);
                Assert.Equal(typeof(Calendar), calStyle.TargetType);

                var datePickerTbStyle = Application.Current!.TryFindResource("ModernDatePickerTextBoxStyle") as Style;
                Assert.NotNull(datePickerTbStyle);
                Assert.Equal(typeof(DatePickerTextBox), datePickerTbStyle.TargetType);

                var datePickerStyle = Application.Current!.TryFindResource("ModernDatePickerStyle") as Style;
                Assert.NotNull(datePickerStyle);
                Assert.Equal(typeof(DatePicker), datePickerStyle.TargetType);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void ThemeResources_Contains_SplitTokenChipStyles()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var dateCapsule = Application.Current!.TryFindResource("TokenChipSplitCapsuleDateStyle") as Style;
                Assert.NotNull(dateCapsule);
                Assert.Equal(typeof(Border), dateCapsule.TargetType);

                var clipCapsule = Application.Current!.TryFindResource("TokenChipSplitCapsuleClipStyle") as Style;
                Assert.NotNull(clipCapsule);
                Assert.Equal(typeof(Border), clipCapsule.TargetType);

                var promptCapsule = Application.Current!.TryFindResource("TokenChipSplitCapsulePromptStyle") as Style;
                Assert.NotNull(promptCapsule);
                Assert.Equal(typeof(Border), promptCapsule.TargetType);

                var dateMainBtn = Application.Current!.TryFindResource("TokenChipSplitDateMainBtnStyle") as Style;
                Assert.NotNull(dateMainBtn);
                Assert.Equal(typeof(Button), dateMainBtn.TargetType);

                var dateArrowBtn = Application.Current!.TryFindResource("TokenChipSplitDateArrowBtnStyle") as Style;
                Assert.NotNull(dateArrowBtn);
                Assert.Equal(typeof(Button), dateArrowBtn.TargetType);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void ModernDatePickerTextBox_InstantiatesWithCustomTemplate_AndTransparentBackground()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var tb = new DatePickerTextBox
                {
                    Style = Application.Current!.TryFindResource("ModernDatePickerTextBoxStyle") as Style
                };

                Assert.Equal(0, tb.BorderThickness.Left);
                Assert.Equal(0, tb.BorderThickness.Top);
                Assert.Equal(0, tb.BorderThickness.Right);
                Assert.Equal(0, tb.BorderThickness.Bottom);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void ModernCalendarItem_HasDisplayModeTriggersAndCalendarButtonStyle()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var calItemStyle = Application.Current!.TryFindResource("ModernCalendarItemStyle") as Style;
                Assert.NotNull(calItemStyle);

                var templateSetter = calItemStyle.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Control.TemplateProperty);
                Assert.NotNull(templateSetter);

                var controlTemplate = templateSetter.Value as ControlTemplate;
                Assert.NotNull(controlTemplate);
                Assert.True(controlTemplate.Triggers.Count >= 2, "ControlTemplate should have at least 2 triggers for Year and Decade display modes.");

                var calBtnStyle = Application.Current!.TryFindResource("ModernCalendarButtonStyle") as Style;
                Assert.NotNull(calBtnStyle);
                Assert.Equal(typeof(CalendarButton), calBtnStyle.TargetType);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
        if (caughtEx != null) throw caughtEx;
    }
}
