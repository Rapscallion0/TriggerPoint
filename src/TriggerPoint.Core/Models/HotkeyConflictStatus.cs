using System;

namespace TriggerPoint.Core.Models;

public record HotkeyConflictStatus
{
    public HotkeyConflictType ConflictType { get; init; } = HotkeyConflictType.None;
    public Guid? ConflictingItemId { get; init; }
    public string? ConflictingActionName { get; init; }
    public string? ConflictingFolderName { get; init; }
    public string? SystemMessage { get; init; }
    public string? ProbedApplicationName { get; init; }

    public bool HasConflict => ConflictType != HotkeyConflictType.None;

    public static HotkeyConflictStatus None => new() { ConflictType = HotkeyConflictType.None };

    public static HotkeyConflictStatus CreateInternal(
        Guid conflictingItemId, 
        string actionName, 
        string folderName, 
        string hotkey) => new()
    {
        ConflictType = HotkeyConflictType.Internal,
        ConflictingItemId = conflictingItemId,
        ConflictingActionName = actionName,
        ConflictingFolderName = folderName,
        SystemMessage = $"Internal Conflict: '{hotkey}' is already assigned to '{actionName}' in folder '{folderName}'."
    };

    public static HotkeyConflictStatus CreateExternal(
        string hotkey, 
        string? probedAppName = null) => new()
    {
        ConflictType = HotkeyConflictType.External,
        ProbedApplicationName = probedAppName,
        SystemMessage = !string.IsNullOrWhiteSpace(probedAppName)
            ? $"System Conflict: '{hotkey}' is locked by '{probedAppName}'. Please rebind to an alternative key combination."
            : $"System Conflict: '{hotkey}' is currently locked by Windows or another running background application (e.g., Discord, Slack, graphics drivers, or an assigned Start Menu shortcut). Please rebind to an alternative key combination."
    };
}
