using System;
using System.Collections.Generic;
using TriggerPoint.Core.Services;

namespace TriggerPoint.Core.Units;

public static class FunMeasurementRegistry
{
    private static string FormatNumber(double val)
    {
        double abs = Math.Abs(val);
        if (abs == 0) return "0";
        if (abs >= 1000000000) return $"{val:N1}";
        if (abs >= 1000) return $"{val:N0}";
        if (abs >= 10) return $"{val:N1}";
        if (abs >= 1) return $"{val:N2}";
        if (abs >= 0.01) return $"{val:N3}";
        return $"{val:G4}";
    }

    public static IReadOnlyList<AlternativeMeasurement> GetFunAlternatives(
        UnitCategory category,
        double baseValue,
        double originalValue,
        string originalUnitName)
    {
        var list = new List<AlternativeMeasurement>();
        if (double.IsNaN(baseValue) || double.IsInfinity(baseValue) || baseValue <= 0 && category != UnitCategory.Temperature)
        {
            return list;
        }

        string subject = $"{originalValue:G4} {originalUnitName}";

        switch (category)
        {
            case UnitCategory.Length:
                GenerateLengthAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.Mass:
                GenerateMassAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.Volume:
                GenerateVolumeAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.Area:
                GenerateAreaAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.Speed:
                GenerateSpeedAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.Temperature:
                GenerateTemperatureAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.Energy:
                GenerateEnergyAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.Power:
                GeneratePowerAlternatives(list, baseValue, subject);
                break;
            case UnitCategory.DigitalStorage:
                GenerateStorageAlternatives(list, baseValue, subject);
                break;
        }

        return list;
    }

    private static void GenerateLengthAlternatives(List<AlternativeMeasurement> list, double meters, string subject)
    {
        // 1. Bananas (for scale): ~17.78 cm (7 inches)
        double bananas = meters / 0.1778;
        list.Add(new(
            Label: "Bananas (for scale)",
            FormattedValue: $"{FormatNumber(bananas)} bananas",
            Description: "The internet's beloved universal measurement standard (~7 inches)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🍌 Bananas (for Scale)",
            ConversationalSentence: $"{subject} is approximately {FormatNumber(bananas)} bananas for scale."));

        // 2. Smoots: 1.7018 m (5 ft 7 in)
        double smoots = meters / 1.7018;
        list.Add(new(
            Label: "Smoots",
            FormattedValue: $"{FormatNumber(smoots)} Smoots",
            Description: "MIT's historic unit from Oliver Smoot measuring Harvard Bridge (1958)",
            Category: "Historical",
            IsCommon: false,
            ConceptTitle: "📏 Oliver Smoot Units",
            ConversationalSentence: $"{subject} equals {FormatNumber(smoots)} Smoots."));

        // 3. LEGO Bricks (height): 9.6 mm
        double lego = meters / 0.0096;
        list.Add(new(
            Label: "LEGO Brick Stack",
            FormattedValue: $"{FormatNumber(lego)} bricks",
            Description: "Height of standard classic plastic LEGO bricks stacked vertically (9.6 mm each)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🧱 LEGO Brick Heights",
            ConversationalSentence: $"{subject} is equal to a stack of {FormatNumber(lego)} LEGO bricks."));

        // 4. Light-nanoseconds: ~29.98 cm (11.8 inches)
        double lightNs = meters / 0.299792458;
        list.Add(new(
            Label: "Light-Nanoseconds",
            FormattedValue: $"{FormatNumber(lightNs)} light-ns",
            Description: "Distance a photon travels in vacuum in one nanosecond (~29.98 cm)",
            Category: "Physics",
            IsCommon: false,
            ConceptTitle: "⚡ Light-Nanoseconds",
            ConversationalSentence: $"{subject} represents {FormatNumber(lightNs)} light-nanoseconds."));

        // 5. Beard-seconds: ~5 nanometers
        if (meters <= 0.05)
        {
            double beardSec = meters / 5e-9;
            list.Add(new(
                Label: "Beard-Seconds",
                FormattedValue: $"{FormatNumber(beardSec)} beard-seconds",
                Description: "Distance an average human beard grows in one second (~5 nanometers)",
                Category: "Nature",
                IsCommon: false,
                ConceptTitle: "🧔 Human Beard-Seconds",
                ConversationalSentence: $"{subject} is {FormatNumber(beardSec)} beard-seconds of facial hair growth."));
        }

        // 6. Double-Decker Buses: 8.4 m
        if (meters >= 1.0)
        {
            double buses = meters / 8.4;
            list.Add(new(
                Label: "Double-Decker Buses",
                FormattedValue: $"{FormatNumber(buses)} buses",
                Description: "Classic London AEC Routemaster double-decker bus lengths (8.4m)",
                Category: "PopCulture",
                IsCommon: false,
                ConceptTitle: "🚌 London Double-Decker Buses",
                ConversationalSentence: $"{subject} spans approximately {FormatNumber(buses)} London double-decker buses."));
        }

        // 7. American Football Fields: 91.44 m (100 yards)
        if (meters >= 10.0)
        {
            double fields = meters / 91.44;
            list.Add(new(
                Label: "Football Fields",
                FormattedValue: $"{FormatNumber(fields)} fields",
                Description: "Length of a regulation American football field (100 yards / 91.44m)",
                Category: "Common",
                IsCommon: false,
                ConceptTitle: "🏈 American Football Fields",
                ConversationalSentence: $"{subject} spans {FormatNumber(fields)} American football fields."));
        }

        // 8. Earth Circumference: 40,075 km
        if (meters >= 50000.0)
        {
            double earthCirc = meters / 40075000.0;
            list.Add(new(
                Label: "Earth Circumference",
                FormattedValue: $"{FormatNumber(earthCirc)} Earth trips",
                Description: "Fraction of a complete equatorial journey around the Earth (40,075 km)",
                Category: "Space",
                IsCommon: false,
                ConceptTitle: "🌍 Earth Equatorial Circumference",
                ConversationalSentence: $"{subject} represents {FormatNumber(earthCirc)} trips around Earth's equator."));
        }
    }

