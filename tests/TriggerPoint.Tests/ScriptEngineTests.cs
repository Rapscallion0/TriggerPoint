using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ScriptEngineTests
{
    private class MockPromptService : IPromptDialogService
    {
        public string? NextResponse { get; set; } = "456789";

        public Task<Dictionary<string, string>?> ShowPromptDialogAsync(IReadOnlyList<PromptToken> promptTokens, string? title = null, string? subtitle = null)
        {
            if (NextResponse == null) return Task.FromResult<Dictionary<string, string>?>(null);
            var dict = new Dictionary<string, string>();
            foreach (var t in promptTokens)
            {
                dict[t.RawTag] = NextResponse;
            }
            return Task.FromResult<Dictionary<string, string>?>(dict);
        }
    }

    private class MockConfirmationService : IConfirmationDialogService
    {
        public bool NextResult { get; set; } = true;

        public Task<bool> ShowConfirmationAsync(string message, string title = "TriggerPoint Confirmation", string confirmButtonText = "Confirm", string cancelButtonText = "Cancel")
        {
            return Task.FromResult(NextResult);
        }
    }

    private class MockToastService : IToastNotificationService
    {
        public List<string> Notifications { get; } = [];
        public void ShowSuccess(string title, string message) => Notifications.Add($"SUCCESS:{title}:{message}");
        public void ShowError(string title, string message) => Notifications.Add($"ERROR:{title}:{message}");
        public void ShowWarning(string title, string message) => Notifications.Add($"WARN:{title}:{message}");
    }

    private class MockSnippetService : ISnippetService
    {
        public List<string> Injected { get; } = [];
        public Task InjectSnippetAsync(string template, IntPtr targetHwnd, SnippetContentType contentType = SnippetContentType.PlainText, string? rtfContent = null)
        {
            Injected.Add(template);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteAsync_RunsBasicJavaScript()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();

        var engine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);

        var script = @"
            const a = 10;
            const b = 20;
            tp.vars.sum = (a + b).toString();
        ";

        var result = await engine.ExecuteAsync(script);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("30", result.Variables["sum"]);
    }

    [Fact]
    public async Task ExecuteAsync_InteractivePromptAndConfirmWork()
    {
        var promptMock = new MockPromptService { NextResponse = "ticket-999" };
        var confirmMock = new MockConfirmationService { NextResult = true };
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();

        var engine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);

        var script = @"
            const ticket = await tp.prompt('Enter ticket:');
            tp.vars.ticket = ticket;
            const confirmed = await tp.confirm('Create folder?');
            tp.vars.confirmed = confirmed.toString();
        ";

        var result = await engine.ExecuteAsync(script);

        Assert.True(result.Success);
        Assert.Equal("ticket-999", result.Variables["ticket"]);
        Assert.Equal("true", result.Variables["confirmed"]);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesCancellationGracefully()
    {
        var promptMock = new MockPromptService();
        var confirmMock = new MockConfirmationService();
        var toastMock = new MockToastService();
        var snippetMock = new MockSnippetService();

        var engine = new JintScriptEngineService(promptMock, confirmMock, toastMock, snippetMock);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        var script = @"
            await tp.delay(5000);
            tp.vars.done = 'true';
        ";

        var result = await engine.ExecuteAsync(script, cancellationToken: cts.Token);
        Assert.False(result.Success);
    }
}
