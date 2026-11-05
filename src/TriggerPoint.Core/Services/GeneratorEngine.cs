using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static class GeneratorEngine
{
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{}|;:,.<>?";

    // Characters that are safe from visual ambiguity (excludes 0, O, o, 1, l, I, |)
    private const string SafeLowercase = "abcdefghijkmnpqrstuvwxyz";
    private const string SafeUppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string SafeDigits = "23456789";
    private const string SafeSymbols = "!@#$%^&*()-_=+[]{};:,.<>?";

    private static readonly string[] FirstNames =
    [
        "Alex", "Jordan", "Taylor", "Morgan", "Casey", "Riley", "Avery", "Jamie", "Logan", "Peyton",
        "Sam", "Dakota", "Reese", "Quinn", "Skyler", "Cameron", "Jesse", "Rowan", "Hayden", "Kendall",
        "Harper", "Elliot", "Finley", "Emerson", "Sage", "River", "Adrian", "Blake", "Charlie", "Dylan"
    ];

    private static readonly string[] LastNames =
    [
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez",
        "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin",
        "Lee", "Perez", "Thompson", "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson",
        "Walker", "Young", "Allen", "King", "Wright", "Scott", "Torres", "Nguyen", "Hill", "Flores"
    ];

    private static readonly string[] EmailDomains =
    [
        "example.com", "testmail.org", "sampledev.net", "demomail.io", "cloudmock.app", "devsandbox.co"
    ];

    private static readonly string[] LoremWords =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do",
        "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", "enim",
        "ad", "minim", "veniam", "quis", "nostrud", "exercitation", "ullamco", "laboris", "nisi", "ut",
        "aliquip", "ex", "ea", "commodo", "consequat", "duis", "aute", "irure", "in", "reprehenderit",
        "voluptate", "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint", "occaecat",
        "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia", "deserunt", "mollit", "anim",
        "id", "est", "laborum", "curabitur", "pretium", "tincidunt", "lacus", "fringilla", "porta", "sagittis",
        "varius", "aenean", "posuere", "tortor", "pellentesque", "facilisis", "ultricies", "integer", "vulputate",
        "vehicula", "elementum", "vivamus", "elementum", "convallis", "rhoncus", "maecenas", "placerat", "fermentum"
    ];

    public static CalculatorResult? TryEvaluate(string input, AppSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();
        if (trimmed.StartsWith("@calc", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[5..].TrimStart(':', ' ');
            if (string.IsNullOrWhiteSpace(trimmed)) return null;
        }

        if (trimmed.StartsWith('='))
        {
            trimmed = trimmed[1..].Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return null;
        }

        settings ??= new AppSettings();

        // Forgiving spacing normalization (e.g. roll2d6 -> roll 2d6, pass16 -> pass 16, phrase4 -> phrase 4, guidv7 -> guid v7)
        trimmed = Regex.Replace(trimmed, @"^(roll|pass|password|pw|pin|phrase|passphrase|guid|uuid)(\d.*)$", "$1 $2", RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"^(guid|uuid)(v7.*)$", "$1 $2", RegexOptions.IgnoreCase);

        // 1. GUID / UUID
        var guidRes = TryEvaluateGuid(trimmed, settings);
        if (guidRes != null) return guidRes;

        // 2. Passwords & PINs
        var passRes = TryEvaluatePassword(trimmed, settings);
        if (passRes != null) return passRes;

        // 3. Passphrases
        var phraseRes = TryEvaluatePassphrase(trimmed, settings);
        if (phraseRes != null) return phraseRes;

        // 4. Hashes & Checksums
        var hashRes = TryEvaluateHash(trimmed);
        if (hashRes != null) return hashRes;

        // 5. Encodings & Transforms
        var encRes = TryEvaluateEncoding(trimmed);
        if (encRes != null) return encRes;

        // 6. Random Numbers, Dice & Coins
        var randRes = TryEvaluateRandomOrDice(trimmed);
        if (randRes != null) return randRes;

        // 7. Mock Data & Lorem
        var mockRes = TryEvaluateMockData(trimmed);
        if (mockRes != null) return mockRes;

        return null;
    }

    #region GUID / UUID Generator

    private static CalculatorResult? TryEvaluateGuid(string input, AppSettings settings)
    {
        var lower = input.ToLowerInvariant();
        if (!lower.StartsWith("guid") && !lower.StartsWith("uuid")) return null;

        // Extract parameters
        var tokens = lower.Split([' ', ',', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || (tokens[0] != "guid" && tokens[0] != "uuid")) return null;

        int candidateCount = settings.GeneratorCandidateCount;
        for (int i = 1; i < tokens.Length; i++)
        {
            if ((tokens[i] == "count" || tokens[i] == "c") && i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int cVal))
            {
                candidateCount = Math.Clamp(cVal, 1, 25);
            }
        }

        GuidFormatOption format = settings.DefaultGuidFormat;
        if (tokens.Any(t => t is "upper" or "uppercase" or "u")) format = GuidFormatOption.Uppercase;
        else if (tokens.Any(t => t is "lower" or "lowercase" or "l")) format = GuidFormatOption.StandardHyphenated;
        else if (tokens.Any(t => t is "nohyphen" or "nohyphens" or "n" or "compact")) format = GuidFormatOption.NoHyphens;
        else if (tokens.Any(t => t is "braces" or "brace" or "b")) format = GuidFormatOption.Braced;
        else if (tokens.Any(t => t is "parens" or "paren" or "p")) format = GuidFormatOption.Parentheses;
        else if (tokens.Any(t => t is "base64" or "b64")) format = GuidFormatOption.Base64UrlSafe;
        else if (tokens.Any(t => t is "v7" or "uuidv7")) format = GuidFormatOption.UuidV7;

        var primaryGuid = format == GuidFormatOption.UuidV7 ? GenerateUuidV7() : Guid.NewGuid();
        string primaryFormatted = FormatGuid(primaryGuid, format);

        var alternatives = new List<AlternativeMeasurement>();

        // 1. Sibling formats of the primary GUID
        var formatMap = new (GuidFormatOption Opt, string Label)[]
        {
            (GuidFormatOption.StandardHyphenated, "Hyphenated Lower"),
            (GuidFormatOption.Uppercase, "Hyphenated Upper"),
            (GuidFormatOption.NoHyphens, "No Hyphens (32-hex)"),
            (GuidFormatOption.Braced, "Braced {...}"),
            (GuidFormatOption.Parentheses, "Parentheses (...)"),
            (GuidFormatOption.Base64UrlSafe, "URL-Safe Base64"),
            (GuidFormatOption.UuidV7, "Timestamp-Ordered (v7)")
        };

        foreach (var (opt, label) in formatMap)
        {
            if (opt != format)
            {
                string variantStr = FormatGuid(primaryGuid, opt);
                alternatives.Add(new AlternativeMeasurement(
                    Label: label,
                    FormattedValue: variantStr,
                    Description: $"Primary GUID formatted as {label}",
                    Category: "Formats",
                    IsCommon: true));
            }
        }

        // 2. Additional fresh candidates matching requested format
        for (int i = 1; i < candidateCount; i++)
        {
            var nextGuid = format == GuidFormatOption.UuidV7 ? GenerateUuidV7() : Guid.NewGuid();
            string nextFormatted = FormatGuid(nextGuid, format);
            alternatives.Add(new AlternativeMeasurement(
                Label: $"Candidate #{i + 1}",
                FormattedValue: nextFormatted,
                Description: $"Fresh distinct GUID candidate ({format})",
                Category: "Candidates",
                IsCommon: true));
        }

        string desc = $"Format: {format} • Default: {settings.DefaultGuidFormat} • 128-bit Cryptographic UUID";
        return new CalculatorResult(input, primaryFormatted, desc, alternatives);
    }

    public static string FormatGuid(Guid guid, GuidFormatOption format)
    {
        return format switch
        {
            GuidFormatOption.StandardHyphenated => guid.ToString("D"),
            GuidFormatOption.Uppercase => guid.ToString("D").ToUpperInvariant(),
            GuidFormatOption.NoHyphens => guid.ToString("N"),
            GuidFormatOption.Braced => guid.ToString("B"),
            GuidFormatOption.Parentheses => guid.ToString("P"),
            GuidFormatOption.Base64UrlSafe => Convert.ToBase64String(guid.ToByteArray()).Replace('+', '-').Replace('/', '_').TrimEnd('='),
            GuidFormatOption.UuidV7 => guid.ToString("D"),
            _ => guid.ToString("D")
        };
    }

    public static Guid GenerateUuidV7()
    {
        return Guid.CreateVersion7();
    }

    #endregion

    #region Password & PIN Generator

    public record PasswordConfig(
        int Length,
        bool IncludeUppercase,
        bool IncludeLowercase,
        bool IncludeDigits,
        bool IncludeSpecial,
        bool ExcludeAmbiguous,
        bool IsPin = false);

    private static CalculatorResult? TryEvaluatePassword(string input, AppSettings settings)
    {
        var lower = input.ToLowerInvariant();
        bool isPw = lower.StartsWith("pass") || lower.StartsWith("pw");
        bool isPin = lower.StartsWith("pin");

        if (!isPw && !isPin) return null;

        var tokens = lower.Split([' ', ',', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        string first = tokens[0];
        if (isPw && first != "pass" && first != "password" && first != "pw") return null;
        if (isPin && first != "pin") return null;

        int length = isPin ? 6 : settings.DefaultPasswordLength;
        int candidateCount = settings.GeneratorCandidateCount;

        bool includeUpper = settings.DefaultPasswordIncludeUppercase;
        bool includeLower = settings.DefaultPasswordIncludeLowercase;
        bool includeDigits = settings.DefaultPasswordIncludeDigits;
        bool includeSpecial = settings.DefaultPasswordIncludeSpecial;
        bool excludeAmbiguous = settings.DefaultPasswordExcludeAmbiguous;

        if (isPin)
        {
            includeUpper = false;
            includeLower = false;
            includeDigits = true;
            includeSpecial = false;
            excludeAmbiguous = false;
        }

        // Parse query tokens
        for (int i = 1; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (int.TryParse(t, out int numVal))
            {
                if (numVal >= 3 && numVal <= 256)
                {
                    length = numVal;
                }
            }
            else if ((t == "count" || t == "c") && i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int cVal))
            {
                candidateCount = Math.Clamp(cVal, 1, 25);
            }
            else if (t is "nosym" or "no-symbols" or "no-special" or "nospecial" or "-s")
            {
                includeSpecial = false;
            }
            else if (t is "sym" or "symbols" or "special" or "+s")
            {
                includeSpecial = true;
            }
            else if (t is "nonum" or "no-numbers" or "no-digits" or "-n")
            {
                includeDigits = false;
            }
            else if (t is "num" or "numbers" or "digits" or "+n")
            {
                includeDigits = true;
            }
            else if (t is "noupper" or "no-upper")
            {
                includeUpper = false;
            }
            else if (t is "upper" or "+u")
            {
                includeUpper = true;
            }
            else if (t is "nolower" or "no-lower")
            {
                includeLower = false;
            }
            else if (t is "lower" or "+l")
            {
                includeLower = true;
            }
            else if (t is "safe" or "readable" or "no-ambiguous" or "clean")
            {
                excludeAmbiguous = true;
            }
            else if (t is "pin")
            {
                includeUpper = false;
                includeLower = false;
                includeDigits = true;
                includeSpecial = false;
                excludeAmbiguous = false;
            }
        }

        // Guarantee at least one character set is selected
        if (!includeUpper && !includeLower && !includeDigits && !includeSpecial)
        {
            includeLower = true;
            includeDigits = true;
        }

        var config = new PasswordConfig(
            length,
            includeUpper,
            includeLower,
            includeDigits,
            includeSpecial,
            excludeAmbiguous,
            isPin);

        var candidates = GeneratePasswordBatch(config, candidateCount);
        if (candidates.Count == 0) return null;

        string primary = candidates[0];
        var alternatives = new List<AlternativeMeasurement>();

        for (int i = 1; i < candidates.Count; i++)
        {
            alternatives.Add(new AlternativeMeasurement(
                Label: $"Candidate #{i + 1}",
                FormattedValue: candidates[i],
                Description: $"{length}-character password meeting all criteria",
                Category: "Candidates",
                IsCommon: true));
        }

        double entropyBits = CalculatePasswordEntropy(config);
        string entropyDesc = entropyBits >= 120 ? "Very Strong" : entropyBits >= 80 ? "Strong" : entropyBits >= 50 ? "Moderate" : "Basic";
        
        var parts = new List<string>();
        if (config.IncludeUppercase) parts.Add("A-Z");
        if (config.IncludeLowercase) parts.Add("a-z");
        if (config.IncludeDigits) parts.Add("0-9");
        if (config.IncludeSpecial) parts.Add("#$!");
        if (config.ExcludeAmbiguous) parts.Add("No Ambiguous");

        string desc = $"{length} chars • [{string.Join(", ", parts)}] • Entropy: {entropyBits:F0} bits ({entropyDesc}) • Default: {settings.DefaultPasswordLength} chars";

        return new CalculatorResult(input, primary, desc, alternatives);
    }

    public static List<string> GeneratePasswordBatch(PasswordConfig config, int count)
    {
        var results = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < count && seen.Count < count; i++)
        {
            string pw = GenerateSinglePassword(config);
            if (seen.Add(pw))
            {
                results.Add(pw);
            }
        }

        while (results.Count < count)
        {
            results.Add(GenerateSinglePassword(config));
        }

        return results;
    }

    public static string GenerateSinglePassword(PasswordConfig config)
    {
        string upper = config.ExcludeAmbiguous ? SafeUppercase : UppercaseChars;
        string lower = config.ExcludeAmbiguous ? SafeLowercase : LowercaseChars;
        string digits = config.ExcludeAmbiguous ? SafeDigits : DigitChars;
        string symbols = config.ExcludeAmbiguous ? SafeSymbols : SymbolChars;

        var pools = new List<string>();
        if (config.IncludeUppercase) pools.Add(upper);
        if (config.IncludeLowercase) pools.Add(lower);
        if (config.IncludeDigits) pools.Add(digits);
        if (config.IncludeSpecial) pools.Add(symbols);

        if (pools.Count == 0) pools.Add(lower);

        var sb = new StringBuilder(config.Length);

        // Ensure at least one character from each active character pool
        foreach (var pool in pools)
        {
            if (sb.Length < config.Length)
            {
                sb.Append(pool[RandomNumberGenerator.GetInt32(pool.Length)]);
            }
        }

        // Fill remaining characters from combined pool
        string allChars = string.Concat(pools);
        while (sb.Length < config.Length)
        {
            sb.Append(allChars[RandomNumberGenerator.GetInt32(allChars.Length)]);
        }

        // Fisher-Yates cryptographic shuffle
        char[] array = sb.ToString().ToCharArray();
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }

        return new string(array);
    }

    private static double CalculatePasswordEntropy(PasswordConfig config)
    {
        int poolSize = 0;
        if (config.IncludeUppercase) poolSize += config.ExcludeAmbiguous ? SafeUppercase.Length : UppercaseChars.Length;
        if (config.IncludeLowercase) poolSize += config.ExcludeAmbiguous ? SafeLowercase.Length : LowercaseChars.Length;
        if (config.IncludeDigits) poolSize += config.ExcludeAmbiguous ? SafeDigits.Length : DigitChars.Length;
        if (config.IncludeSpecial) poolSize += config.ExcludeAmbiguous ? SafeSymbols.Length : SymbolChars.Length;

        if (poolSize <= 1) poolSize = 2;
        return config.Length * Math.Log2(poolSize);
    }

    #endregion

    #region Passphrase Generator (Diceware with Leetspeak & Phonetics)

    public record PassphraseConfig(
        int WordCount,
        PassphraseSeparatorOption Separator,
        bool Capitalize,
        bool IncludeNumber,
        PassphraseLeetMode LeetMode,
        PassphraseLeetIntensity LeetIntensity);

    private static CalculatorResult? TryEvaluatePassphrase(string input, AppSettings settings)
    {
        var lower = input.ToLowerInvariant();
        if (!lower.StartsWith("phrase") && !lower.StartsWith("passphrase") && !lower.StartsWith("diceware") && !lower.StartsWith("xkcd"))
        {
            return null;
        }

        var tokens = lower.Split([' ', ',', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || (tokens[0] != "phrase" && tokens[0] != "passphrase" && tokens[0] != "diceware" && tokens[0] != "xkcd"))
        {
            return null;
        }

        int wordCount = settings.DefaultPassphraseWordCount;
        int candidateCount = settings.GeneratorCandidateCount;
        var sep = settings.DefaultPassphraseSeparator;
        bool cap = settings.DefaultPassphraseCapitalize;
        bool num = settings.DefaultPassphraseIncludeNumber;
        var leetMode = settings.DefaultPassphraseLeetMode;
        var leetIntensity = settings.DefaultPassphraseLeetIntensity;

        for (int i = 1; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (int.TryParse(t, out int val) && val >= 2 && val <= 12)
            {
                wordCount = val;
            }
            else if ((t == "count" || t == "c") && i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int cVal))
            {
                candidateCount = Math.Clamp(cVal, 1, 25);
            }
            else if (t is "hyphen" or "-") sep = PassphraseSeparatorOption.Hyphen;
            else if (t is "underscore" or "_") sep = PassphraseSeparatorOption.Underscore;
            else if (t is "space" or "sp") sep = PassphraseSeparatorOption.Space;
            else if (t is "dot" or ".") sep = PassphraseSeparatorOption.Dot;
            else if (t is "title" or "cap" or "pascal") cap = true;
            else if (t is "nocap" or "lower") cap = false;
            else if (t is "num" or "digit" or "number") num = true;
            else if (t is "nonum" or "nodigit") num = false;
            else if (t is "leet" or "1337") leetMode = PassphraseLeetMode.Visual;
            else if (t is "phonetic" or "b4") leetMode = PassphraseLeetMode.Phonetic;
            else if (t is "combined" or "combo") leetMode = PassphraseLeetMode.Combined;
            else if (t is "none" or "noleet") leetMode = PassphraseLeetMode.None;
            else if (t is "light" or "subtle") leetIntensity = PassphraseLeetIntensity.Light;
            else if (t is "moderate" or "mod") leetIntensity = PassphraseLeetIntensity.Moderate;
            else if (t is "max" or "aggressive" or "heavy" or "agg") leetIntensity = PassphraseLeetIntensity.Aggressive;
        }

        var config = new PassphraseConfig(wordCount, sep, cap, num, leetMode, leetIntensity);
        var candidates = GeneratePassphraseBatch(config, candidateCount);
        if (candidates.Count == 0) return null;

        string primary = candidates[0];
        var alternatives = new List<AlternativeMeasurement>();

        for (int i = 1; i < candidates.Count; i++)
        {
            alternatives.Add(new AlternativeMeasurement(
                Label: $"Candidate #{i + 1}",
                FormattedValue: candidates[i],
                Description: $"{wordCount} words • Separator: '{GetSeparatorChar(sep)}' • Leet: {leetMode}",
                Category: "Candidates",
                IsCommon: true));
        }

        double entropy = wordCount * Math.Log2(DicewareWordList.Words.Length);
        string sepDisplay = sep == PassphraseSeparatorOption.Space ? "space" : $"'{GetSeparatorChar(sep)}'";
        string desc = $"{wordCount} Words • Sep: {sepDisplay} • Leet: {leetMode} ({leetIntensity}) • Entropy: ~{entropy:F0} bits • Default: {settings.DefaultPassphraseWordCount} words";

        return new CalculatorResult(input, primary, desc, alternatives);
    }

    public static List<string> GeneratePassphraseBatch(PassphraseConfig config, int count)
    {
        var results = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < count && seen.Count < count; i++)
        {
            string phrase = GenerateSinglePassphrase(config);
            if (seen.Add(phrase))
            {
                results.Add(phrase);
            }
        }

        while (results.Count < count)
        {
            results.Add(GenerateSinglePassphrase(config));
        }

        return results;
    }

    public static string GenerateSinglePassphrase(PassphraseConfig config)
    {
        var wordPool = DicewareWordList.Words;
        var chosenWords = new List<string>(config.WordCount);

        for (int i = 0; i < config.WordCount; i++)
        {
            string word = wordPool[RandomNumberGenerator.GetInt32(wordPool.Length)];
            if (config.Capitalize)
            {
                word = char.ToUpperInvariant(word[0]) + word[1..];
            }
            if (config.LeetMode != PassphraseLeetMode.None)
            {
                word = ApplyLeetspeak(word, config.LeetMode, config.LeetIntensity);
            }
            chosenWords.Add(word);
        }

        string separator = GetSeparatorChar(config.Separator);
        string result = string.Join(separator, chosenWords);

        if (config.IncludeNumber)
        {
            int digit = RandomNumberGenerator.GetInt32(10, 99);
            result += digit.ToString();
        }

        return result;
    }

    public static string ApplyLeetspeak(string word, PassphraseLeetMode mode, PassphraseLeetIntensity intensity)
    {
        double prob = intensity switch
        {
            PassphraseLeetIntensity.Light => 0.25,
            PassphraseLeetIntensity.Moderate => 0.50,
            PassphraseLeetIntensity.Aggressive => 1.00,
            _ => 0.30
        };

        string current = word;

        // 1. Phonetic replacements
        if (mode is PassphraseLeetMode.Phonetic or PassphraseLeetMode.Combined)
        {
            var phoneticRules = new (string Target, string Sub)[]
            {
                ("before", "b4"),
                ("fore", "4"),
                ("for", "4"),
                ("today", "2day"),
                ("too", "2"),
                ("to", "2"),
                ("great", "gr8"),
                ("mate", "m8"),
                ("late", "l8"),
                ("hate", "h8"),
                ("rate", "r8"),
                ("date", "d8"),
                ("gate", "g8"),
                ("fate", "f8"),
                ("ate", "8"),
                ("won", "1"),
                ("one", "1")
            };

            foreach (var (target, sub) in phoneticRules)
            {
                if (current.Contains(target, StringComparison.OrdinalIgnoreCase))
                {
                    if (intensity == PassphraseLeetIntensity.Aggressive || RandomNumberGenerator.GetInt32(100) < (prob * 100))
                    {
                        current = Regex.Replace(current, Regex.Escape(target), sub, RegexOptions.IgnoreCase);
                    }
                }
            }
        }

        // 2. Visual Leet replacements
        if (mode is PassphraseLeetMode.Visual or PassphraseLeetMode.Combined)
        {
            var visualMap = new Dictionary<char, char>
            {
                ['a'] = '4', ['A'] = '4',
                ['e'] = '3', ['E'] = '3',
                ['i'] = '1', ['I'] = '1',
                ['o'] = '0', ['O'] = '0',
                ['s'] = '5', ['S'] = '5',
                ['t'] = '7', ['T'] = '7',
                ['b'] = '8', ['B'] = '8'
            };

            var sb = new StringBuilder(current.Length);
            foreach (char c in current)
            {
                if (visualMap.TryGetValue(c, out char subChar))
                {
                    if (intensity == PassphraseLeetIntensity.Aggressive || RandomNumberGenerator.GetInt32(100) < (prob * 100))
                    {
                        sb.Append(subChar);
                        continue;
                    }
                }
                sb.Append(c);
            }
            current = sb.ToString();
        }

        return current;
    }

    private static string GetSeparatorChar(PassphraseSeparatorOption sep) => sep switch
    {
        PassphraseSeparatorOption.Hyphen => "-",
        PassphraseSeparatorOption.Underscore => "_",
        PassphraseSeparatorOption.Space => " ",
        PassphraseSeparatorOption.Dot => ".",
        _ => "-"
    };

    #endregion

    #region Hashes & Checksums

    private static CalculatorResult? TryEvaluateHash(string input)
    {
        string[] hashPrefixes = ["sha256", "sha512", "sha1", "md5", "crc32", "hash"];
        string lower = input.ToLowerInvariant();

        string? matchedPrefix = hashPrefixes.FirstOrDefault(p => lower.StartsWith(p + " "));
        if (matchedPrefix == null) return null;

        string payload = input[(matchedPrefix.Length + 1)..].Trim();
        if (string.IsNullOrEmpty(payload)) return null;

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string sha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
        string sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        string md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        string crc32 = ComputeCrc32(bytes).ToString("x8");

        string primary = matchedPrefix switch
        {
            "sha512" => sha512,
            "sha1" => sha1,
            "md5" => md5,
            "crc32" => crc32,
            _ => sha256
        };

        var alternatives = new List<AlternativeMeasurement>
        {
            new("SHA-256", sha256, "256-bit secure cryptographic hash", "Hashes", true),
            new("MD5", md5, "128-bit checksum digest", "Hashes", true),
            new("SHA-512", sha512, "512-bit high-security cryptographic hash", "Hashes", true),
            new("SHA-1", sha1, "160-bit legacy digest", "Hashes", true),
            new("CRC32", crc32, "32-bit cyclic redundancy check", "Hashes", true)
        };

        return new CalculatorResult(input, primary, $"{matchedPrefix.ToUpperInvariant()} hash of '{payload}'", alternatives);
    }

    private static uint ComputeCrc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                uint mask = (uint)-(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320 & mask);
            }
        }
        return ~crc;
    }

    #endregion

    #region Encodings & Transforms

    private static CalculatorResult? TryEvaluateEncoding(string input)
    {
        var lower = input.ToLowerInvariant();

        // 1. Base64 encode
        if (lower.StartsWith("base64 ") || lower.StartsWith("b64 "))
        {
            int offset = lower.StartsWith("base64 ") ? 7 : 4;
            string payload = input[offset..].Trim();
            if (payload.StartsWith("decode ", StringComparison.OrdinalIgnoreCase))
            {
                string b64 = payload[7..].Trim();
                try
                {
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    return new CalculatorResult(input, decoded, "Decoded Base64 string");
                }
                catch
                {
                    return null;
                }
            }

            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            return new CalculatorResult(input, encoded, $"Base64 encoded string ({encoded.Length} chars)");
        }

        // 2. Base64 decode
        if (lower.StartsWith("b64d ") || lower.StartsWith("base64d "))
        {
            int offset = lower.StartsWith("base64d ") ? 8 : 5;
            string b64 = input[offset..].Trim();
            try
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                return new CalculatorResult(input, decoded, "Decoded Base64 string");
            }
            catch
            {
                return null;
            }
        }

        // 3. URL Encode / Decode
        if (lower.StartsWith("urlencode "))
        {
            string payload = input[10..].Trim();
            string encoded = Uri.EscapeDataString(payload);
            return new CalculatorResult(input, encoded, "URL encoded string");
        }
        if (lower.StartsWith("urldecode "))
        {
            string payload = input[10..].Trim();
            string decoded = Uri.UnescapeDataString(payload);
            return new CalculatorResult(input, decoded, "URL decoded string");
        }

        // 4. Slug / Slugify
        if (lower.StartsWith("slug ") || lower.StartsWith("slugify "))
        {
            int offset = lower.StartsWith("slugify ") ? 8 : 5;
            string payload = input[offset..].Trim();
            string slug = GenerateSlug(payload);
            return new CalculatorResult(input, slug, "URL & SEO safe slug");
        }

        return null;
    }

    private static string GenerateSlug(string text)
    {
        string normalized = text.ToLowerInvariant().Trim();
        normalized = Regex.Replace(normalized, @"[^a-z0-9\s-]", "");
        normalized = Regex.Replace(normalized, @"\s+", "-").Trim('-');
        return string.IsNullOrEmpty(normalized) ? "slug" : normalized;
    }

    #endregion

    #region Random Numbers & Dice Rolls

    private static CalculatorResult? TryEvaluateRandomOrDice(string input)
    {
        var lower = input.ToLowerInvariant();

        // 1. Coin Flip
        if (lower is "coin" or "flip" or "coinflip" or "headsortails")
        {
            string result = RandomNumberGenerator.GetInt32(2) == 0 ? "Heads" : "Tails";
            return new CalculatorResult(input, result, "50/50 Coin Flip");
        }

        // 2. Dice roll: roll 2d6, 3d20, d6, 3d6+2, 1d20-4
        var diceMatch = Regex.Match(lower, @"^(?:roll\s*)?([0-9]{0,3})d([0-9]{1,4})(?:\s*([+-])\s*([0-9]{1,4}))?$");
        if (diceMatch.Success)
        {
            string countStr = diceMatch.Groups[1].Value;
            int count = string.IsNullOrEmpty(countStr) ? 1 : int.Parse(countStr);
            if (count < 1) count = 1;
            if (count > 100) count = 100;

            int sides = int.Parse(diceMatch.Groups[2].Value);
            if (sides < 2) sides = 2;
            if (sides > 1000) sides = 1000;

            int modifier = 0;
            if (diceMatch.Groups[3].Success && diceMatch.Groups[4].Success)
            {
                int modVal = int.Parse(diceMatch.Groups[4].Value);
                modifier = diceMatch.Groups[3].Value == "-" ? -modVal : modVal;
            }

            var rolls = new List<int>(count);
            int baseSum = 0;
            for (int i = 0; i < count; i++)
            {
                int r = RandomNumberGenerator.GetInt32(1, sides + 1);
                rolls.Add(r);
                baseSum += r;
            }

            int totalSum = baseSum + modifier;
            string rollsStr = string.Join(", ", rolls);
            string modSuffix = modifier != 0 ? (modifier > 0 ? $" + {modifier}" : $" - {Math.Abs(modifier)}") : string.Empty;
            string desc = count == 1 && modifier == 0
                ? $"Roll 1d{sides}"
                : modifier != 0
                    ? $"Roll {count}d{sides}{diceMatch.Groups[3].Value}{diceMatch.Groups[4].Value}: [{rollsStr}]{modSuffix} = {totalSum}"
                    : $"Roll {count}d{sides}: [{rollsStr}]";

            // Generate batch of alternative candidate rolls
            var alternatives = new List<AlternativeMeasurement>(10);
            for (int a = 1; a <= 10; a++)
            {
                int altSum = 0;
                var altRolls = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    int r = RandomNumberGenerator.GetInt32(1, sides + 1);
                    altRolls.Add(r);
                    altSum += r;
                }
                int altTotal = altSum + modifier;
                string altDesc = count == 1 && modifier == 0
                    ? $"Roll #{a}"
                    : $"[{string.Join(", ", altRolls)}]{modSuffix}";
                alternatives.Add(new AlternativeMeasurement(
                    Label: $"Roll #{a}",
                    FormattedValue: altTotal.ToString(),
                    Description: altDesc,
                    Category: "Candidates",
                    IsCommon: true,
                    ConversationalSentence: $"{altTotal} ({altDesc})"));
            }

            return new CalculatorResult(input, totalSum.ToString(), desc, alternatives);
        }

        // 3. Random Number: rand, rand 1-100, rand 10 500, rand 1..1000
        var randMatch = Regex.Match(lower, @"^(?:rand|random)(?:\s+([0-9]+)(?:[\s\-\.\,]+([0-9]+))?)?$");
        if (randMatch.Success)
        {
            int min = 1;
            int max = 100;

            if (randMatch.Groups[1].Success && randMatch.Groups[2].Success)
            {
                min = int.Parse(randMatch.Groups[1].Value);
                max = int.Parse(randMatch.Groups[2].Value);
                if (min > max) (min, max) = (max, min);
            }
            else if (randMatch.Groups[1].Success)
            {
                max = int.Parse(randMatch.Groups[1].Value);
                min = 1;
            }

            int val = RandomNumberGenerator.GetInt32(min, max + 1);
            return new CalculatorResult(input, val.ToString(), $"Random integer between {min} and {max}");
        }

        return null;
    }

    #endregion

    #region Mock Data & Lorem Ipsum

    private static CalculatorResult? TryEvaluateMockData(string input)
    {
        var lower = input.ToLowerInvariant();

        // 1. Fake Email
        if (lower is "fake email" or "random email" or "mock email" or "test email")
        {
            string first = FirstNames[RandomNumberGenerator.GetInt32(FirstNames.Length)].ToLowerInvariant();
            string last = LastNames[RandomNumberGenerator.GetInt32(LastNames.Length)].ToLowerInvariant();
            int num = RandomNumberGenerator.GetInt32(10, 99);
            string domain = EmailDomains[RandomNumberGenerator.GetInt32(EmailDomains.Length)];
            string email = $"{first}.{last}{num}@{domain}";
            return new CalculatorResult(input, email, "Developer Mock Email Address");
        }

        // 2. Fake Name
        if (lower is "fake name" or "random name" or "mock name" or "test name")
        {
            string first = FirstNames[RandomNumberGenerator.GetInt32(FirstNames.Length)];
            string last = LastNames[RandomNumberGenerator.GetInt32(LastNames.Length)];
            string name = $"{first} {last}";
            return new CalculatorResult(input, name, "Developer Mock Full Name");
        }

        // 3. Fake IPv4
        if (lower is "fake ip" or "fake ipv4" or "random ip" or "mock ip")
        {
            int o1 = RandomNumberGenerator.GetInt32(1, 255);
            int o2 = RandomNumberGenerator.GetInt32(0, 255);
            int o3 = RandomNumberGenerator.GetInt32(0, 255);
            int o4 = RandomNumberGenerator.GetInt32(1, 255);
            string ip = $"{o1}.{o2}.{o3}.{o4}";
            return new CalculatorResult(input, ip, "Developer Mock IPv4 Address");
        }

        // 4. Fake MAC
        if (lower is "fake mac" or "random mac" or "mock mac")
        {
            byte[] bytes = new byte[6];
            RandomNumberGenerator.Fill(bytes);
            bytes[0] = (byte)(bytes[0] & 0xFE | 0x02); // Locally administered unicast
            string mac = string.Join(":", bytes.Select(b => b.ToString("X2")));
            return new CalculatorResult(input, mac, "Developer Mock MAC Address");
        }

        // 5. Lorem Ipsum
        if (lower.StartsWith("lorem"))
        {
            var match = Regex.Match(lower, @"^lorem(?:\s+([0-9]+)\s*(words?|w|sentences?|s|paragraphs?|para|p)?)?$");
            if (match.Success)
            {
                int count = 1;
                string unit = "paragraphs";

                if (match.Groups[1].Success)
                {
                    count = int.Parse(match.Groups[1].Value);
                }

                if (match.Groups[2].Success)
                {
                    string u = match.Groups[2].Value;
                    if (u.StartsWith("w")) unit = "words";
                    else if (u.StartsWith("s")) unit = "sentences";
                    else if (u.StartsWith("p")) unit = "paragraphs";
                }
                else if (count > 1 && !match.Groups[2].Success)
                {
                    unit = "paragraphs";
                }

                count = Math.Clamp(count, 1, 100);

                string loremText = GenerateLorem(count, unit);
                string desc = unit switch
                {
                    "words" => $"Lorem Ipsum ({count} words)",
                    "sentences" => $"Lorem Ipsum ({count} sentences)",
                    _ => $"Lorem Ipsum ({count} {(count == 1 ? "paragraph" : "paragraphs")})"
                };

                return new CalculatorResult(input, loremText, desc);
            }
        }

        return null;
    }

    private static string GenerateLorem(int count, string unit)
    {
        if (unit == "words")
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(LoremWords[RandomNumberGenerator.GetInt32(LoremWords.Length)]);
            }
            return sb.ToString();
        }

        if (unit == "sentences")
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(GenerateSentence());
            }
            return sb.ToString();
        }

        // Paragraphs
        var paras = new List<string>(count);
        for (int p = 0; p < count; p++)
        {
            int sentenceCount = RandomNumberGenerator.GetInt32(3, 6);
            var sb = new StringBuilder();
            for (int s = 0; s < sentenceCount; s++)
            {
                if (s > 0) sb.Append(' ');
                sb.Append(GenerateSentence());
            }
            paras.Add(sb.ToString());
        }

        return string.Join("\n\n", paras);
    }

    private static string GenerateSentence()
    {
        int length = RandomNumberGenerator.GetInt32(8, 16);
        var sb = new StringBuilder();

        for (int i = 0; i < length; i++)
        {
            string word = LoremWords[RandomNumberGenerator.GetInt32(LoremWords.Length)];
            if (i == 0)
            {
                word = char.ToUpperInvariant(word[0]) + word[1..];
            }
            sb.Append(word);
            if (i < length - 1)
            {
                // Occasional comma
                if (i > 2 && i < length - 2 && RandomNumberGenerator.GetInt32(5) == 0)
                {
                    sb.Append(',');
                }
                sb.Append(' ');
            }
        }

        sb.Append('.');
        return sb.ToString();
    }

    #endregion
}