    private static void GenerateMassAlternatives(List<AlternativeMeasurement> list, double kg, string subject)
    {
        // 1. Domestic House Cats: 4.5 kg (~10 lbs)
        double cats = kg / 4.5;
        list.Add(new(
            Label: "Domestic House Cats",
            FormattedValue: $"{FormatNumber(cats)} cats",
            Description: "Average healthy adult domestic house cat (~4.5 kg / 10 lbs)",
            Category: "Nature",
            IsCommon: false,
            ConceptTitle: "🐈 Domestic House Cats",
            ConversationalSentence: $"{subject} is approximately {FormatNumber(cats)} domestic house cats."));

        // 2. Golden Retrievers: 30 kg (~66 lbs)
        double dogs = kg / 30.0;
        list.Add(new(
            Label: "Golden Retrievers",
            FormattedValue: $"{FormatNumber(dogs)} dogs",
            Description: "Average adult Golden Retriever companion dog (~30 kg / 66 lbs)",
            Category: "Nature",
            IsCommon: false,
            ConceptTitle: "🐕 Golden Retrievers",
            ConversationalSentence: $"{subject} weighs as much as {FormatNumber(dogs)} Golden Retrievers."));

        // 3. Medium Apples: 180 g
        double apples = kg / 0.18;
        list.Add(new(
            Label: "Medium Apples",
            FormattedValue: $"{FormatNumber(apples)} apples",
            Description: "Average medium fresh Honeycrisp or Gala apple (~180 grams)",
            Category: "Common",
            IsCommon: false,
            ConceptTitle: "🍎 Fresh Medium Apples",
            ConversationalSentence: $"{subject} weighs approximately {FormatNumber(apples)} medium apples."));

        // 4. US Pennies: 2.5 g
        if (kg <= 20.0)
        {
            double pennies = kg / 0.0025;
            list.Add(new(
                Label: "US Pennies",
                FormattedValue: $"{FormatNumber(pennies)} pennies",
                Description: "Weight of post-1982 copper-plated zinc US one-cent coins (2.5g)",
                Category: "PopCulture",
                IsCommon: false,
                ConceptTitle: "🪙 US One-Cent Pennies",
                ConversationalSentence: $"{subject} is the weight of {FormatNumber(pennies)} US pennies."));
        }

        // 5. Classic VW Beetles: 1,300 kg
        if (kg >= 25.0)
        {
            double bugs = kg / 1300.0;
            list.Add(new(
                Label: "VW Beetles",
                FormattedValue: $"{FormatNumber(bugs)} cars",
                Description: "Curb weight of a vintage Volkswagen Beetle (~1,300 kg)",
                Category: "PopCulture",
                IsCommon: false,
                ConceptTitle: "🚗 Classic VW Beetles",
                ConversationalSentence: $"{subject} is the equivalent of {FormatNumber(bugs)} classic VW Beetles."));
        }

        // 6. African Elephants: ~6,000 kg
        if (kg >= 50.0)
        {
            double elephants = kg / 6000.0;
            list.Add(new(
                Label: "African Elephants",
                FormattedValue: $"{FormatNumber(elephants)} elephants",
                Description: "Average adult African bush elephant (~6,000 kg / 6 metric tons)",
                Category: "Nature",
                IsCommon: false,
                ConceptTitle: "🐘 African Bush Elephants",
                ConversationalSentence: $"{subject} equals {FormatNumber(elephants)} African bush elephants."));
        }

        // 7. Blue Whales: 140,000 kg
        if (kg >= 500.0)
        {
            double whales = kg / 140000.0;
            list.Add(new(
                Label: "Blue Whales",
                FormattedValue: $"{FormatNumber(whales)} whales",
                Description: "Colossal mass of an adult Antarctic blue whale (~140,000 kg)",
                Category: "Nature",
                IsCommon: false,
                ConceptTitle: "🐋 Adult Blue Whales",
                ConversationalSentence: $"{subject} represents {FormatNumber(whales)} adult blue whales."));
        }
    }

