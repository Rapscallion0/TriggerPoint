using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TriggerPoint.Core.Models;

public enum ClipboardItemType
{
    PlainText = 0,
    RichText = 1,
    Image = 2,
    FileList = 3
}

public sealed class ClipboardHistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClipboardItemType Type { get; set; } = ClipboardItemType.PlainText;
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string? PlainText { get; set; }
    public string? RichText { get; set; }
    public string? ImagePath { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public List<string>? FilePaths { get; set; }
    public long SizeBytes { get; set; }
    public string PreviewText { get; set; } = string.Empty;
    public bool IsPinned { get; set; } = false;
    public string? SourceProcessName { get; set; }

    [JsonIgnore]
    public string DisplayTitle => Type switch
    {
        ClipboardItemType.Image => $"Image ({ImageWidth}×{ImageHeight})",
        ClipboardItemType.FileList => FilePaths != null && FilePaths.Count > 0
            ? $"{FilePaths.Count} file(s): {System.IO.Path.GetFileName(FilePaths[0])}"
            : "Files",
        _ => string.IsNullOrWhiteSpace(PreviewText) ? "Empty Text" : PreviewText
    };

    [JsonIgnore]
    public string FormattedSize
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
            return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}

public sealed class ClipboardHistoryDataStore
{
    public int Version { get; set; } = 1;
    public List<ClipboardHistoryItem> Items { get; set; } = [];
}
