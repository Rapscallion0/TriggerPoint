using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using Xunit;

namespace TriggerPoint.Tests;

public class ItemSnapshotTests
{
    [Fact]
    public void Clone_CreatesIndependentDeepCopy_ForSnapshot()
    {
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Original Item",
            Description = "Original Desc",
            ActionType = ActionType.Shell,
            PresentationMode = PresentationMode.Direct,
            Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T"),
            AcceleratorKey = "1",
            Payload = new ActionPayload
            {
                Command = "powershell.exe",
                Arguments = "-NoExit",
                WorkingDirectory = @"C:\Temp",
                RunAsAdmin = true
            },
            ContextFilter = new ContextFilter
            {
                AllowedProcesses = ["cmd.exe", "wt.exe"],
                ExcludedProcesses = ["notepad.exe"],
                AllowedUrls = ["*github.com*"],
                ExcludedUrls = ["*youtube.com*"]
            }
        };

        var snapshot = item.Clone();

        // Mutate original
        item.Name = "Modified Name";
        item.Description = "Modified Desc";
        item.Payload.Command = "cmd.exe";
        item.Payload.RunAsAdmin = false;
        item.ContextFilter.AllowedProcesses.Add("bash.exe");

        // Assert snapshot retained pre-edit state
        Assert.Equal("Original Item", snapshot.Name);
        Assert.Equal("Original Desc", snapshot.Description);
        Assert.Equal("powershell.exe", snapshot.Payload.Command);
        Assert.True(snapshot.Payload.RunAsAdmin);
        Assert.Equal(2, snapshot.ContextFilter.AllowedProcesses.Count);
        Assert.DoesNotContain("bash.exe", snapshot.ContextFilter.AllowedProcesses);
    }

    [Fact]
    public void IsItemMatchingSnapshot_MatchesIdenticalAndClonedItems()
    {
        var item = new TriggerItem
        {
            Name = "Test Action",
            Description = "A description",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowVariables = [new WorkflowVariableDefinition { Name = "env", Value = "prod" }],
                WorkflowSteps =
                [
                    new WorkflowStep { StepType = WorkflowStepType.Delay, DelayMs = 500 }
                ]
            }
        };

        var snapshot = item.Clone();
        Assert.True(TriggerPoint.UI.Views.SettingsWindow.IsItemMatchingSnapshot(item, snapshot));
    }

    [Fact]
    public void IsItemMatchingSnapshot_DetectsModifications_AndRestoresOnUndo()
    {
        var item = new TriggerItem
        {
            Name = "Original Action",
            Description = "Desc",
            Payload = new ActionPayload { Command = "notepad.exe" }
        };

        var snapshot = item.Clone();

        // 1. Initially matches
        Assert.True(TriggerPoint.UI.Views.SettingsWindow.IsItemMatchingSnapshot(item, snapshot));

        // 2. Modified -> does not match (dirty)
        item.Name = "Original Action Edited";
        Assert.False(TriggerPoint.UI.Views.SettingsWindow.IsItemMatchingSnapshot(item, snapshot));

        // 3. User manually undoes change -> matches again (not dirty)
        item.Name = "Original Action";
        Assert.True(TriggerPoint.UI.Views.SettingsWindow.IsItemMatchingSnapshot(item, snapshot));
    }

    [Fact]
    public void IsItemMatchingSnapshot_DetectsWorkflowStepChanges_AndRestoresOnUndo()
    {
        var item = new TriggerItem
        {
            Name = "Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowSteps =
                [
                    new WorkflowStep { StepType = WorkflowStepType.Delay, DelayMs = 100 }
                ]
            }
        };

        var snapshot = item.Clone();

        // Add a step
        var newStep = new WorkflowStep { StepType = WorkflowStepType.SetVariable, SetVariableName = "x", SetVariableValue = "1" };
        item.Payload.WorkflowSteps.Add(newStep);
        Assert.False(TriggerPoint.UI.Views.SettingsWindow.IsItemMatchingSnapshot(item, snapshot));

        // Delete the added step
        item.Payload.WorkflowSteps.Remove(newStep);
        Assert.True(TriggerPoint.UI.Views.SettingsWindow.IsItemMatchingSnapshot(item, snapshot));
    }
}
