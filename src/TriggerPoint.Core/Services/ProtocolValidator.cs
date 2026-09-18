using System;
using System.Collections.Generic;

namespace TriggerPoint.Core.Services;

public static class ProtocolValidator
{
    private static readonly HashSet<string> HazardousSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript",
        "vbscript",
        "data",
        "ms-appinstaller",
        "ms-msdt",
        "search-ms",
        "shell"
    };

    public static bool IsSafeUrl(string? url, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            reason = "URL cannot be empty.";
            return false;
        }

        var trimmed = url.Trim();
        var colonIdx = trimmed.IndexOf(':');
        if (colonIdx > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var scheme = trimmed[..colonIdx].Trim().ToLowerInvariant();

            // Windows drive letters (e.g. C:\...) are local file paths, not URL schemes
            if (scheme.Length == 1 && char.IsLetter(scheme[0]))
            {
                return true;
            }

            if (HazardousSchemes.Contains(scheme))
            {
                reason = $"URL scheme '{scheme}:' is blocked by security policy.";
                return false;
            }
        }

        return true;
    }

    public static bool IsSafeUrl(string? url) => IsSafeUrl(url, out _);
}
