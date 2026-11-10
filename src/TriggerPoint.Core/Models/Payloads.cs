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
    public SnippetContentType SnippetContentType { get; set; } = SnippetContentType.PlainText;
    public string SnippetTemplate { get; set; } = string.Empty;
    public string SnippetRtf { get; set; } = string.Empty;

    // Workflow execution properties
    public WorkflowMode WorkflowMode { get; set; } = WorkflowMode.Visual;
    public List<WorkflowStep> WorkflowSteps { get; set; } = [];
    public List<WorkflowVariableDefinition> WorkflowVariables { get; set; } = [];
    public string ScriptSource { get; set; } = string.Empty;

    // Macro execution properties
    public MacroPayload Macro { get; set; } = new();

    // Windows Service execution properties
    public string ServiceName { get; set; } = string.Empty;
    public ServiceOperation ServiceOperation { get; set; } = ServiceOperation.Toggle;
    public int ServiceTimeoutSeconds { get; set; } = 30;
    public bool ServiceRunAsAdmin { get; set; } = false;

    public ActionPayload Clone()
    {
        return new ActionPayload
        {
            Command = Command,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            RunAsAdmin = RunAsAdmin,
            TargetDisplay = TargetDisplay,
            OpenInNewWindow = OpenInNewWindow,
            SnippetContentType = SnippetContentType,
            SnippetTemplate = SnippetTemplate,
            SnippetRtf = SnippetRtf,
            WorkflowMode = WorkflowMode,
            WorkflowSteps = WorkflowSteps?.ConvertAll(s => s.Clone()) ?? [],
            WorkflowVariables = WorkflowVariables?.ConvertAll(v => v.Clone()) ?? [],
            ScriptSource = ScriptSource,
            Macro = Macro?.Clone() ?? new MacroPayload(),
            ServiceName = ServiceName,
            ServiceOperation = ServiceOperation,
            ServiceTimeoutSeconds = ServiceTimeoutSeconds,
            ServiceRunAsAdmin = ServiceRunAsAdmin
        };
    }
}

public sealed class WorkflowVariableDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSecret { get; set; } = false;

    public WorkflowVariableDefinition Clone()
    {
        return new WorkflowVariableDefinition
        {
            Id = Guid.NewGuid(),
            Name = Name,
            Value = Value,
            Description = Description,
            IsSecret = IsSecret
        };
    }
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
    public string MinNumberText { get; set; } = string.Empty;
    public string MaxNumberText { get; set; } = string.Empty;
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
            MinNumberText = MinNumberText,
            MaxNumberText = MaxNumberText,
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
    public string PromptMinNumberText { get; set; } = string.Empty;
    public string PromptMaxNumberText { get; set; } = string.Empty;
    public string PromptDateFormat { get; set; } = "yyyy-MM-dd";

    // CaptureSelection properties
    public string CaptureSelectionFallbackValue { get; set; } = string.Empty;
    public bool CaptureSelectionTrimWhitespace { get; set; } = false;

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
    public SnippetContentType SnippetContentType { get; set; } = SnippetContentType.PlainText;
    public string SnippetTemplate { get; set; } = string.Empty;
    public string SnippetRtf { get; set; } = string.Empty;

    // Delay properties
    public int DelayMs { get; set; } = 500;

    // RunScript properties
    public string InlineScript { get; set; } = string.Empty;

    // ExecuteAction properties
    public Guid? TargetItemId { get; set; }

    // Dialog properties
    public string DialogTitle { get; set; } = "Notification";
    public string DialogMessage { get; set; } = "Proceed with workflow?";
    public WorkflowDialogButtons DialogButtons { get; set; } = WorkflowDialogButtons.OkCancel;
    public WorkflowDialogIcon DialogIcon { get; set; } = WorkflowDialogIcon.Information;
    public string DialogConfirmText { get; set; } = string.Empty;
    public string DialogCancelText { get; set; } = string.Empty;

    // SetVariable properties
    public string SetVariableName { get; set; } = string.Empty;
    public string SetVariableValue { get; set; } = string.Empty;

    // IfCondition properties
    public string ConditionLeft { get; set; } = string.Empty;
    public ConditionOperator ConditionOperator { get; set; } = ConditionOperator.Equals;
    public string ConditionRight { get; set; } = string.Empty;
    public bool ConditionIgnoreCase { get; set; } = true;
    public bool HasElseBranch { get; set; } = false;
    public List<WorkflowStep> ThenSteps { get; set; } = [];
    public List<WorkflowStep> ElseSteps { get; set; } = [];

    // Macro properties
    public MacroPayload Macro { get; set; } = new();

    // Windows Service properties
    public string ServiceName { get; set; } = string.Empty;
    public ServiceOperation ServiceOperation { get; set; } = ServiceOperation.Restart;
    public bool ServiceWaitForCompletion { get; set; } = true;
    public int ServiceTimeoutSeconds { get; set; } = 30;
    public bool ServiceRunAsAdmin { get; set; } = false;

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
            PromptMinNumberText = PromptMinNumberText,
            PromptMaxNumberText = PromptMaxNumberText,
            PromptDateFormat = PromptDateFormat,
            CaptureSelectionFallbackValue = CaptureSelectionFallbackValue,
            CaptureSelectionTrimWhitespace = CaptureSelectionTrimWhitespace,
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
            SnippetContentType = SnippetContentType,
            SnippetTemplate = SnippetTemplate,
            SnippetRtf = SnippetRtf,
            DelayMs = DelayMs,
            InlineScript = InlineScript,
            TargetItemId = TargetItemId,
            DialogTitle = DialogTitle,
            DialogMessage = DialogMessage,
            DialogButtons = DialogButtons,
            DialogIcon = DialogIcon,
            DialogConfirmText = DialogConfirmText,
            DialogCancelText = DialogCancelText,
            SetVariableName = SetVariableName,
            SetVariableValue = SetVariableValue,
            ConditionLeft = ConditionLeft,
            ConditionOperator = ConditionOperator,
            ConditionRight = ConditionRight,
            ConditionIgnoreCase = ConditionIgnoreCase,
            HasElseBranch = HasElseBranch,
            ThenSteps = ThenSteps?.ConvertAll(s => s.Clone()) ?? [],
            ElseSteps = ElseSteps?.ConvertAll(s => s.Clone()) ?? [],
            Macro = Macro?.Clone() ?? new MacroPayload(),
            ServiceName = ServiceName,
            ServiceOperation = ServiceOperation,
            ServiceWaitForCompletion = ServiceWaitForCompletion,
            ServiceTimeoutSeconds = ServiceTimeoutSeconds,
            ServiceRunAsAdmin = ServiceRunAsAdmin
        };
    }
}

