using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class InteractivePromptDialogTests
{
    private static void EnsureApplicationAndThemeResources()
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
        TriggerPoint.UI.Theme.ThemeManager.ApplyTheme(TriggerPoint.UI.Theme.AppTheme.Dark);
    }

    [Fact]
    public void InteractivePromptDialog_TracksFirstInputControl_ForTextPrompt()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var tokens = new List<PromptToken>
                {
                    new() { RawTag = "{prompt:First Name}", Type = TokenType.PromptText, Label = "First Name", DefaultValue = "Alice" },
                    new() { RawTag = "{prompt:Last Name}", Type = TokenType.PromptText, Label = "Last Name" }
                };

                var dialog = new InteractivePromptDialog(tokens, "Test Dialog", "Subtitle text");

                Assert.NotNull(dialog);
                Assert.NotNull(dialog.FirstInputControl);
                Assert.IsType<TextBox>(dialog.FirstInputControl);

                var tb = (TextBox)dialog.FirstInputControl;
                Assert.Equal("Alice", tb.Text);
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
    public void InteractivePromptDialog_TracksFirstInputControl_ForDatePickerPrompt()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var tokens = new List<PromptToken>
                {
                    new() { RawTag = "{date_picker:Choose Date}", Type = TokenType.PromptDatePicker, Label = "Choose Date", DateFormat = "yyyy-MM-dd" }
                };

                var dialog = new InteractivePromptDialog(tokens);

                Assert.NotNull(dialog);
                Assert.NotNull(dialog.FirstInputControl);
                Assert.IsType<DatePicker>(dialog.FirstInputControl);
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
    public void InteractivePromptDialog_TracksFirstInputControl_ForChoicePrompt()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var tokens = new List<PromptToken>
                {
                    new()
                    {
                        RawTag = "{choice:Env}",
                        Type = TokenType.PromptChoice,
                        Label = "Env",
                        Choices =
                        [
                            new("Production", "prod"),
                            new("Staging", "stage")
                        ],
                        DefaultValue = "stage"
                    }
                };

                var dialog = new InteractivePromptDialog(tokens);

                Assert.NotNull(dialog);
                Assert.NotNull(dialog.FirstInputControl);
                Assert.IsType<ComboBox>(dialog.FirstInputControl);

                var cb = (ComboBox)dialog.FirstInputControl;
                var selected = cb.SelectedItem as ChoiceOption;
                Assert.NotNull(selected);
                Assert.Equal("stage", selected.Value);
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
    public void InteractivePromptDialog_TracksFirstInputControl_ForNumberPrompt()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var tokens = new List<PromptToken>
                {
                    new() { RawTag = "{number:Count}", Type = TokenType.PromptNumber, Label = "Count", MinNumber = 1, MaxNumber = 100, DefaultValue = "5" }
                };

                var dialog = new InteractivePromptDialog(tokens);

                Assert.NotNull(dialog);
                Assert.NotNull(dialog.FirstInputControl);
                Assert.IsType<TextBox>(dialog.FirstInputControl);

                var tb = (TextBox)dialog.FirstInputControl;
                Assert.Equal("5", tb.Text);
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
    public void InteractivePromptDialog_PreviewKeyDown_Enter_SubmitsDialogWithDefaults()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var tokens = new List<PromptToken>
                {
                    new() { RawTag = "{date_picker:Choose Date}", Type = TokenType.PromptDatePicker, Label = "Choose Date", DateFormat = "yyyy-MM-dd" },
                    new() { RawTag = "{text:Name}", Type = TokenType.PromptText, Label = "Name", DefaultValue = "Alice" }
                };

                var dialog = new InteractivePromptDialog(tokens);
                var keyEventArgs = new KeyEventArgs(Keyboard.PrimaryDevice, new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero), 0, Key.Enter)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent
                };

                dialog.Window_PreviewKeyDown(dialog, keyEventArgs);

                Assert.True(keyEventArgs.Handled);
                Assert.NotNull(dialog.Results);
                Assert.True(dialog.Results.ContainsKey("{date_picker:Choose Date}"));
                Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), dialog.Results["{date_picker:Choose Date}"]);
                Assert.Equal("Alice", dialog.Results["{text:Name}"]);
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
    public void InteractivePromptDialog_PreviewKeyDown_Escape_CancelsDialog()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var tokens = new List<PromptToken>
                {
                    new() { RawTag = "{text:Name}", Type = TokenType.PromptText, Label = "Name", DefaultValue = "Alice" }
                };

                var dialog = new InteractivePromptDialog(tokens);
                var keyEventArgs = new KeyEventArgs(Keyboard.PrimaryDevice, new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero), 0, Key.Escape)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent
                };

                dialog.Window_PreviewKeyDown(dialog, keyEventArgs);

                Assert.True(keyEventArgs.Handled);
                Assert.Null(dialog.Results);
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
