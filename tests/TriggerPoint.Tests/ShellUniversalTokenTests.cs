using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ShellUniversalTokenTests
{
    private class MockPromptService : IPromptDialogService
    {
        public string? Response { get; set; } = "78910";
        public Task<Dictionary<string, string>?> ShowPromptDialogAsync(IReadOnlyList<PromptToken> promptTokens, string? title = null, string? subtitle = null)
        {
            if (Response == null) return Task.FromResult<Dictionary<string, string>?>(null);
            var dict = new Dictionary<string, string>();
            foreach (var t in promptTokens)
            {
                dict[t.RawTag] = Response;
            }
            return Task.FromResult<Dictionary<string, string>?>(dict);
        }
    }

    private class MockSnippetService : ISnippetService
    {
        public Task InjectSnippetAsync(string template, IntPtr targetHwnd) => Task.CompletedTask;
    }

    private class MockTelemetryService : ITelemetryService
    {
        public Task RecordExecutionAsync(Guid itemId) => Task.CompletedTask;
    }

    private class MockContextFilterService : IContextFilterService
    {
        public IntPtr LastExternalForegroundHwnd { get; set; } = IntPtr.Zero;
        public IntPtr GetForegroundWindowHandle() => IntPtr.Zero;
        public string? GetForegroundProcessName() => null;
        public string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null) => null;
        public bool ShouldExecute(TriggerItem item) => true;
        public bool ShouldExecute(TriggerItem item, IReadOnlyList<TriggerItem>? allItems) => true;
        public void SetAllItemsProvider(Func<IReadOnlyList<TriggerItem>>? provider) { }
        public List<TriggerItem> GetInheritanceChain(TriggerItem item, IReadOnlyList<TriggerItem> allItems) => [];
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsUniversalTokensAndPrompts()
    {
        var promptMock = new MockPromptService { Response = "998877" };
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var executor = new ShellActionExecutor(
            snippetMock,
            telemetryMock,
            contextMock,
            promptDialogService: promptMock);

        var item = new TriggerItem
        {
            Name = "Open Ticket URL",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload
            {
                Command = "https://example.com/tickets/{text:Ticket Number}",
                Arguments = "{text:Ticket Number}"
            }
        };

        // User cancelled prompt
        promptMock.Response = null;
        await executor.ExecuteAsync(item);
    }

    [Fact]
    public async Task ExecuteAsync_RevealInExplorer_NonExistentFile_FailsGracefullyWithoutExecuting()
    {
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var executor = new ShellActionExecutor(snippetMock, telemetryMock, contextMock);

        string? failureMessage = null;
        executor.ExecutionFailed += (item, msg) => failureMessage = msg;

        var item = new TriggerItem
        {
            Name = "Non-existent File Target",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload
            {
                Command = @"C:\NonExistentFolder_12345\FakeApp.exe"
            }
        };

        await executor.ExecuteAsync(item, ExecutionOverride.RevealInExplorer);

        Assert.NotNull(failureMessage);
        Assert.Contains("is not a local file or directory", failureMessage);
    }

    [Fact]
    public async Task ExecuteAsync_RevealInExplorer_Url_FailsGracefully()
    {
        var snippetMock = new MockSnippetService();
        var telemetryMock = new MockTelemetryService();
        var contextMock = new MockContextFilterService();

        var executor = new ShellActionExecutor(snippetMock, telemetryMock, contextMock);

        string? failureMessage = null;
        executor.ExecutionFailed += (item, msg) => failureMessage = msg;

        var item = new TriggerItem
        {
            Name = "Google Website",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload
            {
                Command = "https://www.google.com"
            }
        };

        await executor.ExecuteAsync(item, ExecutionOverride.RevealInExplorer);

        Assert.NotNull(failureMessage);
        Assert.Contains("is not a local file or directory", failureMessage);
    }
}
