using System;
using System.Collections.Generic;

namespace TriggerPoint.Core.Models;

public sealed class ActionPayload
{
    // Shell execution properties
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; } = false;

    // Snippet execution properties
    public string SnippetTemplate { get; set; } = string.Empty;
}

public sealed class ContextFilter
{
    public List<string> AllowedProcesses { get; set; } = [];
    public List<string> ExcludedProcesses { get; set; } = [];
    public List<string> AllowedUrls { get; set; } = [];
    public List<string> ExcludedUrls { get; set; } = [];

    public bool IsActiveForProcess(string? processName)
    {
        return IsActive(processName, null);
    }

    public bool IsActive(string? processName, string? activeUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            var cleanName = processName.Trim();
            if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = cleanName[..^4];
            }

            // If excluded processes match, reject
            foreach (var excl in ExcludedProcesses)
            {
                var target = excl.Trim();
                if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    target = target[..^4];
                if (string.Equals(cleanName, target, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // If allowed processes is specified, must match one
            if (AllowedProcesses.Count > 0)
            {
                bool matchedAny = false;
                foreach (var allow in AllowedProcesses)
                {
                    var target = allow.Trim();
                    if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        target = target[..^4];
                    if (string.Equals(cleanName, target, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedAny = true;
                        break;
                    }
                }
                if (!matchedAny) return false;
            }
        }
        else if (AllowedProcesses.Count > 0)
        {
            return false;
        }

        // Evaluate URL rules if specified
        if (!string.IsNullOrWhiteSpace(activeUrl))
        {
            foreach (var excl in ExcludedUrls)
            {
                if (MatchesWildcard(activeUrl, excl.Trim())) return false;
            }

            if (AllowedUrls.Count > 0)
            {
                bool matchedUrl = false;
                foreach (var allow in AllowedUrls)
                {
                    if (MatchesWildcard(activeUrl, allow.Trim()))
                    {
                        matchedUrl = true;
                        break;
                    }
                }
                if (!matchedUrl) return false;
            }
        }
        else if (AllowedUrls.Count > 0 && IsKnownBrowser(processName))
        {
            // Browser focused but active URL not matched
            return false;
        }

        return true;
    }

    public static bool MatchesWildcard(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        pattern = pattern.Trim();
        text = text.Trim();

        // If no wildcard symbols, simple substring search
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        // 1. Direct match
        if (CheckRegexMatch(text, pattern)) return true;

        // 2. Scheme-stripped match (e.g. text "http://localhost:3000" matches "localhost:*")
        int schemeIdx = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            string stripped = text[(schemeIdx + 3)..];
            if (CheckRegexMatch(stripped, pattern)) return true;
        }

        // 3. Implied leading wildcard for domain patterns (e.g. "github.com/*" matches "https://github.com/repo")
        if (!pattern.StartsWith('*') && !pattern.Contains("://"))
        {
            if (CheckRegexMatch(text, "*" + pattern)) return true;
        }

        return false;
    }

    private static bool CheckRegexMatch(string text, string pattern)
    {
        string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";

        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                text,
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(50));
        }
        catch
        {
            return text.Contains(pattern.Replace("*", ""), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsKnownBrowser(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var p = processName.Trim().ToLowerInvariant();
        if (p.EndsWith(".exe")) p = p[..^4];
        return p is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi" or "arc";
    }
}

public sealed class UsageStats
{
    public long LaunchCount { get; set; } = 0;
    public DateTime? LastExecutedUtc { get; set; }
}
