using System;

namespace TriggerPoint.Core.Models;

public enum AbbreviationConflictType
{
    None,
    ExactDuplicate,
    PrefixShadow
}

public sealed class AbbreviationConflictDiagnostic
{
    public AbbreviationConflictType Type { get; set; } = AbbreviationConflictType.None;
    public Guid ConflictingItemId { get; set; }
    public string ConflictingItemName { get; set; } = string.Empty;
    public string ConflictingAbbreviation { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
