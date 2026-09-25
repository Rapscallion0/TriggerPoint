using System;
using System.Collections.Generic;
using System.Linq;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static class HotkeyRegistryValidator
{
    public static Dictionary<Guid, HotkeyConflictStatus> ValidateTier1Conflicts(
        IEnumerable<TriggerItem> allItems, 
        AppSettings? appSettings = null)
    {
        var conflicts = new Dictionary<Guid, HotkeyConflictStatus>();
        var itemsList = allItems.ToList();

        // Build folder lookup for friendly folder names
        var folderLookup = itemsList
            .Where(x => x.ActionType == ActionType.Folder)
            .ToDictionary(x => x.Id, x => x.Name);

        var registeredMap = new Dictionary<ShortcutBinding, List<TriggerItem>>();

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

            // Check against application-level shortcuts
            if (appSettings != null)
            {
                if (appSettings.OpenSettingsHotkey != null && item.Hotkey == appSettings.OpenSettingsHotkey)
                {
                    var conflict = HotkeyConflictStatus.CreateInternal(
                        Guid.Empty,
                        "Open Action Manager",
                        "Application Settings",
                        item.Hotkey.DisplayText);
                    conflicts[item.Id] = conflict;
                    item.ConflictStatus = conflict;
                    continue;
                }

                if (appSettings.CommandPaletteHotkey != null && item.Hotkey == appSettings.CommandPaletteHotkey)
                {
                    var conflict = HotkeyConflictStatus.CreateInternal(
                        Guid.Empty,
                        "Command Palette",
                        "Application Settings",
                        item.Hotkey.DisplayText);
                    conflicts[item.Id] = conflict;
                    item.ConflictStatus = conflict;
                    continue;
                }

                if (appSettings.CheatSheetHotkey != null && item.Hotkey == appSettings.CheatSheetHotkey)
                {
                    var conflict = HotkeyConflictStatus.CreateInternal(
                        Guid.Empty,
                        "Cheat Sheet HUD",
                        "Application Settings",
                        item.Hotkey.DisplayText);
                    conflicts[item.Id] = conflict;
                    item.ConflictStatus = conflict;
                    continue;
                }
            }

            if (registeredMap.TryGetValue(item.Hotkey, out var existingList))
            {
                var existingItem = existingList.FirstOrDefault(ex => ContextFiltersOverlap(item, ex));
                if (existingItem != null)
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
                    existingList.Add(item);
                }
            }
            else
            {
                registeredMap[item.Hotkey] = [item];
            }
        }

        return conflicts;
    }

    public static bool ContextFiltersOverlap(TriggerItem itemA, TriggerItem itemB)
    {
        var filterA = itemA.ContextFilter;
        var filterB = itemB.ContextFilter;

        // If either item has no filter or no AllowedProcesses, it applies globally (to all processes)
        if (filterA == null || filterA.AllowedProcesses.Count == 0 ||
            filterB == null || filterB.AllowedProcesses.Count == 0)
        {
            return true; // Overlap! Global vs Specific or Global vs Global
        }

        // Both items specify AllowedProcesses. Check if their sets intersect.
        var setA = new HashSet<string>(filterA.AllowedProcesses.Select(p => p.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        foreach (var proc in filterB.AllowedProcesses)
        {
            if (setA.Contains(proc.Trim().ToLowerInvariant()))
            {
                return true; // Overlap! Both claim the same process!
            }
        }

        // Disjoint sets of allowed processes -> NO conflict! Multiplexed!
        return false;
    }

    public static HotkeyConflictStatus? CheckPotentialConflict(
        TriggerItem candidateItem,
        ShortcutBinding? newBinding,
        IEnumerable<TriggerItem> allItems,
        AppSettings? appSettings = null)
    {
        if (newBinding == null || newBinding.IsEmpty) return null;

        if (appSettings != null)
        {
            if (appSettings.OpenSettingsHotkey != null && appSettings.OpenSettingsHotkey == newBinding)
            {
                return HotkeyConflictStatus.CreateInternal(
                    Guid.Empty,
                    "Open Action Manager",
                    "Application Settings",
                    newBinding.DisplayText);
            }

            if (appSettings.CommandPaletteHotkey != null && appSettings.CommandPaletteHotkey == newBinding)
            {
                return HotkeyConflictStatus.CreateInternal(
                    Guid.Empty,
                    "Command Palette",
                    "Application Settings",
                    newBinding.DisplayText);
            }

            if (appSettings.CheatSheetHotkey != null && appSettings.CheatSheetHotkey == newBinding)
            {
                return HotkeyConflictStatus.CreateInternal(
                    Guid.Empty,
                    "Cheat Sheet HUD",
                    "Application Settings",
                    newBinding.DisplayText);
            }
        }

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
                if (!ContextFiltersOverlap(candidateItem, item))
                {
                    continue; // Non-overlapping context filters -> Allowed!
                }

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

    public static HotkeyConflictStatus? CheckApplicationHotkeyConflict(
        string appActionName,
        ShortcutBinding? binding,
        IEnumerable<TriggerItem> allItems,
        params (string Name, ShortcutBinding? Binding)[] otherAppBindings)
    {
        if (binding == null || binding.IsEmpty) return null;

        // 1. Check against other app bindings
        foreach (var (otherName, otherBinding) in otherAppBindings)
        {
            if (otherBinding != null && !otherBinding.IsEmpty && otherBinding == binding)
            {
                return HotkeyConflictStatus.CreateInternal(
                    Guid.Empty,
                    otherName,
                    "Application Settings",
                    binding.DisplayText);
            }
        }

        // 2. Check against tree items
        var folderLookup = allItems
            .Where(x => x.ActionType == ActionType.Folder)
            .ToDictionary(x => x.Id, x => x.Name);

        foreach (var item in allItems)
        {
            if (!item.IsEnabled || item.Hotkey == null || item.Hotkey.IsEmpty) continue;

            if (item.Hotkey == binding)
            {
                string folder = item.ParentId.HasValue && folderLookup.TryGetValue(item.ParentId.Value, out var fName)
                    ? fName
                    : "Root";

                return HotkeyConflictStatus.CreateInternal(
                    item.Id,
                    item.Name,
                    folder,
                    binding.DisplayText);
            }
        }

        return null;
    }

    public static (bool IsValid, string? ErrorMessage) ValidateApplicationHotkeys(
        ShortcutBinding? openSettingsHotkey,
        ShortcutBinding? commandPaletteHotkey,
        IEnumerable<TriggerItem> allItems,
        ShortcutBinding? cheatSheetHotkey = null)
    {
        if (openSettingsHotkey != null && !openSettingsHotkey.IsEmpty)
        {
            var conflict = CheckApplicationHotkeyConflict(
                "Open Action Manager", 
                openSettingsHotkey, 
                allItems, 
                ("Command Palette", commandPaletteHotkey),
                ("Cheat Sheet HUD", cheatSheetHotkey));

            if (conflict != null)
            {
                return (false, $"The shortcut '{openSettingsHotkey.DisplayText}' for 'Open Action Manager' conflicts with '{conflict.ConflictingActionName}' ({conflict.ConflictingFolderName}).");
            }
        }

        if (commandPaletteHotkey != null && !commandPaletteHotkey.IsEmpty)
        {
            var conflict = CheckApplicationHotkeyConflict(
                "Command Palette", 
                commandPaletteHotkey, 
                allItems, 
                ("Open Action Manager", openSettingsHotkey),
                ("Cheat Sheet HUD", cheatSheetHotkey));

            if (conflict != null)
            {
                return (false, $"The shortcut '{commandPaletteHotkey.DisplayText}' for 'Command Palette' conflicts with '{conflict.ConflictingActionName}' ({conflict.ConflictingFolderName}).");
            }
        }

        if (cheatSheetHotkey != null && !cheatSheetHotkey.IsEmpty)
        {
            var conflict = CheckApplicationHotkeyConflict(
                "Cheat Sheet HUD", 
                cheatSheetHotkey, 
                allItems, 
                ("Open Action Manager", openSettingsHotkey),
                ("Command Palette", commandPaletteHotkey));

            if (conflict != null)
            {
                return (false, $"The shortcut '{cheatSheetHotkey.DisplayText}' for 'Cheat Sheet HUD' conflicts with '{conflict.ConflictingActionName}' ({conflict.ConflictingFolderName}).");
            }
        }

        return (true, null);
    }
}
