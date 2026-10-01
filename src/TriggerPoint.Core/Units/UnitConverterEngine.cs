using System;
using System.Collections.Generic;
using System.Globalization;
using TriggerPoint.Core.Services;

namespace TriggerPoint.Core.Units;

public static class UnitConverterEngine
{
    public static CalculatorResult? TryConvert(string input)
    {
        if (!UnitConversionParser.TryParse(input, out var query))
        {
            return null;
        }

        if (query.IsIncompatible)
        {
            return HandleIncompatibleConversion(query);
        }

        double baseValue = query.FromUnit.ToBase(query.Value);
        double targetValue = query.ToUnit.FromBase(baseValue);

        string formattedDecimal = FormatDouble(targetValue);
        string primaryDisplay = FractionalFormatter.FormatFractionForDisplay(
            targetValue,
            query.ToUnit,
            query.ForceFraction,
            query.ForceDecimal,
            formattedDecimal,
            out bool usedFraction);

        string fromName = Math.Abs(query.Value - 1.0) < 0.0001 ? query.FromUnit.SingularName : query.FromUnit.PluralName;
        string toName = query.ToUnit.PluralName;
        string decimalWithUnit = FractionalFormatter.FormatValueWithUnit(formattedDecimal, query.ToUnit);

        string desc = usedFraction
            ? $"{query.FromUnit.Category} conversion: {FormatDouble(query.Value)} {fromName} = {primaryDisplay} ({decimalWithUnit})"
            : $"{query.FromUnit.Category} conversion: {FormatDouble(query.Value)} {fromName} in {toName}";

        var alternatives = GenerateAlternatives(baseValue, query, usedFraction, decimalWithUnit);

        string fromDisplay = FractionalFormatter.FormatValueWithUnit(FormatDouble(query.Value), query.FromUnit);
        string expression = $"{fromDisplay} to {query.ToUnit.Symbol}";
        return new CalculatorResult(expression, primaryDisplay, desc, alternatives);
    }

    private static CalculatorResult HandleIncompatibleConversion(ParsedConversionQuery query)
    {
        string fromDisplay = FractionalFormatter.FormatValueWithUnit(FormatDouble(query.Value), query.FromUnit);
        string expression = $"{fromDisplay} ➔ {query.ToUnit.PluralName}";

        string headline = $"Cannot convert {query.FromUnit.Category} to {query.ToUnit.Category}";
        string wittyMessage = IncompatibleUnitMessageRegistry.GetRandomMessage(
            query.FromUnit.Category,
            query.ToUnit.Category);

        var alternatives = new List<AlternativeMeasurement>();

        // Check if there is a dimensional bridge
        if (IncompatibleUnitMessageRegistry.TryGetDimensionalBridge(query, out var bridge) && bridge != null)
        {
            alternatives.Add(bridge);
        }

        // Add standard peer alternatives for the source unit so the user still gets useful conversion data
        double baseValue = query.FromUnit.ToBase(query.Value);
        var peers = UnitRegistry.GetUnitsInCategory(query.FromUnit.Category);
        foreach (var peer in peers)
        {
            if (peer.Id == query.FromUnit.Id) continue;
            double peerValue = peer.FromBase(baseValue);
            string peerDec = FormatDouble(peerValue);
            string peerDisplay = FractionalFormatter.FormatFractionForDisplay(
                peerValue,
                peer,
                forceFraction: false,
                forceDecimal: false,
                peerDec,
                out _);

            alternatives.Add(new AlternativeMeasurement(
                Label: peer.PluralName,
                FormattedValue: peerDisplay,
                Description: $"{peer.Category} in {peer.PluralName}",
                Category: "Common",
                IsCommon: peer.IsCommonAlternative,
                ConceptTitle: $"{peer.Symbol} ({peer.SingularName})",
                ConversationalSentence: $"{FormatDouble(query.Value)} {query.FromUnit.PluralName} is approximately {peerDisplay}."));
        }

        // Add fun alternatives for source unit
        var funAlternatives = FunMeasurementRegistry.GetFunAlternatives(
            query.FromUnit.Category,
            baseValue,
            query.Value,
            query.FromUnit.PluralName);
        alternatives.AddRange(funAlternatives);

        string description = bridge != null
            ? $"{wittyMessage} (Tip: {bridge.Description})"
            : wittyMessage;

        return new CalculatorResult(expression, headline, description, alternatives);
    }

