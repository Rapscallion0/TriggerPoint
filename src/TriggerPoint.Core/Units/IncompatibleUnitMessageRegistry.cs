using System;
using System.Collections.Generic;
using TriggerPoint.Core.Services;

namespace TriggerPoint.Core.Units;

public static class IncompatibleUnitMessageRegistry
{
    private static readonly Dictionary<(UnitCategory, UnitCategory), string[]> PairMessages = new()
    {
        // Length <-> Volume
        {
            (UnitCategory.Length, UnitCategory.Volume), new[]
            {
                "Unless you're measuring a 1-dimensional puddle, length and volume don't mix.",
                "Trying to pour a ruler into a cup won't give you liters!",
                "Length is a line, volume is a bucket. You'll need another dimension or two.",
                "Converting distance into liquid capacity requires bending the fabric of spacetime.",
                "You can't pour a yardstick into a jug—try cubic units instead!"
            }
        },
        // Length <-> Mass
        {
            (UnitCategory.Length, UnitCategory.Mass), new[]
            {
                "Length has no weight, unless you count the existential burden of a tape measure.",
                "You can't weigh a distance! (Unless you drop a 10-meter steel beam on a scale).",
                "A meter doesn't have mass until you carve it out of solid marble.",
                "In this universe, distance and heft require a density to meet.",
                "Stretching a tape measure doesn't make it any heavier!"
            }
        },
        // Mass <-> Volume
        {
            (UnitCategory.Mass, UnitCategory.Volume), new[]
            {
                "Mass and volume need a density to talk—are we measuring gold, water, or feathers?",
                "A kilogram of lead and a liter of helium would like a word about density.",
                "Without knowing what liquid or solid you're holding, mass can't become volume.",
                "You can't convert weight to capacity without bringing chemistry into it!",
                "Scales measure pull; beakers measure space. They only agree if you specify the substance."
            }
        },
        // Temperature <-> Length
        {
            (UnitCategory.Temperature, UnitCategory.Length), new[]
            {
                "Things might expand when heated, but temperature still isn't a distance.",
                "You can't measure how hot it is with a yardstick!",
                "Thermal kinetic energy refuses to be laid out along a ruler.",
                "Degrees describe molecular dance speed, not how far the dancers traveled.",
                "A fever won't tell you how many miles to your destination."
            }
        },
        // Temperature <-> Mass
        {
            (UnitCategory.Temperature, UnitCategory.Mass), new[]
            {
                "It's getting warm in here, but you can't weigh the heat!",
                "Thermal energy has no heft—a hot cup of coffee weighs the same as a cold one (relativity aside).",
                "You can't put degrees on a balance scale.",
                "Temperature measures thermal agitation, not gravitational pull."
            }
        },
        // Temperature <-> Volume
        {
            (UnitCategory.Temperature, UnitCategory.Volume), new[]
            {
                "Thermal kinetic energy refuses to be poured into a measuring cup.",
                "You can't pour 50 degrees into a bucket!",
                "Temperature describes heat intensity, not liquid capacity.",
                "Adding degrees won't fill your glass any higher."
            }
        },
        // Time <-> Length
        {
            (UnitCategory.Time, UnitCategory.Length), new[]
            {
                "Unless you're measuring in light-years or talking to Doctor Who, time isn't distance.",
                "You can spend time and walk distance, but you can't convert seconds into inches.",
                "A minute is 60 seconds, not 60 feet (even if you're running fast).",
                "Time flies, but it doesn't fly with a tape measure.",
                "Clocks tick forward, rulers stay put—they live along different axes."
            }
        },
        // Speed <-> Time
        {
            (UnitCategory.Speed, UnitCategory.Time), new[]
            {
                "Going 60 mph won't tell you what time it is—distance is still missing from that equation.",
                "Speed is how fast you're moving; time is how long it took. They need distance to connect.",
                "You can't convert velocity directly into duration without knowing how far you went.",
                "A speedometer can't replace a stopwatch!"
            }
        },
        // Digital Storage <-> Mass
        {
            (UnitCategory.DigitalStorage, UnitCategory.Mass), new[]
            {
                "While electrons have mass, a gigabyte still won't register on your bathroom scale.",
                "Downloading 50 gigabytes won't make your laptop or phone weigh any more!",
                "Data is purely bits and bytes—it travels light without carrying pounds.",
                "You can't weigh a file, no matter how heavy the content is."
            }
        },
        // Digital Storage <-> Length
        {
            (UnitCategory.DigitalStorage, UnitCategory.Length), new[]
            {
                "You can't stretch a megabyte across your kitchen floor.",
                "Data packets travel along wires, but bytes don't have a physical length.",
                "Stacking up hard drive sectors won't give you inches or meters.",
                "Storage measures information entropy, not physical linear distance."
            }
        },
        // Typography <-> Mass
        {
            (UnitCategory.Typography, UnitCategory.Mass), new[]
            {
                "Points and pixels look great on screen, but typography has zero gravitational weight.",
                "Typesetting has style, but it definitely has no heft on a scale.",
                "You can't weigh a 16-pixel font!",
                "Designers might obsess over font weight, but scales don't care."
            }
        },
        // Typography <-> Volume
        {
            (UnitCategory.Typography, UnitCategory.Volume), new[]
            {
                "Points and pixels look great on screen, but they won't fill a glass.",
                "A 12pt font won't pour into a measuring cup!",
                "Screen pixels don't have liquid displacement.",
                "You can't hydrate with digital typography."
            }
        },
        // Area <-> Volume
        {
            (UnitCategory.Area, UnitCategory.Volume), new[]
            {
                "Area is a flat surface; volume needs depth. You're one dimension short!",
                "You can paint an area, but you need a tank to hold volume.",
                "Square units cover planes; cubic units fill spaces.",
                "Flat squares have zero capacity until you give them some height."
            }
        },
        // Energy / Power <-> Length
        {
            (UnitCategory.Energy, UnitCategory.Length), new[]
            {
                "Joules describe the ability to do work, not the distance of the track.",
                "Energy can push an object along a distance, but it isn't distance itself.",
                "You can't stretch a kilowatt-hour with a ruler.",
                "Power and distance need a force vector to talk to each other."
            }
        }
    };

