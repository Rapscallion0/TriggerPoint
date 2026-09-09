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
    // Pattern to match any dynamic token {...}
    // Handles {date}, {time}, {datetime}, {clipboard}, {cursor}
    // and {text:Label}, {number:Label|min,max}, {choice:Label|opt1=val1,opt2}, {multiline:Label}, {date_picker:Label}
    private static readonly Regex TokenRegex = new(@"\{(?<tag>[^{}]+)\}", RegexOptions.Compiled);

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

            var token = ParsePromptToken(rawTag);
            if (token != null)
            {
                seenTags.Add(rawTag);
                list.Add(token);
            }
        }

        return list;
    }

    private static PromptToken? ParsePromptToken(string rawTag)
    {
        // Check if it starts with a prompt identifier
        var colonIndex = rawTag.IndexOf(':');
        if (colonIndex <= 0) return null;

        var typeStr = rawTag[..colonIndex].Trim().ToLowerInvariant();
        var rest = rawTag[(colonIndex + 1)..].Trim();

        return typeStr switch
        {
            "text" => new PromptToken
            {
                Type = TokenType.PromptText,
                RawTag = rawTag,
                Label = rest
            },
            "number" => ParseNumberToken(rawTag, rest),
            "choice" => ParseChoiceToken(rawTag, rest),
            "multiline" => new PromptToken
            {
                Type = TokenType.PromptMultiline,
                RawTag = rawTag,
                Label = rest
            },
            "date_picker" => new PromptToken
            {
                Type = TokenType.PromptDatePicker,
                RawTag = rawTag,
                Label = rest
            },
            _ => null
        };
    }

    private static PromptToken ParseNumberToken(string rawTag, string rest)
    {
        // e.g. "Age|0,120" or "Quantity"
        var parts = rest.Split('|', 2, StringSplitOptions.TrimEntries);
        var label = parts[0];
        double? min = null;
        double? max = null;

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

        return new PromptToken
        {
            Type = TokenType.PromptNumber,
            RawTag = rawTag,
            Label = label,
            MinNumber = min,
            MaxNumber = max
        };
    }

    private static PromptToken ParseChoiceToken(string rawTag, string rest)
    {
        // e.g. "Environment|Production=prod,Staging=stg,Local"
        var parts = rest.Split('|', 2, StringSplitOptions.TrimEntries);
        var label = parts[0];
        var choices = new List<ChoiceOption>();

        if (parts.Length > 1)
        {
            var rawChoices = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawChoice in rawChoices)
            {
                var equalsIndex = rawChoice.IndexOf('=');
                if (equalsIndex > 0)
                {
                    var display = rawChoice[..equalsIndex].Trim();
                    var val = rawChoice[(equalsIndex + 1)..].Trim();
                    choices.Add(new ChoiceOption(display, val));
                }
                else
                {
                    choices.Add(new ChoiceOption(rawChoice, rawChoice));
                }
            }
        }

        return new PromptToken
        {
            Type = TokenType.PromptChoice,
            RawTag = rawTag,
            Label = label,
            Choices = choices
        };
    }

    public static async Task<string> EvaluateAsync(
        string template, 
        Func<Task<string>>? clipboardProvider = null,
        IReadOnlyDictionary<string, string>? promptResponses = null,
        DateTime? referenceTime = null)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var now = referenceTime ?? DateTime.Now;

        // Replace tokens
        var result = new StringBuilder();
        int lastIndex = 0;

        foreach (Match match in TokenRegex.Matches(template))
        {
            result.Append(template[lastIndex..match.Index]);
            lastIndex = match.Index + match.Length;

            var rawTag = match.Groups["tag"].Value.Trim();
            var lowerTag = rawTag.ToLowerInvariant();

            if (lowerTag == "date")
            {
                result.Append(now.ToString("yyyy-MM-dd"));
            }
            else if (lowerTag == "time")
            {
                result.Append(now.ToString("HH:mm:ss"));
            }
            else if (lowerTag == "datetime")
            {
                result.Append(now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            else if (lowerTag == "clipboard")
            {
                if (clipboardProvider != null)
                {
                    var clipText = await clipboardProvider().ConfigureAwait(false);
                    result.Append(clipText ?? string.Empty);
                }
            }
            else if (lowerTag == "cursor")
            {
                // Preserve {cursor} token for caret positioning in the final step
                result.Append(CursorToken);
            }
            else
            {
                // Interactive prompt replacement
                if (promptResponses != null && promptResponses.TryGetValue(rawTag, out var responseValue))
                {
                    result.Append(responseValue);
                }
                else
                {
                    // If no prompt response provided, leave original token
                    result.Append(match.Value);
                }
            }
        }

        result.Append(template[lastIndex..]);
        return result.ToString();
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
        // Caret offset from end of clean text: how many left arrow keystrokes to get to cursor position
        var offsetFromEnd = cleanText.Length - cursorIdx;
        return (cleanText, offsetFromEnd);
    }
}
