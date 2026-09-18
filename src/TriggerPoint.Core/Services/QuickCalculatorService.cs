using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TriggerPoint.Core.Services;

public record CalculatorResult(string Expression, string FormattedResult, string Description);

public static class QuickCalculatorService
{
    private static readonly Regex UnitConversionRegex = new(
        @"^\s*([0-9]+(?:\.[0-9]+)?)\s*(px|rem|mb|gb|kb)\s+(?:to|in)\s+(px|rem|mb|gb|kb)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HexToRgbRegex = new(
        @"^\s*(?:hex\s+to\s+rgb\s+)?#?([0-9a-f]{3}|[0-9a-f]{6})\s*(?:to\s+rgb)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CalculatorResult? TryEvaluate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();

        // 1. Check unit conversions
        var unitMatch = UnitConversionRegex.Match(trimmed);
        if (unitMatch.Success)
        {
            if (double.TryParse(unitMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                string fromUnit = unitMatch.Groups[2].Value.ToLowerInvariant();
                string toUnit = unitMatch.Groups[3].Value.ToLowerInvariant();

                var converted = ConvertUnits(val, fromUnit, toUnit);
                if (converted != null) return converted;
            }
        }

        // 2. Check Hex to RGB
        if (trimmed.StartsWith("hex ", StringComparison.OrdinalIgnoreCase) || 
            trimmed.EndsWith(" to rgb", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.StartsWith("#") && trimmed.Contains("rgb", StringComparison.OrdinalIgnoreCase)))
        {
            var hexMatch = HexToRgbRegex.Match(trimmed);
            if (hexMatch.Success)
            {
                string hex = hexMatch.Groups[1].Value;
                if (hex.Length == 3)
                {
                    hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
                }
                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexVal))
                {
                    int r = (hexVal >> 16) & 0xFF;
                    int g = (hexVal >> 8) & 0xFF;
                    int b = hexVal & 0xFF;
                    return new CalculatorResult(trimmed, $"rgb({r}, {g}, {b})", "Color conversion (Hex to RGB)");
                }
            }
        }

        // 3. Math evaluation (starts with '=' or contains numbers and math symbols)
        bool explicitMath = trimmed.StartsWith('=');
        string mathExpr = explicitMath ? trimmed[1..].Trim() : trimmed;

        if (string.IsNullOrWhiteSpace(mathExpr)) return null;

        // Ensure expression has math operators or numbers
        if (!explicitMath)
        {
            // Only evaluate implicit math if it contains digits AND at least one operator (+, -, *, /, ^, %)
            bool hasDigit = false;
            bool hasOp = false;
            bool hasLetters = false;

            foreach (char c in mathExpr)
            {
                if (char.IsDigit(c)) hasDigit = true;
                else if (c is '+' or '-' or '*' or '/' or '^' or '%') hasOp = true;
                else if (char.IsLetter(c)) hasLetters = true;
            }

            if (!hasDigit || !hasOp || hasLetters)
            {
                return null;
            }
        }

        try
        {
            // Normalize expression for DataTable Compute
            string sanitized = mathExpr.Replace('^', '*'); // DataTable doesn't support ^ natively; handle basic replacement
            using var dt = new DataTable();
            var result = dt.Compute(mathExpr, null);

            if (result != null && double.TryParse(result.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
            {
                if (double.IsNaN(num) || double.IsInfinity(num)) return null;

                string formatted = num % 1 == 0 
                    ? num.ToString("N0", CultureInfo.InvariantCulture) 
                    : Math.Round(num, 4).ToString("0.####", CultureInfo.InvariantCulture);

                return new CalculatorResult(mathExpr, formatted, "Calculator result (Enter to copy)");
            }
        }
        catch
        {
            // Invalid math syntax — silently ignore
        }

        return null;
    }

    private static CalculatorResult? ConvertUnits(double val, string from, string to)
    {
        // px <-> rem (standard 16px baseline)
        if (from == "px" && to == "rem")
        {
            double res = Math.Round(val / 16.0, 4);
            return new CalculatorResult($"{val}px to rem", $"{res}rem", "Unit conversion (16px base)");
        }
        if (from == "rem" && to == "px")
        {
            double res = Math.Round(val * 16.0, 2);
            return new CalculatorResult($"{val}rem to px", $"{res}px", "Unit conversion (16px base)");
        }

        // Data units
        double bytes = from switch
        {
            "kb" => val * 1024,
            "mb" => val * 1024 * 1024,
            "gb" => val * 1024 * 1024 * 1024,
            _ => 0
        };

        if (bytes <= 0) return null;

        double converted = to switch
        {
            "kb" => bytes / 1024,
            "mb" => bytes / (1024 * 1024),
            "gb" => bytes / (1024 * 1024 * 1024),
            _ => 0
        };

        if (converted <= 0) return null;

        string formatted = Math.Round(converted, 4).ToString("0.####", CultureInfo.InvariantCulture);
        return new CalculatorResult($"{val} {from.ToUpperInvariant()} to {to.ToUpperInvariant()}", $"{formatted} {to.ToUpperInvariant()}", "Digital storage conversion");
    }
}