public sealed class MacroEvent
{
    public MacroEventType Type { get; set; } = MacroEventType.Delay;
    public int KeyCode { get; set; }
    public string? KeyName { get; set; }
    public MacroMouseButton MouseButton { get; set; } = MacroMouseButton.Left;
    public int X { get; set; }
    public int Y { get; set; }
    public int DelayMs { get; set; }

    public MacroEvent Clone()
    {
        return new MacroEvent
        {
            Type = Type,
            KeyCode = KeyCode,
            KeyName = KeyName,
            MouseButton = MouseButton,
            X = X,
            Y = Y,
            DelayMs = DelayMs
        };
    }
}

public sealed class MacroPayload
{
    public List<MacroEvent> Events { get; set; } = [];
    public int RepeatCount { get; set; } = 1;
    public double PlaybackSpeed { get; set; } = 1.0;

    public MacroPayload Clone()
    {
        return new MacroPayload
        {
            Events = Events.Select(e => e.Clone()).ToList(),
            RepeatCount = RepeatCount,
            PlaybackSpeed = PlaybackSpeed
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

        return true;
    }

    public bool IsActive(string? processName, string? activeUrl = null, string? activeWindowTitle = null)
    {
        if (!IsActiveForProcess(processName))
            return false;

        bool hasUrlOrTitleCandidates = !string.IsNullOrWhiteSpace(activeUrl) || !string.IsNullOrWhiteSpace(activeWindowTitle);

        // Evaluate URL & Tab Title rules if specified
        if (hasUrlOrTitleCandidates)
        {
            foreach (var excl in ExcludedUrls)
            {
                var rule = excl.Trim();
                if (string.IsNullOrEmpty(rule)) continue;

                if (!string.IsNullOrWhiteSpace(activeUrl) && MatchesUrlRule(activeUrl, rule, allowSpaFragmentFallback: false))
                    return false;

                if (!string.IsNullOrWhiteSpace(activeWindowTitle) && MatchesWildcard(activeWindowTitle, rule))
                    return false;
            }

            if (AllowedUrls.Count > 0)
            {
                bool matchedUrlOrTitle = false;
                foreach (var allow in AllowedUrls)
                {
                    var rule = allow.Trim();
                    if (string.IsNullOrEmpty(rule)) continue;

                    if ((!string.IsNullOrWhiteSpace(activeUrl) && MatchesUrlRule(activeUrl, rule, allowSpaFragmentFallback: true)) ||
                        (!string.IsNullOrWhiteSpace(activeWindowTitle) && MatchesWildcard(activeWindowTitle, rule)))
                    {
                        matchedUrlOrTitle = true;
                        break;
                    }
                }
                if (!matchedUrlOrTitle) return false;
            }
        }
        else if (AllowedUrls.Count > 0)
        {
            // Browser URL / Tab Title rules are configured, but no active URL or title was matched
            return false;
        }

        return true;
    }

    public static bool MatchesUrlRule(string? text, string? pattern, bool allowSpaFragmentFallback = true)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(text))
            return false;

        pattern = pattern.Trim();
        text = text.Trim();

        // 1. Direct wildcard / substring match
        if (MatchesWildcard(text, pattern))
            return true;

        // 2. Normalized URL match (strips http://, https://, www., and trailing /)
        string normText = NormalizeUrl(text);
        string normPattern = NormalizeUrl(pattern);
        if (!string.IsNullOrEmpty(normText) && !string.IsNullOrEmpty(normPattern))
        {
            if (MatchesWildcard(normText, normPattern))
                return true;
        }