    private static void GenerateVolumeAlternatives(List<AlternativeMeasurement> list, double m3, string subject)
    {
        // 1. Soda Cans: 355 ml (0.000355 m³)
        double cans = m3 / 0.000355;
        list.Add(new(
            Label: "Standard Soda Cans",
            FormattedValue: $"{FormatNumber(cans)} cans",
            Description: "Standard 12 fluid ounce aluminum beverage cans (355 ml)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🥤 12 oz Soda Cans",
            ConversationalSentence: $"{subject} could fill {FormatNumber(cans)} standard 12 oz soda cans."));

        // 2. Coffee Mugs: 250 ml (0.00025 m³)
        double mugs = m3 / 0.00025;
        list.Add(new(
            Label: "Fresh Coffee Mugs",
            FormattedValue: $"{FormatNumber(mugs)} mugs",
            Description: "Standard ceramic morning coffee mugs (~250 ml / 8.5 fl oz)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "☕ Morning Coffee Mugs",
            ConversationalSentence: $"{subject} is enough liquid for {FormatNumber(mugs)} mugs of fresh coffee."));

        // 3. Bathtubs: 150 liters (0.15 m³)
        double tubs = m3 / 0.15;
        list.Add(new(
            Label: "Bathtubs Filled",
            FormattedValue: $"{FormatNumber(tubs)} bathtubs",
            Description: "Standard household bathtubs filled to comfortable bathing capacity (~150 L)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🛁 Full Household Bathtubs",
            ConversationalSentence: $"{subject} is equal to {FormatNumber(tubs)} full bathtubs of water."));

        // 4. Half-Barrel Beer Kegs: 58.67 L (0.05867 m³)
        double kegs = m3 / 0.05867;
        list.Add(new(
            Label: "Half-Barrel Beer Kegs",
            FormattedValue: $"{FormatNumber(kegs)} kegs",
            Description: "Standard American commercial half-barrel kegs (15.5 gallons / 58.7 liters)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🍺 Brewery Half-Barrel Kegs",
            ConversationalSentence: $"{subject} equals {FormatNumber(kegs)} brewery half-barrel kegs."));

        // 5. Olympic Swimming Pools: 2,500,000 L (2,500 m³)
        if (m3 >= 0.5)
        {
            double pools = m3 / 2500.0;
            list.Add(new(
                Label: "Olympic Swimming Pools",
                FormattedValue: $"{FormatNumber(pools)} pools",
                Description: "Regulation 50m x 25m Olympic competition pools (2.5M liters / 660,000 gal)",
                Category: "Common",
                IsCommon: false,
                ConceptTitle: "🏊 Olympic Swimming Pools",
                ConversationalSentence: $"{subject} represents {FormatNumber(pools)} Olympic-sized swimming pools."));
        }

        // 6. Raindrops: 0.05 ml (5e-8 m³)
        if (m3 <= 0.01)
        {
            double drops = m3 / 5e-8;
            list.Add(new(
                Label: "Raindrops",
                FormattedValue: $"{FormatNumber(drops)} drops",
                Description: "Average spherical atmospheric raindrops (~0.05 ml / 50 µL)",
                Category: "Nature",
                IsCommon: false,
                ConceptTitle: "💧 Atmospheric Raindrops",
                ConversationalSentence: $"{subject} is approximately {FormatNumber(drops)} raindrops."));
        }
    }