    private static readonly string[] UniversalFallbackMessages =
    {
        "Those units are from completely different dimensions—even theoretical physics can't bridge that gap!",
        "Nice try, but those measurements live in different physical universes.",
        "That conversion would require redefining the fundamental constants of nature.",
        "You can't compare apples and light-years!",
        "Dimensional analysis says no, but your curiosity gets an A+.",
        "Even the most ambitious dimensional portal can't connect those two units.",
        "Those units represent completely incompatible dimensions of reality."
    };

    public static string GetRandomMessage(UnitCategory catA, UnitCategory catB)
    {
        string[]? pool = null;
        if (PairMessages.TryGetValue((catA, catB), out var list1))
        {
            pool = list1;
        }
        else if (PairMessages.TryGetValue((catB, catA), out var list2))
        {
            pool = list2;
        }

        pool ??= UniversalFallbackMessages;
        int idx = Random.Shared.Next(pool.Length);
        return pool[idx];
    }

    public static bool TryGetDimensionalBridge(
        ParsedConversionQuery query,
        out AlternativeMeasurement? bridge)
    {
        bridge = null;
        var fromCat = query.FromUnit.Category;
        var toCat = query.ToUnit.Category;
        double val = query.Value;

        // 1. Length -> Volume (Cubic interpretation)
        if (fromCat == UnitCategory.Length && toCat == UnitCategory.Volume)
        {
            double cubicM3 = val * Math.Pow(query.FromUnit.FactorToBase, 3);
            double targetVal = query.ToUnit.FromBase(cubicM3);
            string targetFormatted = FractionalFormatter.FormatValueWithUnit(
                UnitConverterEngine.FormatDouble(targetVal), query.ToUnit);

            bridge = new AlternativeMeasurement(
                Label: $"Cubic {query.FromUnit.PluralName}",
                FormattedValue: targetFormatted,
                Description: $"Assuming {UnitConverterEngine.FormatDouble(val)} cubic {query.FromUnit.PluralName} = {targetFormatted}",
                Category: "Suggestion",
                IsCommon: true,
                ConceptTitle: "📦 Cubic Volume Bridge",
                ConversationalSentence: $"If you meant {UnitConverterEngine.FormatDouble(val)} cubic {query.FromUnit.PluralName}, that equals {targetFormatted}.");
            return true;
        }

        // 2. Length -> Area (Square interpretation)
        if (fromCat == UnitCategory.Length && toCat == UnitCategory.Area)
        {
            double sqM2 = val * Math.Pow(query.FromUnit.FactorToBase, 2);
            double targetVal = query.ToUnit.FromBase(sqM2);
            string targetFormatted = FractionalFormatter.FormatValueWithUnit(
                UnitConverterEngine.FormatDouble(targetVal), query.ToUnit);

            bridge = new AlternativeMeasurement(
                Label: $"Square {query.FromUnit.PluralName}",
                FormattedValue: targetFormatted,
                Description: $"Assuming {UnitConverterEngine.FormatDouble(val)} square {query.FromUnit.PluralName} = {targetFormatted}",
                Category: "Suggestion",
                IsCommon: true,
                ConceptTitle: "📐 Square Area Bridge",
                ConversationalSentence: $"If you meant {UnitConverterEngine.FormatDouble(val)} square {query.FromUnit.PluralName}, that equals {targetFormatted}.");
            return true;
        }

        // 3. Mass -> Volume (Assuming Water density: 1000 kg/m³)
        if (fromCat == UnitCategory.Mass && toCat == UnitCategory.Volume)
        {
            double baseKg = query.FromUnit.ToBase(val);
            double waterM3 = baseKg / 1000.0;
            double targetVal = query.ToUnit.FromBase(waterM3);
            string targetFormatted = FractionalFormatter.FormatValueWithUnit(
                UnitConverterEngine.FormatDouble(targetVal), query.ToUnit);

            bridge = new AlternativeMeasurement(
                Label: "Water Volume (1 g/ml)",
                FormattedValue: targetFormatted,
                Description: $"At water density (1 g/cm³): {UnitConverterEngine.FormatDouble(val)} {query.FromUnit.PluralName} = {targetFormatted}",
                Category: "Suggestion",
                IsCommon: true,
                ConceptTitle: "💧 Water Density Bridge",
                ConversationalSentence: $"For water (1 g/ml), {UnitConverterEngine.FormatDouble(val)} {query.FromUnit.PluralName} would be {targetFormatted}.");
            return true;
        }

        // 4. Volume -> Mass (Assuming Water density: 1000 kg/m³)
        if (fromCat == UnitCategory.Volume && toCat == UnitCategory.Mass)
        {
            double baseM3 = query.FromUnit.ToBase(val);
            double waterKg = baseM3 * 1000.0;
            double targetVal = query.ToUnit.FromBase(waterKg);
            string targetFormatted = FractionalFormatter.FormatValueWithUnit(
                UnitConverterEngine.FormatDouble(targetVal), query.ToUnit);

            bridge = new AlternativeMeasurement(
                Label: "Water Mass (1 g/ml)",
                FormattedValue: targetFormatted,
                Description: $"At water density (1 g/cm³): {UnitConverterEngine.FormatDouble(val)} {query.FromUnit.PluralName} = {targetFormatted}",
                Category: "Suggestion",
                IsCommon: true,
                ConceptTitle: "💧 Water Density Bridge",
                ConversationalSentence: $"For water (1 g/ml), {UnitConverterEngine.FormatDouble(val)} {query.FromUnit.PluralName} would weigh {targetFormatted}.");
            return true;
        }

        return false;
    }
}
