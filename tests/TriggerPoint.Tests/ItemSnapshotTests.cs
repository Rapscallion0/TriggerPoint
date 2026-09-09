using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using Xunit;

namespace TriggerPoint.Tests;

public class ItemSnapshotTests
{
    [Fact]
    public void Clone_CreatesIndependentDeepCopy_ForSnapshot()
    {
        var item = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Original Item",
            Description = "Original Desc",
            ActionType = ActionType.Shell,
            PresentationMode = PresentationMode.Direct,
            Hotkey = new ShortcutBinding(ModifierKeys.Control | ModifierKeys.Alt, 84, "T"),
            AcceleratorKey = "1",
            Payload = new ActionPayload
            {
                Command = "powershell.exe",
                Arguments = "-NoExit",
                WorkingDirectory = @"C:\Temp",
                RunAsAdmin = true
            },
            ContextFilter = new ContextFilter
            {
                AllowedProcesses = ["cmd.exe", "wt.exe"],
                ExcludedProcesses = ["notepad.exe"],
                AllowedUrls = ["*github.com*"],
                ExcludedUrls = ["*youtube.com*"]
            }
        };

        var snapshot = item.Clone();

        // Mutate original
        item.Name = "Modified Name";
        item.Description = "Modified Desc";
        item.Payload.Command = "cmd.exe";
        item.Payload.RunAsAdmin = false;
        item.ContextFilter.AllowedProcesses.Add("bash.exe");

        // Assert snapshot retained pre-edit state
        Assert.Equal("Original Item", snapshot.Name);
        Assert.Equal("Original Desc", snapshot.Description);
        Assert.Equal("powershell.exe", snapshot.Payload.Command);
        Assert.True(snapshot.Payload.RunAsAdmin);
        Assert.Equal(2, snapshot.ContextFilter.AllowedProcesses.Count);
        Assert.DoesNotContain("bash.exe", snapshot.ContextFilter.AllowedProcesses);
    }
}