    private static void GenerateAreaAlternatives(List<AlternativeMeasurement> list, double sqm, string subject)
    {
        // 1. Tennis Courts: 260.87 m²
        double courts = sqm / 260.87;
        list.Add(new(
            Label: "Tennis Courts",
            FormattedValue: $"{FormatNumber(courts)} courts",
            Description: "Regulation doubles tennis courts (260.87 m² / 2,808 ft²)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🎾 Regulation Tennis Courts",
            ConversationalSentence: $"{subject} is equal to {FormatNumber(courts)} doubles tennis courts."));

        // 2. American Football Fields: 5,351.2 m²
        if (sqm >= 50.0)
        {
            double fields = sqm / 5351.2;
            list.Add(new(
                Label: "Football Fields",
                FormattedValue: $"{FormatNumber(fields)} fields",
                Description: "Regulation American football field including end zones (5,351 m² / 57,600 ft²)",
                Category: "Common",
                IsCommon: false,
                ConceptTitle: "🏈 American Football Fields",
                ConversationalSentence: $"{subject} spans {FormatNumber(fields)} American football fields."));
        }

        // 3. FIFA Soccer Pitches: 7,140 m²
        if (sqm >= 100.0)
        {
            double pitches = sqm / 7140.0;
            list.Add(new(
                Label: "FIFA Soccer Pitches",
                FormattedValue: $"{FormatNumber(pitches)} pitches",
                Description: "Standard international soccer pitch (105m x 68m = 7,140 m²)",
                Category: "PopCulture",
                IsCommon: false,
                ConceptTitle: "⚽ FIFA Match Soccer Pitches",
                ConversationalSentence: $"{subject} is {FormatNumber(pitches)} FIFA soccer pitches."));
        }

        // 4. Central Park NYC: 3.41 km²
        if (sqm >= 10000.0)
        {
            double parks = sqm / 3410000.0;
            list.Add(new(
                Label: "Central Parks (NYC)",
                FormattedValue: $"{FormatNumber(parks)} Central Parks",
                Description: "Manhattan's iconic Central Park (843 acres / 3.41 km²)",
                Category: "PopCulture",
                IsCommon: false,
                ConceptTitle: "🌳 Central Park (Manhattan)",
                ConversationalSentence: $"{subject} represents {FormatNumber(parks)} NYC Central Parks."));
        }

        // 5. Vatican City: 49 hectares (490,000 m²)
        if (sqm >= 5000.0)
        {
            double vaticans = sqm / 490000.0;
            list.Add(new(
                Label: "Vatican Cities",
                FormattedValue: $"{FormatNumber(vaticans)} Vatican Cities",
                Description: "The world's smallest independent sovereign state (49 hectares / 121 acres)",
                Category: "Historical",
                IsCommon: false,
                ConceptTitle: "🇻🇦 Sovereign Vatican Cities",
                ConversationalSentence: $"{subject} represents {FormatNumber(vaticans)} Vatican Cities."));
        }
    }

