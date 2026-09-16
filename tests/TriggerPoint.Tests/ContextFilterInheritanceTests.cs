using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ContextFilterInheritanceTests
{
    private class TestableContextFilterService : ContextFilterService
    {
        public string? MockForegroundProcess { get; set; }
        public string? MockBrowserUrl { get; set; }

        public override string? GetForegroundProcessName() => MockForegroundProcess;
        public override IntPtr GetForegroundWindowHandle() => (IntPtr)12345;
        public override string? GetActiveBrowserUrl(IntPtr hWnd, string? processName = null) => MockBrowserUrl;
    }

    [Fact]
    public void Child_Inherits_ParentFolder_Rules_By_Default()
    {
        var service = new TestableContextFilterService();
        var folderId = Guid.NewGuid();

        var parentFolder = new TriggerItem
        {
            Id = folderId,
            Name = "Dev Folder",
            ActionType = ActionType.Folder,
            ContextFilter = new ContextFilter
            {
                AllowedProcesses = ["code.exe"]
            }
        };

        var childItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Format Code",
            ActionType = ActionType.Shell,
            InheritContextFilter = true
        };

        var allItems = new List<TriggerItem> { parentFolder, childItem };

        // Test in notepad -> Should fail parent rule
        service.MockForegroundProcess = "notepad.exe";
        Assert.False(service.ShouldExecute(childItem, allItems));

        // Test in VS Code -> Should pass parent rule
        service.MockForegroundProcess = "code.exe";
        Assert.True(service.ShouldExecute(childItem, allItems));
    }

    [Fact]
    public void Child_Can_Opt_Out_Of_Parent_Inheritance()
    {
        var service = new TestableContextFilterService();
        var folderId = Guid.NewGuid();

        var parentFolder = new TriggerItem
        {
            Id = folderId,
            Name = "Dev Folder",
            ActionType = ActionType.Folder,
            ContextFilter = new ContextFilter
            {
                AllowedProcesses = ["code.exe"]
            }
        };

        var childItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "General Utility",
            ActionType = ActionType.Shell,
            InheritContextFilter = false // Opt-out
        };

        var allItems = new List<TriggerItem> { parentFolder, childItem };

        // Test in notepad -> Parent rule would fail, but inheritance is disabled so it succeeds
        service.MockForegroundProcess = "notepad.exe";
        Assert.True(service.ShouldExecute(childItem, allItems));
    }

    [Fact]
    public void Child_And_Parent_Rules_Are_Both_Enforced_When_Inheriting()
    {
        var service = new TestableContextFilterService();
        var folderId = Guid.NewGuid();

        var parentFolder = new TriggerItem
        {
            Id = folderId,
            Name = "Web Dev Folder",
            ActionType = ActionType.Folder,
            ContextFilter = new ContextFilter
            {
                AllowedProcesses = ["chrome.exe"]
            }
        };

        var childItem = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = folderId,
            Name = "Deploy Prod",
            ActionType = ActionType.Shell,
            InheritContextFilter = true,
            ContextFilter = new ContextFilter
            {
                AllowedUrls = ["*dashboard.example.com*"]
            }
        };

        var allItems = new List<TriggerItem> { parentFolder, childItem };

        // In Chrome on general site -> Parent succeeds, Child URL rule fails
        service.MockForegroundProcess = "chrome.exe";
        service.MockBrowserUrl = "https://google.com";
        Assert.False(service.ShouldExecute(childItem, allItems));

        // In Chrome on target site -> Both succeed
        service.MockForegroundProcess = "chrome.exe";
        service.MockBrowserUrl = "https://dashboard.example.com/status";
        Assert.True(service.ShouldExecute(childItem, allItems));

        // In Firefox on target site -> Parent process rule fails
        service.MockForegroundProcess = "firefox.exe";
        service.MockBrowserUrl = "https://dashboard.example.com/status";
        Assert.False(service.ShouldExecute(childItem, allItems));
    }

    [Fact]
    public void MultiTier_Hierarchy_Inherits_All_Ancestors()
    {
        var service = new TestableContextFilterService();
        var grandparent = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Engineering",
            ActionType = ActionType.Folder,
            ContextFilter = new ContextFilter { AllowedProcesses = ["code.exe", "devenv.exe"] }
        };

        var parent = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = grandparent.Id,
            Name = "VS Code Only",
            ActionType = ActionType.Folder,
            InheritContextFilter = true,
            ContextFilter = new ContextFilter { AllowedProcesses = ["code.exe"] }
        };

        var child = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            Name = "Format Document",
            ActionType = ActionType.Shell,
            InheritContextFilter = true
        };

        var allItems = new List<TriggerItem> { grandparent, parent, child };

        // In devenv.exe -> Grandparent passes, but Parent fails -> Child fails
        service.MockForegroundProcess = "devenv.exe";
        Assert.False(service.ShouldExecute(child, allItems));

        // In code.exe -> Grandparent passes, Parent passes, Child passes
        service.MockForegroundProcess = "code.exe";
        Assert.True(service.ShouldExecute(child, allItems));

        // In notepad.exe -> Grandparent fails -> Child fails
        service.MockForegroundProcess = "notepad.exe";
        Assert.False(service.ShouldExecute(child, allItems));
    }

    [Fact]
    public void GetInheritanceChain_Discovers_Ancestors_With_Active_Rules()
    {
        var service = new TestableContextFilterService();

        var grandparent = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Grandparent",
            ActionType = ActionType.Folder,
            ContextFilter = new ContextFilter { AllowedProcesses = ["code.exe"] }
        };

        var emptyFolder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = grandparent.Id,
            Name = "Empty Rules Folder",
            ActionType = ActionType.Folder,
            InheritContextFilter = true
            // ContextFilter is empty
        };

        var child = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = emptyFolder.Id,
            Name = "Action",
            ActionType = ActionType.Shell,
            InheritContextFilter = true
        };

        var allItems = new List<TriggerItem> { grandparent, emptyFolder, child };

        var chain = service.GetInheritanceChain(child, allItems);

        // emptyFolder has no rules, so chain should skip emptyFolder and contain grandparent
        Assert.Single(chain);
        Assert.Equal(grandparent.Id, chain[0].Id);
    }

    [Fact]
    public void CircularReference_Does_Not_Throw_Or_Hang()
    {
        var service = new TestableContextFilterService();
        service.MockForegroundProcess = "notepad.exe";

        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        var itemA = new TriggerItem
        {
            Id = idA,
            ParentId = idB,
            Name = "Item A",
            InheritContextFilter = true,
            ContextFilter = new ContextFilter { AllowedProcesses = ["code.exe"] }
        };

        var itemB = new TriggerItem
        {
            Id = idB,
            ParentId = idA,
            Name = "Item B",
            InheritContextFilter = true,
            ContextFilter = new ContextFilter { AllowedProcesses = ["code.exe"] }
        };

        var allItems = new List<TriggerItem> { itemA, itemB };

        // Should return false safely without StackOverflowException
        bool result = service.ShouldExecute(itemA, allItems);
        Assert.False(result);

        // GetInheritanceChain should terminate without hang
        var chain = service.GetInheritanceChain(itemA, allItems);
        Assert.NotNull(chain);
    }

    [Fact]
    public void TriggerItem_Clone_Preserves_InheritContextFilter()
    {
        var original = new TriggerItem
        {
            Name = "Test",
            InheritContextFilter = false
        };

        var clone = original.Clone();
        Assert.False(clone.InheritContextFilter);

        original.InheritContextFilter = true;
        clone = original.Clone();
        Assert.True(clone.InheritContextFilter);
    }
}