        // 3. SPA #fragment resilience (e.g., rule "mail.google.com/mail/u/0/#inbox" matches
        //    "#inbox/FMfcgz...", "#search/query/FMfcgz...", "#all/...", "#sent/...", or "?ui=2...")
        if (allowSpaFragmentFallback)
        {
            int hashIdx = normPattern.IndexOf('#');
            if (hashIdx > 0)
            {
                string fragmentPart = normPattern[(hashIdx + 1)..];
                if (!fragmentPart.Contains('*') && !fragmentPart.Contains('?'))
                {
                    string basePattern = normPattern[..hashIdx].TrimEnd('/');
                    if (basePattern.Length >= 4 && (basePattern.Contains('.') || basePattern.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (normText.StartsWith(basePattern, StringComparison.OrdinalIgnoreCase))
                        {
                            if (normText.Length == basePattern.Length)
                                return true;

                            char nextChar = normText[basePattern.Length];
                            if (nextChar is '/' or '#' or '?')
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    public static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        string s = url.Trim();

        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = s[8..];
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            s = s[7..];

        if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            s = s[4..];

        return s.TrimEnd('/');
    }

    public static string? ExtractBaseWebAppUrlPattern(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return null;
        string norm = NormalizeUrl(rawUrl);
        if (string.IsNullOrEmpty(norm)) return null;

        int cutIdx = norm.IndexOfAny(['#', '?']);
        if (cutIdx > 0)
        {
            string basePart = norm[..cutIdx].TrimEnd('/');
            if (!string.IsNullOrEmpty(basePart))
            {
                return basePart + "/*";
            }
        }

        return norm + "*";
    }

    private static readonly string[] BrowserTitleSuffixes =
    [
        " - Google Chrome",
        " - Chromium",
        " - Microsoft Edge",
        " - Microsoft\u200B Edge",
        " — Mozilla Firefox",
        " - Mozilla Firefox",
        " - Brave",
        " - Opera",
        " - Vivaldi",
        " - Arc"
    ];

    public static string? CleanBrowserWindowTitle(string? rawWindowTitle)
    {
        if (string.IsNullOrWhiteSpace(rawWindowTitle)) return null;
        string title = rawWindowTitle.Trim();

        foreach (var suffix in BrowserTitleSuffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^suffix.Length].Trim();
                break;
            }
        }

        // Also handle Edge profile suffixes like " - Personal - Microsoft Edge" or " and 3 more pages - Work - Microsoft Edge"
        int edgeIdx = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (edgeIdx > 0 && (rawWindowTitle.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && rawWindowTitle.Contains("Edge", StringComparison.OrdinalIgnoreCase)))
        {
            string possibleProfile = title[(edgeIdx + 3)..].Trim();
            if (possibleProfile.Equals("Personal", StringComparison.OrdinalIgnoreCase) ||
                possibleProfile.Equals("Work", StringComparison.OrdinalIgnoreCase) ||
                possibleProfile.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
            {
                title = title[..edgeIdx].Trim();
                int morePagesIdx = title.LastIndexOf(" and ", StringComparison.OrdinalIgnoreCase);
                if (morePagesIdx > 0 && title.EndsWith("more pages", StringComparison.OrdinalIgnoreCase) || title.EndsWith("more page", StringComparison.OrdinalIgnoreCase))
                {
                    title = title[..morePagesIdx].Trim();
                }
            }
        }

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    public static string? ExtractTabTitleKeywordPattern(string? rawWindowTitle)
    {
        var clean = CleanBrowserWindowTitle(rawWindowTitle);
        if (string.IsNullOrWhiteSpace(clean)) return null;

        // Many web apps format titles as "Page/Thread - Account - AppName" (e.g. "Inbox (4) - user@gmail.com - Gmail")
        var segments = clean.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length >= 2)
        {
            var lastSegment = segments[^1];
            if (lastSegment.Length >= 2 && !lastSegment.Contains('@'))
            {
                return $"*{lastSegment}*";
            }
        }

        return $"*{clean}*";
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
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // Also support matching when pattern has scheme (https://...) and text does not, or vice versa
            string normText = NormalizeUrl(text);
            string normPattern = NormalizeUrl(pattern);
            if (!string.IsNullOrEmpty(normText) && !string.IsNullOrEmpty(normPattern))
            {
                return normText.Contains(normPattern, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        // 1. Direct match
        if (CheckRegexMatch(text, pattern)) return true;

        // 2. Scheme-stripped / normalized match (e.g. text "http://localhost:3000" matches "localhost:*")
        int schemeIdx = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            string stripped = text[(schemeIdx + 3)..];
            if (CheckRegexMatch(stripped, pattern)) return true;
        }

        string normalizedText = NormalizeUrl(text);
        string normalizedPattern = NormalizeUrl(pattern);
        if (!string.IsNullOrEmpty(normalizedText) && !string.IsNullOrEmpty(normalizedPattern))
        {
            if (CheckRegexMatch(normalizedText, normalizedPattern)) return true;
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