    private static void GenerateSpeedAlternatives(List<AlternativeMeasurement> list, double mps, string subject)
    {
        // 1. Usain Bolt Top Speed: 12.42 m/s (27.78 mph)
        double bolt = mps / 12.42;
        list.Add(new(
            Label: "Usain Bolt Top Speed",
            FormattedValue: $"{FormatNumber(bolt)} Bolt sprints",
            Description: "Peak record speed clocked by Usain Bolt in the 100m sprint (12.42 m/s / 27.78 mph)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🏃 Usain Bolt Peak Velocity",
            ConversationalSentence: $"{subject} is {FormatNumber(bolt)} times Usain Bolt's record top sprint."));

        // 2. Cheetah Sprint: 31.29 m/s (70 mph)
        double cheetah = mps / 31.29;
        list.Add(new(
            Label: "Cheetah Sprint",
            FormattedValue: $"{FormatNumber(cheetah)} cheetah sprints",
            Description: "Maximum pursuit sprint speed of a wild African cheetah (~70 mph / 112 km/h)",
            Category: "Nature",
            IsCommon: false,
            ConceptTitle: "🐆 African Cheetah Sprints",
            ConversationalSentence: $"{subject} is {FormatNumber(cheetah)} times a cheetah's maximum sprint speed."));

        // 3. Peregrine Falcon Dive: 108 m/s (242 mph)
        double falcon = mps / 108.0;
        list.Add(new(
            Label: "Peregrine Falcon Dive",
            FormattedValue: $"{FormatNumber(falcon)} falcon dives",
            Description: "Terminal hunting stoop velocity of the fastest animal on Earth (108 m/s / 242 mph)",
            Category: "Nature",
            IsCommon: false,
            ConceptTitle: "🦅 Peregrine Falcon Stoop Dive",
            ConversationalSentence: $"{subject} is {FormatNumber(falcon)} times a peregrine falcon's stoop dive."));

        // 4. Speed of Sound: 343 m/s (Mach 1)
        double mach = mps / 343.0;
        list.Add(new(
            Label: "Mach (Speed of Sound)",
            FormattedValue: $"Mach {FormatNumber(mach)}",
            Description: "Speed of an acoustic pressure wave in dry air at 20°C (343 m/s / 1,235 km/h)",
            Category: "Physics",
            IsCommon: false,
            ConceptTitle: "🔊 Speed of Sound (Mach 1)",
            ConversationalSentence: $"{subject} corresponds to Mach {FormatNumber(mach)}."));

        // 5. Garden Snail Pace: 0.0013 m/s
        double snail = mps / 0.0013;
        list.Add(new(
            Label: "Garden Snail Pace",
            FormattedValue: $"{FormatNumber(snail)} snail paces",
            Description: "Top leisurely crawl of a common garden snail (~1.3 mm per second)",
            Category: "Nature",
            IsCommon: false,
            ConceptTitle: "🐌 Common Garden Snail Pace",
            ConversationalSentence: $"{subject} is {FormatNumber(snail)} times faster than a garden snail."));

        // 6. ISS Orbital Speed: 7,660 m/s (17,150 mph)
        if (mps >= 10.0)
        {
            double iss = mps / 7660.0;
            list.Add(new(
                Label: "ISS Orbital Speed",
                FormattedValue: $"{FormatNumber(iss)} ISS velocities",
                Description: "Speed required to stay in low Earth orbit aboard the ISS (7,660 m/s / 17,150 mph)",
                Category: "Space",
                IsCommon: false,
                ConceptTitle: "🛰️ ISS Orbital Speed",
                ConversationalSentence: $"{subject} is {FormatNumber(iss)} times the orbital speed of the ISS."));
        }
    }

