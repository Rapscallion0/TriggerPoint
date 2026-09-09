using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static class HotkeyRegistryValidator
{
    public static Dictionary<Guid, HotkeyConflictStatus> ValidateTier1Conflicts(IEnumerable<TriggerItem> allItems)
    {
        var conflicts = new Dictionary<Guid, HotkeyConflictStatus>();
        var itemsList = allItems.ToList();

        // Build folder lookup for friendly folder names
        var folderLookup = itemsList
            .Where(x => x.ActionType == ActionType.Folder)
            .ToDictionary(x => x.Id, x => x.Name);

        var registeredMap = new Dictionary<ShortcutBinding, TriggerItem>();

        foreach (var item in itemsList)
        {
            item.ConflictStatus = HotkeyConflictStatus.None;
        }

        foreach (var item in itemsList)
        {
            if (item.Hotkey == null || item.Hotkey.IsEmpty || !item.IsEnabled)
            {
                continue;
            }

            if (registeredMap.TryGetValue(item.Hotkey, out var existingItem))
            {
                // Internal Collision!
                string existingFolder = existingItem.ParentId.HasValue && folderLookup.TryGetValue(existingItem.ParentId.Value, out var efName)
                    ? efName
                    : "Root";

                string currentFolder = item.ParentId.HasValue && folderLookup.TryGetValue(item.ParentId.Value, out var cfName)
                    ? cfName
                    : "Root";

                // Flag the current item
                var currentConflict = HotkeyConflictStatus.CreateInternal(
                    existingItem.Id,
                    existingItem.Name,
                    existingFolder,
                    item.Hotkey.DisplayText);

                conflicts[item.Id] = currentConflict;
                item.ConflictStatus = currentConflict;

                // Also flag existing item if not already flagged
                if (!conflicts.ContainsKey(existingItem.Id))
                {
                    var existingConflict = HotkeyConflictStatus.CreateInternal(
                        item.Id,
                        item.Name,
                        currentFolder,
                        item.Hotkey.DisplayText);

                    conflicts[existingItem.Id] = existingConflict;
                    existingItem.ConflictStatus = existingConflict;
                }
            }
            else
            {
                registeredMap[item.Hotkey] = item;
            }
        }

        return conflicts;
    }

    public static HotkeyConflictStatus? CheckPotentialConflict(
        TriggerItem candidateItem,
        ShortcutBinding? newBinding,
        IEnumerable<TriggerItem> allItems)
    {
        if (newBinding == null || newBinding.IsEmpty) return null;

        var itemsList = allItems.ToList();
        var folderLookup = itemsList
            .Where(x => x.ActionType == ActionType.Folder)
            .ToDictionary(x => x.Id, x => x.Name);

        foreach (var item in itemsList)
        {
            if (item.Id == candidateItem.Id || !item.IsEnabled || item.Hotkey == null || item.Hotkey.IsEmpty)
                continue;

            if (item.Hotkey == newBinding)
            {
                string folder = item.ParentId.HasValue && folderLookup.TryGetValue(item.ParentId.Value, out var fName)
                    ? fName
                    : "Root";

                return HotkeyConflictStatus.CreateInternal(
                    item.Id,
                    item.Name,
                    folder,
                    newBinding.DisplayText);
            }
        }

        return null;
    }
}
