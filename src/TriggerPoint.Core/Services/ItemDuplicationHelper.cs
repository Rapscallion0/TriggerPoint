using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static class ItemDuplicationHelper
{
    public static string GenerateDuplicateName(string originalName, IEnumerable<string> existingNames)
    {
        var existingSet = new HashSet<string>(existingNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        string cleanName = string.IsNullOrWhiteSpace(originalName) ? "New Action" : originalName.Trim();

        var match = Regex.Match(cleanName, @"^(.*?)(?:\s*-\s*Copy(?:\s*\((\d+)\))?)?$", RegexOptions.IgnoreCase);
        string baseName = match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
            ? match.Groups[1].Value.Trim()
            : cleanName;

        // First attempt: "{baseName} - Copy"
        string candidate = $"{baseName} - Copy";
        if (!existingSet.Contains(candidate))
        {
            return candidate;
        }

        // Subsequent attempts: "{baseName} - Copy (2)", (3), ...
        int counter = 2;
        while (true)
        {
            candidate = $"{baseName} - Copy ({counter})";
            if (!existingSet.Contains(candidate))
            {
                return candidate;
            }
            counter++;
        }
    }

    public static TriggerItem CreateDuplicate(TriggerItem original, IEnumerable<TriggerItem> allItems)
    {
        ArgumentNullException.ThrowIfNull(original);

        var siblings = (allItems ?? Enumerable.Empty<TriggerItem>()).Where(x => x.ParentId == original.ParentId);
        string newName = GenerateDuplicateName(original.Name, siblings.Select(s => s.Name));

        var duplicate = original.Clone();
        duplicate.Id = Guid.NewGuid();
        duplicate.Name = newName;
        duplicate.Hotkey = null; // Clear hotkey to avoid collision
        duplicate.ConflictStatus = HotkeyConflictStatus.None;
        duplicate.ParentId = original.ParentId;
        duplicate.OrderIndex = original.OrderIndex + 1;
        duplicate.UsageStats = new UsageStats();

        return duplicate;
    }

    public static void InsertDuplicateIntoList(List<TriggerItem> items, TriggerItem original, TriggerItem duplicate)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(duplicate);

        // Shift subsequent siblings' OrderIndex
        foreach (var sibling in items.Where(x => x.ParentId == original.ParentId && x.OrderIndex > original.OrderIndex))
        {
            sibling.OrderIndex++;
        }

        items.Add(duplicate);
    }

    public static List<TriggerItem> DuplicateItemOrFolder(TriggerItem itemToDuplicate, List<TriggerItem> items)
    {
        ArgumentNullException.ThrowIfNull(itemToDuplicate);
        ArgumentNullException.ThrowIfNull(items);

        if (itemToDuplicate.ActionType != ActionType.Folder)
        {
            var duplicate = CreateDuplicate(itemToDuplicate, items);
            InsertDuplicateIntoList(items, itemToDuplicate, duplicate);
            return [duplicate];
        }

        // Folder duplication with full hierarchy
        var siblings = items.Where(x => x.ParentId == itemToDuplicate.ParentId);
        string newFolderName = GenerateDuplicateName(itemToDuplicate.Name, siblings.Select(s => s.Name));

        var duplicateFolder = itemToDuplicate.Clone();
        duplicateFolder.Id = Guid.NewGuid();
        duplicateFolder.Name = newFolderName;
        duplicateFolder.Hotkey = null;
        duplicateFolder.ConflictStatus = HotkeyConflictStatus.None;
        duplicateFolder.ParentId = itemToDuplicate.ParentId;
        duplicateFolder.OrderIndex = itemToDuplicate.OrderIndex + 1;
        duplicateFolder.UsageStats = new UsageStats();

        // Shift subsequent siblings of original folder
        foreach (var sibling in items.Where(x => x.ParentId == itemToDuplicate.ParentId && x.OrderIndex > itemToDuplicate.OrderIndex))
        {
            sibling.OrderIndex++;
        }

        var clonedItems = new List<TriggerItem> { duplicateFolder };

        void CloneDescendants(Guid oldParentId, Guid newParentId)
        {
            var children = items.Where(x => x.ParentId == oldParentId).OrderBy(x => x.OrderIndex).ToList();
            foreach (var child in children)
            {
                var clonedChild = child.Clone();
                clonedChild.Id = Guid.NewGuid();
                clonedChild.ParentId = newParentId;
                clonedChild.Hotkey = null; // Clear hotkey to prevent collisions
                clonedChild.ConflictStatus = HotkeyConflictStatus.None;
                clonedChild.UsageStats = new UsageStats();
                clonedChild.OrderIndex = child.OrderIndex;

                clonedItems.Add(clonedChild);

                if (child.ActionType == ActionType.Folder)
                {
                    CloneDescendants(child.Id, clonedChild.Id);
                }
            }
        }

        CloneDescendants(itemToDuplicate.Id, duplicateFolder.Id);
        items.AddRange(clonedItems);

        return clonedItems;
    }
}