    private static void GenerateTemperatureAlternatives(List<AlternativeMeasurement> list, double kelvin, string subject)
    {
        double c = kelvin - 273.15;
        double f = c * 9.0 / 5.0 + 32.0;

        // 1. Above Absolute Zero
        list.Add(new(
            Label: "Above Absolute Zero",
            FormattedValue: $"{FormatNumber(kelvin)} K above 0 K",
            Description: "Thermal offset from the coldest conceivable physical temperature (0 Kelvin)",
            Category: "Physics",
            IsCommon: false,
            ConceptTitle: "❄️ Offset Above Absolute Zero",
            ConversationalSentence: $"{subject} is {FormatNumber(kelvin)} Kelvin above absolute zero."));

        // 2. Relative to Human Body Temperature: 37 °C (98.6 °F)
        double diffBody = c - 37.0;
        string bodySign = diffBody >= 0 ? $"+{FormatNumber(diffBody)}" : $"{FormatNumber(diffBody)}";
        list.Add(new(
            Label: "Human Body Temp Delta",
            FormattedValue: $"{bodySign} °C vs body",
            Description: "Thermal difference from average human core body temperature (37.0°C / 98.6°F)",
            Category: "Nature",
            IsCommon: false,
            ConceptTitle: "🌡️ Normal Human Body Temperature",
            ConversationalSentence: $"{subject} is {bodySign} °C relative to standard human body temperature."));

        // 3. Pizza Baking Oven: 260 °C (500 °F)
        double pizzaRatio = (c + 273.15) / (260.0 + 273.15);
        list.Add(new(
            Label: "Pizza Oven Comparison",
            FormattedValue: $"{FormatNumber(pizzaRatio)}x pizza oven",
            Description: "Comparison to a roaring hot wood-fired pizza oven (260°C / 500°F)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🍕 Wood-Fired Pizza Baking Oven",
            ConversationalSentence: $"{subject} is {FormatNumber(pizzaRatio)} times the absolute temperature of a 500°F pizza oven."));

        // 4. Surface of Venus: 465 °C (738.15 K)
        double venusRatio = kelvin / 738.15;
        list.Add(new(
            Label: "Surface of Venus",
            FormattedValue: $"{FormatNumber(venusRatio)}x Venus surface",
            Description: "Runaway greenhouse surface temperature of planet Venus (465°C / 870°F)",
            Category: "Space",
            IsCommon: false,
            ConceptTitle: "🪐 Surface Temperature of Venus",
            ConversationalSentence: $"{subject} is {FormatNumber(venusRatio)} times the scorching surface temperature of Venus."));

        // 5. Surface of the Sun: 5,778 K
        double sunRatio = kelvin / 5778.0;
        list.Add(new(
            Label: "Surface of the Sun",
            FormattedValue: $"{FormatNumber(sunRatio)}x solar surface",
            Description: "Photospheric surface temperature of our Sun (~5,505°C / 9,940°F)",
            Category: "Space",
            IsCommon: false,
            ConceptTitle: "☀️ Solar Photosphere Temperature",
            ConversationalSentence: $"{subject} represents {FormatNumber(sunRatio)} of the Sun's surface temperature."));
    }

