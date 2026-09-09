using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Models;
using Xunit;

namespace TriggerPoint.Tests;

public class TreeDeletionTests
{
    private static TriggerItem? ResolveFocusCandidate(List<TriggerItem> items, TriggerItem itemToDelete)
    {
        var siblings = items
            .Where(x => x.ParentId == itemToDelete.ParentId)
            .OrderBy(x => x.OrderIndex)
            .ToList();

        int currentIndex = siblings.FindIndex(x => x.Id == itemToDelete.Id);

        // 1. Next sibling
        if (currentIndex >= 0 && currentIndex < siblings.Count - 1)
        {
            return siblings[currentIndex + 1];
        }

        // 2. Previous sibling
        if (currentIndex > 0)
        {
            return siblings[currentIndex - 1];
        }

        // 3. Parent folder (if inside a folder)
        if (itemToDelete.ParentId.HasValue)
        {
            var parent = items.FirstOrDefault(x => x.Id == itemToDelete.ParentId.Value);
            if (parent != null) return parent;
        }

        // 4. Any remaining root item
        var remainingRoot = items
            .Where(x => !x.ParentId.HasValue && x.Id != itemToDelete.Id)
            .OrderBy(x => x.OrderIndex)
            .FirstOrDefault();
        if (remainingRoot != null) return remainingRoot;

        // 5. Any remaining item at all
        return items.FirstOrDefault(x => x.Id != itemToDelete.Id);
    }

    [Fact]
    public void ResolveFocusCandidate_SelectsNextSibling_WhenAvailable()
    {
        var folderId = Guid.NewGuid();
        var item1 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folderId, Name = "Item 1", OrderIndex = 0 };
        var item2 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folderId, Name = "Item 2", OrderIndex = 1 };
        var item3 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folderId, Name = "Item 3", OrderIndex = 2 };
        var items = new List<TriggerItem> { item1, item2, item3 };

        var candidate = ResolveFocusCandidate(items, item1);

        Assert.NotNull(candidate);
        Assert.Equal("Item 2", candidate.Name);
    }

    [Fact]
    public void ResolveFocusCandidate_SelectsPreviousSibling_WhenLastChildDeleted()
    {
        var folderId = Guid.NewGuid();
        var item1 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folderId, Name = "Item 1", OrderIndex = 0 };
        var item2 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folderId, Name = "Item 2", OrderIndex = 1 };
        var items = new List<TriggerItem> { item1, item2 };

        var candidate = ResolveFocusCandidate(items, item2);

        Assert.NotNull(candidate);
        Assert.Equal("Item 1", candidate.Name);
    }

    [Fact]
    public void ResolveFocusCandidate_SelectsParentFolder_WhenOnlyChildDeleted()
    {
        var folder = new TriggerItem { Id = Guid.NewGuid(), Name = "My Folder", ActionType = ActionType.Folder, OrderIndex = 0 };
        var child = new TriggerItem { Id = Guid.NewGuid(), ParentId = folder.Id, Name = "Only Child", OrderIndex = 1 };
        var items = new List<TriggerItem> { folder, child };

        var candidate = ResolveFocusCandidate(items, child);

        Assert.NotNull(candidate);
        Assert.Equal(folder.Id, candidate.Id);
    }

    [Fact]
    public void ResolveFocusCandidate_ReturnsNull_WhenLastItemInTreeDeleted()
    {
        var item = new TriggerItem { Id = Guid.NewGuid(), Name = "Sole Item", OrderIndex = 0 };
        var items = new List<TriggerItem> { item };

        var candidate = ResolveFocusCandidate(items, item);

        Assert.Null(candidate);
    }

    [Fact]
    public void FolderDeletion_MoveToRoot_UnparentsDirectChildren()
    {
        var folder = new TriggerItem { Id = Guid.NewGuid(), Name = "Workflows", ActionType = ActionType.Folder };
        var child1 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folder.Id, Name = "Action 1" };
        var child2 = new TriggerItem { Id = Guid.NewGuid(), ParentId = folder.Id, Name = "Action 2" };
        var items = new List<TriggerItem> { folder, child1, child2 };

        // Option 2 Move to Root logic
        var directChildren = items.Where(x => x.ParentId == folder.Id).ToList();
        foreach (var child in directChildren)
        {
            child.ParentId = null;
        }
        items.Remove(folder);

        Assert.DoesNotContain(folder, items);
        Assert.Contains(child1, items);
        Assert.Contains(child2, items);
        Assert.Null(child1.ParentId);
        Assert.Null(child2.ParentId);
    }

    [Fact]
    public void FolderDeletion_DeleteAll_CascadesToAllDescendants()
    {
        var parentFolder = new TriggerItem { Id = Guid.NewGuid(), Name = "Parent", ActionType = ActionType.Folder };
        var subFolder = new TriggerItem { Id = Guid.NewGuid(), ParentId = parentFolder.Id, Name = "SubFolder", ActionType = ActionType.Folder };
        var actionInSub = new TriggerItem { Id = Guid.NewGuid(), ParentId = subFolder.Id, Name = "Action" };
        var items = new List<TriggerItem> { parentFolder, subFolder, actionInSub };

        var descendants = new List<TriggerItem>();
        void CollectDescendants(Guid folderId)
        {
            var children = items.Where(x => x.ParentId == folderId).ToList();
            descendants.AddRange(children);
            foreach (var child in children.Where(c => c.ActionType == ActionType.Folder))
            {
                CollectDescendants(child.Id);
            }
        }
        CollectDescendants(parentFolder.Id);

        var toDelete = new List<TriggerItem> { parentFolder };
        toDelete.AddRange(descendants);
        foreach (var item in toDelete)
        {
            items.Remove(item);
        }

        Assert.Empty(items);
    }
}
