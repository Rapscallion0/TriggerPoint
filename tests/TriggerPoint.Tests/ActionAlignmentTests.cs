using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Controls;
using Xunit;

namespace TriggerPoint.Tests;

public class ActionAlignmentTests
{
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
}