    private static void GenerateEnergyAlternatives(List<AlternativeMeasurement> list, double joules, string subject)
    {
        // 1. Glazed Donuts: ~250 kcal (1,046,000 J)
        double donuts = joules / 1046000.0;
        list.Add(new(
            Label: "Glazed Donuts",
            FormattedValue: $"{FormatNumber(donuts)} donuts",
            Description: "Nutritional food energy stored in fresh glazed donuts (~250 kcal / 1.05 MJ)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🍩 Glazed Donut Nutritional Energy",
            ConversationalSentence: $"{subject} contains the energy of {FormatNumber(donuts)} glazed donuts."));

        // 2. Smartphone Battery Charges: ~15 Wh (54,000 J)
        double phoneCharges = joules / 54000.0;
        list.Add(new(
            Label: "Smartphone Recharges",
            FormattedValue: $"{FormatNumber(phoneCharges)} charges",
            Description: "Full battery recharge cycles of an iPhone / Android smartphone (~15 Wh / 54 kJ)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "📱 Full Smartphone Battery Charges",
            ConversationalSentence: $"{subject} could power {FormatNumber(phoneCharges)} full smartphone recharges."));

        // 3. AA Alkaline Batteries: ~3.9 Wh (14,040 J)
        double aaBatts = joules / 14040.0;
        list.Add(new(
            Label: "AA Alkaline Batteries",
            FormattedValue: $"{FormatNumber(aaBatts)} batteries",
            Description: "Total chemical energy stored in standard 1.5V AA alkaline batteries (~3.9 Wh)",
            Category: "Common",
            IsCommon: false,
            ConceptTitle: "🔋 Standard AA Alkaline Batteries",
            ConversationalSentence: $"{subject} is equivalent to {FormatNumber(aaBatts)} AA alkaline batteries."));

        // 4. Grams of TNT: 4,184 J
        double tnt = joules / 4184.0;
        list.Add(new(
            Label: "Grams of TNT",
            FormattedValue: $"{FormatNumber(tnt)} g of TNT",
            Description: "Energy released by the detonation of TNT (1 gram of TNT = 4,184 Joules)",
            Category: "Physics",
            IsCommon: false,
            ConceptTitle: "💥 Grams of TNT Equivalent",
            ConversationalSentence: $"{subject} has the explosive energy yield of {FormatNumber(tnt)} grams of TNT."));

        // 5. Cloud-to-Ground Lightning Strike: ~1 GJ (1e9 J)
        if (joules >= 1e6)
        {
            double strikes = joules / 1e9;
            list.Add(new(
                Label: "Lightning Strikes",
                FormattedValue: $"{FormatNumber(strikes)} strikes",
                Description: "Average electrical energy discharged by a cloud-to-ground lightning bolt (~1 GJ)",
                Category: "Nature",
                IsCommon: false,
                ConceptTitle: "⚡ Cloud-to-Ground Lightning Bolts",
                ConversationalSentence: $"{subject} represents {FormatNumber(strikes)} cloud-to-ground lightning strikes."));
        }
    }

    private static void GeneratePowerAlternatives(List<AlternativeMeasurement> list, double watts, string subject)
    {
        // 1. Human Resting Metabolism: 100 Watts
        double humans = watts / 100.0;
        list.Add(new(
            Label: "Human Basal Metabolic Power",
            FormattedValue: $"{FormatNumber(humans)} humans",
            Description: "Resting metabolic heat output of an average adult human (~100 Watts)",
            Category: "Nature",
            IsCommon: false,
            ConceptTitle: "🧍 Resting Human Basal Heat",
            ConversationalSentence: $"{subject} produces the continuous power output of {FormatNumber(humans)} resting humans."));

        // 2. Kitchen Microwave Ovens: 1,000 Watts (1 kW)
        double microwaves = watts / 1000.0;
        list.Add(new(
            Label: "Kitchen Microwaves",
            FormattedValue: $"{FormatNumber(microwaves)} microwaves",
            Description: "Full cooking power of a standard countertop microwave oven (1,000 W / 1 kW)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🍿 Countertop Microwave Ovens",
            ConversationalSentence: $"{subject} could run {FormatNumber(microwaves)} kitchen microwaves at full blast."));

        // 3. Formula 1 Power Units: ~745.7 kW (~1,000 hp)
        if (watts >= 500.0)
        {
            double f1 = watts / 745700.0;
            list.Add(new(
                Label: "Formula 1 Power Units",
                FormattedValue: $"{FormatNumber(f1)} F1 engines",
                Description: "Total hybrid power output of an elite Formula 1 racing engine (~1,000 horsepower)",
                Category: "PopCulture",
                IsCommon: false,
                ConceptTitle: "🏎️ Formula 1 V6 Turbo Hybrids",
                ConversationalSentence: $"{subject} is equal to {FormatNumber(f1)} Formula 1 hybrid racing engines."));
        }

        // 4. Hovering Hummingbird: 2.0 Watts
        if (watts <= 100.0)
        {
            double birds = watts / 2.0;
            list.Add(new(
                Label: "Hovering Hummingbirds",
                FormattedValue: $"{FormatNumber(birds)} hummingbirds",
                Description: "Continuous mechanical power expended by a hovering hummingbird (~2 Watts)",
                Category: "Nature",
                IsCommon: false,
                ConceptTitle: "🐦 Hovering Hummingbirds",
                ConversationalSentence: $"{subject} powers the flight of {FormatNumber(birds)} hovering hummingbirds."));
        }
    }

