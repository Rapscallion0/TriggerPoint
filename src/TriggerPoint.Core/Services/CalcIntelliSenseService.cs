using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TriggerPoint.Core.Services;

public enum CalcIntelliSenseKind
{
    Keyword,
    Modifier,
    Function,
    Unit,
    Example
}

public class CalcIntelliSenseItem
{
    public required string Text { get; set; }
    public required string DisplayText { get; set; }
    public required string Description { get; set; }
    public required CalcIntelliSenseKind Kind { get; set; }
    public string? Category { get; set; }
    public string? IconBadge { get; set; }
    public string? TokenToReplace { get; set; }
    public bool AppendSpaceAfter { get; set; } = false;
}

public static class CalcIntelliSenseService
{
    private static readonly List<CalcIntelliSenseItem> KeywordCatalog =
    [
        new() { Text = "pass", DisplayText = "pass [len] [nosym] [leet]", Description = "Generate secure random password with custom length & modifiers", Kind = CalcIntelliSenseKind.Keyword, Category = "Generators", IconBadge = "🔑", AppendSpaceAfter = true },
        new() { Text = "phrase", DisplayText = "phrase [words] [sep] [leet]", Description = "Generate memorable multi-word Diceware passphrase", Kind = CalcIntelliSenseKind.Keyword, Category = "Generators", IconBadge = "💬", AppendSpaceAfter = true },
        new() { Text = "guid", DisplayText = "guid [v7] [upper] [nohyphen]", Description = "Generate standard or UUIDv7 identifier", Kind = CalcIntelliSenseKind.Keyword, Category = "Generators", IconBadge = "🆔", AppendSpaceAfter = true },
        new() { Text = "roll", DisplayText = "roll 3d6+2 (or 1d20, 2d10-3)", Description = "Roll dice with count, sides, and positive/negative bonus modifiers", Kind = CalcIntelliSenseKind.Keyword, Category = "Dice", IconBadge = "🎲", AppendSpaceAfter = true },
        new() { Text = "rand", DisplayText = "rand [min-max] (e.g. rand 1-100)", Description = "Generate cryptographically secure random number in range", Kind = CalcIntelliSenseKind.Keyword, Category = "Math", IconBadge = "🔢", AppendSpaceAfter = true },
        new() { Text = "flip", DisplayText = "flip (or coin)", Description = "Flip a coin (Heads or Tails)", Kind = CalcIntelliSenseKind.Keyword, Category = "Random", IconBadge = "🪙", AppendSpaceAfter = false },
        new() { Text = "sha256", DisplayText = "sha256 <text>", Description = "Compute SHA-256 cryptographic hash of text", Kind = CalcIntelliSenseKind.Keyword, Category = "Hashes", IconBadge = "🔒", AppendSpaceAfter = true },
        new() { Text = "md5", DisplayText = "md5 <text>", Description = "Compute MD5 checksum hash of text", Kind = CalcIntelliSenseKind.Keyword, Category = "Hashes", IconBadge = "🔒", AppendSpaceAfter = true },
        new() { Text = "base64", DisplayText = "base64 <text>", Description = "Encode or decode Base64 string", Kind = CalcIntelliSenseKind.Keyword, Category = "Encoding", IconBadge = "🔤", AppendSpaceAfter = true },
        new() { Text = "urlencode", DisplayText = "urlencode <text>", Description = "URL encode query parameter characters", Kind = CalcIntelliSenseKind.Keyword, Category = "Encoding", IconBadge = "🌐", AppendSpaceAfter = true },
        new() { Text = "lorem", DisplayText = "lorem [3p] [50w]", Description = "Generate Lorem Ipsum placeholder paragraphs or words", Kind = CalcIntelliSenseKind.Keyword, Category = "Mock", IconBadge = "📄", AppendSpaceAfter = true },
        new() { Text = "mock name", DisplayText = "mock name", Description = "Generate realistic mock person full name", Kind = CalcIntelliSenseKind.Keyword, Category = "Mock", IconBadge = "👤", AppendSpaceAfter = false },
        new() { Text = "mock email", DisplayText = "mock email", Description = "Generate realistic mock email address", Kind = CalcIntelliSenseKind.Keyword, Category = "Mock", IconBadge = "✉️", AppendSpaceAfter = false },
        new() { Text = "now", DisplayText = "now [+ 2 days, 3 hours]", Description = "Current timestamp with natural offset arithmetic", Kind = CalcIntelliSenseKind.Keyword, Category = "Date", IconBadge = "⏰", AppendSpaceAfter = true },
        new() { Text = "today", DisplayText = "today [+ 1 week]", Description = "Current calendar date offset", Kind = CalcIntelliSenseKind.Keyword, Category = "Date", IconBadge = "📅", AppendSpaceAfter = true },
        new() { Text = "sqrt", DisplayText = "sqrt(x)", Description = "Square root mathematical function", Kind = CalcIntelliSenseKind.Function, Category = "Math", IconBadge = "📐", AppendSpaceAfter = false },
        new() { Text = "km to miles", DisplayText = "10 km to miles", Description = "Convert distance between metric and imperial units", Kind = CalcIntelliSenseKind.Unit, Category = "Units", IconBadge = "📏", AppendSpaceAfter = false },
        new() { Text = "c to f", DisplayText = "100 c to f", Description = "Convert Celsius temperature to Fahrenheit", Kind = CalcIntelliSenseKind.Unit, Category = "Units", IconBadge = "🌡️", AppendSpaceAfter = false },
        new() { Text = "kg to lbs", DisplayText = "75 kg to lbs", Description = "Convert mass between kilograms and pounds", Kind = CalcIntelliSenseKind.Unit, Category = "Units", IconBadge = "⚖️", AppendSpaceAfter = false },
        new() { Text = "bytes to gb", DisplayText = "1073741824 bytes to gb", Description = "Convert digital storage sizes", Kind = CalcIntelliSenseKind.Unit, Category = "Units", IconBadge = "💾", AppendSpaceAfter = false }
    ];

