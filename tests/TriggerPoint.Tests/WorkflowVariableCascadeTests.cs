using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class WorkflowVariableCascadeTests
{
    [Fact]
    public void CountVariableReferences_FindsReferencesAcrossStepProperties()
    {
        var steps = new List<WorkflowStep>
        {
            new WorkflowStep
            {
                StepType = WorkflowStepType.OpenUrl,
                Url = "https://example.com/api/{apiKey}?env={apiKey}"
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.LaunchApp,
                Command = "deploy.exe",
                Arguments = "--key {apiKey} --dir {workDir}",
                WorkingDirectory = "C:\\Deploy\\{apiKey}"
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.InjectSnippet,
                SnippetTemplate = "Bearer {apiKey}"
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.SetVariable,
                SetVariableName = "apiKey",
                SetVariableValue = "prefix_{apiKey}"
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.IfCondition,
                ConditionLeft = "{apiKey}",
                ConditionRight = "secret"
            }
        };

        // 2 in Url, 1 in Arguments, 1 in WorkingDir, 1 in Snippet, 1 SetVarName + 1 SetVarValue, 1 in ConditionLeft = 8 total
        int count = WorkflowVariableCascadeHelper.CountVariableReferences(steps, "apiKey");
        Assert.Equal(8, count);
    }

    [Fact]
    public void CountVariableReferences_FindsReferencesInNestedThenElseSteps()
    {
        var parentStep = new WorkflowStep
        {
            StepType = WorkflowStepType.IfCondition,
            ConditionLeft = "{status}",
            ConditionRight = "ready",
            ThenSteps = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    StepType = WorkflowStepType.OpenUrl,
                    Url = "https://status.io/{status}"
                }
            },
            ElseSteps = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    StepType = WorkflowStepType.Dialog,
                    DialogMessage = "Current status is {status}"
                }
            }
        };

        int count = WorkflowVariableCascadeHelper.CountVariableReferences(new[] { parentStep }, "status");
        // 1 in ConditionLeft, 1 in ThenSteps Url, 1 in ElseSteps DialogMessage = 3 total
        Assert.Equal(3, count);
    }

    [Fact]
    public void CountVariableReferences_ReturnsZero_WhenVariableNotReferenced()
    {
        var steps = new List<WorkflowStep>
        {
            new WorkflowStep { StepType = WorkflowStepType.OpenUrl, Url = "https://example.com/{unrelated}" }
        };

        int count = WorkflowVariableCascadeHelper.CountVariableReferences(steps, "targetVar");
        Assert.Equal(0, count);
    }

    [Fact]
    public void ReplaceVariableReferences_ReplacesAllOccurrences_AndPreservesUnrelated()
    {
        var steps = new List<WorkflowStep>
        {
            new WorkflowStep
            {
                StepType = WorkflowStepType.OpenUrl,
                Url = "https://api.test.com/{oldVar}?other={preserveMe}"
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.LaunchApp,
                Arguments = "--token={oldVar}"
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.SetVariable,
                SetVariableName = "oldVar",
                SetVariableValue = "val_{oldVar}"
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.Prompt,
                VariableName = "oldVar",
                PromptFields = new List<WorkflowPromptField>
                {
                    new WorkflowPromptField
                    {
                        VariableName = "oldVar",
                        DefaultValue = "default_{oldVar}"
                    },
                    new WorkflowPromptField
                    {
                        VariableName = "preserveMe",
                        DefaultValue = "constant"
                    }
                }
            },
            new WorkflowStep
            {
                StepType = WorkflowStepType.IfCondition,
                ConditionLeft = "{oldVar}",
                ConditionRight = "done",
                ThenSteps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Dialog,
                        DialogTitle = "Done {oldVar}",
                        DialogMessage = "Completed {oldVar}"
                    }
                }
            }
        };

        int replaced = WorkflowVariableCascadeHelper.ReplaceVariableReferences(steps, "oldVar", "newVar");
        Assert.True(replaced > 0);

        // Verify replacements
        Assert.Equal("https://api.test.com/{newVar}?other={preserveMe}", steps[0].Url);
        Assert.Equal("--token={newVar}", steps[1].Arguments);
        Assert.Equal("newVar", steps[2].SetVariableName);
        Assert.Equal("val_{newVar}", steps[2].SetVariableValue);
        Assert.Equal("newVar", steps[3].VariableName);
        Assert.Equal("newVar", steps[3].PromptFields[0].VariableName);
        Assert.Equal("default_{newVar}", steps[3].PromptFields[0].DefaultValue);
        Assert.Equal("preserveMe", steps[3].PromptFields[1].VariableName);
        Assert.Equal("{newVar}", steps[4].ConditionLeft);
        Assert.Equal("Done {newVar}", steps[4].ThenSteps[0].DialogTitle);
        Assert.Equal("Completed {newVar}", steps[4].ThenSteps[0].DialogMessage);

        // Verify zero occurrences of oldVar remain
        int remaining = WorkflowVariableCascadeHelper.CountVariableReferences(steps, "oldVar");
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void CountVariableReferences_WithSkipStepDefinition_ExcludesSelfDeclaration()
    {
        var setVarStep = new WorkflowStep
        {
            StepType = WorkflowStepType.SetVariable,
            SetVariableName = "myVar",
            SetVariableValue = "constant"
        };
        var urlStep = new WorkflowStep
        {
            StepType = WorkflowStepType.OpenUrl,
            Url = "https://example.com/{myVar}"
        };

        var steps = new List<WorkflowStep> { setVarStep, urlStep };

        // Without skip: 1 declaration in SetVariable + 1 token in OpenUrl = 2
        int countWithoutSkip = WorkflowVariableCascadeHelper.CountVariableReferences(steps, "myVar");
        Assert.Equal(2, countWithoutSkip);

        // With skip: excludes SetVariable's declaration, only counts the 1 reference in OpenUrl
        int countWithSkip = WorkflowVariableCascadeHelper.CountVariableReferences(steps, "myVar", skipStepDefinition: setVarStep);
        Assert.Equal(1, countWithSkip);
    }

    [Fact]
    public void ReplaceVariableReferences_WithSkipStepDefinition_ReplacesDownstreamOnly()
    {
        var setVarStep = new WorkflowStep
        {
            StepType = WorkflowStepType.SetVariable,
            SetVariableName = "myVar",
            SetVariableValue = "constant"
        };
        var cmdStep = new WorkflowStep
        {
            StepType = WorkflowStepType.LaunchApp,
            Command = "run.exe",
            Arguments = "--arg {myVar}"
        };

        var steps = new List<WorkflowStep> { setVarStep, cmdStep };

        int replaced = WorkflowVariableCascadeHelper.ReplaceVariableReferences(steps, "myVar", "newVar", skipStepDefinition: setVarStep);
        Assert.Equal(1, replaced);

        // setVarStep definition is not changed by cascade replacement because it was skipped
        Assert.Equal("myVar", setVarStep.SetVariableName);
        // cmdStep reference was updated
        Assert.Equal("--arg {newVar}", cmdStep.Arguments);
    }
}