    private static IReadOnlyList<AlternativeMeasurement> GenerateAlternatives(
        double baseValue,
        ParsedConversionQuery query,
        bool usedFractionForPrimary,
        string primaryDecimalWithUnit)
    {
        var list = new List<AlternativeMeasurement>();

        // If the primary output was formatted as a fraction (e.g. 1/2 in), offer the exact decimal equivalent
        if (usedFractionForPrimary)
        {
            list.Add(new AlternativeMeasurement(
                Label: $"{query.ToUnit.SingularName} (decimal)",
                FormattedValue: primaryDecimalWithUnit,
                Description: $"Decimal representation: {primaryDecimalWithUnit}",
                Category: "Common",
                IsCommon: true,
                ConceptTitle: $"{query.ToUnit.Symbol} (decimal)",
                ConversationalSentence: $"{FormatDouble(query.Value)} {query.FromUnit.PluralName} in decimal is {primaryDecimalWithUnit}."));
        }

        var peers = UnitRegistry.GetUnitsInCategory(query.FromUnit.Category);

        foreach (var peer in peers)
        {
            if (peer.Id == query.ToUnit.Id) continue;

            double peerValue = peer.FromBase(baseValue);
            string peerDec = FormatDouble(peerValue);
            string peerDisplay = FractionalFormatter.FormatFractionForDisplay(
                peerValue,
                peer,
                forceFraction: false,
                forceDecimal: false,
                peerDec,
                out _);

            list.Add(new AlternativeMeasurement(
                Label: peer.PluralName,
                FormattedValue: peerDisplay,
                Description: $"{peer.Category} in {peer.PluralName}",
                Category: "Common",
                IsCommon: peer.IsCommonAlternative,
                ConceptTitle: $"{peer.Symbol} ({peer.SingularName})",
                ConversationalSentence: $"{FormatDouble(query.Value)} {query.FromUnit.PluralName} is approximately {peerDisplay}."));
        }

        // Dedicated architectural/machining fractional accuracy alternatives for Length
        if (query.FromUnit.Category == UnitCategory.Length)
        {
            if (UnitRegistry.TryFindUnit("in", out var inchUnit))
            {
                double inchVal = inchUnit.FromBase(baseValue);
                if (FractionalFormatter.TryToFraction(inchVal, 16, isCulinary: false, out var f16))
                {
                    string f16Formatted = FractionalFormatter.FormatValueWithUnit(f16.Formatted, inchUnit);
                    list.Add(new AlternativeMeasurement(
                        Label: "Nearest 1/16\"",
                        FormattedValue: f16Formatted,
                        Description: "Imperial construction fraction rounded to nearest 1/16th",
                        Category: "Fractions",
                        IsCommon: false,
                        ConceptTitle: "📐 1/16\" Precision",
                        ConversationalSentence: $"At 1/16th inch precision, it is {f16Formatted}."));
                }

                if (FractionalFormatter.TryToFraction(inchVal, 32, isCulinary: false, out var f32))
                {
                    string f32Formatted = FractionalFormatter.FormatValueWithUnit(f32.Formatted, inchUnit);
                    list.Add(new AlternativeMeasurement(
                        Label: "Nearest 1/32\"",
                        FormattedValue: f32Formatted,
                        Description: "Woodworking / carpentry precision rounded to nearest 1/32nd",
                        Category: "Fractions",
                        IsCommon: false,
                        ConceptTitle: "🪚 1/32\" Precision",
                        ConversationalSentence: $"At 1/32nd inch precision, it is {f32Formatted}."));
                }

                if (FractionalFormatter.TryToFraction(inchVal, 64, isCulinary: false, out var f64))
                {
                    string f64Formatted = FractionalFormatter.FormatValueWithUnit(f64.Formatted, inchUnit);
                    list.Add(new AlternativeMeasurement(
                        Label: "Nearest 1/64\"",
                        FormattedValue: f64Formatted,
                        Description: "Machining & 3D printing precision rounded to nearest 1/64th",
                        Category: "Fractions",
                        IsCommon: false,
                        ConceptTitle: "⚙️ 1/64\" Precision",
                        ConversationalSentence: $"At 1/64th inch precision, it is {f64Formatted}."));
                }
            }
        }

        var funAlternatives = FunMeasurementRegistry.GetFunAlternatives(
            query.FromUnit.Category,
            baseValue,
            query.Value,
            query.FromUnit.PluralName);
        list.AddRange(funAlternatives);

        return list;
    }

    public static string FormatDouble(double val)
    {
        if (double.IsNaN(val) || double.IsInfinity(val)) return "0";

        if (Math.Abs(val % 1) < 0.0000001)
        {
            return val.ToString("0.####", CultureInfo.InvariantCulture);
        }

        if (Math.Abs(val) < 0.0001 && val != 0)
        {
            return val.ToString("0.######", CultureInfo.InvariantCulture);
        }

        return Math.Round(val, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