    private static readonly List<CalcIntelliSenseItem> PasswordModifiers =
    [
        new() { Text = "16", DisplayText = "16 (or len 16)", Description = "Standard 16-character password length", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "📏", AppendSpaceAfter = true },
        new() { Text = "24", DisplayText = "24 (or len 24)", Description = "Strong 24-character password length", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "📏", AppendSpaceAfter = true },
        new() { Text = "12", DisplayText = "12 (or len 12)", Description = "Compact 12-character password length", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "📏", AppendSpaceAfter = true },
        new() { Text = "nosym", DisplayText = "nosym", Description = "Exclude special symbols (!@#$)", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "🚫", AppendSpaceAfter = true },
        new() { Text = "symbols", DisplayText = "symbols (or sym)", Description = "Include special symbols", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "✨", AppendSpaceAfter = true },
        new() { Text = "leet", DisplayText = "leet (or 1337)", Description = "Replace letters with visually similar numbers (A→4, E→3, S→5)", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "👾", AppendSpaceAfter = true },
        new() { Text = "1337", DisplayText = "1337", Description = "Alias for leet substitutions", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "👾", AppendSpaceAfter = true },
        new() { Text = "b4", DisplayText = "b4", Description = "Phonetic leetspeak substitutions (before→b4, great→gr8, mate→m8)", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "🔤", AppendSpaceAfter = true },
        new() { Text = "mild", DisplayText = "mild", Description = "Mild leet intensity (modest 30% letter substitution rate)", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "🌱", AppendSpaceAfter = true },
        new() { Text = "agg", DisplayText = "agg (or extreme)", Description = "Aggressive leet intensity (high substitution rate)", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "🔥", AppendSpaceAfter = true },
        new() { Text = "safe", DisplayText = "safe (or readable)", Description = "Exclude visually ambiguous characters (0, O, o, 1, l, I, |)", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "👁️", AppendSpaceAfter = true },
        new() { Text = "upper", DisplayText = "upper", Description = "Ensure uppercase characters are included", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "🔠", AppendSpaceAfter = true },
        new() { Text = "lower", DisplayText = "lower", Description = "Ensure lowercase characters are included", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "🔡", AppendSpaceAfter = true },
        new() { Text = "digits", DisplayText = "digits (or nums)", Description = "Ensure numeric digits are included", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "🔢", AppendSpaceAfter = true },
        new() { Text = "count 10", DisplayText = "count 10", Description = "Generate 10 alternative candidate options", Kind = CalcIntelliSenseKind.Modifier, Category = "Password", IconBadge = "📋", AppendSpaceAfter = true }
    ];

