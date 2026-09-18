using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class WorkflowVariablesTests
{
    private class MockPromptService : IPromptDialogService
    {
        public Task<Dictionary<string, string>?> ShowPromptDialogAsync(
            IReadOnlyList<PromptToken> promptTokens,
            string? title = null,
            string? subtitle = null) => Task.FromResult<Dictionary<string, string>?>(new());
    }

    private class MockConfirmationService : IConfirmationDialogService
    {
        public Task<bool> ShowConfirmationAsync(string message, string title = "TriggerPoint Confirmation", string confirmButtonText = "Confirm", string cancelButtonText = "Cancel")
            => Task.FromResult(true);
    }

    private class MockToastService : IToastNotificationService
    {
        public void ShowSuccess(string title, string message) { }
        public void ShowError(string title, string message) { }
        public void ShowWarning(string title, string message) { }
    }

    private class MockSnippetService : ISnippetService
    {
        public List<string> Snippets { get; } = [];
        public Task InjectSnippetAsync(string template, IntPtr targetHwnd, SnippetContentType contentType = SnippetContentType.PlainText, string? rtfContent = null)
        {
            Snippets.Add(template);
            return Task.CompletedTask;
        }
    }

    private class MockTelemetryService : ITelemetryService
    {
        public Task RecordExecutionAsync(Guid itemId) => Task.CompletedTask;
    }

    private class MockContextFilterService : IContextFilterService
    {
        public IntPtr LastExternalForegroundHwnd { get; set; } = IntPtr.Zero;
        public IntPtr GetForegroundWindowHandle() => IntPtr.Zero;
        public string? GetForegroundProcessName() => "explorer";
        public string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null) => null;
        public bool ShouldExecute(TriggerItem item) => true;
        public bool ShouldExecute(TriggerItem item, IReadOnlyList<TriggerItem>? allItems) => true;
        public void SetAllItemsProvider(Func<IReadOnlyList<TriggerItem>>? provider) { }
        public List<TriggerItem> GetInheritanceChain(TriggerItem item, IReadOnlyList<TriggerItem> allItems) => [];
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WorkflowVariables_SeededAndResolvedInSteps()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "Workflow Variables Test",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition { Name = "baseUrl", Value = "https://api.example.com" },
                    new WorkflowVariableDefinition { Name = "envName", Value = "production" }
                ],
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.InjectSnippet,
                        SnippetTemplate = "{baseUrl}/v1/{envName}"
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);
        Assert.Single(snippetMock.Snippets);
        Assert.Equal("https://api.example.com/v1/production", snippetMock.Snippets[0]);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_SetVariableStep_UpdatesVariableForSubsequentSteps()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "Set Variable Step Test",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition { Name = "counter", Value = "initial" }
                ],
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.SetVariable,
                        SetVariableName = "counter",
                        SetVariableValue = "updated_val"
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.SetVariable,
                        SetVariableName = "computedVar",
                        SetVariableValue = "prefix_{counter}_suffix"
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.InjectSnippet,
                        SnippetTemplate = "{computedVar}"
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);
        Assert.Single(snippetMock.Snippets);
        Assert.Equal("prefix_updated_val_suffix", snippetMock.Snippets[0]);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WindowsEnvironmentVariables_ExpandCorrectly()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        string expectedSystemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";

        var item = new TriggerItem
        {
            Name = "Env Var Expansion Test",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.InjectSnippet,
                        SnippetTemplate = "Root: %SystemRoot%"
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);
        Assert.Single(snippetMock.Snippets);
        Assert.Equal($"Root: {expectedSystemRoot}", snippetMock.Snippets[0]);
    }

    [Fact]
    public void WorkflowVariableDefinition_Clone_CreatesDistinctDeepCopy()
    {
        var original = new WorkflowVariableDefinition
        {
            Name = "ApiKey",
            Value = "secret_123",
            Description = "Production API Key"
        };

        var copy = original.Clone();

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.Name, copy.Name);
        Assert.Equal(original.Value, copy.Value);
        Assert.Equal(original.Description, copy.Description);

        copy.Value = "new_secret";
        Assert.Equal("secret_123", original.Value);
    }

    [Fact]
    public void TriggerItem_Clone_ClonesWorkflowVariablesAndSteps()
    {
        var item = new TriggerItem
        {
            Name = "Source Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition { Name = "foo", Value = "bar" }
                ],
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.SetVariable,
                        SetVariableName = "foo",
                        SetVariableValue = "baz"
                    }
                ]
            }
        };

        var cloned = item.Clone();

        Assert.Single(cloned.Payload.WorkflowVariables);
        Assert.Equal("foo", cloned.Payload.WorkflowVariables[0].Name);
        Assert.Equal("bar", cloned.Payload.WorkflowVariables[0].Value);
        Assert.NotEqual(item.Payload.WorkflowVariables[0].Id, cloned.Payload.WorkflowVariables[0].Id);

        Assert.Single(cloned.Payload.WorkflowSteps);
        Assert.Equal(WorkflowStepType.SetVariable, cloned.Payload.WorkflowSteps[0].StepType);
        Assert.Equal("foo", cloned.Payload.WorkflowSteps[0].SetVariableName);
        Assert.Equal("baz", cloned.Payload.WorkflowSteps[0].SetVariableValue);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_MultipleAndCustomEnvironmentVariables_ExpandCorrectly()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        string tempCustomKey = "TRIGGERPOINT_TEST_VAR_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(tempCustomKey, "CustomValue123");

        try
        {
            var item = new TriggerItem
            {
                Name = "Custom Env Var Test",
                ActionType = ActionType.Workflow,
                Payload = new ActionPayload
                {
                    WorkflowMode = WorkflowMode.Visual,
                    WorkflowSteps =
                    [
                        new WorkflowStep
                        {
                            StepType = WorkflowStepType.InjectSnippet,
                            SnippetTemplate = $"Value is %{tempCustomKey}%"
                        }
                    ]
                }
            };

            await executor.ExecuteWorkflowAsync(item);
            Assert.Single(snippetMock.Snippets);
            Assert.Equal("Value is CustomValue123", snippetMock.Snippets[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tempCustomKey, null);
        }
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_IfCondition_BranchesToThenWhenTrue()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "If True Test",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition { Name = "targetEnv", Value = "prod" }
                ],
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.IfCondition,
                        ConditionLeft = "{targetEnv}",
                        ConditionOperator = ConditionOperator.Equals,
                        ConditionRight = "prod",
                        ConditionIgnoreCase = true,
                        HasElseBranch = true,
                        ThenSteps =
                        [
                            new WorkflowStep
                            {
                                StepType = WorkflowStepType.InjectSnippet,
                                SnippetTemplate = "Branch: THEN"
                            }
                        ],
                        ElseSteps =
                        [
                            new WorkflowStep
                            {
                                StepType = WorkflowStepType.InjectSnippet,
                                SnippetTemplate = "Branch: ELSE"
                            }
                        ]
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);
        Assert.Single(snippetMock.Snippets);
        Assert.Equal("Branch: THEN", snippetMock.Snippets[0]);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_IfCondition_BranchesToElseWhenFalse()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "If False Test",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition { Name = "targetEnv", Value = "staging" }
                ],
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.IfCondition,
                        ConditionLeft = "{targetEnv}",
                        ConditionOperator = ConditionOperator.Equals,
                        ConditionRight = "prod",
                        ConditionIgnoreCase = true,
                        HasElseBranch = true,
                        ThenSteps =
                        [
                            new WorkflowStep
                            {
                                StepType = WorkflowStepType.InjectSnippet,
                                SnippetTemplate = "Branch: THEN"
                            }
                        ],
                        ElseSteps =
                        [
                            new WorkflowStep
                            {
                                StepType = WorkflowStepType.InjectSnippet,
                                SnippetTemplate = "Branch: ELSE"
                            }
                        ]
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);
        Assert.Single(snippetMock.Snippets);
        Assert.Equal("Branch: ELSE", snippetMock.Snippets[0]);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_IfCondition_PropagatesVariablesFromBranchToMainWorkflow()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "Variable Propagation Test",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.IfCondition,
                        ConditionLeft = "10",
                        ConditionOperator = ConditionOperator.GreaterThan,
                        ConditionRight = "5",
                        ThenSteps =
                        [
                            new WorkflowStep
                            {
                                StepType = WorkflowStepType.SetVariable,
                                SetVariableName = "resultCode",
                                SetVariableValue = "OK_200"
                            }
                        ]
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.InjectSnippet,
                        SnippetTemplate = "Final Code: {resultCode}"
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);
        Assert.Single(snippetMock.Snippets);
        Assert.Equal("Final Code: OK_200", snippetMock.Snippets[0]);
    }
}
