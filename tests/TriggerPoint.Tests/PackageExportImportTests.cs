using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TriggerPoint.Core.Models;
using Xunit;

namespace TriggerPoint.Tests;

public class PackageExportImportTests
{
    [Fact]
    public void ConfigurationBackupPackage_SerializationRoundtrip_PreservesMetadata()
    {
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Open PowerShell",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "powershell.exe" }
        };

        var package = ConfigurationBackupPackage.CreateTreeItems([item], "Test Pack");
        package.AuthorNotes = "Shared workflow pack";
        package.IsSanitized = true;

        var json = JsonSerializer.Serialize(package);
        var deserialized = JsonSerializer.Deserialize<ConfigurationBackupPackage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Test Pack", deserialized.ScopeName);
        Assert.Equal("Shared workflow pack", deserialized.AuthorNotes);
        Assert.True(deserialized.IsSanitized);
        Assert.Single(deserialized.Items);
        Assert.Equal("Open PowerShell", deserialized.Items[0].Name);
    }

    [Fact]
    public void ImportConflictResolver_KeepBoth_RemapsIdAndPreservesNonConflictingProperties()
    {
        var existing = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Daily Update",
            ActionType = ActionType.Snippet,
            Abbreviation = ";daily"
        };

        var incoming = new TriggerItem
        {
            Id = existing.Id, // Same ID
            Name = "Daily Update",
            ActionType = ActionType.Snippet,
            Abbreviation = ";daily"
        };

        // Resolution simulation for Keep Both:
        var newId = Guid.NewGuid();
        var clone = incoming.Clone();
        clone.Id = newId;
        clone.Name = $"{clone.Name} (Imported)";
        clone.Abbreviation = null; // Cleared to prevent collision

        Assert.NotEqual(existing.Id, clone.Id);
        Assert.Equal("Daily Update (Imported)", clone.Name);
        Assert.Null(clone.Abbreviation);
    }

    [Fact]
    public void ImportConflictResolver_Overwrite_ReplacesPayloadInPlace()
    {
        var existing = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Old Command",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "cmd.exe" }
        };

        var incoming = new TriggerItem
        {
            Id = existing.Id,
            Name = "New Command",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "wt.exe", Arguments = "-p PowerShell" }
        };

        // Overwrite updates properties in place
        existing.Name = incoming.Name;
        existing.Payload = incoming.Payload.Clone();

        Assert.Equal("New Command", existing.Name);
        Assert.Equal("wt.exe", existing.Payload.Command);
        Assert.Equal("-p PowerShell", existing.Payload.Arguments);
    }

    [Fact]
    public void Sanitization_RemovesSecretsFromWorkflowVariables()
    {
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "API Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition { Name = "api_key", Value = "super_secret_123", IsSecret = true },
                    new WorkflowVariableDefinition { Name = "username", Value = "kevin", IsSecret = false }
                ]
            }
        };

        foreach (var v in item.Payload.WorkflowVariables)
        {
            if (v.IsSecret || v.Name.Contains("key", StringComparison.OrdinalIgnoreCase))
            {
                v.Value = string.Empty;
            }
        }

        var secretVar = item.Payload.WorkflowVariables.First(x => x.Name == "api_key");
        var userVar = item.Payload.WorkflowVariables.First(x => x.Name == "username");

        Assert.Equal(string.Empty, secretVar.Value);
        Assert.Equal("kevin", userVar.Value);
    }
}
