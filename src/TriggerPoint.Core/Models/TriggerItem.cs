using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TriggerPoint.Core.Models;

public sealed class TriggerItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public string? AcceleratorKey { get; set; } // e.g. "1" - "9", "A" - "Z" for cursor menu
    public ShortcutBinding? Hotkey { get; set; }
    public PresentationMode PresentationMode { get; set; } = PresentationMode.Direct;
    public FolderAutoNumberMode AutoNumberMode { get; set; } = FolderAutoNumberMode.Off;
    public ActionType ActionType { get; set; } = ActionType.Shell;
    public ActionPayload Payload { get; set; } = new();
    public ContextFilter ContextFilter { get; set; } = new();
    public bool InheritContextFilter { get; set; } = true;
    public UsageStats UsageStats { get; set; } = new();
    public int OrderIndex { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public bool IsExpanded { get; set; } = true;

    [JsonIgnore]
    public HotkeyConflictStatus ConflictStatus { get; set; } = HotkeyConflictStatus.None;

    [JsonIgnore]
    public List<TriggerItem> Children { get; set; } = [];

    public TriggerItem Clone()
    {
        return new TriggerItem
        {
            Id = Id,
            ParentId = ParentId,
            Name = Name,
            Description = Description,
            IconPath = IconPath,
            AcceleratorKey = AcceleratorKey,
            Hotkey = Hotkey != null ? new ShortcutBinding(Hotkey.Modifiers, Hotkey.VirtualKey, Hotkey.KeyName) : null,
            PresentationMode = PresentationMode,
            AutoNumberMode = AutoNumberMode,
            ActionType = ActionType,
            IsExpanded = IsExpanded,
            Payload = Payload?.Clone() ?? new ActionPayload(),
            ContextFilter = new ContextFilter
            {
                AllowedProcesses = [.. ContextFilter.AllowedProcesses],
                ExcludedProcesses = [.. ContextFilter.ExcludedProcesses],
                AllowedUrls = [.. ContextFilter.AllowedUrls],
                ExcludedUrls = [.. ContextFilter.ExcludedUrls]
            },
            UsageStats = new UsageStats
            {
                LaunchCount = UsageStats.LaunchCount,
                LastExecutedUtc = UsageStats.LastExecutedUtc
            },
            OrderIndex = OrderIndex,
            IsEnabled = IsEnabled,
            InheritContextFilter = InheritContextFilter,
            ConflictStatus = ConflictStatus
        };
    }
}
