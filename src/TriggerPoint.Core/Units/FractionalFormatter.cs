using System;
using System.Globalization;

namespace TriggerPoint.Core.Units;

public record FractionalResult(
    long Whole,
    int Numerator,
    int Denominator,
    bool IsExact,
    double Difference,
    string Formatted)
{
    public string FormattedWithUnit(string unitSymbol)
    {
        return $"{Formatted} {unitSymbol}".Trim();
    }
}

public static class FractionalFormatter
{
    private static readonly int[] ImperialDenominators = { 2, 4, 8, 16, 32, 64 };
    private static readonly int[] CulinaryDenominators = { 2, 3, 4, 8 };

    public static bool TryToFraction(double value, int maxDenominator, bool isCulinary, out FractionalResult result)
    {
        result = null!;
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;

        bool isNegative = value < 0;
        double absVal = Math.Abs(value);
        long whole = (long)Math.Floor(absVal);
        double remainder = absVal - whole;

        // If almost an integer
        if (remainder < 0.0001)
        {
            string formattedWhole = (isNegative ? "-" : "") + whole.ToString(CultureInfo.InvariantCulture);
            result = new FractionalResult(
                Whole: isNegative ? -whole : whole,
                Numerator: 0,
                Denominator: 1,
                IsExact: true,
                Difference: 0,
                Formatted: formattedWhole);
            return true;
        }

        if (remainder > 0.9999)
        {
            whole += 1;
            string formattedWhole = (isNegative ? "-" : "") + whole.ToString(CultureInfo.InvariantCulture);
            result = new FractionalResult(
                Whole: isNegative ? -whole : whole,
                Numerator: 0,
                Denominator: 1,
                IsExact: true,
                Difference: 0,
                Formatted: formattedWhole);
            return true;
        }

        int[] candidateDenominators = isCulinary ? CulinaryDenominators : ImperialDenominators;
        int bestNum = 0;
        int bestDen = 1;
        double bestDiff = double.MaxValue;
        bool foundExact = false;

        foreach (int den in candidateDenominators)
        {
            if (den > maxDenominator) continue;

            int num = (int)Math.Round(remainder * den);
            if (num == 0) continue;
            if (num == den)
            {
                num = 0;
            }

            double approx = (double)num / den;
            double diff = Math.Abs(remainder - approx);

            if (diff < 0.0001)
            {
                bestNum = num;
                bestDen = den;
                bestDiff = diff;
                foundExact = true;
                break;
            }

            if (diff < bestDiff)
            {
                bestNum = num;
                bestDen = den;
                bestDiff = diff;
            }
        }

        if (bestNum == 0 && !foundExact)
        {
            return false;
        }

        // Simplify fraction
        long gcd = Gcd(bestNum, bestDen);
        int finalNum = (int)(bestNum / gcd);
        int finalDen = (int)(bestDen / gcd);

        string fracPart = $"{finalNum}/{finalDen}";
        string sign = isNegative ? "-" : "";
        string formattedStr;
        if (whole == 0)
        {
            formattedStr = $"{sign}{fracPart}";
        }
        else
        {
            formattedStr = $"{sign}{whole} {fracPart}";
        }

        result = new FractionalResult(
            Whole: isNegative ? -whole : whole,
            Numerator: finalNum,
            Denominator: finalDen,
            IsExact: foundExact,
            Difference: bestDiff,
            Formatted: formattedStr);

        return true;
    }

    public static string FormatValueWithUnit(string valStr, UnitDefinition unit)
    {
        if (unit.Category == UnitCategory.Typography && (unit.Id is "px" or "rem" or "pt_typo"))
        {
            return $"{valStr}{unit.Symbol}";
        }
        if (unit.Category == UnitCategory.Temperature && unit.Symbol.StartsWith("°"))
        {
            return $"{valStr}{unit.Symbol}";
        }
        if (unit.Category == UnitCategory.Angle && unit.Symbol == "°")
        {
            return $"{valStr}°";
        }
        if (unit.Category == UnitCategory.Volume && unit.Id == "cup")
        {
            return (valStr == "1" || valStr == "1.0") ? "1 cup" : $"{valStr} cups";
        }
        if (unit.Category == UnitCategory.DigitalStorage && (unit.Id is "kb" or "mb" or "gb" or "tb" or "pb"))
        {
            return $"{valStr} {unit.Symbol.ToUpperInvariant()}";
        }
        return $"{valStr} {unit.Symbol}".Trim();
    }

    public static string FormatFractionForDisplay(
        double value,
        UnitDefinition targetUnit,
        bool forceFraction,
        bool forceDecimal,
        string formattedDecimal,
        out bool usedFraction)
    {
        usedFraction = false;
        string decimalOutput = FormatValueWithUnit(formattedDecimal, targetUnit);

        if (forceDecimal || !targetUnit.SupportsFractions || targetUnit.FractionMaxDenominator <= 0)
        {
            return decimalOutput;
        }

        bool isCulinary = targetUnit.Category == UnitCategory.Volume;
        if (TryToFraction(value, targetUnit.FractionMaxDenominator, isCulinary, out var fracResult))
        {
            if (fracResult.Numerator == 0)
            {
                return decimalOutput;
            }

            string fracOutput = FormatValueWithUnit(fracResult.Formatted, targetUnit);

            if (forceFraction)
            {
                usedFraction = true;
                return fracOutput;
            }

            if (fracResult.IsExact)
            {
                usedFraction = true;
                return fracOutput;
            }
        }

        return decimalOutput;
    }

    public static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            long t = b;
            b = a % b;
            a = t;
        }
        return Math.Abs(a);
    }
}
