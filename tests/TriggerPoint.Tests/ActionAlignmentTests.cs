using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Controls;
using TriggerPoint.UI.Theme;
using Xunit;

namespace TriggerPoint.Tests;

public class ActionAlignmentTests
{
    private static void EnsureApplication()
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
    private static void RunOnStaThread(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new AggregateException(ex);
        }
    }

    [Fact]
    public void SnippetEditorControl_InitializePlainText_ConfiguresPlainTextMode()
    {
        RunOnStaThread(() =>
        {
            var editor = new SnippetEditorControl();
            editor.Initialize(SnippetContentType.PlainText, "Hello Plain World", string.Empty);

            Assert.Equal(SnippetContentType.PlainText, editor.ContentType);
            var (plain, rtf) = editor.GetSnippetPayload();
            Assert.Equal("Hello Plain World", plain);
        });
    }

    [Fact]
    public void SnippetEditorControl_InitializeRichText_ConfiguresRichTextMode()
    {
        RunOnStaThread(() =>
        {
            var editor = new SnippetEditorControl();
            string rtf = @"{\rtf1\ansi\b Formatted Title\b0}";
            editor.Initialize(SnippetContentType.RichText, "Formatted Title", rtf);

            Assert.Equal(SnippetContentType.RichText, editor.ContentType);
            var (plain, resultRtf) = editor.GetSnippetPayload();
            Assert.Contains("Formatted Title", plain);
            Assert.NotEmpty(resultRtf);
        });
    }

    [Fact]
    public void SnippetEditorControl_AvailableWorkflowVariables_PopulatesCorrectly()
    {
        RunOnStaThread(() =>
        {
            var editor = new SnippetEditorControl();
            var variables = new List<string> { "CustomerName", "InvoiceNumber", "DateGenerated" };
            editor.Initialize(SnippetContentType.PlainText, "Invoice for {CustomerName}", string.Empty, variables);

            Assert.Equal(3, editor.AvailableVariables.Count);
            Assert.Contains("CustomerName", editor.AvailableVariables);
            Assert.Contains("InvoiceNumber", editor.AvailableVariables);
            Assert.Contains("DateGenerated", editor.AvailableVariables);
        });
    }

    [Fact]
    public void SnippetEditorControl_InsertToken_UpdatesTemplateAndFiresEvent()
    {
        RunOnStaThread(() =>
        {
            var editor = new SnippetEditorControl();
            bool eventFired = false;
            editor.SnippetChanged += (s, e) => eventFired = true;

            editor.Initialize(SnippetContentType.PlainText, "Base Text ", string.Empty);
            editor.InsertToken("{clipboard}");

            var (plain, _) = editor.GetSnippetPayload();
            Assert.Contains("{clipboard}", plain);
            Assert.True(eventFired);
        });
    }

    [Fact]
    public void SnippetEditorControl_FormatSwitching_PreservesLivePreviewContrastAndDynamicForeground()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var editor = new SnippetEditorControl();
            editor.Initialize(SnippetContentType.PlainText, "Sample snippet text", string.Empty);

            // Switch Plain -> Rich -> Plain
            editor.SnippetFormatRichSegment.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(SnippetContentType.RichText, editor.ContentType);

            editor.SnippetFormatPlainSegment.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(SnippetContentType.PlainText, editor.ContentType);

            // Verify SnippetLivePreviewText is NOT hardcoded to #1E293B
            var fg = editor.SnippetLivePreviewText.Foreground;
            if (fg is SolidColorBrush scb)
            {
                // Contrast check: Should not be hardcoded dark slate #1E293B
                Assert.NotEqual(Color.FromRgb(30, 41, 59), scb.Color);
            }
        });
    }

    [Fact]
    public void SnippetEditorControl_InlineTokenPills_RendersAndExtractsCorrectly()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var editor = new SnippetEditorControl();
            string template = "Prefix {date:MM/dd/yyyy} Middle {clipboard} Suffix";
            editor.Initialize(SnippetContentType.PlainText, template, string.Empty);

            // Verify document has InlineUIContainer pills
            var pills = editor.SnippetPlainTextBox.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<InlineUIContainer>()
                .ToList();

            Assert.Equal(2, pills.Count);
            Assert.Equal("{date:MM/dd/yyyy}", pills[0].Tag);
            Assert.Equal("{clipboard}", pills[1].Tag);

            // Verify roundtrip extracted plain text
            var (extracted, _) = editor.GetSnippetPayload();
            Assert.Equal(template, extracted);
        });
    }

    [Fact]
    public void SnippetEditorControl_InlineTokenPill_RemoveAndEditUpdatesPayload()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var editor = new SnippetEditorControl();
            editor.Initialize(SnippetContentType.PlainText, "Start {date:MM/dd/yyyy} End", string.Empty);

            var pill = editor.SnippetPlainTextBox.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<InlineUIContainer>()
                .First();

            // Edit pill
            editor.UpdateTokenPillValue(pill, "{date:dd/MM/yyyy}");
            var (updated, _) = editor.GetSnippetPayload();
            Assert.Equal("Start {date:dd/MM/yyyy} End", updated);

            // Verify clicking close button directly on updated pill deletes it (regression test for captured uic)
            var border = pill.Child as Border;
            Assert.NotNull(border);
            var panel = border.Child as StackPanel;
            Assert.NotNull(panel);
            var closeBtn = panel.Children.OfType<Button>().Last();
            Assert.Equal("✕", closeBtn.Content);
            closeBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            var (removed, _) = editor.GetSnippetPayload();
            Assert.Equal("Start  End", removed);
        });
    }

    [Fact]
    public void SnippetEditorControl_ContextMenu_IndicatesActiveFormat()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var editor = new SnippetEditorControl();
            editor.Initialize(SnippetContentType.PlainText, "{date:MM/dd/yyyy}", string.Empty);

            var pill = editor.SnippetPlainTextBox.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<InlineUIContainer>()
                .First();

            var border = pill.Child as Border;
            Assert.NotNull(border?.ContextMenu);

            var menu = border.ContextMenu;
            var usItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Tag as string) == "{date:MM/dd/yyyy}");
            Assert.NotNull(usItem);
            Assert.True(usItem.IsChecked);
            Assert.StartsWith("✓", usItem.Header?.ToString() ?? string.Empty);

            var euItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Tag as string) == "{date:dd/MM/yyyy}");
            Assert.NotNull(euItem);
            Assert.False(euItem.IsChecked);
        });
    }

    [Fact]
    public void SnippetEditorControl_DeprecatedDateToken_ShowsUpgradeBanner()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var editor = new SnippetEditorControl();

            // When template does not have legacy date tokens, banner is collapsed
            editor.Initialize(SnippetContentType.PlainText, "Text with {datetime:yyyy-MM-dd HH:mm}", string.Empty);
            Assert.Equal(Visibility.Collapsed, editor.SnippetTokenUpgradeBanner.Visibility);

            // When template contains legacy {date} token, banner is displayed
            editor.Initialize(SnippetContentType.PlainText, "Today is {date}", string.Empty);
            Assert.Equal(Visibility.Visible, editor.SnippetTokenUpgradeBanner.Visibility);
        });
    }

    [Fact]
    public void SnippetEditorControl_ConvertDateTokens_ConvertsToDatetimeTokens()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var editor = new SnippetEditorControl();
            editor.Initialize(SnippetContentType.PlainText, "Logged {date} at {date:HH:mm}", string.Empty);

            Assert.Equal(Visibility.Visible, editor.SnippetTokenUpgradeBanner.Visibility);

            // Trigger 1-click conversion
            editor.ConvertDeprecatedDateTokens();

            // Banner is dismissed and tokens are converted to {datetime}
            Assert.Equal(Visibility.Collapsed, editor.SnippetTokenUpgradeBanner.Visibility);
            Assert.Contains("{datetime:yyyy-MM-dd}", editor.PlainTextTemplate);
            Assert.Contains("{datetime:HH:mm}", editor.PlainTextTemplate);
            Assert.DoesNotContain("{date}", editor.PlainTextTemplate);
            Assert.DoesNotContain("{date:", editor.PlainTextTemplate);
        });
    }

    [Fact]
    public void WorkflowStep_InjectSnippet_FullyAlignedProperties()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.InjectSnippet,
            SnippetContentType = SnippetContentType.RichText,
            SnippetTemplate = "Welcome to TriggerPoint",
            SnippetRtf = @"{\rtf1\ansi\b Welcome to TriggerPoint\b0}"
        };

        var clone = step.Clone();

        Assert.Equal(SnippetContentType.RichText, clone.SnippetContentType);
        Assert.Equal("Welcome to TriggerPoint", clone.SnippetTemplate);
        Assert.Equal(@"{\rtf1\ansi\b Welcome to TriggerPoint\b0}", clone.SnippetRtf);
    }

    [Fact]
    public void WorkflowStep_LaunchApp_AlignedWithStandaloneProperties()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.LaunchApp,
            Command = @"C:\Tools\app.exe",
            Arguments = "--silent --batch",
            WorkingDirectory = @"C:\Tools",
            RunAsAdmin = true,
            TargetDisplay = "1"
        };

        var clone = step.Clone();

        Assert.Equal(@"C:\Tools\app.exe", clone.Command);
        Assert.Equal("--silent --batch", clone.Arguments);
        Assert.Equal(@"C:\Tools", clone.WorkingDirectory);
        Assert.True(clone.RunAsAdmin);
        Assert.Equal("1", clone.TargetDisplay);
    }

    [Fact]
    public void WorkflowStep_Service_AlignedWithStandaloneProperties()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Service,
            ServiceName = "wuauserv",
            ServiceOperation = ServiceOperation.Restart,
            ServiceTimeoutSeconds = 45,
            ServiceWaitForCompletion = true
        };

        var clone = step.Clone();

        Assert.Equal("wuauserv", clone.ServiceName);
        Assert.Equal(ServiceOperation.Restart, clone.ServiceOperation);
        Assert.Equal(45, clone.ServiceTimeoutSeconds);
        Assert.True(clone.ServiceWaitForCompletion);
    }

    [Fact]
    public void TokenConfigurationDialog_FormatsTokensCorrectly()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var dateDlg = new TriggerPoint.UI.Views.TokenConfigurationDialog("{date:yyyy-MM-dd}");
            Assert.Equal("{datetime:yyyy-MM-dd}", dateDlg.ResultToken);

            var datetimeDlg = new TriggerPoint.UI.Views.TokenConfigurationDialog("{datetime:yyyy-MM-dd HH:mm}");
            Assert.Equal("{datetime:yyyy-MM-dd HH:mm}", datetimeDlg.ResultToken);

            var numberDlg = new TriggerPoint.UI.Views.TokenConfigurationDialog("{number:Quantity|1,100|10}");
            Assert.Equal("{number:Quantity|1,100|10}", numberDlg.ResultToken);

            var choiceDlg = new TriggerPoint.UI.Views.TokenConfigurationDialog("{choice:Fruit|Apple,Banana*,Orange}");
            Assert.Equal("{choice:Fruit|Apple,Banana*,Orange}", choiceDlg.ResultToken);
        });
    }
}
