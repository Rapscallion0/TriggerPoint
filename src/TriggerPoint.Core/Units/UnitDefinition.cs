using System;
using System.Collections.Generic;

namespace TriggerPoint.Core.Units;

public record UnitDefinition(
    string Id,
    string SingularName,
    string PluralName,
    string Symbol,
    UnitCategory Category,
    double FactorToBase,
    IReadOnlyList<string> Aliases,
    bool IsCommonAlternative = false,
    bool SupportsFractions = false,
    int FractionMaxDenominator = 0,
    Func<double, double>? CustomToBase = null,
    Func<double, double>? CustomFromBase = null)
{
    public double ToBase(double value)
    {
        if (CustomToBase != null) return CustomToBase(value);
        return value * FactorToBase;
    }

    public double FromBase(double baseValue)
    {
        if (CustomFromBase != null) return CustomFromBase(baseValue);
        return baseValue / FactorToBase;
    }
}
