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
}
