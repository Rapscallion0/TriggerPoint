using System;
using System.IO;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ActionShortcutRelevanceHelperTests
{
    [Fact]
    public void IsWebUrl_DetectsHttpAndHttpsCorrectly()
    {
        Assert.True(ActionShortcutRelevanceHelper.IsWebUrl("http://example.com"));
        Assert.True(ActionShortcutRelevanceHelper.IsWebUrl("https://github.com/Rapscallion0/TriggerPoint"));
        Assert.True(ActionShortcutRelevanceHelper.IsWebUrl("HTTPS://WWW.GOOGLE.COM"));
        Assert.True(ActionShortcutRelevanceHelper.IsWebUrl("ftp://files.example.com"));
        
        Assert.False(ActionShortcutRelevanceHelper.IsWebUrl("notepad.exe"));
        Assert.False(ActionShortcutRelevanceHelper.IsWebUrl(@"C:\Windows\System32\notepad.exe"));
        Assert.False(ActionShortcutRelevanceHelper.IsWebUrl(""));
        Assert.False(ActionShortcutRelevanceHelper.IsWebUrl(null));
    }

    [Fact]
    public void CanRevealInExplorer_SnippetAndFolderAndWorkflow_ReturnsFalse()
    {
        var snippet = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "My Snippet",
            ActionType = ActionType.Snippet,
            Payload = new ActionPayload { SnippetTemplate = "Hello" }
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRevealInExplorer(snippet));

        var folder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Work Tools",
            ActionType = ActionType.Folder
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRevealInExplorer(folder));

        var workflow = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Deploy Workflow",
            ActionType = ActionType.Workflow
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRevealInExplorer(workflow));

        Assert.False(ActionShortcutRelevanceHelper.CanRevealInExplorer(null));
    }

    [Fact]
    public void CanRevealInExplorer_WebUrl_ReturnsFalse()
    {
        var urlItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Google",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "https://www.google.com" }
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRevealInExplorer(urlItem));
    }

    [Fact]
    public void CanRevealInExplorer_LocalExistingFileOrDir_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var item = new TriggerItem
            {
                Id = Guid.NewGuid(),
                Name = "Temp File",
                ActionType = ActionType.Shell,
                Payload = new ActionPayload { Command = tempFile }
            };
            Assert.True(ActionShortcutRelevanceHelper.CanRevealInExplorer(item));

            // Also test quoted path
            item.Payload.Command = $"\"{tempFile}\"";
            Assert.True(ActionShortcutRelevanceHelper.CanRevealInExplorer(item));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CanRevealInExplorer_ResolvesExecutableFromPath()
    {
        // cmd.exe is always present on Windows PATH
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Command Prompt",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "cmd.exe" }
        };
        Assert.True(ActionShortcutRelevanceHelper.CanRevealInExplorer(item));
    }

    [Fact]
    public void CanRevealInExplorer_NonExistentCommand_ReturnsFalse()
    {
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Imaginary Executable",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "this_executable_does_not_exist_987654321.exe" }
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRevealInExplorer(item));
    }

    [Fact]
    public void CanRunAsAdmin_SnippetAndFolder_ReturnsFalse()
    {
        var snippet = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Snippet",
            ActionType = ActionType.Snippet
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRunAsAdmin(snippet));

        var folder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Folder",
            ActionType = ActionType.Folder
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRunAsAdmin(folder));

        Assert.False(ActionShortcutRelevanceHelper.CanRunAsAdmin(null));
    }

    [Fact]
    public void CanRunAsAdmin_WebUrl_ReturnsFalse()
    {
        var urlItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "URL",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "https://example.com" }
        };
        Assert.False(ActionShortcutRelevanceHelper.CanRunAsAdmin(urlItem));
    }

    [Fact]
    public void CanRunAsAdmin_LocalShell_ReturnsTrueWhenNotConfiguredAdmin()
    {
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Terminal",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "cmd.exe", RunAsAdmin = false }
        };
        Assert.True(ActionShortcutRelevanceHelper.CanRunAsAdmin(item));
    }

    [Fact]
    public void CanRunAsAdmin_LocalShell_ReturnsFalseWhenAlreadyConfiguredAdmin()
    {
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Elevated Terminal",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "cmd.exe", RunAsAdmin = true }
        };
        // Since RunAsAdmin is already true, normal Enter executes elevated,
        // so the Ctrl+Enter modifier hint is not needed.
        Assert.False(ActionShortcutRelevanceHelper.CanRunAsAdmin(item));
        Assert.Equal("Run as Admin", ActionShortcutRelevanceHelper.GetPrimaryActionVerb(item));
    }

    [Fact]
    public void CanRunAsAdmin_Workflow_ReturnsTrueWhenNotConfiguredAdmin()
    {
        var workflow = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "My Flow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload { RunAsAdmin = false }
        };
        Assert.True(ActionShortcutRelevanceHelper.CanRunAsAdmin(workflow));
        Assert.Equal("Run", ActionShortcutRelevanceHelper.GetPrimaryActionVerb(workflow));
    }

    [Fact]
    public void GetPrimaryActionVerb_ReturnsAccurateLabels()
    {
        var folder = new TriggerItem { ActionType = ActionType.Folder };
        Assert.Equal("Open Submenu", ActionShortcutRelevanceHelper.GetPrimaryActionVerb(folder));

        var snippet = new TriggerItem { ActionType = ActionType.Snippet };
        Assert.Equal("Paste", ActionShortcutRelevanceHelper.GetPrimaryActionVerb(snippet));

        var url = new TriggerItem { ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "https://bing.com" } };
        Assert.Equal("Open URL", ActionShortcutRelevanceHelper.GetPrimaryActionVerb(url));

        var app = new TriggerItem { ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "calc.exe" } };
        Assert.Equal("Run", ActionShortcutRelevanceHelper.GetPrimaryActionVerb(app));

        var adminApp = new TriggerItem { ActionType = ActionType.Shell, Payload = new ActionPayload { Command = "regedit.exe", RunAsAdmin = true } };
        Assert.Equal("Run as Admin", ActionShortcutRelevanceHelper.GetPrimaryActionVerb(adminApp));
    }

    [Fact]
    public void GetContextualShortcuts_BuildsAccurateDescriptors()
    {
        var snippet = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Snippet",
            ActionType = ActionType.Snippet
        };
        var sc = ActionShortcutRelevanceHelper.GetContextualShortcuts(snippet, isInsideSubfolder: true);
        Assert.Equal("Paste", sc.PrimaryActionVerb);
        Assert.False(sc.CanRunAsAdmin);
        Assert.False(sc.CanRevealInExplorer);
        Assert.True(sc.CanOpenSettings);
        Assert.Equal("◀ Back (Esc)", sc.NavigationBackText);
        Assert.Equal("Copy Snippet", sc.CopyLabel);
    }
}