    private static void GenerateStorageAlternatives(List<AlternativeMeasurement> list, double bytes, string subject)
    {
        // 1. 3.5" Floppy Disks: 1.44 MB (1,474,560 bytes)
        double floppies = bytes / 1474560.0;
        list.Add(new(
            Label: "3.5\" HD Floppy Disks",
            FormattedValue: $"{FormatNumber(floppies)} diskettes",
            Description: "Classic 3.5-inch high-density magnetic floppy diskettes (1.44 MB)",
            Category: "Historical",
            IsCommon: false,
            ConceptTitle: "💾 Classic 3.5\" Floppy Disks",
            ConversationalSentence: $"{subject} would fill {FormatNumber(floppies)} 3.5-inch floppy disks."));

        // 2. 320kbps MP3 Songs: ~8 MB (8,388,608 bytes)
        double songs = bytes / 8388608.0;
        list.Add(new(
            Label: "320kbps MP3 Songs",
            FormattedValue: $"{FormatNumber(songs)} songs",
            Description: "High-quality 320kbps MP3 audio tracks (~3.5 minutes each, ~8 MB)",
            Category: "PopCulture",
            IsCommon: false,
            ConceptTitle: "🎵 320kbps MP3 Audio Tracks",
            ConversationalSentence: $"{subject} is enough storage for {FormatNumber(songs)} high-bitrate MP3 songs."));

        // 3. Audio CDs: 700 MB (734,003,200 bytes)
        if (bytes >= 1048576.0)
        {
            double cds = bytes / 734003200.0;
            list.Add(new(
                Label: "700 MB Audio CDs",
                FormattedValue: $"{FormatNumber(cds)} CDs",
                Description: "Standard 80-minute 700 MB compact discs",
                Category: "Historical",
                IsCommon: false,
                ConceptTitle: "💿 700 MB Compact Discs",
                ConversationalSentence: $"{subject} equals {FormatNumber(cds)} standard 700 MB compact discs."));
        }

        // 4. Single-Layer DVDs: 4.7 GB (5,046,586,573 bytes)
        if (bytes >= 1e8)
        {
            double dvds = bytes / 5046586572.8;
            list.Add(new(
                Label: "4.7 GB Single-Layer DVDs",
                FormattedValue: $"{FormatNumber(dvds)} DVDs",
                Description: "Classic 4.7 GB single-layer digital versatile discs (DVDs)",
                Category: "Historical",
                IsCommon: false,
                ConceptTitle: "📀 4.7 GB Digital Video Discs",
                ConversationalSentence: $"{subject} could hold {FormatNumber(dvds)} standard single-layer DVDs."));
        }

        // 5. English Wikipedia Copies: ~22 GB compressed (23,622,320,128 bytes)
        if (bytes >= 1e9)
        {
            double wikis = bytes / 23622320128.0;
            list.Add(new(
                Label: "English Wikipedia Dumps",
                FormattedValue: $"{FormatNumber(wikis)} Wikipedias",
                Description: "Complete compressed text-only database dump of all English Wikipedia articles (~22 GB)",
                Category: "PopCulture",
                IsCommon: false,
                ConceptTitle: "📚 English Wikipedia Text Dumps",
                ConversationalSentence: $"{subject} represents {FormatNumber(wikis)} complete text dumps of English Wikipedia."));
        }
    }
}
