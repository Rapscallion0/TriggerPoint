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
    public string? TargetDisplay { get; set; }
    public bool OpenInNewWindow { get; set; } = false;

    // Snippet execution properties
    public string SnippetTemplate { get; set; } = string.Empty;

    // Workflow execution properties
    public WorkflowMode WorkflowMode { get; set; } = WorkflowMode.Visual;
    public List<WorkflowStep> WorkflowSteps { get; set; } = [];
    public string ScriptSource { get; set; } = string.Empty;
}

public sealed class WorkflowPromptField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string VariableName { get; set; } = "input";
    public string Label { get; set; } = "Enter value";
    public string DefaultValue { get; set; } = string.Empty;
    public TokenType Type { get; set; } = TokenType.PromptText;
    public string Choices { get; set; } = string.Empty;
    public double? MinNumber { get; set; }
    public double? MaxNumber { get; set; }
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    public WorkflowPromptField Clone()
    {
        return new WorkflowPromptField
        {
            Id = Guid.NewGuid(),
            VariableName = VariableName,
            Label = Label,
            DefaultValue = DefaultValue,
            Type = Type,
            Choices = Choices,
            MinNumber = MinNumber,
            MaxNumber = MaxNumber,
            DateFormat = DateFormat
        };
    }
}

public sealed class WorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkflowStepType StepType { get; set; } = WorkflowStepType.Prompt;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsCollapsed { get; set; } = false;
    public StepErrorPolicy OnError { get; set; } = StepErrorPolicy.StopWorkflow;

    // Prompt properties
    public string PromptTitle { get; set; } = string.Empty;
    public string PromptSubtitle { get; set; } = string.Empty;
    public List<WorkflowPromptField> PromptFields { get; set; } = [];
    public string VariableName { get; set; } = string.Empty;
    public string PromptLabel { get; set; } = string.Empty;
    public string PromptDefaultValue { get; set; } = string.Empty;
    public TokenType PromptType { get; set; } = TokenType.PromptText;
    public string PromptChoices { get; set; } = string.Empty;
    public double? PromptMinNumber { get; set; }
    public double? PromptMaxNumber { get; set; }
    public string PromptDateFormat { get; set; } = "yyyy-MM-dd";

    // OpenUrl properties
    public string Url { get; set; } = string.Empty;
    public string? BrowserTarget { get; set; }
    public string? BrowserProfile { get; set; }
    public bool OpenInNewWindow { get; set; } = false;

    // LaunchApp properties
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; } = false;
    public string? TargetDisplay { get; set; }

    // EnsureDirectory properties
    public string DirectoryPath { get; set; } = string.Empty;
    public DirectoryMissingPolicy DirectoryMissingPolicy { get; set; } = DirectoryMissingPolicy.PromptToCreate;
    public bool OpenInExplorer { get; set; } = false;

    // InjectSnippet properties
    public string SnippetTemplate { get; set; } = string.Empty;

    // Delay properties
    public int DelayMs { get; set; } = 500;

    // RunScript properties
    public string InlineScript { get; set; } = string.Empty;

    // ExecuteAction properties
    public Guid? TargetItemId { get; set; }

    public WorkflowStep Clone()
    {
        return new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepType = StepType,
            Name = Name,
            IsEnabled = IsEnabled,
            IsCollapsed = IsCollapsed,
            OnError = OnError,
            PromptTitle = PromptTitle,
            PromptSubtitle = PromptSubtitle,
            PromptFields = PromptFields.Select(f => f.Clone()).ToList(),
            VariableName = VariableName,
            PromptLabel = PromptLabel,
            PromptDefaultValue = PromptDefaultValue,
            PromptType = PromptType,
            PromptChoices = PromptChoices,
            PromptMinNumber = PromptMinNumber,
            PromptMaxNumber = PromptMaxNumber,
            PromptDateFormat = PromptDateFormat,
            Url = Url,
            BrowserTarget = BrowserTarget,
            BrowserProfile = BrowserProfile,
            OpenInNewWindow = OpenInNewWindow,
            Command = Command,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            RunAsAdmin = RunAsAdmin,
            TargetDisplay = TargetDisplay,
            DirectoryPath = DirectoryPath,
            DirectoryMissingPolicy = DirectoryMissingPolicy,
            OpenInExplorer = OpenInExplorer,
            SnippetTemplate = SnippetTemplate,
            DelayMs = DelayMs,
            InlineScript = InlineScript,
            TargetItemId = TargetItemId
        };
    }
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
