using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class LowFootprintEnhancementsTests : IDisposable
{
    private readonly string _tempDir;

    public LowFootprintEnhancementsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_LowFootprintTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private sealed class CountingSecretsVault : ISecretsVaultService
    {
        public int UnprotectCalls { get; private set; }
        public int ProtectCalls { get; private set; }

        public string Protect(string plainText)
        {
            ProtectCalls++;
            return "ENC:" + plainText;
        }

        public string Unprotect(string cipherText)
        {
            UnprotectCalls++;
            return cipherText.StartsWith("ENC:", StringComparison.Ordinal) ? cipherText[4..] : cipherText;
        }

        public bool IsProtected(string? value) =>
            !string.IsNullOrEmpty(value) && value.StartsWith("ENC:", StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonConfigRepository_UsesInMemoryCache_AndReturnsIsolatedClones()
    {
        var vault = new CountingSecretsVault();
        var repo = new JsonConfigRepository(_tempDir, vault);

        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Secret Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition { Name = "ApiKey", Value = "super-secret", IsSecret = true }
                ]
            }
        };

        await repo.SaveAsync([item]);
        Assert.Equal(1, vault.ProtectCalls);
        Assert.Equal(0, vault.UnprotectCalls);

        // Subsequent LoadAsync calls should hit the in-memory cache without calling Unprotect or re-reading disk
        var load1 = await repo.LoadAsync();
        var load2 = await repo.LoadAsync();
        Assert.Equal(0, vault.UnprotectCalls);
        Assert.Equal("super-secret", load1[0].Payload.WorkflowVariables[0].Value);

        // Mutating returned list/item must not corrupt cached copy
        load1[0].Name = "Mutated In Caller";
        var load3 = await repo.LoadAsync();
        Assert.Equal("Secret Workflow", load3[0].Name);
    }

    [Fact]
    public async Task TelemetryService_UpdatesInMemoryImmediately_AndFlushesWithoutSnapshotChurn()
    {
        var repo = new JsonConfigRepository(_tempDir);
        var actionId = Guid.NewGuid();
        var item = new TriggerItem
        {
            Id = actionId,
            Name = "Frequent Action",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "notepad.exe" }
        };

        await repo.SaveAsync([item], createHistoricalSnapshot: false);
        var snapshotsBefore = Directory.GetFiles(repo.BackupsDirectory, "triggerpoint_*.json").Length;

        using (var telemetry = new TelemetryService(repo, TimeSpan.FromMilliseconds(120)))
        {
            await telemetry.RecordExecutionAsync(actionId);
            await telemetry.RecordExecutionAsync(actionId);
            await telemetry.RecordExecutionAsync(actionId);

            // In-memory cache must immediately reflect all 3 executions before disk flush
            var immediateLoad = await repo.LoadAsync();
            Assert.Equal(3, immediateLoad.First(x => x.Id == actionId).UsageStats.LaunchCount);

            // Flush to disk
            await telemetry.FlushAsync();
        }

        // Verify disk persistence by creating a brand new repository instance
        var freshRepo = new JsonConfigRepository(_tempDir);
        var freshLoad = await freshRepo.LoadAsync();
        Assert.Equal(3, freshLoad.First(x => x.Id == actionId).UsageStats.LaunchCount);

        // Verify zero historical backup snapshots were created by telemetry flushes
        var snapshotsAfter = Directory.GetFiles(repo.BackupsDirectory, "triggerpoint_*.json").Length;
        Assert.Equal(snapshotsBefore, snapshotsAfter);
    }

    [Fact]
    public void AbbreviationTrie_ReverseSuffixSinglePass_PrefersLongestMatch_AndSupportsSpan()
    {
        var trie = new AbbreviationTrie();
        var shortItem = new TriggerItem { Name = "Short", Abbreviation = "sig", AbbreviationMode = AbbreviationTriggerMode.Immediate };
        var longItem = new TriggerItem { Name = "Long", Abbreviation = ";sig", AbbreviationMode = AbbreviationTriggerMode.Immediate };
        var ciItem = new TriggerItem { Name = "CaseInsensitive", Abbreviation = "; shrugging", AbbreviationMode = AbbreviationTriggerMode.Immediate };

        trie.Insert("sig", shortItem, isCaseSensitive: true);
        trie.Insert(";sig", longItem, isCaseSensitive: true);
        trie.Insert(";shrug", ciItem, isCaseSensitive: false);

        // Longest suffix match wins in single backward pass
        var matchedLong = trie.MatchSuffix("hello ;sig".AsSpan(), AbbreviationTriggerMode.Immediate);
        Assert.Same(longItem, matchedLong);

        var matchedShort = trie.MatchSuffix("hello sig".AsSpan(), AbbreviationTriggerMode.Immediate);
        Assert.Same(shortItem, matchedShort);

        // Case-insensitive fallback works without allocating lowercased string
        var matchedCi = trie.MatchSuffix("typing ;SHRUG".AsSpan(), AbbreviationTriggerMode.Immediate);
        Assert.Same(ciItem, matchedCi);
    }

    [Fact]
    public void ExtractContextExcerpt_NormalizesNewlinesAndTabs_WithExactHighlightParity()
    {
        string text = "Line one\r\nLine two\tTargetToken is right here\nLine three";
        int tokenIdx = text.IndexOf("TargetToken", StringComparison.Ordinal);
        var indices = Enumerable.Range(tokenIdx, "TargetToken".Length).ToList();

        var (excerpt, excerptIndices) = SearchQueryParser.ExtractContextExcerpt(text, indices, maxCharsBefore: 15, maxCharsAfter: 15);

        Assert.DoesNotContain("\r", excerpt);
        Assert.DoesNotContain("\n", excerpt);
        Assert.DoesNotContain("\t", excerpt);
        Assert.Equal("TargetToken".Length, excerptIndices.Count);

        var highlightedChars = new string(excerptIndices.Select(i => excerpt[i]).ToArray());
        Assert.Equal("TargetToken", highlightedChars);
    }

    [Theory]
    [InlineData("sqrt(16 + 9)", "5")]
    [InlineData("2 * (3 + sqrt(81))", "24")]
    [InlineData("5!", "120")]
    [InlineData("2^3^2", "512")]
    [InlineData("2(3 + 4)", "14")]
    [InlineData("round(3.14159, 2)", "3.14")]
    [InlineData("min(10, 4, 8) + max(2, 9, 5)", "13")]
    [InlineData("pow(2, 10)", "1,024")]
    public void QuickCalculator_NativeRecursiveDescent_EvaluatesComplexMathExpressions(string expr, string expected)
    {
        var result = QuickCalculatorService.TryEvaluate(expr);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.FormattedResult);
    }

    [Fact]
    public async Task PlaceholderParser_SupportsExpandedClipboardTransforms_AndChaining()
    {
        Assert.Equal("Hello World Example", await PlaceholderParser.EvaluateAsync("{clipboard:title}", () => Task.FromResult("hELLO wORLD example")));
        Assert.Equal("myVariableName", await PlaceholderParser.EvaluateAsync("{clipboard:camel}", () => Task.FromResult("My Variable_Name")));
        Assert.Equal("MyVariableName", await PlaceholderParser.EvaluateAsync("{clipboard:pascal}", () => Task.FromResult("my_variable-name")));
        Assert.Equal("my_variable_name", await PlaceholderParser.EvaluateAsync("{clipboard:snake}", () => Task.FromResult("MyVariableName")));
        Assert.Equal("my-variable-name", await PlaceholderParser.EvaluateAsync("{clipboard:kebab}", () => Task.FromResult("MyVariableName")));
        Assert.Equal("hello-world-2026", await PlaceholderParser.EvaluateAsync("{clipboard:slug}", () => Task.FromResult("Hello, World! 2026")));

        string b64 = await PlaceholderParser.EvaluateAsync("{clipboard:base64}", () => Task.FromResult("TriggerPoint"));
        Assert.Equal("VHJpZ2dlclBvaW50", b64);
        Assert.Equal("TriggerPoint", await PlaceholderParser.EvaluateAsync("{clipboard:base64decode}", () => Task.FromResult(b64)));

        Assert.Equal("{\"a\":1,\"b\":2}", await PlaceholderParser.EvaluateAsync("{clipboard:jsonmin}", () => Task.FromResult("{ \"a\": 1, \n \"b\": 2 }")));
        var prettyJson = await PlaceholderParser.EvaluateAsync("{clipboard:json}", () => Task.FromResult("{\"a\":1}"));
        Assert.Contains("\n", prettyJson);

        Assert.Equal("line1\\n\\\"quoted\\\"", await PlaceholderParser.EvaluateAsync("{clipboard:escape}", () => Task.FromResult("line1\n\"quoted\"")));
        Assert.Equal("apple\nbanana\ncherry", await PlaceholderParser.EvaluateAsync("{clipboard:sort}", () => Task.FromResult("cherry\napple\nbanana")));
        Assert.Equal("b\na\nc", await PlaceholderParser.EvaluateAsync("{clipboard:dedupe}", () => Task.FromResult("b\na\nb\nc\na")));
        Assert.Equal("12", await PlaceholderParser.EvaluateAsync("{clipboard:count}", () => Task.FromResult("TriggerPoint")));

        // Chained modifiers
        Assert.Equal("hello_world", await PlaceholderParser.EvaluateAsync("{clipboard:trim:snake}", () => Task.FromResult("  Hello World  ")));
    }

    [Fact]
    public void CommandPaletteView_RecognizesWindowManagementVirtualSystemActions()
    {
        Assert.True(CommandPaletteView.IsVirtualSystemActionId(CommandPaletteView.ToggleAlwaysOnTopVirtualId));
        Assert.True(CommandPaletteView.IsVirtualSystemActionId(CommandPaletteView.CenterActiveWindowVirtualId));
        Assert.True(CommandPaletteView.IsVirtualSystemActionId(CommandPaletteView.MoveWindowNextMonitorVirtualId));
        Assert.False(CommandPaletteView.IsVirtualSystemActionId(Guid.NewGuid()));
    }
}

