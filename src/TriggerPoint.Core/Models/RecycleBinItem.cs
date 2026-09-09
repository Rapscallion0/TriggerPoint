using System;

namespace TriggerPoint.Core.Models;

public class RecycleBinItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? OriginalParentId { get; set; }
    public string OriginalPath { get; set; } = string.Empty;
    public TriggerItem Item { get; set; } = new();
}
