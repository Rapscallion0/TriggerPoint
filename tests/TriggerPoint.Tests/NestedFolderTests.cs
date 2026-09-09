using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Models;
using Xunit;

namespace TriggerPoint.Tests;

public class NestedFolderTests
{
    private static List<TriggerItem> CreateHelpStructure(
        out TriggerItem helpFolder,
        out TriggerItem v3Folder,
        out TriggerItem v4Folder,
        out TriggerItem v3UsersHelp,
        out TriggerItem v3MembersHelp,
        out TriggerItem v4UsersHelp,
        out TriggerItem v4MembersHelp)
    {
        helpFolder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            Name = "Help",
            ActionType = ActionType.Folder,
            PresentationMode = PresentationMode.CursorMenu
        };

        v3Folder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = helpFolder.Id,
            Name = "V3.3",
            ActionType = ActionType.Folder,
            AcceleratorKey = "3"
        };

        v4Folder = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = helpFolder.Id,
            Name = "V4",
            ActionType = ActionType.Folder,
            AcceleratorKey = "4"
        };

        v3UsersHelp = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = v3Folder.Id,
            Name = "Usershelp.chm",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "hh.exe", Arguments = "users_v3.chm" }
        };

        v3MembersHelp = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = v3Folder.Id,
            Name = "Membershelp.chm",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "hh.exe", Arguments = "members_v3.chm" }
        };

        v4UsersHelp = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = v4Folder.Id,
            Name = "Usershelp.chm",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "hh.exe", Arguments = "users_v4.chm" }
        };

        v4MembersHelp = new TriggerItem
        {
            Id = Guid.NewGuid(),
            ParentId = v4Folder.Id,
            Name = "Membershelp.chm",
            ActionType = ActionType.Shell,
            Payload = new ActionPayload { Command = "hh.exe", Arguments = "members_v4.chm" }
        };

        return new List<TriggerItem>
        {
            helpFolder,
            v3Folder,
            v4Folder,
            v3UsersHelp,
            v3MembersHelp,
            v4UsersHelp,
            v4MembersHelp
        };
    }

    [Fact]
    public void NestedFolderStructure_HasCorrectParentChildRelationships()
    {
        var items = CreateHelpStructure(
            out var help, out var v3, out var v4,
            out var v3Users, out var v3Members,
            out var v4Users, out var v4Members);

        // Help has 2 direct subfolder children
        var helpChildren = items.Where(x => x.ParentId == help.Id).ToList();
        Assert.Equal(2, helpChildren.Count);
        Assert.Contains(v3, helpChildren);
        Assert.Contains(v4, helpChildren);

        // V3.3 has 2 direct action children
        var v3Children = items.Where(x => x.ParentId == v3.Id).ToList();
        Assert.Equal(2, v3Children.Count);
        Assert.Contains(v3Users, v3Children);
        Assert.Contains(v3Members, v3Children);

        // V4 has 2 direct action children
        var v4Children = items.Where(x => x.ParentId == v4.Id).ToList();
        Assert.Equal(2, v4Children.Count);
        Assert.Contains(v4Users, v4Children);
        Assert.Contains(v4Members, v4Children);
    }

    [Fact]
    public void ScopedSearch_RecursivelyCollectsAllDescendantActions()
    {
        var items = CreateHelpStructure(
            out var help, out var v3, out var v4,
            out var v3Users, out var v3Members,
            out var v4Users, out var v4Members);

        // Emulate the CommandPalette recursive collector
        var descendantFolderIds = new HashSet<Guid> { help.Id };
        bool added;
        do
        {
            added = false;
            foreach (var item in items.Where(x => x.ActionType == ActionType.Folder && x.ParentId.HasValue))
            {
                if (descendantFolderIds.Contains(item.ParentId!.Value) && descendantFolderIds.Add(item.Id))
                {
                    added = true;
                }
            }
        } while (added);

        var scopedActions = items.Where(x => x.ActionType != ActionType.Folder 
                                          && x.ParentId.HasValue 
                                          && descendantFolderIds.Contains(x.ParentId.Value)).ToList();

        Assert.Equal(4, scopedActions.Count);
        Assert.Contains(v3Users, scopedActions);
        Assert.Contains(v3Members, scopedActions);
        Assert.Contains(v4Users, scopedActions);
        Assert.Contains(v4Members, scopedActions);
    }

    [Fact]
    public void BreadcrumbGeneration_GeneratesProperPath()
    {
        var help = new TriggerItem { Name = "Help", ActionType = ActionType.Folder };
        var v3 = new TriggerItem { Name = "V3.3", ActionType = ActionType.Folder };

        var navHistory = new Stack<TriggerItem?>();
        navHistory.Push(help);
        var currentFolder = v3;

        var pathSegments = navHistory
            .Reverse()
            .Concat(new[] { currentFolder })
            .Where(f => f != null)
            .Select(f => f!.Name.ToUpperInvariant());

        var breadcrumb = string.Join("  ›  ", pathSegments);
        Assert.Equal("HELP  ›  V3.3", breadcrumb);
    }
}
