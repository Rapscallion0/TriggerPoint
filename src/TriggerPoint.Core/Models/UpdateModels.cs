using System;
using System.Collections.Generic;

namespace TriggerPoint.Core.Models;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string TagName { get; set; } = "";
    public string Title { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "TriggerPointSetup.exe";
    public long FileSizeBytes { get; set; }
    public string? Sha256Hash { get; set; }
    public string HtmlUrl { get; set; } = "";
    public bool IsPreRelease { get; set; }
}

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "";
    public UpdateInfo? LatestUpdate { get; set; }
    public List<UpdateInfo> IntermediateReleases { get; set; } = [];
    public string CombinedChangelog { get; set; } = "";
    public bool IsIgnored { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}

public class UpdateDownloadProgress
{
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public double Percent => TotalBytes > 0 ? Math.Clamp((double)BytesReceived / TotalBytes * 100.0, 0.0, 100.0) : 0.0;
    public double BytesPerSecond { get; set; }
}
