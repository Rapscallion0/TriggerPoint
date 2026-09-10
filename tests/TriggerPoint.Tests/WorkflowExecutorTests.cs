using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class WorkflowExecutorTests
{
    private class MockPromptService : IPromptDialogService
    {
        public string? Response { get; set; } = "777";
        public Dictionary<string, string>? CannedResponses { get; set; }
        public string? LastTitle { get; private set; }
        public string? LastSubtitle { get; private set; }
        public IReadOnlyList<PromptToken>? LastTokens { get; private set; }

        public Task<Dictionary<string, string>?> ShowPromptDialogAsync(
            IReadOnlyList<PromptToken> promptTokens,
            string? title = null,
            string? subtitle = null)
        {
            LastTokens = promptTokens;
            LastTitle = title;
            LastSubtitle = subtitle;

            if (Response == null && CannedResponses == null) return Task.FromResult<Dictionary<string, string>?>(null);
            var dict = new Dictionary<string, string>();
            foreach (var t in promptTokens)
            {
                if (CannedResponses != null && CannedResponses.TryGetValue(t.RawTag, out var val))
                {
                    dict[t.RawTag] = val;
                }
                else
                {
                    dict[t.RawTag] = Response ?? string.Empty;
                }
            }
            return Task.FromResult<Dictionary<string, string>?>(dict);
        }
    }

    private class MockConfirmationService : IConfirmationDialogService
    {
        public bool Result { get; set; } = true;
        public Task<bool> ShowConfirmationAsync(string message, string title = "TriggerPoint Confirmation", string confirmButtonText = "Confirm", string cancelButtonText = "Cancel")
        {
            return Task.FromResult(Result);
        }
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
        public Task InjectSnippetAsync(string template, IntPtr targetHwnd)
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
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PropagatesVariablesAcrossSteps()
    {
        var promptMock = new MockPromptService { Response = "ticket-42" };
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var tempDir = Path.Combine(Path.GetTempPath(), "TriggerPointTest_" + Guid.NewGuid().ToString("N"));

        try
        {
            var item = new TriggerItem
            {
                Name = "Test Workflow",
                ActionType = ActionType.Workflow,
                Payload = new ActionPayload
                {
                    WorkflowMode = WorkflowMode.Visual,
                    WorkflowSteps =
                    [
                        new WorkflowStep
                        {
                            StepType = WorkflowStepType.Prompt,
                            VariableName = "ticket",
                            PromptLabel = "Ticket",
                            OnError = StepErrorPolicy.StopWorkflow
                        },
                        new WorkflowStep
                        {
                            StepType = WorkflowStepType.EnsureDirectory,
                            DirectoryPath = Path.Combine(tempDir, "{ticket}"),
                            DirectoryMissingPolicy = DirectoryMissingPolicy.CreateSilently,
                            OnError = StepErrorPolicy.StopWorkflow
                        },
                        new WorkflowStep
                        {
                            StepType = WorkflowStepType.InjectSnippet,
                            SnippetTemplate = "Opened ticket {ticket}",
                            OnError = StepErrorPolicy.StopWorkflow
                        }
                    ]
                }
            };

            await executor.ExecuteWorkflowAsync(item);

            var createdPath = Path.Combine(tempDir, "ticket-42");
            Assert.True(Directory.Exists(createdPath));
            Assert.Single(snippetMock.Snippets);
            Assert.Equal("Opened ticket ticket-42", snippetMock.Snippets[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_StopsOnCancelledPromptWhenStopWorkflow()
    {
        var promptMock = new MockPromptService { Response = null }; // User cancels
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "Cancelled Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        VariableName = "ticket",
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.InjectSnippet,
                        SnippetTemplate = "Should not be executed",
                        OnError = StepErrorPolicy.StopWorkflow
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);
        Assert.Empty(snippetMock.Snippets);
    }

    private class MockBrowserDetectionService : IBrowserDetectionService
    {
        public List<(string Url, string? Browser, string? Profile, bool NewWindow)> LaunchedUrls { get; } = [];
        public IReadOnlyList<BrowserInfo> GetInstalledBrowsers() =>
        [
            new BrowserInfo { Id = "chrome", Name = "Google Chrome", Kind = BrowserKind.Chromium },
            new BrowserInfo { Id = "firefox", Name = "Mozilla Firefox", Kind = BrowserKind.Firefox }
        ];
        public IReadOnlyList<BrowserProfileInfo> GetProfiles(string browserId) =>
        [
            new BrowserProfileInfo { Id = "Profile 1", DisplayName = "Work (Profile 1)" }
        ];
        public bool LaunchUrl(string url, string? browserId = null, string? profileId = null, bool newWindow = false)
        {
            LaunchedUrls.Add((url, browserId, profileId, newWindow));
            return true;
        }
    }

    [Fact]
    public async Task ExecuteSingleStepAsync_ExecutesSingleStepWithBrowserTargeting()
    {
        var promptMock = new MockPromptService { Response = "12345" };
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();
        var browserMock = new MockBrowserDetectionService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock, browserMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock, browserMock);

        var parentItem = new TriggerItem
        {
            Name = "Parent Workflow",
            ActionType = ActionType.Workflow
        };

        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.OpenUrl,
            Url = "https://example.com/tickets/{ticket}",
            BrowserTarget = "chrome",
            BrowserProfile = "Profile 1"
        };

        var result = await executor.ExecuteSingleStepAsync(step, parentItem);

        Assert.True(result);
        Assert.Single(browserMock.LaunchedUrls);
        Assert.Equal("https://example.com/tickets/12345", browserMock.LaunchedUrls[0].Url);
        Assert.Equal("chrome", browserMock.LaunchedUrls[0].Browser);
        Assert.Equal("Profile 1", browserMock.LaunchedUrls[0].Profile);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ExecutesActionStepViaHandler()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var targetActionId = Guid.NewGuid();
        var handlerCalledWith = Guid.Empty;

        executor.ActionExecutionHandler = (id, hwnd) =>
        {
            handlerCalledWith = id;
            return Task.FromResult(true);
        };

        var workflowItem = new TriggerItem
        {
            Name = "Master Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.ExecuteAction,
                        Name = "Call Sub Action",
                        TargetItemId = targetActionId
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(workflowItem);

        Assert.Equal(targetActionId, handlerCalledWith);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PreventsRecursionLoop()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var loopItem = new TriggerItem
        {
            Name = "Recursive Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload()
        };

        // Step calls itself
        loopItem.Payload.WorkflowSteps =
        [
            new WorkflowStep
            {
                StepType = WorkflowStepType.ExecuteAction,
                Name = "Call Self",
                TargetItemId = loopItem.Id
            }
        ];

        bool failedEventFired = false;
        executor.WorkflowFailed += (item, err) =>
        {
            failedEventFired = true;
            Assert.Contains("loop detected", err, StringComparison.OrdinalIgnoreCase);
        };

        // Handler calls ExecuteWorkflowAsync to simulate a circular chain
        executor.ActionExecutionHandler = async (id, hwnd) =>
        {
            if (id == loopItem.Id)
            {
                await executor.ExecuteWorkflowAsync(loopItem);
                return true;
            }
            return false;
        };

        await executor.ExecuteWorkflowAsync(loopItem);

        Assert.True(failedEventFired);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_MultiFieldPrompt_CollectsAllVariablesAndPassesCustomHeaders()
    {
        var promptMock = new MockPromptService
        {
            CannedResponses = new Dictionary<string, string>
            {
                ["ticket"] = "4040",
                ["customer"] = "Alice Doe",
                ["prio"] = "High"
            }
        };
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "Multi Prompt Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        PromptTitle = "Ticket Intake",
                        PromptSubtitle = "Please fill in ticket details",
                        PromptFields =
                        [
                            new WorkflowPromptField { VariableName = "ticket", Label = "Ticket Number", DefaultValue = "1000" },
                            new WorkflowPromptField { VariableName = "customer", Label = "Customer Name", DefaultValue = "John" },
                            new WorkflowPromptField { VariableName = "prio", Label = "Priority", DefaultValue = "Normal" }
                        ]
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.InjectSnippet,
                        SnippetTemplate = "Ticket #{ticket} for {customer} [{prio}]"
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);

        Assert.Equal("Ticket Intake", promptMock.LastTitle);
        Assert.Equal("Please fill in ticket details", promptMock.LastSubtitle);
        Assert.NotNull(promptMock.LastTokens);
        Assert.Equal(3, promptMock.LastTokens.Count);

        Assert.Single(snippetMock.Snippets);
        Assert.Equal("Ticket #4040 for Alice Doe [High]", snippetMock.Snippets[0]);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PromptFields_PropagatesNumberAndDatePickerMetadata()
    {
        var promptMock = new MockPromptService
        {
            CannedResponses = new Dictionary<string, string>
            {
                ["quantity"] = "15",
                ["deliveryDate"] = "2026-12-25"
            }
        };
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var scriptEngine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);
        var executor = new WorkflowExecutor(scriptEngine, promptMock, confirmMock, toastMock, snippetMock, telemetryMock, contextMock);

        var item = new TriggerItem
        {
            Name = "Order Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowMode = WorkflowMode.Visual,
                WorkflowSteps =
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        PromptFields =
                        [
                            new WorkflowPromptField
                            {
                                VariableName = "quantity",
                                Label = "Order Quantity",
                                Type = TokenType.PromptNumber,
                                MinNumber = 1,
                                MaxNumber = 50,
                                DefaultValue = "10"
                            },
                            new WorkflowPromptField
                            {
                                VariableName = "deliveryDate",
                                Label = "Delivery Date",
                                Type = TokenType.PromptDatePicker,
                                DateFormat = "yyyy-MM-dd",
                                DefaultValue = "2026-10-01"
                            }
                        ]
                    }
                ]
            }
        };

        await executor.ExecuteWorkflowAsync(item);

        Assert.NotNull(promptMock.LastTokens);
        Assert.Equal(2, promptMock.LastTokens.Count);

        var numToken = promptMock.LastTokens[0];
        Assert.Equal(TokenType.PromptNumber, numToken.Type);
        Assert.Equal(1, numToken.MinNumber);
        Assert.Equal(50, numToken.MaxNumber);
        Assert.Equal("10", numToken.DefaultValue);

        var dateToken = promptMock.LastTokens[1];
        Assert.Equal(TokenType.PromptDatePicker, dateToken.Type);
        Assert.Equal("yyyy-MM-dd", dateToken.DateFormat);
    }
}