    private static readonly List<CalcIntelliSenseItem> PassphraseModifiers =
    [
        new() { Text = "4", DisplayText = "4 (or words 4)", Description = "Generate passphrase with 4 words (balanced)", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "💬", AppendSpaceAfter = true },
        new() { Text = "5", DisplayText = "5 (or words 5)", Description = "Generate passphrase with 5 words (strong)", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "💬", AppendSpaceAfter = true },
        new() { Text = "6", DisplayText = "6 (or words 6)", Description = "Generate passphrase with 6 words (high security)", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "💬", AppendSpaceAfter = true },
        new() { Text = "sep -", DisplayText = "sep - (or hyphen)", Description = "Use hyphen as word separator", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "➖", AppendSpaceAfter = true },
        new() { Text = "sep _", DisplayText = "sep _ (or underscore)", Description = "Use underscore as word separator", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "＿", AppendSpaceAfter = true },
        new() { Text = "sep .", DisplayText = "sep . (or dot)", Description = "Use period/dot as word separator", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "•", AppendSpaceAfter = true },
        new() { Text = "sep space", DisplayText = "sep space", Description = "Use space as word separator", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "␣", AppendSpaceAfter = true },
        new() { Text = "title", DisplayText = "title", Description = "Capitalize each word (TitleCase)", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "🔠", AppendSpaceAfter = true },
        new() { Text = "upper", DisplayText = "upper", Description = "All uppercase letters", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "🔠", AppendSpaceAfter = true },
        new() { Text = "lower", DisplayText = "lower", Description = "All lowercase letters", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "🔡", AppendSpaceAfter = true },
        new() { Text = "leet", DisplayText = "leet (or 1337)", Description = "Visual leetspeak letter substitutions", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "👾", AppendSpaceAfter = true },
        new() { Text = "1337", DisplayText = "1337", Description = "Alias for leet substitutions", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "👾", AppendSpaceAfter = true },
        new() { Text = "b4", DisplayText = "b4", Description = "Phonetic leetspeak substitutions", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "🔤", AppendSpaceAfter = true },
        new() { Text = "count 10", DisplayText = "count 10", Description = "Generate 10 alternative candidate passphrases", Kind = CalcIntelliSenseKind.Modifier, Category = "Passphrase", IconBadge = "📋", AppendSpaceAfter = true }
    ];

    private static readonly List<CalcIntelliSenseItem> GuidModifiers =
    [
        new() { Text = "v7", DisplayText = "v7 (or uuidv7)", Description = "Generate time-ordered UUIDv7 (RFC 9562)", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "⏱️", AppendSpaceAfter = true },
        new() { Text = "upper", DisplayText = "upper (or uppercase)", Description = "Uppercase hex characters (e.g. 4F3E8B...)", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "🔠", AppendSpaceAfter = true },
        new() { Text = "lower", DisplayText = "lower (or lowercase)", Description = "Lowercase hex characters (default)", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "🔡", AppendSpaceAfter = true },
        new() { Text = "nohyphen", DisplayText = "nohyphen (or compact)", Description = "Compact 32-character hex string without hyphens", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "➖", AppendSpaceAfter = true },
        new() { Text = "braces", DisplayText = "braces", Description = "Enclose in curly braces: {xxxxxxxx-xxxx-...}", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "{ }", AppendSpaceAfter = true },
        new() { Text = "parens", DisplayText = "parens", Description = "Enclose in parentheses: (xxxxxxxx-xxxx-...)", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "( )", AppendSpaceAfter = true },
        new() { Text = "base64", DisplayText = "base64 (or b64)", Description = "Compact URL-safe 22-character Base64 string", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "🔤", AppendSpaceAfter = true },
        new() { Text = "count 10", DisplayText = "count 10", Description = "Generate 10 candidate UUIDs", Kind = CalcIntelliSenseKind.Modifier, Category = "GUID", IconBadge = "📋", AppendSpaceAfter = true }
    ];

