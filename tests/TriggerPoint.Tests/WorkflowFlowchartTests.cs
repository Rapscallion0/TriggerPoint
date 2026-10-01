using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class WorkflowFlowchartTests
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
    [InlineData(WorkflowStepType.IfCondition, "IF")]
    [InlineData(WorkflowStepType.Prompt, "PROMPT")]
    [InlineData(WorkflowStepType.OpenUrl, "URL")]
    [InlineData(WorkflowStepType.LaunchApp, "APP")]
    [InlineData(WorkflowStepType.EnsureDirectory, "DIR")]
    [InlineData(WorkflowStepType.InjectSnippet, "SNIP")]
    [InlineData(WorkflowStepType.Delay, "DELAY")]
    [InlineData(WorkflowStepType.RunScript, "SCRIPT")]
    [InlineData(WorkflowStepType.ExecuteAction, "ACTION")]
    [InlineData(WorkflowStepType.Dialog, "DIALOG")]
    [InlineData(WorkflowStepType.Macro, "MACRO")]
    [InlineData(WorkflowStepType.SetVariable, "SET")]
    public void GetStepBadgeShortName_ReturnsCorrectBadges(WorkflowStepType type, string expected)
    {
        var badge = SettingsWindow.GetStepBadgeShortName(type);
        Assert.Equal(expected, badge);
    }

    [Fact]
    public void MeasureSequenceWidth_EmptyOrNull_ReturnsDefaultWidth()
    {
        Assert.Equal(240.0, SettingsWindow.MeasureSequenceWidth(null));
        Assert.Equal(240.0, SettingsWindow.MeasureSequenceWidth([]));
    }

    [Fact]
    public void MeasureSequenceWidth_LinearSteps_ReturnsSingleNodeWidth()
    {
        var steps = new List<WorkflowStep>
        {
            new() { StepType = WorkflowStepType.LaunchApp, Command = "notepad.exe" },
            new() { StepType = WorkflowStepType.Delay, DelayMs = 500 },
            new() { StepType = WorkflowStepType.OpenUrl, Url = "https://example.com" }
        };

        var width = SettingsWindow.MeasureSequenceWidth(steps);
        Assert.Equal(240.0, width);
    }

    [Fact]
    public void MeasureSubtreeWidth_IfConditionWithBranches_CalculatesCombinedWidth()
    {
        var ifStep = new WorkflowStep
        {
            StepType = WorkflowStepType.IfCondition,
            HasElseBranch = true,
            ThenSteps =
            [
                new WorkflowStep { StepType = WorkflowStepType.InjectSnippet, SnippetTemplate = "True!" }
            ],
            ElseSteps =
            [
                new WorkflowStep { StepType = WorkflowStepType.InjectSnippet, SnippetTemplate = "False!" }
            ]
        };

        // thenW = 240, elseW = 240, gap = 40 => 240 + 40 + 240 = 520
        var width = SettingsWindow.MeasureSubtreeWidth(ifStep);
        Assert.Equal(520.0, width);
    }

    [Fact]
    public void MeasureSubtreeWidth_NestedIfConditions_PropagatesWidthCorrectly()
    {
        var nestedIf = new WorkflowStep
        {
            StepType = WorkflowStepType.IfCondition,
            HasElseBranch = true,
            ThenSteps =
            [
                new WorkflowStep { StepType = WorkflowStepType.LaunchApp }
            ],
            ElseSteps =
            [
                new WorkflowStep { StepType = WorkflowStepType.LaunchApp }
            ]
        };

        var outerIf = new WorkflowStep
        {
            StepType = WorkflowStepType.IfCondition,
            HasElseBranch = true,
            ThenSteps = [nestedIf], // 520 wide
            ElseSteps = [new WorkflowStep { StepType = WorkflowStepType.OpenUrl }] // 240 wide
        };

        // 520 + 40 + 240 = 800
        var width = SettingsWindow.MeasureSubtreeWidth(outerIf);
        Assert.Equal(800.0, width);
    }
}
