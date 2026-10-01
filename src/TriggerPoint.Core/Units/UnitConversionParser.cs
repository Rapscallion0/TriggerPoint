using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TriggerPoint.Core.Units;

public record ParsedConversionQuery(
    double Value,
    UnitDefinition FromUnit,
    UnitDefinition ToUnit,
    string OriginalInput,
    bool ForceFraction = false,
    bool ForceDecimal = false,
    bool IsIncompatible = false);

public static class UnitConversionParser
{
    private static readonly Regex ConversationalRegex = new(
        @"^\s*(?:how\s+many\s+)?([a-z0-9°µ²³\/'""\-_\s]+?)\s+(?:in|are\s+in)\s+([0-9\/\.\s+\-]+?)\s*([a-z°µ²³'""\-_]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string input, out ParsedConversionQuery query)
    {
        query = null!;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string trimmed = input.Trim();

        // Strip "@calc" or "@calc " prefix
        if (trimmed.StartsWith("@calc", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[5..].TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed)) return false;
        }

        // Strip leading '='
        if (trimmed.StartsWith('='))
        {
            trimmed = trimmed[1..].Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return false;
        }

        // Strip leading "convert "
        if (trimmed.StartsWith("convert ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[8..].Trim();
        }

        // Check for explicit format modifiers at the end
        bool forceFraction = false;
        bool forceDecimal = false;

        if (trimmed.EndsWith(" as fraction", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" in fraction", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" as a fraction", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(" frac", StringComparison.OrdinalIgnoreCase))
        {
            forceFraction = true;
            int idx = trimmed.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = trimmed.LastIndexOf(" in ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = trimmed.LastIndexOf(" frac", StringComparison.OrdinalIgnoreCase);
            trimmed = trimmed[..idx].Trim();
        }
        else if (trimmed.EndsWith(" as decimal", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.EndsWith(" in decimal", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.EndsWith(" dec", StringComparison.OrdinalIgnoreCase))
        {
            forceDecimal = true;
            int idx = trimmed.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = trimmed.LastIndexOf(" in ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = trimmed.LastIndexOf(" dec", StringComparison.OrdinalIgnoreCase);
            trimmed = trimmed[..idx].Trim();
        }

        ParsedConversionQuery? bestIncompatible = null;

        // 1. Check conversational: "how many mm in 10 inches"
        if (trimmed.StartsWith("how many ", StringComparison.OrdinalIgnoreCase))
        {
            var m = ConversationalRegex.Match(trimmed);
            if (m.Success)
            {
                string targetToken = m.Groups[1].Value.Trim();
                string valToken = m.Groups[2].Value.Trim();
                string fromToken = m.Groups[3].Value.Trim();

                if (TryParseValue(valToken, out double val))
                {
                    var fromCandidates = UnitRegistry.FindCandidateUnits(fromToken);
                    var toCandidates = UnitRegistry.FindCandidateUnits(targetToken);

                    foreach (var fromCandidate in fromCandidates)
                    {
                        foreach (var toCandidate in toCandidates)
                        {
                            if (fromCandidate.Category == toCandidate.Category)
                            {
                                query = new ParsedConversionQuery(val, fromCandidate, toCandidate, input, forceFraction, forceDecimal);
                                return true;
                            }
                            bestIncompatible ??= new ParsedConversionQuery(val, fromCandidate, toCandidate, input, forceFraction, forceDecimal, IsIncompatible: true);
                        }
                    }
                }
            }
        }

        // 2. Standard syntax: <val_and_from> (to|into|as|->|=|in) <to_unit>
        // Note: 'in' can be both the preposition and the unit for inches.
        string[] separators = { " to ", " into ", " as ", " -> ", " = ", " in " };

        foreach (var sep in separators)
        {
            int sepIdx = -1;
            while (true)
            {
                sepIdx = trimmed.IndexOf(sep, sepIdx + 1, StringComparison.OrdinalIgnoreCase);
                if (sepIdx <= 0) break;

                string left = trimmed[..sepIdx].Trim();
                string right = trimmed[(sepIdx + sep.Length)..].Trim();

                if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) continue;

                if (!TryParseValueAndUnit(left, out double val, out string fromToken)) continue;

                var fromCandidates = UnitRegistry.FindCandidateUnits(fromToken);
                var toCandidates = UnitRegistry.FindCandidateUnits(right);

                foreach (var fromCandidate in fromCandidates)
                {
                    foreach (var toCandidate in toCandidates)
                    {
                        if (fromCandidate.Category == toCandidate.Category)
                        {
                            query = new ParsedConversionQuery(val, fromCandidate, toCandidate, input, forceFraction, forceDecimal);
                            return true;
                        }
                        bestIncompatible ??= new ParsedConversionQuery(val, fromCandidate, toCandidate, input, forceFraction, forceDecimal, IsIncompatible: true);
                    }
                }
            }
        }

        if (bestIncompatible != null)
        {
            query = bestIncompatible;
            return true;
        }

        return false;
    }

    private static bool TryParseValueAndUnit(string text, out double value, out string unitToken)
    {
        value = 1.0;
        unitToken = string.Empty;
        text = text.Trim();

        if (string.IsNullOrEmpty(text)) return false;

        // Special case: symbols attached at end, e.g. 10" or 6' or 25°C
        if (text.EndsWith('"'))
        {
            string numPart = text[..^1].Trim();
            if (TryParseValue(numPart, out value))
            {
                unitToken = "in";
                return true;
            }
        }
        if (text.EndsWith('\''))
        {
            string numPart = text[..^1].Trim();
            if (TryParseValue(numPart, out value))
            {
                unitToken = "ft";
                return true;
            }
        }

        // Check if there is space between number and unit
        int lastSpace = text.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            string potentialNum = text[..lastSpace].Trim();
            string potentialUnit = text[(lastSpace + 1)..].Trim();

            if (TryParseValue(potentialNum, out value))
            {
                unitToken = potentialUnit;
                return true;
            }
        }

        // Suffix attached directly without space: e.g. 10in, 100km, -40c, 32f, 100c
        int firstLetterIdx = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLetter(c) || c == '°' || c == 'µ')
            {
                firstLetterIdx = i;
                break;
            }
        }

        if (firstLetterIdx > 0)
        {
            string potentialNum = text[..firstLetterIdx].Trim();
            string potentialUnit = text[firstLetterIdx..].Trim();

            if (TryParseValue(potentialNum, out value))
            {
                unitToken = potentialUnit;
                return true;
            }
        }

        // Implicit value: user typed "inches" or "km" without any number
        if (UnitRegistry.TryFindUnit(text, out _))
        {
            value = 1.0;
            unitToken = text;
            return true;
        }

        return false;
    }

    public static bool TryParseValue(string text, out double value)
    {
        value = 0;
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        // Simple decimal parse
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        // Mixed fraction: "1 1/2" or "-2 3/4"
        int spaceIdx = text.IndexOf(' ');
        if (spaceIdx > 0)
        {
            string wholePart = text[..spaceIdx].Trim();
            string fracPart = text[(spaceIdx + 1)..].Trim();

            if (long.TryParse(wholePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole) &&
                TryParseSimpleFraction(fracPart, out double fracVal))
            {
                value = whole >= 0 ? whole + fracVal : whole - fracVal;
                return true;
            }
        }

        // Simple fraction: "1/2" or "-3/4"
        if (TryParseSimpleFraction(text, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseSimpleFraction(string text, out double value)
    {
        value = 0;
        int slashIdx = text.IndexOf('/');
        if (slashIdx > 0 && slashIdx < text.Length - 1)
        {
            string numStr = text[..slashIdx].Trim();
            string denStr = text[(slashIdx + 1)..].Trim();

            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) &&
                double.TryParse(denStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double den) &&
                den != 0)
            {
                value = num / den;
                return true;
            }
        }
        return false;
    }
}