    public static string? GetSignature(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string clean = input.TrimStart();
        if (clean.StartsWith("@calc", StringComparison.OrdinalIgnoreCase)) clean = clean[5..].TrimStart(':', ' ');
        else if (clean.StartsWith('=')) clean = clean[1..].TrimStart();

        var tokens = clean.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;
        string first = tokens[0].ToLowerInvariant();

        if (first.StartsWith("roll")) return "roll <count>d<sides> [+/-modifier]";
        if (first is "pass" or "password" or "pw" || first.StartsWith("pass")) return "pass [length] [nosym] [leet] [safe] [upper] [lower] [digits] [count N]";
        if (first is "phrase" or "passphrase" || first.StartsWith("phrase")) return "phrase [words] [sep -|_|. ] [title|upper|lower] [leet] [count N]";
        if (first is "guid" or "uuid" || first.StartsWith("guid") || first.StartsWith("uuid")) return "guid [v7] [upper] [nohyphen] [braces] [parens] [base64] [count N]";
        if (first.StartsWith("rand")) return "rand [min-max] (e.g. rand 1-100)";
        if (first.StartsWith("lorem")) return "lorem [paragraphs] [words]";

        return null;
    }

    public static IReadOnlyList<CalcIntelliSenseItem> GetSuggestions(string input, int caretPosition = -1)
    {
        if (input == null) return [];

        var text = input.TrimStart();
        if (text.StartsWith("@calc", StringComparison.OrdinalIgnoreCase))
        {
            text = text[5..].TrimStart(':', ' ');
        }
        else if (text.StartsWith('='))
        {
            text = text[1..].TrimStart();
        }

        // Handle attached parameter queries (e.g. "roll2", "pass16", "phrase4", "guidv7")
        string normalized = text;
        var attachedMatch = Regex.Match(text, @"^(roll|pass|password|pw|phrase|passphrase|guid|uuid)(\d.*|[a-zA-Z]\d.*)$", RegexOptions.IgnoreCase);
        if (attachedMatch.Success)
        {
            normalized = attachedMatch.Groups[1].Value + " " + attachedMatch.Groups[2].Value;
        }

        var tokens = normalized.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        bool hasTrailingSpace = text.EndsWith(' ');

        // If empty or only typed '=': return top keywords with trailing space flag
        if (tokens.Length == 0)
        {
            return KeywordCatalog.Take(12).ToList();
        }

        // If typing the initial keyword without a space:
        if (tokens.Length == 1 && !hasTrailingSpace)
        {
            string single = tokens[0].ToLowerInvariant();

            // Exact keyword match: suggest the command itself with a trailing space + initial parameter completions
            if (single is "roll")
            {
                return GetRollSuggestions("", "");
            }
            if (single is "pass" or "password" or "pw")
            {
                return GetPasswordSuggestions("", tokens);
            }
            if (single is "phrase" or "passphrase")
            {
                return GetPassphraseSuggestions("", tokens);
            }
            if (single is "guid" or "uuid")
            {
                return GetGuidSuggestions("", tokens);
            }

            // Partial keyword match: return matching keywords with TokenToReplace set to user query
            var matchingKeywords = KeywordCatalog
                .Where(k => k.Text.StartsWith(single, StringComparison.OrdinalIgnoreCase) ||
                            k.DisplayText.Contains(single, StringComparison.OrdinalIgnoreCase))
                .Select(k => new CalcIntelliSenseItem
                {
                    Text = k.Text,
                    DisplayText = k.DisplayText,
                    Description = k.Description,
                    Kind = k.Kind,
                    Category = k.Category,
                    IconBadge = k.IconBadge,
                    TokenToReplace = single,
                    AppendSpaceAfter = true
                })
                .ToList();

            if (matchingKeywords.Count > 0)
            {
                return matchingKeywords;
            }

            return KeywordCatalog.Take(8).ToList();
        }

        string cmd = tokens[0].ToLowerInvariant();
        string activeToken = hasTrailingSpace ? "" : tokens[^1];

        if (cmd == "roll")
        {
            return GetRollSuggestions(activeToken, normalized);
        }
        if (cmd is "pass" or "password" or "pw")
        {
            return GetPasswordSuggestions(activeToken, tokens);
        }
        if (cmd is "phrase" or "passphrase")
        {
            return GetPassphraseSuggestions(activeToken, tokens);
        }
        if (cmd is "guid" or "uuid")
        {
            return GetGuidSuggestions(activeToken, tokens);
        }
        if (cmd == "rand")
        {
            return GetRandSuggestions(activeToken);
        }

        // Generic query / arithmetic autocomplete
        string search = (hasTrailingSpace ? "" : activeToken).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(search))
        {
            return KeywordCatalog.Take(12).ToList();
        }

