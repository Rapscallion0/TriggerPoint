using System;
using System.Collections.Generic;
using System.Linq;

namespace TriggerPoint.Core.Units;

public static class UnitRegistry
{
    private static readonly List<UnitDefinition> AllUnitsList;
    private static readonly Dictionary<string, List<UnitDefinition>> MultiAliasMap;
    private static readonly Dictionary<UnitCategory, List<UnitDefinition>> CategoryMap;

    static UnitRegistry()
    {
        AllUnitsList = new List<UnitDefinition>();
        MultiAliasMap = new Dictionary<string, List<UnitDefinition>>(StringComparer.OrdinalIgnoreCase);
        CategoryMap = new Dictionary<UnitCategory, List<UnitDefinition>>();

        // 1. Length & Distance (Base: Meter 'm')
        Register(new("mm", "millimeter", "millimeters", "mm", UnitCategory.Length, 0.001,
            new[] { "mm", "millimeter", "millimeters", "millimetre", "millimetres" }, IsCommonAlternative: true));
        Register(new("cm", "centimeter", "centimeters", "cm", UnitCategory.Length, 0.01,
            new[] { "cm", "centimeter", "centimeters", "centimetre", "centimetres" }, IsCommonAlternative: true));
        Register(new("dm", "decimeter", "decimeters", "dm", UnitCategory.Length, 0.1,
            new[] { "dm", "decimeter", "decimeters", "decimetre", "decimetres" }));
        Register(new("m", "meter", "meters", "m", UnitCategory.Length, 1.0,
            new[] { "m", "meter", "meters", "metre", "metres" }, IsCommonAlternative: true));
        Register(new("km", "kilometer", "kilometers", "km", UnitCategory.Length, 1000.0,
            new[] { "km", "kilometer", "kilometers", "kilometre", "kilometres" }, IsCommonAlternative: true));
        Register(new("in", "inch", "inches", "in", UnitCategory.Length, 0.0254,
            new[] { "in", "inch", "inches", "\"" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 64));
        Register(new("ft", "foot", "feet", "ft", UnitCategory.Length, 0.3048,
            new[] { "ft", "foot", "feet", "'" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 16));
        Register(new("yd", "yard", "yards", "yd", UnitCategory.Length, 0.9144,
            new[] { "yd", "yard", "yards" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 16));
        Register(new("mi", "mile", "miles", "mi", UnitCategory.Length, 1609.344,
            new[] { "mi", "mile", "miles" }, IsCommonAlternative: true));
        Register(new("nmi", "nautical mile", "nautical miles", "nmi", UnitCategory.Length, 1852.0,
            new[] { "nmi", "nautical mile", "nautical miles", "nmile" }));
        Register(new("um", "micrometer", "micrometers", "µm", UnitCategory.Length, 1e-6,
            new[] { "um", "µm", "micron", "microns", "micrometer", "micrometers", "micrometre" }));
        Register(new("nm", "nanometer", "nanometers", "nm", UnitCategory.Length, 1e-9,
            new[] { "nm", "nanometer", "nanometers", "nanometre" }));
        Register(new("mil", "mil", "mils", "mil", UnitCategory.Length, 0.0000254,
            new[] { "mil", "mils", "thou" }));

        // 2. Area (Base: Square Meter 'm²')
        Register(new("sqmm", "square millimeter", "square millimeters", "mm²", UnitCategory.Area, 1e-6,
            new[] { "sq mm", "sqmm", "mm2", "mm²", "square millimeter", "square millimeters" }));
        Register(new("sqcm", "square centimeter", "square centimeters", "cm²", UnitCategory.Area, 1e-4,
            new[] { "sq cm", "sqcm", "cm2", "cm²", "square centimeter", "square centimeters" }));
        Register(new("sqm", "square meter", "square meters", "m²", UnitCategory.Area, 1.0,
            new[] { "sq m", "sqm", "m2", "m²", "square meter", "square meters" }, IsCommonAlternative: true));
        Register(new("sqkm", "square kilometer", "square kilometers", "km²", UnitCategory.Area, 1e6,
            new[] { "sq km", "sqkm", "km2", "km²", "square kilometer", "square kilometers" }, IsCommonAlternative: true));
        Register(new("sqin", "square inch", "square inches", "in²", UnitCategory.Area, 0.00064516,
            new[] { "sq in", "sqin", "in2", "in²", "square inch", "square inches" }, IsCommonAlternative: true));
        Register(new("sqft", "square foot", "square feet", "ft²", UnitCategory.Area, 0.09290304,
            new[] { "sq ft", "sqft", "ft2", "ft²", "square foot", "square feet" }, IsCommonAlternative: true));
        Register(new("sqyd", "square yard", "square yards", "yd²", UnitCategory.Area, 0.83612736,
            new[] { "sq yd", "sqyd", "yd2", "yd²", "square yard", "square yards" }));
        Register(new("sqmi", "square mile", "square miles", "mi²", UnitCategory.Area, 2589988.110336,
            new[] { "sq mi", "sqmi", "mi2", "mi²", "square mile", "square miles" }));
        Register(new("acre", "acre", "acres", "ac", UnitCategory.Area, 4046.8564224,
            new[] { "acre", "acres", "ac" }, IsCommonAlternative: true));
        Register(new("ha", "hectare", "hectares", "ha", UnitCategory.Area, 10000.0,
            new[] { "ha", "hectare", "hectares" }, IsCommonAlternative: true));

        // 3. Volume & Liquid Capacity (Base: Cubic Meter 'm³')
        Register(new("ml", "milliliter", "milliliters", "ml", UnitCategory.Volume, 1e-6,
            new[] { "ml", "milliliter", "milliliters", "millilitre", "millilitres" }, IsCommonAlternative: true));
        Register(new("l", "liter", "liters", "L", UnitCategory.Volume, 0.001,
            new[] { "l", "liter", "liters", "litre", "litres" }, IsCommonAlternative: true));
        Register(new("cc", "cubic centimeter", "cubic centimeters", "cc", UnitCategory.Volume, 1e-6,
            new[] { "cc", "cm3", "cm³", "cubic centimeter", "cubic centimeters" }));
        Register(new("cum", "cubic meter", "cubic meters", "m³", UnitCategory.Volume, 1.0,
            new[] { "m3", "m³", "cubic meter", "cubic meters", "cu m" }, IsCommonAlternative: true));
        Register(new("floz", "fluid ounce", "fluid ounces", "fl oz", UnitCategory.Volume, 2.95735295625e-5,
            new[] { "fl oz", "floz", "fl-oz", "fluid ounce", "fluid ounces" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 8));
        Register(new("cup", "cup", "cups", "cup", UnitCategory.Volume, 0.0002365882365,
            new[] { "cup", "cups" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 8));
        Register(new("pt", "pint", "pints", "pt", UnitCategory.Volume, 0.000473176473,
            new[] { "pint", "pints" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 8));
        Register(new("qt", "quart", "quarts", "qt", UnitCategory.Volume, 0.000946352946,
            new[] { "qt", "quart", "quarts" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 8));
        Register(new("gal", "gallon", "gallons", "gal", UnitCategory.Volume, 0.003785411784,
            new[] { "gal", "gallon", "gallons" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 8));
        Register(new("tbsp", "tablespoon", "tablespoons", "tbsp", UnitCategory.Volume, 1.478676478125e-5,
            new[] { "tbsp", "tablespoon", "tablespoons", "tbs" }, SupportsFractions: true, FractionMaxDenominator: 8));
        Register(new("tsp", "teaspoon", "teaspoons", "tsp", UnitCategory.Volume, 4.92892159375e-6,
            new[] { "tsp", "teaspoon", "teaspoons" }, SupportsFractions: true, FractionMaxDenominator: 8));
        Register(new("cuin", "cubic inch", "cubic inches", "in³", UnitCategory.Volume, 1.6387064e-5,
            new[] { "in3", "in³", "cu in", "cubic inch", "cubic inches" }));
        Register(new("cuft", "cubic foot", "cubic feet", "ft³", UnitCategory.Volume, 0.028316846592,
            new[] { "ft3", "ft³", "cu ft", "cubic foot", "cubic feet" }));

        // 4. Mass & Weight (Base: Kilogram 'kg')
        Register(new("mcg", "microgram", "micrograms", "µg", UnitCategory.Mass, 1e-9,
            new[] { "mcg", "ug", "µg", "microgram", "micrograms" }));
        Register(new("mg", "milligram", "milligrams", "mg", UnitCategory.Mass, 1e-6,
            new[] { "mg", "milligram", "milligrams" }, IsCommonAlternative: true));
        Register(new("g", "gram", "grams", "g", UnitCategory.Mass, 0.001,
            new[] { "g", "gram", "grams" }, IsCommonAlternative: true));
        Register(new("kg", "kilogram", "kilograms", "kg", UnitCategory.Mass, 1.0,
            new[] { "kg", "kilo", "kilos", "kilogram", "kilograms" }, IsCommonAlternative: true));
        Register(new("oz", "ounce", "ounces", "oz", UnitCategory.Mass, 0.028349523125,
            new[] { "oz", "ounce", "ounces" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 16));
        Register(new("lb", "pound", "pounds", "lbs", UnitCategory.Mass, 0.45359237,
            new[] { "lb", "lbs", "pound", "pounds" }, IsCommonAlternative: true, SupportsFractions: true, FractionMaxDenominator: 16));
        Register(new("st", "stone", "stones", "st", UnitCategory.Mass, 6.35029318,
            new[] { "st", "stone", "stones" }));
        Register(new("t", "metric ton", "metric tons", "t", UnitCategory.Mass, 1000.0,
            new[] { "t", "tonne", "tonnes", "metric ton", "metric tons" }, IsCommonAlternative: true));
        Register(new("ton", "short ton", "short tons", "ton", UnitCategory.Mass, 907.18474,
            new[] { "ton", "tons", "short ton", "short tons" }));
        Register(new("ct", "carat", "carats", "ct", UnitCategory.Mass, 0.0002,
            new[] { "ct", "carat", "carats" }));

        // 5. Temperature (Base: Kelvin 'K' via Affine transformations)
        Register(new("c", "celsius", "celsius", "°C", UnitCategory.Temperature, 1.0,
            new[] { "c", "°c", "celsius", "centigrade", "deg c", "degrees c" },
            IsCommonAlternative: true,
            CustomToBase: c => c + 273.15,
            CustomFromBase: k => k - 273.15));
        Register(new("f", "fahrenheit", "fahrenheit", "°F", UnitCategory.Temperature, 1.0,
            new[] { "f", "°f", "fahrenheit", "deg f", "degrees f" },
            IsCommonAlternative: true,
            CustomToBase: f => (f - 32.0) * 5.0 / 9.0 + 273.15,
            CustomFromBase: k => (k - 273.15) * 9.0 / 5.0 + 32.0));
        Register(new("k", "kelvin", "kelvin", "K", UnitCategory.Temperature, 1.0,
            new[] { "k", "kelvin", "deg k", "degrees k" },
            IsCommonAlternative: true,
            CustomToBase: k => k,
            CustomFromBase: k => k));
        Register(new("r", "rankine", "rankine", "°R", UnitCategory.Temperature, 1.0,
            new[] { "r", "°r", "rankine" },
            CustomToBase: r => r * 5.0 / 9.0,
            CustomFromBase: k => k * 9.0 / 5.0));

        // 6. Speed & Velocity (Base: Meter/second 'm/s')
        Register(new("mps", "meter per second", "meters per second", "m/s", UnitCategory.Speed, 1.0,
            new[] { "m/s", "mps", "meter per second", "meters per second" }, IsCommonAlternative: true));
        Register(new("kmh", "kilometer per hour", "kilometers per hour", "km/h", UnitCategory.Speed, 1.0 / 3.6,
            new[] { "km/h", "kmh", "kph", "kilometer per hour", "kilometers per hour" }, IsCommonAlternative: true));
        Register(new("mph", "mile per hour", "miles per hour", "mph", UnitCategory.Speed, 0.44704,
            new[] { "mph", "mile per hour", "miles per hour" }, IsCommonAlternative: true));
        Register(new("knot", "knot", "knots", "knot", UnitCategory.Speed, 0.5144444444,
            new[] { "knot", "knots", "kt" }, IsCommonAlternative: true));
        Register(new("fps", "foot per second", "feet per second", "ft/s", UnitCategory.Speed, 0.3048,
            new[] { "ft/s", "fps", "foot per second", "feet per second" }));
        Register(new("mach", "mach", "mach", "mach", UnitCategory.Speed, 340.29,
            new[] { "mach" }));

        // 7. Pressure (Base: Pascal 'Pa')
        Register(new("pa", "pascal", "pascals", "Pa", UnitCategory.Pressure, 1.0,
            new[] { "pa", "pascal", "pascals" }));
        Register(new("kpa", "kilopascal", "kilopascals", "kPa", UnitCategory.Pressure, 1000.0,
            new[] { "kpa", "kilopascal", "kilopascals" }, IsCommonAlternative: true));
        Register(new("mpa", "megapascal", "megapascals", "MPa", UnitCategory.Pressure, 1e6,
            new[] { "mpa", "megapascal", "megapascals" }));
        Register(new("bar", "bar", "bars", "bar", UnitCategory.Pressure, 100000.0,
            new[] { "bar", "bars" }, IsCommonAlternative: true));
        Register(new("mbar", "millibar", "millibars", "mbar", UnitCategory.Pressure, 100.0,
            new[] { "mbar", "millibar", "millibars", "hpa" }));
        Register(new("psi", "pound per square inch", "pounds per square inch", "psi", UnitCategory.Pressure, 6894.757293,
            new[] { "psi", "lb/in2", "pounds per square inch" }, IsCommonAlternative: true));
        Register(new("atm", "atmosphere", "atmospheres", "atm", UnitCategory.Pressure, 101325.0,
            new[] { "atm", "atmosphere", "atmospheres" }, IsCommonAlternative: true));
        Register(new("torr", "torr", "torr", "mmHg", UnitCategory.Pressure, 133.322387415,
            new[] { "torr", "mmhg", "mm hg" }));

        // 8. Energy & Work (Base: Joule 'J')
        Register(new("j", "joule", "joules", "J", UnitCategory.Energy, 1.0,
            new[] { "j", "joule", "joules" }, IsCommonAlternative: true));
        Register(new("kj", "kilojoule", "kilojoules", "kJ", UnitCategory.Energy, 1000.0,
            new[] { "kj", "kilojoule", "kilojoules" }, IsCommonAlternative: true));
        Register(new("cal", "calorie", "calories", "cal", UnitCategory.Energy, 4.184,
            new[] { "cal", "calorie", "calories" }));
        Register(new("kcal", "kilocalorie", "kilocalories", "kcal", UnitCategory.Energy, 4184.0,
            new[] { "kcal", "food calorie", "kilocalorie", "kilocalories" }, IsCommonAlternative: true));
        Register(new("wh", "watt-hour", "watt-hours", "Wh", UnitCategory.Energy, 3600.0,
            new[] { "wh", "watt-hour", "watt-hours" }));
        Register(new("kwh", "kilowatt-hour", "kilowatt-hours", "kWh", UnitCategory.Energy, 3.6e6,
            new[] { "kwh", "kilowatt-hour", "kilowatt-hours" }, IsCommonAlternative: true));
        Register(new("btu", "british thermal unit", "british thermal units", "BTU", UnitCategory.Energy, 1055.05585,
            new[] { "btu", "btus" }, IsCommonAlternative: true));
        Register(new("ev", "electronvolt", "electronvolts", "eV", UnitCategory.Energy, 1.602176634e-19,
            new[] { "ev", "electronvolt", "electronvolts" }));

        // 9. Power (Base: Watt 'W')
        Register(new("w", "watt", "watts", "W", UnitCategory.Power, 1.0,
            new[] { "w", "watt", "watts" }, IsCommonAlternative: true));
        Register(new("kw", "kilowatt", "kilowatts", "kW", UnitCategory.Power, 1000.0,
            new[] { "kw", "kilowatt", "kilowatts" }, IsCommonAlternative: true));
        Register(new("mw", "megawatt", "megawatts", "MW", UnitCategory.Power, 1e6,
            new[] { "mw", "megawatt", "megawatts" }, IsCommonAlternative: true));
        Register(new("hp", "horsepower", "horsepower", "hp", UnitCategory.Power, 745.699872,
            new[] { "hp", "horsepower" }, IsCommonAlternative: true));
        Register(new("ps", "metric horsepower", "metric horsepower", "ps", UnitCategory.Power, 735.49875,
            new[] { "ps", "cv" }));
        Register(new("btuh", "BTU per hour", "BTU per hour", "BTU/h", UnitCategory.Power, 0.29307107,
            new[] { "btu/h", "btuh" }));

        // 10. Time (Base: Second 's')
        Register(new("ns", "nanosecond", "nanoseconds", "ns", UnitCategory.Time, 1e-9,
            new[] { "ns", "nanosecond", "nanoseconds" }));
        Register(new("us", "microsecond", "microseconds", "µs", UnitCategory.Time, 1e-6,
            new[] { "us", "µs", "microsecond", "microseconds" }));
        Register(new("ms", "millisecond", "milliseconds", "ms", UnitCategory.Time, 0.001,
            new[] { "ms", "millisecond", "milliseconds" }, IsCommonAlternative: true));
        Register(new("s", "second", "seconds", "s", UnitCategory.Time, 1.0,
            new[] { "s", "sec", "second", "seconds" }, IsCommonAlternative: true));
        Register(new("min", "minute", "minutes", "min", UnitCategory.Time, 60.0,
            new[] { "min", "mins", "minute", "minutes" }, IsCommonAlternative: true));
        Register(new("h", "hour", "hours", "h", UnitCategory.Time, 3600.0,
            new[] { "h", "hr", "hrs", "hour", "hours" }, IsCommonAlternative: true));
        Register(new("d", "day", "days", "d", UnitCategory.Time, 86400.0,
            new[] { "d", "day", "days" }, IsCommonAlternative: true));
        Register(new("w", "week", "weeks", "w", UnitCategory.Time, 604800.0,
            new[] { "w", "week", "weeks" }, IsCommonAlternative: true));
        Register(new("mo", "month", "months", "mo", UnitCategory.Time, 2629800.0, // 30.4375 days
            new[] { "mo", "month", "months" }));
        Register(new("yr", "year", "years", "yr", UnitCategory.Time, 31557600.0, // 365.25 days
            new[] { "yr", "year", "years", "y" }));

        // 11. Digital Storage (Base: Byte 'B', using 1024 binary scale)
        Register(new("b", "bit", "bits", "b", UnitCategory.DigitalStorage, 0.125,
            new[] { "bit", "bits" }));
        Register(new("B", "byte", "bytes", "B", UnitCategory.DigitalStorage, 1.0,
            new[] { "byte", "bytes" }, IsCommonAlternative: true));
        Register(new("kb", "kilobyte", "kilobytes", "KB", UnitCategory.DigitalStorage, 1024.0,
            new[] { "kb", "kilobyte", "kilobytes", "kib" }, IsCommonAlternative: true));
        Register(new("mb", "megabyte", "megabytes", "MB", UnitCategory.DigitalStorage, 1048576.0,
            new[] { "mb", "megabyte", "megabytes", "mib" }, IsCommonAlternative: true));
        Register(new("gb", "gigabyte", "gigabytes", "GB", UnitCategory.DigitalStorage, 1073741824.0,
            new[] { "gb", "gigabyte", "gigabytes", "gib" }, IsCommonAlternative: true));
        Register(new("tb", "terabyte", "terabytes", "TB", UnitCategory.DigitalStorage, 1099511627776.0,
            new[] { "tb", "terabyte", "terabytes", "tib" }, IsCommonAlternative: true));
        Register(new("pb", "petabyte", "petabytes", "PB", UnitCategory.DigitalStorage, 1.125899906842624e15,
            new[] { "pb", "petabyte", "petabytes", "pib" }));

        // 12. Data Transfer Rates (Base: bits/sec 'bps')
        Register(new("bps", "bit per second", "bits per second", "bps", UnitCategory.DataTransfer, 1.0,
            new[] { "bps", "bit per second", "bits per second" }));
        Register(new("kbps", "kilobit per second", "kilobits per second", "kbps", UnitCategory.DataTransfer, 1000.0,
            new[] { "kbps", "kbit/s", "kilobit per second" }, IsCommonAlternative: true));
        Register(new("mbps", "megabit per second", "megabits per second", "Mbps", UnitCategory.DataTransfer, 1e6,
            new[] { "mbps", "mbit/s", "megabit per second" }, IsCommonAlternative: true));
        Register(new("gbps", "gigabit per second", "gigabits per second", "Gbps", UnitCategory.DataTransfer, 1e9,
            new[] { "gbps", "gbit/s", "gigabit per second" }, IsCommonAlternative: true));
        Register(new("B_s", "byte per second", "bytes per second", "B/s", UnitCategory.DataTransfer, 8.0,
            new[] { "B/s", "Bps", "byte per second" }));
        Register(new("KB_s", "kilobyte per second", "kilobytes per second", "KB/s", UnitCategory.DataTransfer, 8000.0,
            new[] { "KB/s", "KBps", "kb/s", "kilobyte per second" }, IsCommonAlternative: true));
        Register(new("MB_s", "megabyte per second", "megabytes per second", "MB/s", UnitCategory.DataTransfer, 8e6,
            new[] { "MB/s", "mb/s", "megabyte per second" }, IsCommonAlternative: true));
        Register(new("GB_s", "gigabyte per second", "gigabytes per second", "GB/s", UnitCategory.DataTransfer, 8e9,
            new[] { "GB/s", "gb/s", "gigabyte per second" }, IsCommonAlternative: true));

        // 13. Angle (Base: Radian 'rad')
        Register(new("deg", "degree", "degrees", "°", UnitCategory.Angle, Math.PI / 180.0,
            new[] { "deg", "degree", "degrees", "°" }, IsCommonAlternative: true));
        Register(new("rad", "radian", "radians", "rad", UnitCategory.Angle, 1.0,
            new[] { "rad", "radian", "radians" }, IsCommonAlternative: true));
        Register(new("grad", "gradian", "gradians", "grad", UnitCategory.Angle, Math.PI / 200.0,
            new[] { "grad", "gradians", "gon" }));
        Register(new("turn", "turn", "turns", "turn", UnitCategory.Angle, 2.0 * Math.PI,
            new[] { "turn", "turns", "rev", "revolution", "revolutions" }));

        // 14. Typography & UI (Base: Pixel 'px' based on 96 DPI / 16px root)
        Register(new("px", "pixel", "pixels", "px", UnitCategory.Typography, 1.0,
            new[] { "px", "pixel", "pixels" }, IsCommonAlternative: true));
        Register(new("rem", "root em", "root em", "rem", UnitCategory.Typography, 16.0,
            new[] { "rem" }, IsCommonAlternative: true));
        Register(new("em", "em", "em", "em", UnitCategory.Typography, 16.0,
            new[] { "em" }, IsCommonAlternative: true));
        Register(new("pt_typo", "point", "points", "pt", UnitCategory.Typography, 4.0 / 3.0, // 72 pt = 96 px -> 1 pt = 4/3 px
            new[] { "pt", "point", "points" }, IsCommonAlternative: true));
        Register(new("pc", "pica", "picas", "pc", UnitCategory.Typography, 16.0, // 1 pc = 12 pt = 16 px
            new[] { "pc", "pica", "picas" }));
        Register(new("dp", "device independent pixel", "device independent pixels", "dp", UnitCategory.Typography, 1.0,
            new[] { "dp", "sp" }));

        // 15. Fun & Cultural Measurements (Registered for first-class query support, IsCommonAlternative: false)
        RegisterFunUnits();
    }

    private static void RegisterFunUnits()
    {
        // Length
        Register(new("banana", "banana", "bananas", "🍌", UnitCategory.Length, 0.1778,
            new[] { "banana", "bananas" }, IsCommonAlternative: false));
        Register(new("smoot", "smoot", "smoots", "smoot", UnitCategory.Length, 1.7018,
            new[] { "smoot", "smoots" }, IsCommonAlternative: false));
        Register(new("lego", "LEGO brick", "LEGO bricks", "bricks", UnitCategory.Length, 0.0096,
            new[] { "lego", "legos", "lego brick", "lego bricks" }, IsCommonAlternative: false));
        Register(new("light_ns", "light-nanosecond", "light-nanoseconds", "light-ns", UnitCategory.Length, 0.299792458,
            new[] { "light-ns", "light-nanosecond", "light-nanoseconds", "light ns" }, IsCommonAlternative: false));
        Register(new("beard_sec", "beard-second", "beard-seconds", "beard-sec", UnitCategory.Length, 5e-9,
            new[] { "beard-second", "beard-seconds", "beard-sec", "beardsec" }, IsCommonAlternative: false));
        Register(new("bus", "double-decker bus", "double-decker buses", "buses", UnitCategory.Length, 8.4,
            new[] { "bus", "buses", "double-decker bus", "double-decker buses", "double decker bus", "double decker buses" }, IsCommonAlternative: false));
        Register(new("football_field", "football field", "football fields", "fields", UnitCategory.Length, 91.44,
            new[] { "football field", "football fields" }, IsCommonAlternative: false));

        // Mass
        Register(new("cat", "domestic cat", "domestic cats", "cats", UnitCategory.Mass, 4.5,
            new[] { "cat", "cats", "house cat", "house cats" }, IsCommonAlternative: false));
        Register(new("dog", "golden retriever", "golden retrievers", "dogs", UnitCategory.Mass, 30.0,
            new[] { "dog", "dogs", "golden retriever", "golden retrievers" }, IsCommonAlternative: false));
        Register(new("apple", "medium apple", "medium apples", "apples", UnitCategory.Mass, 0.18,
            new[] { "apple", "apples" }, IsCommonAlternative: false));
        Register(new("penny", "US penny", "US pennies", "pennies", UnitCategory.Mass, 0.0025,
            new[] { "penny", "pennies", "us penny", "us pennies" }, IsCommonAlternative: false));
        Register(new("beetle", "VW Beetle", "VW Beetles", "beetles", UnitCategory.Mass, 1300.0,
            new[] { "beetle", "beetles", "vw beetle", "vw beetles" }, IsCommonAlternative: false));
        Register(new("elephant", "African elephant", "African elephants", "elephants", UnitCategory.Mass, 6000.0,
            new[] { "elephant", "elephants", "african elephant", "african elephants" }, IsCommonAlternative: false));
        Register(new("whale", "blue whale", "blue whales", "whales", UnitCategory.Mass, 140000.0,
            new[] { "whale", "whales", "blue whale", "blue whales" }, IsCommonAlternative: false));

        // Volume
        Register(new("soda_can", "soda can", "soda cans", "cans", UnitCategory.Volume, 0.000355,
            new[] { "soda can", "soda cans", "soda-can", "soda-cans" }, IsCommonAlternative: false));
        Register(new("coffee_mug", "coffee mug", "coffee mugs", "mugs", UnitCategory.Volume, 0.00025,
            new[] { "coffee mug", "coffee mugs", "mug", "mugs" }, IsCommonAlternative: false));
        Register(new("bathtub", "bathtub", "bathtubs", "bathtubs", UnitCategory.Volume, 0.15,
            new[] { "bathtub", "bathtubs", "tub", "tubs" }, IsCommonAlternative: false));
        Register(new("keg", "beer keg", "beer kegs", "kegs", UnitCategory.Volume, 0.05867,
            new[] { "keg", "kegs", "beer keg", "beer kegs" }, IsCommonAlternative: false));
        Register(new("olympic_pool", "Olympic swimming pool", "Olympic swimming pools", "pools", UnitCategory.Volume, 2500.0,
            new[] { "olympic pool", "olympic pools", "swimming pool", "swimming pools" }, IsCommonAlternative: false));
        Register(new("raindrop", "raindrop", "raindrops", "drops", UnitCategory.Volume, 5e-8,
            new[] { "raindrop", "raindrops" }, IsCommonAlternative: false));

        // Digital Storage
        Register(new("floppy", "3.5\" floppy disk", "3.5\" floppy disks", "floppies", UnitCategory.DigitalStorage, 1474560.0,
            new[] { "floppy", "floppies", "floppy disk", "floppy disks", "diskette", "diskettes" }, IsCommonAlternative: false));
        Register(new("cd", "compact disc", "compact discs", "CDs", UnitCategory.DigitalStorage, 734003200.0,
            new[] { "cd", "cds", "compact disc", "compact discs" }, IsCommonAlternative: false));
        Register(new("dvd", "DVD", "DVDs", "DVDs", UnitCategory.DigitalStorage, 5046586572.8,
            new[] { "dvd", "dvds" }, IsCommonAlternative: false));

        // Time
        Register(new("jiffy", "jiffy", "jiffies", "jiffies", UnitCategory.Time, 1.0 / 60.0,
            new[] { "jiffy", "jiffies" }, IsCommonAlternative: false));
        Register(new("fortnight", "fortnight", "fortnights", "fortnights", UnitCategory.Time, 1209600.0,
            new[] { "fortnight", "fortnights" }, IsCommonAlternative: false));
    }

    private static void Register(UnitDefinition unit)
    {
        AllUnitsList.Add(unit);

        if (!CategoryMap.TryGetValue(unit.Category, out var list))
        {
            list = new List<UnitDefinition>();
            CategoryMap[unit.Category] = list;
        }
        list.Add(unit);

        RegisterAlias(unit.Id, unit);
        RegisterAlias(unit.SingularName, unit);
        RegisterAlias(unit.PluralName, unit);
        RegisterAlias(unit.Symbol, unit);

        foreach (var alias in unit.Aliases)
        {
            RegisterAlias(alias, unit);
        }
    }

    private static void RegisterAlias(string alias, UnitDefinition unit)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;
        string key = alias.Trim();
        if (!MultiAliasMap.TryGetValue(key, out var list))
        {
            list = new List<UnitDefinition>();
            MultiAliasMap[key] = list;
        }
        if (!list.Contains(unit))
        {
            list.Add(unit);
        }
    }

    public static IReadOnlyList<UnitDefinition> FindCandidateUnits(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return Array.Empty<UnitDefinition>();
        string cleaned = token.Trim();

        if (MultiAliasMap.TryGetValue(cleaned, out var list)) return list;

        if (cleaned.EndsWith("es", StringComparison.OrdinalIgnoreCase) &&
            MultiAliasMap.TryGetValue(cleaned[..^2], out list)) return list;

        if (cleaned.EndsWith('s') &&
            MultiAliasMap.TryGetValue(cleaned[..^1], out list)) return list;

        return Array.Empty<UnitDefinition>();
    }

    public static bool TryFindUnit(string token, out UnitDefinition unit)
    {
        unit = null!;
        var list = FindCandidateUnits(token);
        if (list.Count > 0)
        {
            unit = list[0];
            return true;
        }
        return false;
    }

    public static IReadOnlyList<UnitDefinition> GetUnitsInCategory(UnitCategory category)
    {
        if (CategoryMap.TryGetValue(category, out var list))
        {
            return list;
        }
        return Array.Empty<UnitDefinition>();
    }

    public static IReadOnlyList<UnitDefinition> GetAllUnits() => AllUnitsList;
}
