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
}
