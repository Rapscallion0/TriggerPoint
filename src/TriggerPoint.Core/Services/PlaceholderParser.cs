using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static partial class PlaceholderParser
{
    // Pattern to match any dynamic token {{...}} or {...}
    private static readonly Regex TokenRegex = new(@"\{\{(?<tag>[^{}]+)\}\}|\{(?<tag>[^{}]+)\}", RegexOptions.Compiled);

    public const string CursorToken = "{cursor}";

    public static IReadOnlyList<PromptToken> ExtractPromptTokens(string template)
    {
        if (string.IsNullOrEmpty(template)) return [];

        var list = new List<PromptToken>();
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var matches = TokenRegex.Matches(template);
        foreach (Match match in matches)
        {
            var rawTag = match.Groups["tag"].Value.Trim();
            if (seenTags.Contains(rawTag)) continue;

            bool isDoubleBraced = match.Value.StartsWith("{{", StringComparison.Ordinal) && match.Value.EndsWith("}}", StringComparison.Ordinal);
            var token = ParsePromptToken(rawTag, null, isDoubleBraced);
            if (token != null)
            {
                seenTags.Add(rawTag);
                list.Add(token);
            }
        }

        return list;
    }

    public static int CountCursorTokens(string template)
    {
        if (string.IsNullOrEmpty(template)) return 0;
        return Regex.Matches(template, @"\{\{?cursor\}\}?", RegexOptions.IgnoreCase).Count;
    }

    public static List<PromptToken> ExtractAllPromptTokenOccurrences(string template)
    {
        if (string.IsNullOrEmpty(template)) return [];

        var list = new List<PromptToken>();
        var matches = TokenRegex.Matches(template);
        foreach (Match match in matches)
        {
            var rawTag = match.Groups["tag"].Value.Trim();
            bool isDoubleBraced = match.Value.StartsWith("{{", StringComparison.Ordinal) && match.Value.EndsWith("}}", StringComparison.Ordinal);
            var token = ParsePromptToken(rawTag, null, isDoubleBraced);
            if (token != null)
            {
                list.Add(token);
            }
        }

        return list;
    }

    public static string GetDisambiguatedPromptToken(string tokenTag, string currentTemplate)
    {
        if (string.IsNullOrWhiteSpace(tokenTag)) return tokenTag;

        string trimmed = tokenTag.Trim();
        bool isDouble = trimmed.StartsWith("{{", StringComparison.Ordinal) && trimmed.EndsWith("}}", StringComparison.Ordinal);
        bool isSingle = trimmed.StartsWith('{') && trimmed.EndsWith('}');
        if (!isSingle && !isDouble) return tokenTag;

        string inner = isDouble ? trimmed[2..^2].Trim() : trimmed[1..^1].Trim();
        int colonIdx = inner.IndexOf(':');
        if (colonIdx <= 0) return tokenTag;

        string prefix = inner[..colonIdx].Trim();
        string prefixLower = prefix.ToLowerInvariant();
        if (prefixLower is not ("text" or "number" or "choice" or "multiline" or "date_picker"))
        {
            return tokenTag;
        }

        string rest = inner[(colonIdx + 1)..];
        int pipeIdx = rest.IndexOf('|');
        string baseLabel = pipeIdx >= 0 ? rest[..pipeIdx].Trim() : rest.Trim();
        string suffix = pipeIdx >= 0 ? rest[pipeIdx..] : string.Empty;

        var existingTokens = ExtractAllPromptTokenOccurrences(currentTemplate);
        if (!existingTokens.Any(t => string.Equals(t.Label, baseLabel, StringComparison.OrdinalIgnoreCase)))
        {
            return tokenTag;
        }

        // Duplicate label found in existing template. Calculate next unique increment.
        string root = Regex.Replace(baseLabel, @"\s+\d+$", "").Trim();
        var usedNumbers = new HashSet<int>();

        foreach (var t in existingTokens)
        {
            if (string.Equals(t.Label, root, StringComparison.OrdinalIgnoreCase))
            {
                usedNumbers.Add(1);
            }
            else if (t.Label.StartsWith(root + " ", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = t.Label[(root.Length + 1)..].Trim();
                if (int.TryParse(numPart, out int n))
                {
                    usedNumbers.Add(n);
                }
            }
        }

        int next = 2;
        while (usedNumbers.Contains(next))
        {
            next++;
        }

        string newLabel = $"{root} {next}";
        string newInner = $"{prefix}:{newLabel}{suffix}";
        return isDouble ? $"{{{{{newInner}}}}}" : $"{{{newInner}}}";
    }

    public static string DisambiguateDuplicatePromptTokens(string template)
    {
        if (string.IsNullOrEmpty(template)) return template;

        var matches = TokenRegex.Matches(template);
        if (matches.Count == 0) return template;

        var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labelCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        int lastIndex = 0;

        foreach (Match match in matches)
        {
            sb.Append(template[lastIndex..match.Index]);
            lastIndex = match.Index + match.Length;

            var rawTag = match.Groups["tag"].Value.Trim();
            bool isDoubleBraced = match.Value.StartsWith("{{", StringComparison.Ordinal) && match.Value.EndsWith("}}", StringComparison.Ordinal);
            var token = ParsePromptToken(rawTag, null, isDoubleBraced);

            if (token != null)
            {
                string baseLabel = token.Label;
                string root = Regex.Replace(baseLabel, @"\s+\d+$", "").Trim();

                if (!seenLabels.Contains(baseLabel))
                {
                    seenLabels.Add(baseLabel);
                    labelCounters[root] = Math.Max(labelCounters.GetValueOrDefault(root, 1), 1);
                    sb.Append(match.Value);
                }
                else
                {
                    int nextNum = labelCounters.GetValueOrDefault(root, 1) + 1;
                    labelCounters[root] = nextNum;
                    string newLabel = $"{root} {nextNum}";
                    seenLabels.Add(newLabel);

                    // Reconstruct token with new label
                    int colonIdx = rawTag.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string prefix = rawTag[..colonIdx].Trim();
                        string rest = rawTag[(colonIdx + 1)..];
                        int pipeIdx = rest.IndexOf('|');
                        string suffix = pipeIdx >= 0 ? rest[pipeIdx..] : string.Empty;
                        string newInner = $"{prefix}:{newLabel}{suffix}";
                        sb.Append(isDoubleBraced ? $"{{{{{newInner}}}}}" : $"{{{newInner}}}");
                    }
                    else
                    {
                        sb.Append(match.Value);
                    }
                }
            }
            else
            {
                sb.Append(match.Value);
            }
        }

        sb.Append(template[lastIndex..]);
        return sb.ToString();
    }

    private static bool IsSystemToken(string rawTag)
    {
        var colonIdx = rawTag.IndexOf(':');
        if (colonIdx > 0)
        {
            var prefix = rawTag[..colonIdx].Trim().ToLowerInvariant();
            return prefix switch
            {
                "date" => true,
                "time" => true,
                "datetime" => true,
                "clipboard" => true,
                "guid" or "uuid" => true,
                "random" or "rand" => true,
                "env" => true,
                _ => false
            };
        }

        var lower = rawTag.Trim().ToLowerInvariant();
        return lower switch
        {
            "cursor" => true,
            "date" => true,
            "time" => true,
            "datetime" => true,
            "clipboard" => true,
            "guid" or "uuid" => true,
            "random" or "rand" => true,
            "username" or "user" => true,
            "machine" or "computer" or "machinename" => true,
            "active_window" => true,
            "active_process" => true,
            _ => false
        };
    }

    private static PromptToken? ParsePromptToken(string rawTag, DateTime? referenceTime = null, bool isDoubleBraced = false)
    {
        if (IsSystemToken(rawTag)) return null;

        var colonIndex = rawTag.IndexOf(':');
        if (colonIndex > 0)
        {
            var typeStr = rawTag[..colonIndex].Trim().ToLowerInvariant();
            var rest = rawTag[(colonIndex + 1)..].Trim();

            // Known single-prefix syntax
            switch (typeStr)
            {
                case "text":
                    return ParseTextToken(rawTag, rest);
                case "number":
                    return ParseNumberToken(rawTag, rest);
                case "choice":
                    return ParseChoiceToken(rawTag, rest);
                case "multiline":
                    return ParseMultilineToken(rawTag, rest);
                case "date_picker":
                    return ParseDatePickerToken(rawTag, rest, referenceTime);
            }

            // Shorthand field with default: {Field:Default} or {{Field:Default}}
            var fieldName = rawTag[..colonIndex].Trim();
            return new PromptToken
            {
                Type = TokenType.PromptText,
                RawTag = rawTag,
                Label = fieldName,
                DefaultValue = rest
            };
        }

        // Shorthand text with equals default: {Field=Default}
        var eqIndex = rawTag.IndexOf('=');
        if (eqIndex > 0)
        {
            var fieldName = rawTag[..eqIndex].Trim();
            var defaultVal = rawTag[(eqIndex + 1)..].Trim();
            return new PromptToken
            {
                Type = TokenType.PromptText,
                RawTag = rawTag,
                Label = fieldName,
                DefaultValue = defaultVal
            };
        }

        // Shorthand simple text: {{FieldName}}
        if (isDoubleBraced)
        {
            return new PromptToken
            {
                Type = TokenType.PromptText,
                RawTag = rawTag,
                Label = rawTag,
                DefaultValue = string.Empty
            };
        }

        return null;
    }

    private static PromptToken ParseTextToken(string rawTag, string rest)
    {
        var parts = rest.Split('|', 2);
        return new PromptToken
        {
            Type = TokenType.PromptText,
            RawTag = rawTag,
            Label = parts[0].Trim(),
            DefaultValue = parts.Length > 1 ? parts[1].Trim() : string.Empty
        };
    }

    private static PromptToken ParseMultilineToken(string rawTag, string rest)
    {
        var parts = rest.Split('|', 2);
        return new PromptToken
        {
            Type = TokenType.PromptMultiline,
            RawTag = rawTag,
            Label = parts[0].Trim(),
            DefaultValue = parts.Length > 1 ? parts[1].Trim() : string.Empty
        };
    }

    private static PromptToken ParseNumberToken(string rawTag, string rest)
    {
        // e.g. "Age|0,120|25" or "Quantity|1,10" or "Retries"
        var parts = rest.Split('|', 3, StringSplitOptions.TrimEntries);
        var label = parts[0];
        double? min = null;
        double? max = null;
        string defaultVal = string.Empty;

        if (parts.Length > 1)
        {
            var bounds = parts[1].Split(',', 2, StringSplitOptions.TrimEntries);
            if (bounds.Length > 0 && double.TryParse(bounds[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMin))
            {
                min = parsedMin;
            }
            if (bounds.Length > 1 && double.TryParse(bounds[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMax))
            {
                max = parsedMax;
            }
        }

        if (parts.Length > 2)
        {
            defaultVal = parts[2];
        }

        return new PromptToken
        {
            Type = TokenType.PromptNumber,
            RawTag = rawTag,
            Label = label,
            MinNumber = min,
            MaxNumber = max,
            DefaultValue = defaultVal
        };
    }

    private static PromptToken ParseChoiceToken(string rawTag, string rest)
    {
        // e.g. "Environment|Production=prod,Staging*=stg,Local"
        var parts = rest.Split('|', 2, StringSplitOptions.TrimEntries);
        var label = parts[0];
        var choices = new List<ChoiceOption>();
        string defaultVal = string.Empty;

        if (parts.Length > 1)
        {
            var rawChoices = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawChoice in rawChoices)
            {
                var isDefault = false;
                var itemText = rawChoice;
                if (itemText.EndsWith('*'))
                {
                    isDefault = true;
                    itemText = itemText[..^1].Trim();
                }

                var equalsIndex = itemText.IndexOf('=');
                if (equalsIndex > 0)
                {
                    var display = itemText[..equalsIndex].Trim();
                    var val = itemText[(equalsIndex + 1)..].Trim();
                    if (display.EndsWith('*'))
                    {
                        isDefault = true;
                        display = display[..^1].Trim();
                    }
                    choices.Add(new ChoiceOption(display, val));
                    if (isDefault) defaultVal = val;
                }
                else
                {
                    choices.Add(new ChoiceOption(itemText, itemText));
                    if (isDefault) defaultVal = itemText;
                }
            }
        }

        return new PromptToken
        {
            Type = TokenType.PromptChoice,
            RawTag = rawTag,
            Label = label,
            Choices = choices,
            DefaultValue = defaultVal
        };
    }

    private static PromptToken ParseDatePickerToken(string rawTag, string rest, DateTime? referenceTime = null)
    {
        string label;
        string format = "yyyy-MM-dd";
        string defaultOffset = string.Empty;

        if (rest.Contains('|'))
        {
            var parts = rest.Split('|', 3, StringSplitOptions.TrimEntries);
            label = parts[0];
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                format = parts[1];
            }
            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                defaultOffset = parts[2];
            }
        }
        else if (rest.Contains(':'))
        {
            var parts = rest.Split(':', 2, StringSplitOptions.TrimEntries);
            label = parts[0];
            if (!string.IsNullOrWhiteSpace(parts[1]))
            {
                format = parts[1];
            }
        }
        else
        {
            label = rest;
        }

        DateTime targetDate = (referenceTime ?? DateTime.Today).Date;
        if (!string.IsNullOrEmpty(defaultOffset))
        {
            var match = Regex.Match(defaultOffset, @"^([+-]\d+)\s*([dwmy])$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
            {
                var unit = match.Groups[2].Value.ToLowerInvariant();
                targetDate = unit switch
                {
                    "d" => targetDate.AddDays(num),
                    "w" => targetDate.AddDays(num * 7),
                    "m" => targetDate.AddMonths(num),
                    "y" => targetDate.AddYears(num),
                    _ => targetDate
                };
            }
            else if (string.Equals(defaultOffset, "tomorrow", StringComparison.OrdinalIgnoreCase))
            {
                targetDate = targetDate.AddDays(1);
            }
            else if (string.Equals(defaultOffset, "yesterday", StringComparison.OrdinalIgnoreCase))
            {
                targetDate = targetDate.AddDays(-1);
            }
        }

        string formattedDefault;
        try
        {
            formattedDefault = targetDate.ToString(format, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            formattedDefault = targetDate.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        }

        return new PromptToken
        {
            Type = TokenType.PromptDatePicker,
            RawTag = rawTag,
            Label = label,
            DateFormat = format,
            DefaultValue = formattedDefault
        };
    }

    public static async Task<string> EvaluateAsync(
        string template, 
        Func<Task<string>>? clipboardProvider = null,
        IReadOnlyDictionary<string, string>? promptResponses = null,
        DateTime? referenceTime = null,
        string? activeWindowTitle = null,
        string? activeProcessName = null)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var now = referenceTime ?? DateTime.Now;

        var result = new StringBuilder();
        int lastIndex = 0;

        foreach (Match match in TokenRegex.Matches(template))
        {
            result.Append(template[lastIndex..match.Index]);
            lastIndex = match.Index + match.Length;

            var rawTag = match.Groups["tag"].Value.Trim();
            var lowerTag = rawTag.ToLowerInvariant();

            // 1. Cursor token (preserved for final caret positioning)
            if (lowerTag == "cursor")
            {
                result.Append(CursorToken);
            }
            // 2. Date / Time tokens with custom formatting & offsets
            else if (TryEvaluateDateTimeToken(rawTag, now, out var dtResult))
            {
                result.Append(dtResult);
            }
            // 3. Clipboard tokens with modifiers (trim, lower, upper, urlencode, urldecode)
            else if (await TryEvaluateClipboardTokenAsync(rawTag, clipboardProvider).ConfigureAwait(false) is { Handled: true } clipRes)
            {
                result.Append(clipRes.Result);
            }
            // 4. GUID / UUID tokens
            else if (TryEvaluateGuidToken(rawTag, out var guidResult))
            {
                result.Append(guidResult);
            }
            // 5. Random number or choice generator
            else if (TryEvaluateRandomToken(rawTag, out var randResult))
            {
                result.Append(randResult);
            }
            // 6. System & Environment variables ({username}, {machine}, {env:...})
            else if (TryEvaluateSystemToken(rawTag, out var sysResult))
            {
                result.Append(sysResult);
            }
            // 7. Active window & process context tokens
            else if (lowerTag == "active_window")
            {
                result.Append(activeWindowTitle ?? string.Empty);
            }
            else if (lowerTag == "active_process")
            {
                result.Append(activeProcessName ?? string.Empty);
            }
            // 8. Interactive prompt responses (or fallback to default values)
            else if (promptResponses != null && (promptResponses.TryGetValue(rawTag, out var responseValue) ||
                     (rawTag.Contains(':') && promptResponses.TryGetValue(rawTag[..rawTag.IndexOf(':')].Trim(), out responseValue))))
            {
                result.Append(responseValue);
            }
            else
            {
                // Check if prompt has a default value defined (e.g. {{Name:John}} or {text:Label|Default})
                bool isDoubleBraced = match.Value.StartsWith("{{", StringComparison.Ordinal) && match.Value.EndsWith("}}", StringComparison.Ordinal);
                var prompt = ParsePromptToken(rawTag, now, isDoubleBraced);
                if (promptResponses != null)
                {
                    result.Append(prompt != null ? prompt.DefaultValue : string.Empty);
                }
                else if (prompt != null && !string.IsNullOrEmpty(prompt.DefaultValue))
                {
                    result.Append(prompt.DefaultValue);
                }
                else
                {
                    // Leave original token if unhandled or in preview
                    result.Append(match.Value);
                }
            }
        }

        result.Append(template[lastIndex..]);
        return result.ToString();
    }

    private static bool TryEvaluateDateTimeToken(string rawTag, DateTime now, out string result)
    {
        result = string.Empty;
        var tag = rawTag.Trim();

        string prefix;
        string? args = null;

        var colonIdx = tag.IndexOf(':');
        if (colonIdx > 0)
        {
            prefix = tag[..colonIdx].Trim().ToLowerInvariant();
            args = tag[(colonIdx + 1)..].Trim();
        }
        else
        {
            prefix = tag.ToLowerInvariant();
        }

        if (prefix != "date" && prefix != "time" && prefix != "datetime" && 
            prefix != "tomorrow" && prefix != "yesterday")
        {
            return false;
        }

        DateTime target = now;
        if (prefix == "tomorrow") target = target.AddDays(1);
        else if (prefix == "yesterday") target = target.AddDays(-1);

        string defaultFormat = prefix switch
        {
            "time" => "HH:mm:ss",
            "datetime" => "yyyy-MM-dd HH:mm:ss",
            _ => "yyyy-MM-dd"
        };

        string? customFormat = null;

        if (!string.IsNullOrEmpty(args))
        {
            // Check for relative offset: e.g. +1d, -2w, +1m, +1y, +1h, -15m
            var match = Regex.Match(args, @"^([+-]\d+)\s*([dwmyhms])(?::(.*))?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out var num))
                {
                    var unit = match.Groups[2].Value.ToLowerInvariant();
                    target = unit switch
                    {
                        "d" => target.AddDays(num),
                        "w" => target.AddDays(num * 7),
                        "m" => target.AddMonths(num),
                        "y" => target.AddYears(num),
                        "h" => target.AddHours(num),
                        "s" => target.AddSeconds(num),
                        _ => target
                    };
                }
                if (match.Groups[3].Success && !string.IsNullOrWhiteSpace(match.Groups[3].Value))
                {
                    customFormat = match.Groups[3].Value.Trim();
                }
            }
            else
            {
                customFormat = args;
            }
        }

        var fmt = string.IsNullOrEmpty(customFormat) ? defaultFormat : customFormat;
        try
        {
            result = target.ToString(fmt, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            result = target.ToString(defaultFormat, CultureInfo.CurrentCulture);
        }

        return true;
    }

    private static async Task<(bool Handled, string Result)> TryEvaluateClipboardTokenAsync(
        string rawTag, 
        Func<Task<string>>? clipboardProvider)
    {
        var tag = rawTag.Trim();
        var lower = tag.ToLowerInvariant();
        if (!lower.StartsWith("clipboard")) return (false, string.Empty);

        string text = string.Empty;
        if (clipboardProvider != null)
        {
            text = (await clipboardProvider().ConfigureAwait(false)) ?? string.Empty;
        }

        var colonIdx = tag.IndexOf(':');
        if (colonIdx < 0)
        {
            return (true, text);
        }

        var mod = tag[(colonIdx + 1)..].Trim().ToLowerInvariant();
        string res = mod switch
        {
            "trim" => text.Trim(),
            "lower" or "lowercase" => text.ToLowerInvariant(),
            "upper" or "uppercase" => text.ToUpperInvariant(),
            "urlencode" => Uri.EscapeDataString(text),
            "urldecode" => Uri.UnescapeDataString(text),
            _ => text
        };

        return (true, res);
    }

    private static bool TryEvaluateGuidToken(string rawTag, out string result)
    {
        result = string.Empty;
        var tag = rawTag.Trim();
        var lower = tag.ToLowerInvariant();

        if (!lower.StartsWith("guid") && !lower.StartsWith("uuid")) return false;

        var guid = Guid.NewGuid();
        string format = "D"; // standard hyphenated
        bool upper = false;

        var colonIdx = tag.IndexOf(':');
        if (colonIdx > 0)
        {
            var modifier = tag[(colonIdx + 1)..].Trim().ToLowerInvariant();
            if (modifier.Contains("upper")) upper = true;
            if (modifier.Contains('n')) format = "N";
            else if (modifier.Contains('b')) format = "B";
            else if (modifier.Contains('p')) format = "P";
        }

        var str = guid.ToString(format);
        result = upper ? str.ToUpperInvariant() : str.ToLowerInvariant();
        return true;
    }

    private static bool TryEvaluateRandomToken(string rawTag, out string result)
    {
        result = string.Empty;
        var tag = rawTag.Trim();
        if (!tag.StartsWith("random:", StringComparison.OrdinalIgnoreCase)) return false;

        var rest = tag[7..].Trim();
        var parts = rest.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && 
            int.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var min) &&
            int.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var max))
        {
            if (min > max) (min, max) = (max, min);
            result = Random.Shared.Next(min, max + 1).ToString();
            return true;
        }

        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
        {
            result = parts[Random.Shared.Next(parts.Length)];
            return true;
        }

        return false;
    }

    private static bool TryEvaluateSystemToken(string rawTag, out string result)
    {
        result = string.Empty;
        var tag = rawTag.Trim();
        var lower = tag.ToLowerInvariant();

        if (lower == "username" || lower == "user")
        {
            result = Environment.UserName;
            return true;
        }

        if (lower == "machine" || lower == "computer")
        {
            result = Environment.MachineName;
            return true;
        }

        if (lower.StartsWith("env:"))
        {
            var varName = tag[4..].Trim();
            result = Environment.GetEnvironmentVariable(varName) ?? string.Empty;
            return true;
        }

        return false;
    }

    public static (string CleanText, int CaretOffsetFromEnd) ProcessCursorPosition(string textWithCursor)
    {
        if (string.IsNullOrEmpty(textWithCursor)) return (string.Empty, 0);

        var cursorIdx = textWithCursor.IndexOf(CursorToken, StringComparison.OrdinalIgnoreCase);
        if (cursorIdx < 0)
        {
            return (textWithCursor, 0);
        }

        // Remove the {cursor} token
        var cleanText = textWithCursor.Remove(cursorIdx, CursorToken.Length);
        // Caret offset from end of clean text: calculate navigation steps (CRLF and surrogate pairs count as 1 step)
        var suffix = cleanText[cursorIdx..];
        var offsetFromEnd = CalculateCaretStepDistance(suffix);
        return (cleanText, offsetFromEnd);
    }

    /// <summary>
    /// Calculates the number of cursor movement steps required to traverse the given text from end to start,
    /// counting \r\n as a single step and Unicode surrogate pairs (e.g. emojis) as a single step.
    /// </summary>
    public static int CalculateCaretStepDistance(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int steps = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                steps++;
                i++; // Skip \n
            }
            else if (char.IsSurrogatePair(text, i))
            {
                steps++;
                i++; // Skip low surrogate
            }
            else
            {
                steps++;
            }
        }
        return steps;
    }
}
