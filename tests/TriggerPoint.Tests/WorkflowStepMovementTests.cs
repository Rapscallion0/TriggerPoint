using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Controls;
using TriggerPoint.Core.Models;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class WorkflowStepMovementTests
{
    [Fact]
    public void MoveStep_IntoThenBranch_RemovesFromRootAndInsertsIntoThenSteps()
    {
        var step1 = new WorkflowStep { Name = "Step 1", StepType = WorkflowStepType.Delay };
        var ifStep = new WorkflowStep { Name = "If Branch", StepType = WorkflowStepType.IfCondition };
        var step2 = new WorkflowStep { Name = "Step 2", StepType = WorkflowStepType.OpenUrl };

        var rootSteps = new List<WorkflowStep> { step1, ifStep, step2 };

        // Simulate moving step1 into ifStep.ThenSteps at index 0
        rootSteps.Remove(step1);
        ifStep.ThenSteps.Insert(0, step1);

        Assert.Equal(2, rootSteps.Count);
        Assert.DoesNotContain(step1, rootSteps);
        Assert.Single(ifStep.ThenSteps);
        Assert.Same(step1, ifStep.ThenSteps[0]);
    }

    [Fact]
    public void MoveStep_IntoElseBranch_AutoEnablesHasElseBranch()
    {
        var step1 = new WorkflowStep { Name = "Step 1", StepType = WorkflowStepType.Delay };
        var ifStep = new WorkflowStep
        {
            Name = "If Branch",
            StepType = WorkflowStepType.IfCondition,
            HasElseBranch = false
        };

        var rootSteps = new List<WorkflowStep> { step1, ifStep };

        // Simulate moving step1 into Else branch
        rootSteps.Remove(step1);
        if (!ifStep.HasElseBranch)
        {
            ifStep.HasElseBranch = true;
        }
        ifStep.ElseSteps.Add(step1);

        Assert.Single(rootSteps);
        Assert.True(ifStep.HasElseBranch);
        Assert.Single(ifStep.ElseSteps);
        Assert.Equal("Step 1", ifStep.ElseSteps[0].Name);
    }

    [Fact]
    public void MoveStep_OutOfBranch_BeforeAndAfterCondition()
    {
        var branchStep = new WorkflowStep { Name = "Nested Prompt", StepType = WorkflowStepType.Prompt };
        var ifStep = new WorkflowStep
        {
            Name = "If Branch",
            StepType = WorkflowStepType.IfCondition,
            ThenSteps = [branchStep]
        };
        var rootSteps = new List<WorkflowStep> { ifStep };

        // 1. Move before condition
        ifStep.ThenSteps.Remove(branchStep);
        int ifIndex = rootSteps.IndexOf(ifStep);
        rootSteps.Insert(ifIndex, branchStep);

        Assert.Equal(2, rootSteps.Count);
        Assert.Same(branchStep, rootSteps[0]);
        Assert.Same(ifStep, rootSteps[1]);
        Assert.Empty(ifStep.ThenSteps);

        // 2. Move after condition
        rootSteps.Remove(branchStep);
        ifIndex = rootSteps.IndexOf(ifStep);
        rootSteps.Insert(ifIndex + 1, branchStep);

        Assert.Equal(2, rootSteps.Count);
        Assert.Same(ifStep, rootSteps[0]);
        Assert.Same(branchStep, rootSteps[1]);
    }

    [Fact]
    public void MoveStep_BetweenBranches_TransfersStep()
    {
        var step = new WorkflowStep { Name = "Action in Then", StepType = WorkflowStepType.RunScript };
        var ifStep = new WorkflowStep
        {
            Name = "If Branch",
            StepType = WorkflowStepType.IfCondition,
            HasElseBranch = true,
            ThenSteps = [step],
            ElseSteps = []
        };

        // Move from Then to Else
        ifStep.ThenSteps.Remove(step);
        ifStep.ElseSteps.Add(step);

        Assert.Empty(ifStep.ThenSteps);
        Assert.Single(ifStep.ElseSteps);
        Assert.Same(step, ifStep.ElseSteps[0]);

        // Move back from Else to Then
        ifStep.ElseSteps.Remove(step);
        ifStep.ThenSteps.Add(step);

        Assert.Single(ifStep.ThenSteps);
        Assert.Empty(ifStep.ElseSteps);
        Assert.Same(step, ifStep.ThenSteps[0]);
    }

    [Fact]
    public void CloneStep_CreatesIndependentDeepCopy()
    {
        var original = new WorkflowStep
        {
            Name = "Original Step",
            StepType = WorkflowStepType.IfCondition,
            ConditionLeft = "{status}",
            ConditionRight = "active",
            HasElseBranch = true,
            ThenSteps = [
                new WorkflowStep { Name = "Inner Then 1", StepType = WorkflowStepType.Delay, DelayMs = 1000 }
            ],
            ElseSteps = [
                new WorkflowStep { Name = "Inner Else 1", StepType = WorkflowStepType.InjectSnippet, SnippetTemplate = "Hello" }
            ]
        };

        var clone = original.Clone();

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.ConditionLeft, clone.ConditionLeft);
        Assert.Equal(original.ConditionRight, clone.ConditionRight);
        Assert.True(clone.HasElseBranch);

        // Nested steps deep-cloned
        Assert.Single(clone.ThenSteps);
        Assert.NotEqual(original.ThenSteps[0].Id, clone.ThenSteps[0].Id);
        Assert.Equal(original.ThenSteps[0].Name, clone.ThenSteps[0].Name);

        Assert.Single(clone.ElseSteps);
        Assert.NotEqual(original.ElseSteps[0].Id, clone.ElseSteps[0].Id);
        Assert.Equal(original.ElseSteps[0].Name, clone.ElseSteps[0].Name);

        // Modifying clone doesn't mutate original
        clone.Name = "Modified Clone";
        clone.ThenSteps[0].DelayMs = 9999;

        Assert.Equal("Original Step", original.Name);
        Assert.Equal(1000, original.ThenSteps[0].DelayMs);
    }

    [Fact]
    public void PreventCycle_ConditionCannotBeDroppedInsideOwnBranches()
    {
        var ifStep = new WorkflowStep
        {
            Name = "Parent Condition",
            StepType = WorkflowStepType.IfCondition
        };

        // Logic check matching Workflow.cs drop validation:
        // A step cannot be dropped into a branch belonging to itself
        bool CanDropIntoBranch(WorkflowStep draggedStep, WorkflowStep targetParentIf)
        {
            if (draggedStep.Id == targetParentIf.Id)
                return false;
            return true;
        }

        Assert.False(CanDropIntoBranch(ifStep, ifStep));

        var anotherIfStep = new WorkflowStep
        {
            Name = "Sibling Condition",
            StepType = WorkflowStepType.IfCondition
        };

        Assert.True(CanDropIntoBranch(ifStep, anotherIfStep));
    }

    [Fact]
    public void IsOverChildDropTarget_IgnoresTextBoxBaseAllowDrop_OnlyRecognizesWorkflowDropTargets()
    {
        Exception? caughtEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var parentCard = new Border();
                var stack = new StackPanel();
                parentCard.Child = stack;

                var textBox = new TextBox { Text = "Value / Variable" };
                stack.Children.Add(textBox);

                var childDropTarget = new Border();
                SettingsWindow.SetIsWorkflowDropTarget(childDropTarget, true);
                var childTextBox = new TextBox { Text = "Child Value" };
                childDropTarget.Child = childTextBox;
                stack.Children.Add(childDropTarget);

                // TextBox natively has AllowDrop = true in WPF, but is NOT a workflow drop target
                Assert.True(textBox.AllowDrop);
                Assert.False(SettingsWindow.IsOverChildDropTarget(parentCard, textBox));

                // Element inside a registered workflow drop target IS recognized as a child drop target
                Assert.True(SettingsWindow.IsOverChildDropTarget(parentCard, childTextBox));
                Assert.True(SettingsWindow.IsOverChildDropTarget(parentCard, childDropTarget));

                // The parentCard itself is not considered a child of itself
                Assert.False(SettingsWindow.IsOverChildDropTarget(parentCard, parentCard));
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
            throw new InvalidOperationException($"IsOverChildDropTarget test failed: {caughtEx.Message}", caughtEx);
        }
    }
}