        var matched = KeywordCatalog
            .Where(k => k.Text.StartsWith(search, StringComparison.OrdinalIgnoreCase) ||
                        k.Text.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        k.DisplayText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        k.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(k => new CalcIntelliSenseItem
            {
                Text = k.Text,
                DisplayText = k.DisplayText,
                Description = k.Description,
                Kind = k.Kind,
                Category = k.Category,
                IconBadge = k.IconBadge,
                TokenToReplace = search,
                AppendSpaceAfter = k.AppendSpaceAfter
            })
            .ToList();

        return matched.Count > 0 ? matched : KeywordCatalog.Take(8).ToList();
    }

    private static List<CalcIntelliSenseItem> GetRollSuggestions(string activeToken, string fullQuery)
    {
        var list = new List<CalcIntelliSenseItem>();

        // Case A: No token yet (e.g. "=roll " or "=roll")
        if (string.IsNullOrWhiteSpace(activeToken))
        {
            list.Add(new() { Text = "3d6+2", DisplayText = "3d6+2", Description = "Three 6-sided dice with +2 bonus modifier", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "1d20", DisplayText = "1d20", Description = "Standard 20-sided d20 check roll", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "3d6", DisplayText = "3d6", Description = "Three 6-sided dice (classic bell curve)", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "2d6", DisplayText = "2d6", Description = "Two 6-sided dice", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "4d6", DisplayText = "4d6", Description = "Four 6-sided dice", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "1d100", DisplayText = "1d100", Description = "Percentile d100 roll (1–100)", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "2d10", DisplayText = "2d10", Description = "Two 10-sided dice", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "1d12", DisplayText = "1d12", Description = "Single 12-sided die", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "1d8", DisplayText = "1d8", Description = "Single 8-sided die", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            list.Add(new() { Text = "1d6", DisplayText = "1d6", Description = "Single 6-sided die", Kind = CalcIntelliSenseKind.Example, Category = "Dice", IconBadge = "🎲" });
            return list;
        }

        // Case B: User typed just a count number (e.g. "2" or "3") -> autocomplete sides!
        if (Regex.IsMatch(activeToken, @"^\d+$"))
        {
            string count = activeToken;
            int[] commonSides = [6, 8, 10, 12, 20, 100, 4];
            foreach (int sides in commonSides)
            {
                string formula = $"{count}d{sides}";
                list.Add(new()
                {
                    Text = formula,
                    DisplayText = formula,
                    Description = $"Roll {count} {sides}-sided dice",
                    Kind = CalcIntelliSenseKind.Example,
                    Category = "Dice",
                    IconBadge = "🎲",
                    TokenToReplace = activeToken,
                    AppendSpaceAfter = false
                });
            }
            return list;
        }

        // Case C: User typed Nd (e.g. "2d", "1d", "d")
        if (Regex.IsMatch(activeToken, @"^\d*d$", RegexOptions.IgnoreCase))
        {
            string prefix = activeToken.TrimEnd('d', 'D');
            if (string.IsNullOrEmpty(prefix)) prefix = "1";
            int[] commonSides = [6, 8, 10, 12, 20, 100, 4];
            foreach (int sides in commonSides)
            {
                string formula = $"{prefix}d{sides}";
                list.Add(new()
                {
                    Text = formula,
                    DisplayText = formula,
                    Description = $"Roll {prefix} {sides}-sided dice",
                    Kind = CalcIntelliSenseKind.Example,
                    Category = "Dice",
                    IconBadge = "🎲",
                    TokenToReplace = activeToken,
                    AppendSpaceAfter = false
                });
            }
            return list;
        }

        // Case D: User typed NdS (e.g. "3d6", "1d20") -> autocomplete modifiers!
        var diceMatch = Regex.Match(activeToken, @"^(\d{0,3}d\d{1,4})$", RegexOptions.IgnoreCase);
        if (diceMatch.Success)
        {
            string baseDice = diceMatch.Groups[1].Value;
            string[] modOffsets = ["+2", "+5", "+1", "+3", "-2", "-1", "-5"];
            foreach (var mod in modOffsets)
            {
                string formula = $"{baseDice}{mod}";
                list.Add(new()
                {
                    Text = formula,
                    DisplayText = formula,
                    Description = $"{baseDice} with {mod} bonus/penalty",
                    Kind = CalcIntelliSenseKind.Example,
                    Category = "Dice",
                    IconBadge = "🎲",
                    TokenToReplace = activeToken,
                    AppendSpaceAfter = false
                });
            }
            return list;
        }

        // Case E: User typed NdS+ or NdS- (e.g. "3d6+" or "1d20-")
        var plusMatch = Regex.Match(activeToken, @"^(\d{0,3}d\d{1,4}[+-])$", RegexOptions.IgnoreCase);
        if (plusMatch.Success)
        {
            string baseWithSign = plusMatch.Groups[1].Value;
            int[] offsets = [2, 5, 1, 3, 4, 10];
            foreach (var offset in offsets)
            {
                string formula = $"{baseWithSign}{offset}";
                list.Add(new()
                {
                    Text = formula,
                    DisplayText = formula,
                    Description = $"Apply {offset} modifier",
                    Kind = CalcIntelliSenseKind.Example,
                    Category = "Dice",
                    IconBadge = "🎲",
                    TokenToReplace = activeToken,
                    AppendSpaceAfter = false
                });
            }
            return list;
        }

        return list;
    }

    private static List<CalcIntelliSenseItem> GetPasswordSuggestions(string activeToken, string[] existingTokens)
    {
        var existingLower = new HashSet<string>(existingTokens.Select(t => t.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        var list = new List<CalcIntelliSenseItem>();

        // If active token is a number or partial length (e.g. "1", "2")
        if (Regex.IsMatch(activeToken, @"^\d+$"))
        {
            string[] lengths = ["16", "24", "12", "14", "18", "20", "32", "8"];
            foreach (var len in lengths)
            {
                if (len.StartsWith(activeToken, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new()
                    {
                        Text = len,
                        DisplayText = $"{len} characters",
                        Description = $"Set password length to {len} characters",
                        Kind = CalcIntelliSenseKind.Modifier,
                        Category = "Password",
                        IconBadge = "📏",
                        TokenToReplace = activeToken,
                        AppendSpaceAfter = true
                    });
                }
            }
            if (list.Count > 0) return list;
        }

        // Filter standard modifiers
        var pool = PasswordModifiers.Where(m => !existingLower.Contains(m.Text.ToLowerInvariant())).ToList();

        if (string.IsNullOrWhiteSpace(activeToken))
        {
            return pool;
        }

        var filtered = pool
            .Where(m => m.Text.StartsWith(activeToken, StringComparison.OrdinalIgnoreCase) ||
                        m.DisplayText.Contains(activeToken, StringComparison.OrdinalIgnoreCase))
            .Select(m => new CalcIntelliSenseItem
            {
                Text = m.Text,
                DisplayText = m.DisplayText,
                Description = m.Description,
                Kind = m.Kind,
                Category = m.Category,
                IconBadge = m.IconBadge,
                TokenToReplace = activeToken,
                AppendSpaceAfter = true
            })
            .ToList();

        return filtered.Count > 0 ? filtered : pool;
    }

    private static List<CalcIntelliSenseItem> GetPassphraseSuggestions(string activeToken, string[] existingTokens)
    {
        var existingLower = new HashSet<string>(existingTokens.Select(t => t.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

        // If numeric token (e.g. "4", "5")
        if (Regex.IsMatch(activeToken, @"^\d+$"))
        {
            var wordCounts = new[] { "4", "5", "6", "3", "7" };
            var numList = new List<CalcIntelliSenseItem>();
            foreach (var w in wordCounts)
            {
                if (w.StartsWith(activeToken))
                {
                    numList.Add(new()
                    {
                        Text = w,
                        DisplayText = $"{w} words",
                        Description = $"Generate passphrase with {w} Diceware words",
                        Kind = CalcIntelliSenseKind.Modifier,
                        Category = "Passphrase",
                        IconBadge = "💬",
                        TokenToReplace = activeToken,
                        AppendSpaceAfter = true
                    });
                }
            }
            if (numList.Count > 0) return numList;
        }

        var pool = PassphraseModifiers.Where(m => !existingLower.Contains(m.Text.ToLowerInvariant())).ToList();

        if (string.IsNullOrWhiteSpace(activeToken))
        {
            return pool;
        }

        var filtered = pool
            .Where(m => m.Text.StartsWith(activeToken, StringComparison.OrdinalIgnoreCase) ||
                        m.DisplayText.Contains(activeToken, StringComparison.OrdinalIgnoreCase))
            .Select(m => new CalcIntelliSenseItem
            {
                Text = m.Text,
                DisplayText = m.DisplayText,
                Description = m.Description,
                Kind = m.Kind,
                Category = m.Category,
                IconBadge = m.IconBadge,
                TokenToReplace = activeToken,
                AppendSpaceAfter = true
            })
            .ToList();

        return filtered.Count > 0 ? filtered : pool;
    }

    private static List<CalcIntelliSenseItem> GetGuidSuggestions(string activeToken, string[] existingTokens)
    {
        var existingLower = new HashSet<string>(existingTokens.Select(t => t.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        var pool = GuidModifiers.Where(m => !existingLower.Contains(m.Text.ToLowerInvariant())).ToList();

        if (string.IsNullOrWhiteSpace(activeToken))
        {
            return pool;
        }

        var filtered = pool
            .Where(m => m.Text.StartsWith(activeToken, StringComparison.OrdinalIgnoreCase) ||
                        m.DisplayText.Contains(activeToken, StringComparison.OrdinalIgnoreCase))
            .Select(m => new CalcIntelliSenseItem
            {
                Text = m.Text,
                DisplayText = m.DisplayText,
                Description = m.Description,
                Kind = m.Kind,
                Category = m.Category,
                IconBadge = m.IconBadge,
                TokenToReplace = activeToken,
                AppendSpaceAfter = true
            })
            .ToList();

        return filtered.Count > 0 ? filtered : pool;
    }

    private static List<CalcIntelliSenseItem> GetRandSuggestions(string activeToken)
    {
        var list = new List<CalcIntelliSenseItem>
        {
            new() { Text = "1-100", DisplayText = "1-100", Description = "Random number between 1 and 100", Kind = CalcIntelliSenseKind.Example, Category = "Math", IconBadge = "🔢", TokenToReplace = activeToken, AppendSpaceAfter = false },
            new() { Text = "1-10", DisplayText = "1-10", Description = "Random number between 1 and 10", Kind = CalcIntelliSenseKind.Example, Category = "Math", IconBadge = "🔢", TokenToReplace = activeToken, AppendSpaceAfter = false },
            new() { Text = "1-50", DisplayText = "1-50", Description = "Random number between 1 and 50", Kind = CalcIntelliSenseKind.Example, Category = "Math", IconBadge = "🔢", TokenToReplace = activeToken, AppendSpaceAfter = false },
            new() { Text = "1-1000", DisplayText = "1-1000", Description = "Random number between 1 and 1000", Kind = CalcIntelliSenseKind.Example, Category = "Math", IconBadge = "🔢", TokenToReplace = activeToken, AppendSpaceAfter = false },
            new() { Text = "1-6", DisplayText = "1-6", Description = "Random die roll (1 to 6)", Kind = CalcIntelliSenseKind.Example, Category = "Math", IconBadge = "🎲", TokenToReplace = activeToken, AppendSpaceAfter = false }
        };
        return list;
    }

    public static string ApplySuggestion(string currentQuery, CalcIntelliSenseItem item)
    {
        if (string.IsNullOrEmpty(currentQuery))
        {
            return "= " + item.Text + (item.AppendSpaceAfter ? " " : "");
        }

        string prefix = currentQuery.StartsWith("@calc", StringComparison.OrdinalIgnoreCase) ? "@calc " :
                       currentQuery.StartsWith("=") ? "= " : "= ";

        string clean = currentQuery.TrimStart();
        if (clean.StartsWith("@calc", StringComparison.OrdinalIgnoreCase)) clean = clean[5..].TrimStart(':', ' ');
        else if (clean.StartsWith('=')) clean = clean[1..].TrimStart();

        // 1. If item has a designated TokenToReplace
        if (!string.IsNullOrEmpty(item.TokenToReplace))
        {
            string trimmed = clean.TrimEnd();
            if (trimmed.EndsWith(item.TokenToReplace, StringComparison.OrdinalIgnoreCase))
            {
                string before = trimmed.Substring(0, trimmed.Length - item.TokenToReplace.Length);
                string replacement = item.Text + (item.AppendSpaceAfter ? " " : "");
                return prefix + before + replacement;
            }

            int lastIdx = clean.LastIndexOf(item.TokenToReplace, StringComparison.OrdinalIgnoreCase);
            if (lastIdx >= 0)
            {
                string before = clean.Substring(0, lastIdx);
                string after = clean.Substring(lastIdx + item.TokenToReplace.Length);
                string replacement = item.Text + (item.AppendSpaceAfter ? " " : "");
                return prefix + before + replacement + after;
            }
        }

        // 2. Keyword replacement
        var tokens = clean.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        bool endsWithSpace = clean.EndsWith(' ');

        if (item.Kind == CalcIntelliSenseKind.Keyword)
        {
            if (tokens.Length <= 1 && !endsWithSpace)
            {
                return prefix + item.Text + (item.AppendSpaceAfter ? " " : "");
            }
        }

        // 3. Fallback partial match if TokenToReplace is not explicitly set
        if (!endsWithSpace && tokens.Length > 0)
        {
            string last = tokens[^1].ToLowerInvariant();
            if (item.Text.StartsWith(last, StringComparison.OrdinalIgnoreCase))
            {
                tokens[^1] = item.Text;
                return prefix + string.Join(" ", tokens) + (item.AppendSpaceAfter ? " " : "");
            }
        }

        // 4. Modifier or example append
        return prefix + clean + (endsWithSpace ? "" : " ") + item.Text + (item.AppendSpaceAfter ? " " : "");
    }
}
