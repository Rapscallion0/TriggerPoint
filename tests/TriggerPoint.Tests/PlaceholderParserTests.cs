using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class PlaceholderParserTests
{
    [Fact]
    public async Task EvaluateAsync_ReplacesDateAndTimeTokens()
    {
        var fixedTime = new DateTime(2026, 9, 8, 14, 30, 45);
        var template = "Report on {date} at {time} (full: {datetime})";

        var result = await PlaceholderParser.EvaluateAsync(
            template,
            referenceTime: fixedTime);

        Assert.Equal("Report on 2026-09-08 at 14:30:45 (full: 2026-09-08 14:30:45)", result);
    }

    [Fact]
    public async Task EvaluateAsync_ReplacesClipboardToken()
    {
        var template = "Pasting: '{clipboard}'";
        var result = await PlaceholderParser.EvaluateAsync(
            template,
            clipboardProvider: () => Task.FromResult("CopiedContent"));

        Assert.Equal("Pasting: 'CopiedContent'", result);
    }

    [Fact]
    public void ExtractPromptTokens_ParsesChoiceWithFriendlyLabelsAndValues()
    {
        var template = "Deploying to {choice:Environment|Production=prod,Staging=stg,Local Dev}";
        var tokens = PlaceholderParser.ExtractPromptTokens(template);

        Assert.Single(tokens);
        var token = tokens[0];
        Assert.Equal(TokenType.PromptChoice, token.Type);
        Assert.Equal("Environment", token.Label);
        Assert.Equal(3, token.Choices.Count);

        Assert.Equal("Production", token.Choices[0].DisplayName);
        Assert.Equal("prod", token.Choices[0].Value);

        Assert.Equal("Staging", token.Choices[1].DisplayName);
        Assert.Equal("stg", token.Choices[1].Value);

        // Simple option without '=' uses the same string for display and value
        Assert.Equal("Local Dev", token.Choices[2].DisplayName);
        Assert.Equal("Local Dev", token.Choices[2].Value);
    }

    [Fact]
    public void ExtractPromptTokens_ParsesNumberWithBounds()
    {
        var template = "Set retry count: {number:Retries|1,10}";
        var tokens = PlaceholderParser.ExtractPromptTokens(template);

        Assert.Single(tokens);
        var token = tokens[0];
        Assert.Equal(TokenType.PromptNumber, token.Type);
        Assert.Equal("Retries", token.Label);
        Assert.Equal(1.0, token.MinNumber);
        Assert.Equal(10.0, token.MaxNumber);
    }

    [Fact]
    public void ExtractPromptTokens_ParsesTextMultilineAndDatePicker()
    {
        var template = "Task: {text:Summary}\nNotes: {multiline:Details}\nDue: {date_picker:DueDate}";
        var tokens = PlaceholderParser.ExtractPromptTokens(template);

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenType.PromptText, tokens[0].Type);
        Assert.Equal("Summary", tokens[0].Label);

        Assert.Equal(TokenType.PromptMultiline, tokens[1].Type);
        Assert.Equal("Details", tokens[1].Label);

        Assert.Equal(TokenType.PromptDatePicker, tokens[2].Type);
        Assert.Equal("DueDate", tokens[2].Label);
    }

    [Fact]
    public async Task EvaluateAsync_SubstitutesInteractivePromptResponses()
    {
        var template = "Hello {text:Name}, your environment is {choice:Env|Prod=p,Dev=d}";
        var responses = new Dictionary<string, string>
        {
            ["text:Name"] = "Alice",
            ["choice:Env|Prod=p,Dev=d"] = "p" // Emits underlying value
        };

        var result = await PlaceholderParser.EvaluateAsync(template, promptResponses: responses);
        Assert.Equal("Hello Alice, your environment is p", result);
    }

    [Fact]
    public void ProcessCursorPosition_CalculatesOffsetCorrectly()
    {
        var textWithCursor = "function hello() {\n\t{cursor}\n}";
        var (cleanText, offset) = PlaceholderParser.ProcessCursorPosition(textWithCursor);

        Assert.Equal("function hello() {\n\t\n}", cleanText);
        // The characters remaining after {cursor} are "\n}" -> 2 characters
        Assert.Equal(2, offset);
    }

    [Fact]
    public async Task EvaluateAsync_CustomDateFormatsAndOffsets()
    {
        var fixedTime = new DateTime(2026, 9, 8, 14, 30, 45);

        var template = "{date:MM/dd/yyyy} | {date:yyyyMMdd} | {time:hh:mm tt} | {datetime:yyyy-MM-ddTHH:mm:ss}";
        var result = await PlaceholderParser.EvaluateAsync(template, referenceTime: fixedTime);
        Assert.Equal("09/08/2026 | 20260908 | 02:30 PM | 2026-09-08T14:30:45", result);

        // Offsets
        var offsetTemplate = "{date:+1d} | {date:-1d} | {tomorrow} | {yesterday} | {date:+1d:MM/dd/yyyy}";
        var offsetResult = await PlaceholderParser.EvaluateAsync(offsetTemplate, referenceTime: fixedTime);
        Assert.Equal("2026-09-09 | 2026-09-07 | 2026-09-09 | 2026-09-07 | 09/09/2026", offsetResult);

        // Invalid format specifier falls back gracefully
        var fallbackResult = await PlaceholderParser.EvaluateAsync("{date:X}", referenceTime: fixedTime);
        Assert.Equal("2026-09-08", fallbackResult);
    }

    [Fact]
    public async Task EvaluateAsync_ReplacesGuidTokens()
    {
        var template = "{guid} | {guid:N} | {guid:upper} | {guid:B}";
        var result = await PlaceholderParser.EvaluateAsync(template);
        var parts = result.Split(" | ");

        Assert.Equal(4, parts.Length);
        Assert.True(Guid.TryParse(parts[0], out _));
        Assert.Equal(36, parts[0].Length);
        Assert.Equal(32, parts[1].Length); // N format: no hyphens
        Assert.DoesNotContain("-", parts[1]);
        Assert.Equal(parts[2], parts[2].ToUpperInvariant());
        Assert.StartsWith("{", parts[3]);
        Assert.EndsWith("}", parts[3]);
    }

    [Fact]
    public async Task EvaluateAsync_ReplacesSystemAndEnvironmentTokens()
    {
        Environment.SetEnvironmentVariable("TRIGGERPOINT_TEST_VAR", "UnitTestingValue");
        try
        {
            var template = "User: {username}, Host: {machine}, Env: {env:TRIGGERPOINT_TEST_VAR}";
            var result = await PlaceholderParser.EvaluateAsync(template);

            Assert.Contains($"User: {Environment.UserName}", result);
            Assert.Contains($"Host: {Environment.MachineName}", result);
            Assert.Contains("Env: UnitTestingValue", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRIGGERPOINT_TEST_VAR", null);
        }
    }

    [Fact]
    public async Task EvaluateAsync_ReplacesClipboardModifiers()
    {
        var template = "{clipboard:trim} | {clipboard:upper} | {clipboard:lower} | {clipboard:urlencode}";
        var result = await PlaceholderParser.EvaluateAsync(
            template,
            clipboardProvider: () => Task.FromResult("  Hello World & Co  "));

        Assert.Equal("Hello World & Co |   HELLO WORLD & CO   |   hello world & co   | %20%20Hello%20World%20%26%20Co%20%20", result);
    }

    [Fact]
    public async Task EvaluateAsync_ReplacesContextTokens()
    {
        var template = "Window: [{active_window}] Proc: [{active_process}]";
        var result = await PlaceholderParser.EvaluateAsync(
            template,
            activeWindowTitle: "Visual Studio Code",
            activeProcessName: "Code.exe");

        Assert.Equal("Window: [Visual Studio Code] Proc: [Code.exe]", result);
    }

    [Fact]
    public void ExtractPromptTokens_ParsesDefaultsForTextChoiceAndNumber()
    {
        var template = "Name: {text:Full Name|Jane Doe}, Env: {choice:Tier|Prod=p,Staging*=s}, Count: {number:Retries|1,10|5}";
        var tokens = PlaceholderParser.ExtractPromptTokens(template);

        Assert.Equal(3, tokens.Count);
        Assert.Equal("Jane Doe", tokens[0].DefaultValue);
        Assert.Equal("s", tokens[1].DefaultValue);
        Assert.Equal("5", tokens[2].DefaultValue);
    }

    [Fact]
    public async Task EvaluateAsync_FallsBackToPromptDefaultWhenNotAnswered()
    {
        var template = "Welcome, {text:Name|Guest}!";
        var result = await PlaceholderParser.EvaluateAsync(template);
        Assert.Equal("Welcome, Guest!", result);
    }

    [Fact]
    public void ExtractPromptTokens_ParsesDatePickerFormatAndOffset()
    {
        var template = "Due: {date_picker:Due Date|MM/dd/yyyy|+7d}";
        var tokens = PlaceholderParser.ExtractPromptTokens(template);

        Assert.Single(tokens);
        var token = tokens[0];
        Assert.Equal(TokenType.PromptDatePicker, token.Type);
        Assert.Equal("Due Date", token.Label);
        Assert.Equal("MM/dd/yyyy", token.DateFormat);

        // Verify default value calculates +7 days in MM/dd/yyyy format
        var expectedDefault = DateTime.Today.AddDays(7).ToString("MM/dd/yyyy");
        Assert.Equal(expectedDefault, token.DefaultValue);
    }

    [Fact]
    public async Task EvaluateAsync_DatePickerEvaluatesWithFormatAndOffset()
    {
        var fixedTime = new DateTime(2026, 9, 8, 10, 0, 0);
        var template = "Deadline: {date_picker:Deadline|dd/MM/yyyy|+1d}";

        var result = await PlaceholderParser.EvaluateAsync(template, referenceTime: fixedTime);
        Assert.Equal("Deadline: 09/09/2026", result);
    }

    [Fact]
    public async Task EvaluateAsync_DatePickerPrefersSubmittedPromptResponse()
    {
        var template = "Meeting: {date_picker:Meeting Date|yyyy-MM-dd}";
        var responses = new Dictionary<string, string>
        {
            ["date_picker:Meeting Date|yyyy-MM-dd"] = "2026-12-25"
        };

        var result = await PlaceholderParser.EvaluateAsync(template, promptResponses: responses);
        Assert.Equal("Meeting: 2026-12-25", result);
    }
}

