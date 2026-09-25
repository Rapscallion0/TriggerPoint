using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TriggerPoint.Core.Services;

public record AlternativeMeasurement(
    string Label,
    string FormattedValue,
    string Description,
    string Category = "Common",
    bool IsCommon = false,
    string? ConceptTitle = null,
    string? ConversationalSentence = null);

public record CalculatorResult(
    string Expression,
    string FormattedResult,
    string Description,
    IReadOnlyList<AlternativeMeasurement>? Alternatives = null,
    string? HierarchicalBreakdown = null,
    string? CumulativeBreakdown = null);

public static class QuickCalculatorService
{
    private static readonly Regex UnitConversionRegex = new(
        @"^\s*([0-9]+(?:\.[0-9]+)?)\s*(px|rem|pt|mb|gb|kb|tb|pb|b|c|f|k|miles?|mi|km|in|inches|cm|mm|m|meters?|ft|feet|lbs?|pounds?|kg|kilograms?|g|grams?|oz|ounces?|kmh|mph|mps|knot|knots?|ms|s|sec|seconds?|min|mins?|minutes?|h|hrs?|hours?|d|days?|w|weeks?)\s+(?:to|in)\s+(px|rem|pt|mb|gb|kb|tb|pb|b|c|f|k|miles?|mi|km|in|inches|cm|mm|m|meters?|ft|feet|lbs?|pounds?|kg|kilograms?|g|grams?|oz|ounces?|kmh|mph|mps|knot|knots?|ms|s|sec|seconds?|min|mins?|minutes?|h|hrs?|hours?|d|days?|w|weeks?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HexToRgbRegex = new(
        @"^\s*(?:hex\s+to\s+rgb\s+)?#?([0-9a-f]{3}|[0-9a-f]{6})\s*(?:to\s+rgb)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentageOfRegex = new(
        @"^\s*([0-9]+(?:\.[0-9]+)?)\s*%\s+of\s+([0-9]+(?:\.[0-9]+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentAddSubRegex = new(
        @"^\s*([0-9]+(?:\.[0-9]+)?)\s*([+\-])\s*([0-9]+(?:\.[0-9]+)?)\s*%\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentChangeRegex = new(
        @"^\s*([0-9]+(?:\.[0-9]+)?)\s+to\s+([0-9]+(?:\.[0-9]+)?)\s+as\s+%\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CalculatorResult? TryEvaluate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();

        // Strip "@calc" or "@calc " prefix
        if (trimmed.StartsWith("@calc", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[5..].TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }
        }

        bool explicitMath = trimmed.StartsWith('=');
        if (explicitMath)
        {
            trimmed = trimmed[1..].Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return null;
        }

        // 1. Standalone & Multi-Unit Date & Time Math
        var dateRes = EvaluateDateMath(trimmed);
        if (dateRes != null) return dateRes;

        // 2. Base Conversions (to hex, to bin, to dec)
        var baseRes = EvaluateBaseConversions(trimmed);
        if (baseRes != null) return baseRes;

        // 3. Programmer Math (Hex / Bitwise)
        var progRes = EvaluateProgrammerMath(trimmed);
        if (progRes != null) return progRes;

        // 4. Percentage Calculations
        var pctOfMatch = PercentageOfRegex.Match(trimmed);
        if (pctOfMatch.Success &&
            double.TryParse(pctOfMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) &&
            double.TryParse(pctOfMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double total))
        {
            double res = Math.Round((pct / 100.0) * total, 4);
            return new CalculatorResult(trimmed, FormatDouble(res), $"{pct}% of {total}");
        }

        var pctAddSubMatch = PercentAddSubRegex.Match(trimmed);
        if (pctAddSubMatch.Success &&
            double.TryParse(pctAddSubMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double baseVal) &&
            double.TryParse(pctAddSubMatch.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pctDelta))
        {
            string op = pctAddSubMatch.Groups[2].Value;
            double delta = (pctDelta / 100.0) * baseVal;
            double res = op == "+" ? baseVal + delta : baseVal - delta;
            res = Math.Round(res, 4);
            return new CalculatorResult(trimmed, FormatDouble(res), $"{baseVal} {op} {pctDelta}% ({op}{FormatDouble(delta)})");
        }

        var pctChangeMatch = PercentChangeRegex.Match(trimmed);
        if (pctChangeMatch.Success &&
            double.TryParse(pctChangeMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double fromVal) &&
            double.TryParse(pctChangeMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double toVal))
        {
            if (fromVal != 0)
            {
                double change = ((toVal - fromVal) / fromVal) * 100.0;
                string sign = change > 0 ? "+" : "";
                return new CalculatorResult(trimmed, $"{sign}{Math.Round(change, 2)}%", $"Percentage change from {fromVal} to {toVal}");
            }
        }

        // 5. Unit & Color Conversions
        var unitMatch = UnitConversionRegex.Match(trimmed);
        if (unitMatch.Success &&
            double.TryParse(unitMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            string fromUnit = NormalizeUnit(unitMatch.Groups[2].Value);
            string toUnit = NormalizeUnit(unitMatch.Groups[3].Value);

            var converted = ConvertUnits(val, fromUnit, toUnit);
            if (converted != null) return converted;
        }

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

        // 6. Scientific & Standard Math Evaluation
        return EvaluateMath(trimmed, explicitMath);
    }

    private static CalculatorResult? EvaluateDateMath(string input)
    {
        var lower = input.Trim().ToLowerInvariant();
        var now = DateTime.Now;

        // A. Standalone keywords
        if (lower is "now")
        {
            return new CalculatorResult("now", now.ToString("dddd, MMMM d, yyyy h:mm tt"), $"Current date and time • {now:yyyy-MM-dd HH:mm:ss}");
        }
        if (lower is "today")
        {
            var today = DateTime.Today;
            return new CalculatorResult("today", today.ToString("dddd, MMMM d, yyyy"), $"Current date • {today:yyyy-MM-dd}");
        }
        if (lower is "tomorrow")
        {
            var tomorrow = DateTime.Today.AddDays(1);
            return new CalculatorResult("tomorrow", tomorrow.ToString("dddd, MMMM d, yyyy"), $"Tomorrow's date • {tomorrow:yyyy-MM-dd}");
        }
        if (lower is "yesterday")
        {
            var yesterday = DateTime.Today.AddDays(-1);
            return new CalculatorResult("yesterday", yesterday.ToString("dddd, MMMM d, yyyy"), $"Yesterday's date • {yesterday:yyyy-MM-dd}");
        }

        // B. Unit-Flexible "Between" Calculations (hours between, days between, minutes between, etc.)
        var betweenRes = EvaluateBetween(input, now);
        if (betweenRes != null) return betweenRes;

        // C. Countdowns & Countups (days to, days until, days since, days from)
        var countdownRes = EvaluateCountdownAndCountup(input, now);
        if (countdownRes != null) return countdownRes;

        // D. Relative Nth Weekday (3rd friday from now, 2nd monday in the past)
        var nthWkdayRes = EvaluateRelativeNthWeekday(input, now);
        if (nthWkdayRes != null) return nthWkdayRes;

        // E. Ordinal Weekday in Month (second friday next June, first monday of next month)
        var ordMonthWkdayRes = EvaluateOrdinalWeekdayInMonth(input, now);
        if (ordMonthWkdayRes != null) return ordMonthWkdayRes;

        // F. Day of Year & Day of Month (25th day of 2020, 9th day of next June)
        var ordDayRes = EvaluateOrdinalDayOfYearOrMonth(input, now);
        if (ordDayRes != null) return ordDayRes;

        // G. Relative Weekdays (next friday, last friday, this monday)
        var relWkdayRes = EvaluateRelativeWeekday(input, now);
        if (relWkdayRes != null) return relWkdayRes;

        // H. Conversational Offsets (in 3 weeks, 5 days ago, end of month)
        var convOffsetRes = EvaluateNaturalOffsets(input, now);
        if (convOffsetRes != null) return convOffsetRes;

        // I. Standalone Holidays (Valentine's Day, Boxing Day, Easter, Thanksgiving)
        var holidayRes = EvaluateStandaloneHoliday(input, now);
        if (holidayRes != null) return holidayRes;

        // I. Unix / Epoch timestamp
        var unixMatch = Regex.Match(lower, @"^(?:unix|epoch)\s+([0-9]{9,12})$");
        if (unixMatch.Success && long.TryParse(unixMatch.Groups[1].Value, out long unixSeconds))
        {
            try
            {
                var dto = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                var local = dto.ToLocalTime();
                return new CalculatorResult(input, $"{local:yyyy-MM-dd HH:mm:ss} (Local)", $"UTC: {dto:yyyy-MM-dd HH:mm:ss}");
            }
            catch { }
        }

        // J. Multi-Unit Offsets: "now + 2 days, 3 hours and 26 minutes", "today + 14d", "now - 4h 30m"
        var offsetMatch = Regex.Match(lower, @"^(now|today|tomorrow|yesterday|[0-9]{4}-[0-9]{2}-[0-9]{2})\s*([+\-])\s*(.+)$");
        if (offsetMatch.Success)
        {
            string baseKeyword = offsetMatch.Groups[1].Value;
            string op = offsetMatch.Groups[2].Value;
            string offsetBody = offsetMatch.Groups[3].Value;

            DateTime baseDate = baseKeyword switch
            {
                "now" => DateTime.Now,
                "today" => DateTime.Today,
                "tomorrow" => DateTime.Today.AddDays(1),
                "yesterday" => DateTime.Today.AddDays(-1),
                _ => DateTime.TryParse(baseKeyword, out var dt) ? dt : DateTime.MinValue
            };

            if (baseDate == DateTime.MinValue) return null;

            bool isSubtract = op == "-";
            bool hasTimeComponent = baseKeyword == "now";

            // Extract tokens: "2 days", "3 hours", "26 minutes", "14d", "4h"
            var tokenMatches = Regex.Matches(offsetBody, @"([0-9]+(?:\.[0-9]+)?)\s*(years?|y|months?|mo|weeks?|w|days?|d|hours?|hrs?|hr|h|minutes?|mins?|min|m|seconds?|secs?|sec|s)");
            if (tokenMatches.Count == 0) return null;

            DateTime resultDate = baseDate;

            foreach (Match match in tokenMatches)
            {
                if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double count))
                    continue;

                if (isSubtract) count = -count;
                string u = match.Groups[2].Value.ToLowerInvariant();

                if (u is "y" or "year" or "years")
                {
                    resultDate = resultDate.AddYears((int)count);
                }
                else if (u is "mo" or "month" or "months")
                {
                    resultDate = resultDate.AddMonths((int)count);
                }
                else if (u is "w" or "week" or "weeks")
                {
                    resultDate = resultDate.AddDays(count * 7);
                }
                else if (u is "d" or "day" or "days")
                {
                    resultDate = resultDate.AddDays(count);
                }
                else if (u is "h" or "hr" or "hrs" or "hour" or "hours")
                {
                    hasTimeComponent = true;
                    resultDate = resultDate.AddHours(count);
                }
                else if (u is "m" or "min" or "mins" or "minute" or "minutes")
                {
                    hasTimeComponent = true;
                    resultDate = resultDate.AddMinutes(count);
                }
                else if (u is "s" or "sec" or "secs" or "second" or "seconds")
                {
                    hasTimeComponent = true;
                    resultDate = resultDate.AddSeconds(count);
                }
            }

            string formatted = hasTimeComponent
                ? resultDate.ToString("dddd, MMMM d, yyyy h:mm tt")
                : resultDate.ToString("dddd, MMMM d, yyyy");

            string desc = hasTimeComponent
                ? $"Date & Time calculation • {resultDate:yyyy-MM-dd HH:mm:ss}"
                : $"Date calculation • {resultDate:yyyy-MM-dd}";

            var span = (resultDate - baseDate).Duration();
            string hierarchical = FormatHierarchicalSpan(span);
            string cumulative = FormatCumulativeDaysSpan(span);
            var alternatives = GenerateDateSpanAlternatives(span, resultDate, input);
            return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
        }

        return null;
    }

    private static CalculatorResult? EvaluateCountdownAndCountup(string input, DateTime now)
    {
        var lower = input.Trim().ToLowerInvariant();

        bool isFuture = false;
        bool isPast = false;
        string targetStr = string.Empty;
        string unitReq = "days";

        var futureMatch = Regex.Match(lower, @"^(?:(?:from\s+now\s+(?:until|to))|(?:(minutes?|mins?|m|seconds?|secs?|s|hours?|hrs?|h|weeks?|w|days?|d|business\s+days?|work\s+days?|working\s+days?|time)\s+(?:to|until))|until|time\s+to)\s+(?<term>.+)$");
        if (futureMatch.Success)
        {
            isFuture = true;
            if (futureMatch.Groups[1].Success)
            {
                unitReq = futureMatch.Groups[1].Value.Trim();
            }
            targetStr = futureMatch.Groups["term"].Value.Trim();
        }
        else
        {
            var pastMatch = Regex.Match(lower, @"^(?:(?:(minutes?|mins?|m|seconds?|secs?|s|hours?|hrs?|h|weeks?|w|days?|d|business\s+days?|work\s+days?|working\s+days?|time)\s+(?:since|from))|since|time\s+since|time\s+from)\s+(?<term>.+)$");
            if (pastMatch.Success)
            {
                isPast = true;
                if (pastMatch.Groups[1].Success)
                {
                    unitReq = pastMatch.Groups[1].Value.Trim();
                }
                targetStr = pastMatch.Groups["term"].Value.Trim();
            }
        }

        if (!isFuture && !isPast) return null;

        var targetOpt = ParseFlexibleDateTime(targetStr, now, isPast ? -1 : 1);
        if (targetOpt == null) return null;

        var dt = targetOpt.Value;
        bool isSubDayUnit = unitReq is "minutes" or "minute" or "mins" or "min" or "m"
                            or "seconds" or "second" or "secs" or "sec" or "s"
                            or "hours" or "hour" or "hrs" or "hr" or "h"
                            or "time";
        bool hasTimeComponent = IsTimeString(targetStr) || (dt.TimeOfDay != TimeSpan.Zero);

        TimeSpan span = (isSubDayUnit || hasTimeComponent)
            ? (dt - now).Duration()
            : (dt.Date - now.Date).Duration();

        string primary;
        if (unitReq is "minutes" or "minute" or "mins" or "min" or "m")
        {
            long mins = (long)Math.Round(span.TotalMinutes);
            primary = $"{mins:N0} minute{(mins == 1 ? "" : "s")}";
        }
        else if (unitReq is "seconds" or "second" or "secs" or "sec" or "s")
        {
            long secs = (long)Math.Round(span.TotalSeconds);
            primary = $"{secs:N0} second{(secs == 1 ? "" : "s")}";
        }
        else if (unitReq is "hours" or "hour" or "hrs" or "hr" or "h")
        {
            double hrs = Math.Round(span.TotalHours, 1);
            primary = $"{FormatDouble(hrs)} hour{(Math.Abs(hrs - 1.0) < 0.05 ? "" : "s")}";
        }
        else if (unitReq is "weeks" or "week" or "w")
        {
            double wks = Math.Round(span.TotalDays / 7.0, 1);
            primary = $"{FormatDouble(wks)} week{(Math.Abs(wks - 1.0) < 0.05 ? "" : "s")}";
        }
        else if (unitReq.Contains("business") || unitReq.Contains("work"))
        {
            int bus = CalculateBusinessDays(now.Date < dt.Date ? now.Date : dt.Date, now.Date < dt.Date ? dt.Date : now.Date);
            primary = $"{bus} business day{(bus == 1 ? "" : "s")}";
        }
        else if (unitReq is "time")
        {
            primary = FormatHierarchicalSpan(span, compact: true);
        }
        else
        {
            int days = (int)Math.Ceiling(span.TotalDays);
            primary = $"{days} day{(days == 1 ? "" : "s")}";
        }

        string hierarchical = FormatHierarchicalSpan(span);
        string cumulative = FormatCumulativeDaysSpan(span);
        string targetFormatted = hasTimeComponent
            ? dt.ToString("dddd, MMMM d, yyyy h:mm tt")
            : dt.ToString("dddd, MMMM d, yyyy");

        string desc = isFuture
            ? $"{hierarchical} until {targetFormatted}"
            : $"{hierarchical} since {targetFormatted}";

        var alternatives = GenerateDateSpanAlternatives(span, dt, input);
        return new CalculatorResult(input, primary, desc, alternatives, hierarchical, cumulative);
    }

    private static CalculatorResult? EvaluateBetween(string input, DateTime now)
    {
        var lower = input.Trim().ToLowerInvariant();
        var match = Regex.Match(lower, @"^(?:(days?|hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s|weeks?|w|business\s+days?|work\s+days?|working\s+days?|time)\s+between\s+|difference\s+between\s+|between\s+)(.+?)\s+(?:and|to)\s+(.+)$");
        if (!match.Success) return null;

        string unitReq = match.Groups[1].Success ? match.Groups[1].Value.ToLowerInvariant() : "days";
        string leftStr = match.Groups[2].Value.Trim();
        string rightStr = match.Groups[3].Value.Trim();

        var startOpt = ParseFlexibleDateTime(leftStr, now, 0);
        var endOpt = ParseFlexibleDateTime(rightStr, now, 0);
        if (startOpt == null || endOpt == null) return null;

        var start = startOpt.Value;
        var end = endOpt.Value;

        // Check for overnight times on same day
        bool startHasTime = IsTimeString(leftStr);
        bool endHasTime = IsTimeString(rightStr);
        if (startHasTime && endHasTime && start.Date == end.Date && end < start)
        {
            end = end.AddDays(1);
        }

        var span = (end - start).Duration();
        var targetDt = end > start ? end : start;

        string primary;
        string desc;

        if (unitReq is "hours" or "hour" or "hrs" or "hr" or "h")
        {
            double hrs = Math.Round(span.TotalHours, 1);
            primary = $"{FormatDouble(hrs)} hour{(hrs == 1 ? "" : "s")}";
            desc = $"{(int)Math.Floor(span.TotalHours)} hours, {span.Minutes} minutes • {span.TotalMinutes:N0} minutes";
        }
        else if (unitReq is "minutes" or "minute" or "mins" or "min" or "m")
        {
            long mins = (long)Math.Round(span.TotalMinutes);
            primary = $"{mins:N0} minute{(mins == 1 ? "" : "s")}";
            desc = $"{(int)Math.Floor(span.TotalHours)} hours, {span.Minutes} minutes";
        }
        else if (unitReq is "seconds" or "second" or "secs" or "sec" or "s")
        {
            long secs = (long)Math.Round(span.TotalSeconds);
            primary = $"{secs:N0} second{(secs == 1 ? "" : "s")}";
            desc = $"{span.TotalMinutes:N1} minutes • {span.TotalHours:N2} hours";
        }
        else if (unitReq is "weeks" or "week" or "w")
        {
            double wks = Math.Round(span.TotalDays / 7.0, 1);
            primary = $"{FormatDouble(wks)} week{(wks == 1 ? "" : "s")}";
            desc = $"{(int)Math.Ceiling(span.TotalDays)} days • {CalculateBusinessDays(start, end)} business days";
        }
        else if (unitReq.Contains("business") || unitReq.Contains("work"))
        {
            int busDays = CalculateBusinessDays(start, end);
            primary = $"{busDays} business day{(busDays == 1 ? "" : "s")}";
            desc = $"Between {start:yyyy-MM-dd} and {end:yyyy-MM-dd} (excludes weekends)";
        }
        else if (unitReq is "time")
        {
            primary = FormatCompositeSpan(span);
            desc = $"Between {start:yyyy-MM-dd HH:mm} and {end:yyyy-MM-dd HH:mm}";
        }
        else
        {
            int days = (int)Math.Ceiling(span.TotalDays);
            primary = $"{days} day{(days == 1 ? "" : "s")}";
            desc = $"Between {start:yyyy-MM-dd} and {end:yyyy-MM-dd} • {Math.Round(days / 7.0, 1):N1} weeks";
        }

        string hierarchical = FormatHierarchicalSpan(span);
        string cumulative = FormatCumulativeDaysSpan(span);
        var alternatives = GenerateDateSpanAlternatives(span, targetDt, input);
        return new CalculatorResult(input, primary, desc, alternatives, hierarchical, cumulative);
    }

    private static CalculatorResult? EvaluateRelativeNthWeekday(string input, DateTime now)
    {
        var lower = input.Trim().ToLowerInvariant();
        var match = Regex.Match(lower, @"^(?:the\s+)?(\d+(?:st|nd|rd|th)?|[a-z]+)\s+(mondays?|tuesdays?|wednesdays?|thursdays?|fridays?|saturdays?|sundays?|mons?|tues?|weds?|thurs?|thu?|fris?|sats?|suns?)\s+(from\s+now|from\s+today|in\s+(?:the\s+)?future|in\s+(?:the\s+)?past|ago)$");
        if (!match.Success) return null;

        int n = ParseOrdinal(match.Groups[1].Value);
        if (n <= 0) return null;

        var dowOpt = ParseDayOfWeek(match.Groups[2].Value);
        if (dowOpt == null) return null;
        var dow = dowOpt.Value;

        string dir = match.Groups[3].Value;
        bool isForward = dir.Contains("now") || dir.Contains("today") || dir.Contains("future");

        var cur = now.Date;
        int count = 0;
        DateTime result = cur;

        while (count < n)
        {
            cur = isForward ? cur.AddDays(1) : cur.AddDays(-1);
            if (cur.DayOfWeek == dow)
            {
                count++;
                if (count == n)
                {
                    result = cur;
                    break;
                }
            }
        }

        string formatted = result.ToString("dddd, MMMM d, yyyy");
        var span = (result - now.Date).Duration();
        string hierarchical = FormatHierarchicalSpan(span);
        string cumulative = FormatCumulativeDaysSpan(span);
        string desc = isForward
            ? $"{result:yyyy-MM-dd} • {hierarchical} from now"
            : $"{result:yyyy-MM-dd} • {hierarchical} in the past";
        var alternatives = GenerateDateSpanAlternatives(span, result, input);

        return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
    }

    private static CalculatorResult? EvaluateOrdinalWeekdayInMonth(string input, DateTime now)
    {
        var lower = input.Trim().ToLowerInvariant();
        var match = Regex.Match(lower, @"^(?:the\s+)?(\d+(?:st|nd|rd|th)?|[a-z]+)\s+(mondays?|tuesdays?|wednesdays?|thursdays?|fridays?|saturdays?|sundays?|mons?|tues?|weds?|thurs?|thu?|fris?|sats?|suns?)\s+(?:of|in|next)?\s*(this\s+month|next\s+month|next\s+[a-z]+|[a-z]+(?:\s+[0-9]{4})?)$");
        if (!match.Success) return null;

        int ord = ParseOrdinal(match.Groups[1].Value);
        if (ord == 0) return null;

        var dowOpt = ParseDayOfWeek(match.Groups[2].Value);
        if (dowOpt == null) return null;
        var dow = dowOpt.Value;

        string targetMonthStr = match.Groups[3].Value.Trim();
        var (targetYear, targetMonth) = ResolveMonthAndYear(targetMonthStr, now);
        if (targetMonth == 0) return null;

        var date = GetNthWeekdayOfMonth(targetYear, targetMonth, dow, ord);
        if (date == null) return null;

        var dt = date.Value;
        string ordName = ord == -1 ? "Last" : FormatOrdinal(ord);
        string formatted = dt.ToString("dddd, MMMM d, yyyy");
        string desc = $"{ordName} {dow} of {dt:MMMM yyyy} • {dt:yyyy-MM-dd}";

        var span = (dt.Date - now.Date).Duration();
        string hierarchical = FormatHierarchicalSpan(span);
        string cumulative = FormatCumulativeDaysSpan(span);
        var alternatives = GenerateDateSpanAlternatives(span, dt, input);
        return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
    }

    private static CalculatorResult? EvaluateOrdinalDayOfYearOrMonth(string input, DateTime now)
    {
        var lower = input.Trim().ToLowerInvariant();
        var match = Regex.Match(lower, @"^(?:the\s+)?(\d+(?:st|nd|rd|th)?|[a-z]+)\s+day\s+of\s+(.+)$");
        if (!match.Success) return null;

        int ord = ParseOrdinal(match.Groups[1].Value);
        if (ord <= 0) return null;

        string target = match.Groups[2].Value.Trim();

        // 1. Day of Year (e.g. "2020", "2026", "this year", "next year", "last year")
        int targetYear = 0;
        if (int.TryParse(target, out int yr) && yr is >= 1900 and <= 2100)
        {
            targetYear = yr;
        }
        else if (target is "this year" or "year")
        {
            targetYear = now.Year;
        }
        else if (target is "next year")
        {
            targetYear = now.Year + 1;
        }
        else if (target is "last year")
        {
            targetYear = now.Year - 1;
        }

        if (targetYear > 0)
        {
            bool isLeap = DateTime.IsLeapYear(targetYear);
            int maxDays = isLeap ? 366 : 365;
            if (ord > maxDays) return null;

            var dt = new DateTime(targetYear, 1, 1).AddDays(ord - 1);
            string formatted = dt.ToString("dddd, MMMM d, yyyy");
            string desc = $"{FormatOrdinal(ord)} day of {targetYear} • {dt:yyyy-MM-dd}";
            var span = (dt.Date - now.Date).Duration();
            string hierarchical = FormatHierarchicalSpan(span);
            string cumulative = FormatCumulativeDaysSpan(span);
            var alternatives = GenerateDateSpanAlternatives(span, dt, input);
            return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
        }

        // 2. Day of Month (e.g. "next June", "December", "this month", "next month")
        var (mYear, mMonth) = ResolveMonthAndYear(target, now);
        if (mMonth > 0)
        {
            int maxDays = DateTime.DaysInMonth(mYear, mMonth);
            if (ord > maxDays) return null;

            var dt = new DateTime(mYear, mMonth, ord);
            string formatted = dt.ToString("dddd, MMMM d, yyyy");
            string desc = $"{FormatOrdinal(ord)} day of {dt:MMMM yyyy} • {dt:yyyy-MM-dd}";
            var span = (dt.Date - now.Date).Duration();
            string hierarchical = FormatHierarchicalSpan(span);
            string cumulative = FormatCumulativeDaysSpan(span);
            var alternatives = GenerateDateSpanAlternatives(span, dt, input);
            return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
        }

        return null;
    }

    private static CalculatorResult? EvaluateRelativeWeekday(string input, DateTime now)
    {
        var lower = input.Trim().ToLowerInvariant();
        var match = Regex.Match(lower, @"^(next|last|this|past|previous)\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tue|wed|thu|fri|sat|sun)$");
        if (!match.Success) return null;

        string modifier = match.Groups[1].Value;
        var dowOpt = ParseDayOfWeek(match.Groups[2].Value);
        if (dowOpt == null) return null;
        var dow = dowOpt.Value;

        var today = now.Date;
        DateTime result;

        if (modifier is "next")
        {
            result = today.AddDays(1);
            while (result.DayOfWeek != dow)
            {
                result = result.AddDays(1);
            }
        }
        else if (modifier is "last" or "past" or "previous")
        {
            result = today.AddDays(-1);
            while (result.DayOfWeek != dow)
            {
                result = result.AddDays(-1);
            }
        }
        else // "this"
        {
            result = today;
            if (result.DayOfWeek != dow)
            {
                result = today.AddDays(1);
                while (result.DayOfWeek != dow)
                {
                    result = result.AddDays(1);
                }
            }
        }

        string formatted = result.ToString("dddd, MMMM d, yyyy");
        var span = (result - today).Duration();
        string hierarchical = FormatHierarchicalSpan(span);
        string cumulative = FormatCumulativeDaysSpan(span);
        bool isFuture = result > today;
        string desc = isFuture
            ? $"{result:yyyy-MM-dd} • {hierarchical} from now"
            : (result < today ? $"{result:yyyy-MM-dd} • {hierarchical} ago" : $"{result:yyyy-MM-dd} • Today");
        var alternatives = GenerateDateSpanAlternatives(span, result, input);
        return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
    }

    private static CalculatorResult? EvaluateNaturalOffsets(string input, DateTime now)
    {
        var lower = input.Trim().ToLowerInvariant();

        // 1. "in 3 weeks", "in 5 days", "5 days from now", "3 weeks ago", "two days from now"
        var inMatch = Regex.Match(lower, @"^(?:in\s+)?([0-9]+(?:\.[0-9]+)?|[a-z]+)\s*(years?|y|months?|mo|weeks?|w|days?|d|hours?|hrs?|hr|h|minutes?|mins?|min|m|seconds?|secs?|sec|s)(?:\s+(?:from\s+now|from\s+today))?$");
        var agoMatch = Regex.Match(lower, @"^([0-9]+(?:\.[0-9]+)?|[a-z]+)\s*(years?|y|months?|mo|weeks?|w|days?|d|hours?|hrs?|hr|h|minutes?|mins?|min|m|seconds?|secs?|sec|s)\s+ago$");

        bool isFuture = inMatch.Success;
        bool isPast = agoMatch.Success;
        Match m = isFuture ? inMatch : agoMatch;

        if (isFuture || isPast)
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double qty) ||
                (qty = ParseOrdinal(m.Groups[1].Value)) > 0)
            {
                if (isPast) qty = -qty;
                string u = m.Groups[2].Value;
                bool hasTime = u is "h" or "hr" or "hrs" or "hour" or "hours" or "m" or "min" or "mins" or "minute" or "minutes" or "s" or "sec" or "secs" or "second" or "seconds";
                DateTime dt = hasTime ? now : now.Date;

                if (u is "y" or "year" or "years") dt = dt.AddYears((int)qty);
                else if (u is "mo" or "month" or "months") dt = dt.AddMonths((int)qty);
                else if (u is "w" or "week" or "weeks") dt = dt.AddDays(qty * 7);
                else if (u is "d" or "day" or "days") dt = dt.AddDays(qty);
                else if (u is "h" or "hr" or "hrs" or "hour" or "hours") dt = dt.AddHours(qty);
                else if (u is "m" or "min" or "mins" or "minute" or "minutes") dt = dt.AddMinutes(qty);
                else if (u is "s" or "sec" or "secs" or "second" or "seconds") dt = dt.AddSeconds(qty);

                string formatted = hasTime ? dt.ToString("dddd, MMMM d, yyyy h:mm tt") : dt.ToString("dddd, MMMM d, yyyy");
                string desc = hasTime ? $"Date & Time • {dt:yyyy-MM-dd HH:mm:ss}" : $"Date • {dt:yyyy-MM-dd}";
                var span = (dt - now).Duration();
                string hierarchical = FormatHierarchicalSpan(span);
                string cumulative = FormatCumulativeDaysSpan(span);
                var alternatives = GenerateDateSpanAlternatives(span, dt, input);
                return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
            }
        }

        // 2. Calendar Landmarks: "end of month", "start of next month", "end of year", "end of quarter"
        var landmarkMatch = Regex.Match(lower, @"^(end|start)\s+of\s+(this\s+month|next\s+month|month|this\s+year|next\s+year|year|this\s+quarter|next\s+quarter|quarter|this\s+week|next\s+week|week)$");
        if (landmarkMatch.Success)
        {
            string type = landmarkMatch.Groups[1].Value;
            string target = landmarkMatch.Groups[2].Value;
            DateTime res = DateTime.MinValue;

            if (target.Contains("month"))
            {
                int y = now.Year;
                int month = target.Contains("next") ? now.Month + 1 : now.Month;
                if (month > 12) { month = 1; y++; }
                res = type == "start" ? new DateTime(y, month, 1) : new DateTime(y, month, DateTime.DaysInMonth(y, month));
            }
            else if (target.Contains("year"))
            {
                int y = target.Contains("next") ? now.Year + 1 : now.Year;
                res = type == "start" ? new DateTime(y, 1, 1) : new DateTime(y, 12, 31);
            }
            else if (target.Contains("quarter"))
            {
                int currentQ = (now.Month - 1) / 3 + 1;
                int targetQ = target.Contains("next") ? currentQ + 1 : currentQ;
                int targetY = now.Year;
                if (targetQ > 4) { targetQ = 1; targetY++; }
                int startMonth = (targetQ - 1) * 3 + 1;
                int endMonth = startMonth + 2;
                res = type == "start" ? new DateTime(targetY, startMonth, 1) : new DateTime(targetY, endMonth, DateTime.DaysInMonth(targetY, endMonth));
            }
            else if (target.Contains("week"))
            {
                var cur = now.Date;
                if (target.Contains("next")) cur = cur.AddDays(7);
                int diff = (7 + (cur.DayOfWeek - DayOfWeek.Monday)) % 7;
                var mon = cur.AddDays(-1 * diff);
                res = type == "start" ? mon : mon.AddDays(6);
            }

            if (res != DateTime.MinValue)
            {
                string formatted = res.ToString("dddd, MMMM d, yyyy");
                string desc = $"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(type)} of {target} • {res:yyyy-MM-dd}";
                var span = (res - now.Date).Duration();
                string hierarchical = FormatHierarchicalSpan(span);
                string cumulative = FormatCumulativeDaysSpan(span);
                var alternatives = GenerateDateSpanAlternatives(span, res, input);
                return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
            }
        }

        return null;
    }

    public static int CalculateBusinessDays(DateTime start, DateTime end)
    {
        if (start > end) (start, end) = (end, start);
        int businessDays = 0;
        var current = start.Date;
        var target = end.Date;
        while (current < target)
        {
            current = current.AddDays(1);
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                businessDays++;
            }
        }
        return businessDays;
    }

    private static string FormatCompositeSpan(TimeSpan span)
    {
        var parts = new List<string>();
        if (span.Days > 0) parts.Add($"{span.Days} day{(span.Days == 1 ? "" : "s")}");
        if (span.Hours > 0) parts.Add($"{span.Hours} hour{(span.Hours == 1 ? "" : "s")}");
        if (span.Minutes > 0) parts.Add($"{span.Minutes} minute{(span.Minutes == 1 ? "" : "s")}");
        if (parts.Count == 0 && span.Seconds > 0) parts.Add($"{span.Seconds} second{(span.Seconds == 1 ? "" : "s")}");
        return parts.Count > 0 ? string.Join(", ", parts) : "0 seconds";
    }

    private static bool IsTimeString(string str)
    {
        return Regex.IsMatch(str.Trim(), @"^(?:[0-2]?[0-9](?::[0-5][0-9])?\s*(?:am|pm)|[0-2][0-9]:[0-5][0-9]|noon|midnight)$", RegexOptions.IgnoreCase);
    }

    private static DateTime? ParseFlexibleDateTime(string expr, DateTime now, int directionHint = 0)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        var trimmed = expr.Trim();
        var lower = trimmed.ToLowerInvariant();

        // 1. Direct keywords
        if (lower is "now") return now;
        if (lower is "today") return now.Date;
        if (lower is "tomorrow") return now.Date.AddDays(1);
        if (lower is "yesterday") return now.Date.AddDays(-1);

        // 2. Pure Time (e.g. 9am, 5:30pm, 14:00, noon, midnight)
        if (lower is "noon") return now.Date.AddHours(12);
        if (lower is "midnight") return now.Date.AddHours(0);
        var timeMatch = Regex.Match(lower, @"^([0-2]?[0-9])(?::([0-5][0-9]))?\s*(am|pm)?$");
        if (timeMatch.Success)
        {
            int h = int.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            int m = timeMatch.Groups[2].Success ? int.Parse(timeMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            string ampm = timeMatch.Groups[3].Value;
            if (ampm == "pm" && h < 12) h += 12;
            if (ampm == "am" && h == 12) h = 0;
            if (h <= 23 && m <= 59)
            {
                return now.Date.AddHours(h).AddMinutes(m);
            }
        }

        // 3. Named Holidays
        var holidayDate = EvaluateHolidayDate(trimmed, now.Year);
        if (holidayDate != null)
        {
            var hDate = holidayDate.Value;
            if (directionHint > 0 && hDate < now.Date)
            {
                // Roll forward to next year
                var nextYearHoliday = EvaluateHolidayDate(trimmed, now.Year + 1);
                if (nextYearHoliday != null) return nextYearHoliday;
            }
            else if (directionHint < 0 && hDate > now.Date)
            {
                // Roll backward to previous year
                var prevYearHoliday = EvaluateHolidayDate(trimmed, now.Year - 1);
                if (prevYearHoliday != null) return prevYearHoliday;
            }
            return hDate;
        }

        // 4. Relative Weekday (next friday, last monday)
        var relWk = EvaluateRelativeWeekday(trimmed, now);
        if (relWk != null && DateTime.TryParse(relWk.Description.Split('•').LastOrDefault()?.Trim(), out var relDt))
        {
            return relDt;
        }

        // 5. Standard DateTime.TryParse
        if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsedDt) ||
            DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDt))
        {
            // If year was not explicitly supplied, check directionHint
            bool hasExplicitYear = Regex.IsMatch(trimmed, @"\b(?:19|20)\d{2}\b");
            if (!hasExplicitYear)
            {
                if (directionHint > 0 && parsedDt.Date < now.Date)
                {
                    parsedDt = parsedDt.AddYears(1);
                }
                else if (directionHint < 0 && parsedDt.Date > now.Date)
                {
                    parsedDt = parsedDt.AddYears(-1);
                }
            }
            return parsedDt;
        }

        return null;
    }

    private static CalculatorResult? EvaluateStandaloneHoliday(string input, DateTime now)
    {
        var date = EvaluateHolidayDate(input, now.Year);
        if (date == null) return null;

        var target = date.Value;
        if (target < now.Date)
        {
            var nextYearDate = EvaluateHolidayDate(input, now.Year + 1);
            if (nextYearDate != null) target = nextYearDate.Value;
        }

        string formatted = target.ToString("dddd, MMMM d, yyyy");
        int days = (int)(target.Date - now.Date).TotalDays;
        var span = (target.Date - now.Date).Duration();
        string hierarchical = FormatHierarchicalSpan(span);
        string cumulative = FormatCumulativeDaysSpan(span);
        string desc = days == 0
            ? $"{input.Trim()} is today!"
            : (days > 0 ? $"{hierarchical} until {input.Trim()} • {target:yyyy-MM-dd}" : $"{hierarchical} since {input.Trim()} • {target:yyyy-MM-dd}");

        var alternatives = GenerateDateSpanAlternatives(span, target, input);
        return new CalculatorResult(input, formatted, desc, alternatives, hierarchical, cumulative);
    }

    public static DateTime? EvaluateHolidayDate(string holidayName, int targetYear)
    {
        var lower = holidayName.Trim().ToLowerInvariant()
            .Replace("'", "").Replace("’", "").Replace("‘", "");

        // Fixed-date holidays
        if (lower is "new years day" or "new years" or "new year")
            return new DateTime(targetYear, 1, 1);
        if (lower is "valentines day" or "valentines" or "valentine")
            return new DateTime(targetYear, 2, 14);
        if (lower is "st patricks day" or "st paddys day" or "st patricks" or "st patrick")
            return new DateTime(targetYear, 3, 17);
        if (lower is "earth day")
            return new DateTime(targetYear, 4, 22);
        if (lower is "cinco de mayo")
            return new DateTime(targetYear, 5, 5);
        if (lower is "juneteenth")
            return new DateTime(targetYear, 6, 19);
        if (lower is "canada day")
            return new DateTime(targetYear, 7, 1);
        if (lower is "independence day" or "4th of july" or "fourth of july" or "july 4th" or "july 4")
            return new DateTime(targetYear, 7, 4);
        if (lower is "halloween")
            return new DateTime(targetYear, 10, 31);
        if (lower is "veterans day" or "remembrance day" or "veterans")
            return new DateTime(targetYear, 11, 11);
        if (lower is "christmas eve")
            return new DateTime(targetYear, 12, 24);
        if (lower is "christmas" or "christmas day" or "xmas")
            return new DateTime(targetYear, 12, 25);
        if (lower is "boxing day")
            return new DateTime(targetYear, 12, 26);
        if (lower is "new years eve")
            return new DateTime(targetYear, 12, 31);

        // Floating holidays
        if (lower is "mlk day" or "martin luther king day" or "martin luther king jr day")
            return GetNthWeekdayOfMonth(targetYear, 1, DayOfWeek.Monday, 3);
        if (lower is "presidents day" or "presidents")
            return GetNthWeekdayOfMonth(targetYear, 2, DayOfWeek.Monday, 3);
        if (lower is "mothers day" or "mother day")
            return GetNthWeekdayOfMonth(targetYear, 5, DayOfWeek.Sunday, 2);
        if (lower is "memorial day")
            return GetNthWeekdayOfMonth(targetYear, 5, DayOfWeek.Monday, -1);
        if (lower is "fathers day" or "father day")
            return GetNthWeekdayOfMonth(targetYear, 6, DayOfWeek.Sunday, 3);
        if (lower is "labor day" or "labour day")
            return GetNthWeekdayOfMonth(targetYear, 9, DayOfWeek.Monday, 1);
        if (lower is "columbus day" or "indigenous peoples day")
            return GetNthWeekdayOfMonth(targetYear, 10, DayOfWeek.Monday, 2);
        if (lower is "canadian thanksgiving")
            return GetNthWeekdayOfMonth(targetYear, 10, DayOfWeek.Monday, 2);
        if (lower is "thanksgiving" or "thanksgiving day" or "us thanksgiving")
            return GetNthWeekdayOfMonth(targetYear, 11, DayOfWeek.Thursday, 4);
        if (lower is "black friday")
        {
            var tg = GetNthWeekdayOfMonth(targetYear, 11, DayOfWeek.Thursday, 4);
            return tg?.AddDays(1);
        }
        if (lower is "easter" or "easter sunday")
            return CalculateEasterSunday(targetYear);

        return null;
    }

    private static DateTime CalculateEasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateTime(year, month, day);
    }

    private static DateTime? GetNthWeekdayOfMonth(int year, int month, DayOfWeek dow, int ordinal)
    {
        if (ordinal == 0) return null;
        int daysInMonth = DateTime.DaysInMonth(year, month);

        if (ordinal > 0)
        {
            int count = 0;
            for (int d = 1; d <= daysInMonth; d++)
            {
                var dt = new DateTime(year, month, d);
                if (dt.DayOfWeek == dow)
                {
                    count++;
                    if (count == ordinal) return dt;
                }
            }
            return null;
        }
        else // -1 means Last
        {
            for (int d = daysInMonth; d >= 1; d--)
            {
                var dt = new DateTime(year, month, d);
                if (dt.DayOfWeek == dow) return dt;
            }
            return null;
        }
    }

    private static (int Year, int Month) ResolveMonthAndYear(string text, DateTime now)
    {
        var lower = text.Trim().ToLowerInvariant();
        if (lower is "this month" or "month") return (now.Year, now.Month);
        if (lower is "next month")
        {
            int m = now.Month + 1;
            int y = now.Year;
            if (m > 12) { m = 1; y++; }
            return (y, m);
        }

        bool isNext = lower.StartsWith("next ");
        string clean = isNext ? lower[5..].Trim() : lower;

        // Check for month with year (e.g. "June 2027")
        var myMatch = Regex.Match(clean, @"^([a-z]+)\s+([0-9]{4})$");
        if (myMatch.Success)
        {
            int yr = int.Parse(myMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            int m = ParseMonth(myMatch.Groups[1].Value);
            return (yr, m);
        }

        int targetM = ParseMonth(clean);
        if (targetM > 0)
        {
            int yr = now.Year;
            if (isNext)
            {
                yr = targetM > now.Month ? now.Year : now.Year + 1;
            }
            else if (targetM < now.Month)
            {
                yr = now.Year + 1;
            }
            return (yr, targetM);
        }

        return (0, 0);
    }

    private static int ParseMonth(string monthStr)
    {
        return monthStr.ToLowerInvariant() switch
        {
            "january" or "jan" => 1,
            "february" or "feb" => 2,
            "march" or "mar" => 3,
            "april" or "apr" => 4,
            "may" => 5,
            "june" or "jun" => 6,
            "july" or "jul" => 7,
            "august" or "aug" => 8,
            "september" or "sep" or "sept" => 9,
            "october" or "oct" => 10,
            "november" or "nov" => 11,
            "december" or "dec" => 12,
            _ => 0
        };
    }

    private static DayOfWeek? ParseDayOfWeek(string text)
    {
        return text.ToLowerInvariant().Trim() switch
        {
            "monday" or "mondays" or "mon" or "mons" => DayOfWeek.Monday,
            "tuesday" or "tuesdays" or "tue" or "tues" => DayOfWeek.Tuesday,
            "wednesday" or "wednesdays" or "wed" or "weds" => DayOfWeek.Wednesday,
            "thursday" or "thursdays" or "thu" or "thur" or "thurs" => DayOfWeek.Thursday,
            "friday" or "fridays" or "fri" or "fris" => DayOfWeek.Friday,
            "saturday" or "saturdays" or "sat" or "sats" => DayOfWeek.Saturday,
            "sunday" or "sundays" or "sun" or "suns" => DayOfWeek.Sunday,
            _ => null
        };
    }

    private static int ParseOrdinal(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        if (lower == "last") return -1;

        var numMatch = Regex.Match(lower, @"^([0-9]+)(?:st|nd|rd|th)?$");
        if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int n))
        {
            return n;
        }

        return lower switch
        {
            "one" or "first" or "1st" => 1,
            "two" or "second" or "2nd" => 2,
            "three" or "third" or "3rd" => 3,
            "four" or "fourth" or "4th" => 4,
            "five" or "fifth" or "5th" => 5,
            "six" or "sixth" or "6th" => 6,
            "seven" or "seventh" or "7th" => 7,
            "eight" or "eighth" or "8th" => 8,
            "nine" or "ninth" or "9th" => 9,
            "ten" or "tenth" or "10th" => 10,
            "eleven" or "eleventh" or "11th" => 11,
            "twelve" or "twelfth" or "12th" => 12,
            "thirteen" or "thirteenth" or "13th" => 13,
            "fourteen" or "fourteenth" or "14th" => 14,
            "fifteen" or "fifteenth" or "15th" => 15,
            "sixteen" or "sixteenth" or "16th" => 16,
            "seventeen" or "seventeenth" or "17th" => 17,
            "eighteen" or "eighteenth" or "18th" => 18,
            "nineteen" or "nineteenth" or "19th" => 19,
            "twenty" or "twentieth" or "20th" => 20,
            "twenty-one" or "twenty one" or "twenty-first" or "twenty first" or "21st" => 21,
            "twenty-two" or "twenty two" or "twenty-second" or "twenty second" or "22nd" => 22,
            "twenty-three" or "twenty three" or "twenty-third" or "twenty third" or "23rd" => 23,
            "twenty-four" or "twenty four" or "twenty-fourth" or "twenty fourth" or "24th" => 24,
            "twenty-five" or "twenty five" or "twenty-fifth" or "twenty fifth" or "25th" => 25,
            "twenty-six" or "twenty six" or "twenty-sixth" or "twenty sixth" or "26th" => 26,
            "twenty-seven" or "twenty seven" or "twenty-seventh" or "twenty seventh" or "27th" => 27,
            "twenty-eight" or "twenty eight" or "twenty-eighth" or "twenty eighth" or "28th" => 28,
            "twenty-nine" or "twenty nine" or "twenty-ninth" or "twenty ninth" or "29th" => 29,
            "thirty" or "thirtieth" or "30th" => 30,
            "thirty-one" or "thirty one" or "thirty-first" or "thirty first" or "31st" => 31,
            _ => 0
        };
    }

    private static string FormatOrdinal(int n)
    {
        if (n <= 0) return n.ToString();
        int mod100 = n % 100;
        if (mod100 is 11 or 12 or 13) return $"{n}th";
        return (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th"
        };
    }

    public static string FormatHierarchicalSpan(TimeSpan span, bool compact = false)
    {
        int totalDays = (int)Math.Floor(span.TotalDays);
        int weeks = totalDays / 7;
        int days = totalDays % 7;
        int hours = span.Hours;
        int minutes = span.Minutes;
        int seconds = span.Seconds;

        var parts = new List<string>();
        if (compact)
        {
            if (weeks > 0) parts.Add($"{weeks}w");
            if (days > 0) parts.Add($"{days}d");
            if (hours > 0) parts.Add($"{hours}h");
            if (minutes > 0) parts.Add($"{minutes}m");
            if (parts.Count == 0 && seconds > 0) parts.Add($"{seconds}s");
            return parts.Count > 0 ? string.Join(" ", parts) : "0s";
        }

        if (weeks > 0) parts.Add($"{weeks} week{(weeks == 1 ? "" : "s")}");
        if (days > 0) parts.Add($"{days} day{(days == 1 ? "" : "s")}");
        if (hours > 0) parts.Add($"{hours} hour{(hours == 1 ? "" : "s")}");
        if (minutes > 0) parts.Add($"{minutes} minute{(minutes == 1 ? "" : "s")}");
        if (seconds > 0 || parts.Count == 0) parts.Add($"{seconds} second{(seconds == 1 ? "" : "s")}");

        if (parts.Count == 1) return parts[0];
        if (parts.Count == 2) return $"{parts[0]} and {parts[1]}";
        return $"{string.Join(", ", parts.Take(parts.Count - 1))}, and {parts.Last()}";
    }

    public static string FormatCumulativeDaysSpan(TimeSpan span, bool compact = false)
    {
        int days = (int)Math.Floor(span.TotalDays);
        int hours = span.Hours;
        int minutes = span.Minutes;
        int seconds = span.Seconds;

        var parts = new List<string>();
        if (compact)
        {
            if (days > 0) parts.Add($"{days}d");
            if (hours > 0) parts.Add($"{hours}h");
            if (minutes > 0) parts.Add($"{minutes}m");
            if (parts.Count == 0 && seconds > 0) parts.Add($"{seconds}s");
            return parts.Count > 0 ? string.Join(" ", parts) : "0s";
        }

        if (days > 0) parts.Add($"{days} day{(days == 1 ? "" : "s")}");
        if (hours > 0) parts.Add($"{hours} hour{(hours == 1 ? "" : "s")}");
        if (minutes > 0) parts.Add($"{minutes} minute{(minutes == 1 ? "" : "s")}");
        if (seconds > 0 || parts.Count == 0) parts.Add($"{seconds} second{(seconds == 1 ? "" : "s")}");

        if (parts.Count == 1) return parts[0];
        if (parts.Count == 2) return $"{parts[0]} and {parts[1]}";
        return $"{string.Join(", ", parts.Take(parts.Count - 1))}, and {parts.Last()}";
    }

    public static string BuildQuerySubject(string expression, DateTime targetDate)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return targetDate.Date >= DateTime.Today
                ? $"From now until {targetDate:MMMM d, yyyy}"
                : $"Since {targetDate:MMMM d, yyyy}";
        }

        var trimmed = expression.Trim();
        var lower = trimmed.ToLowerInvariant();

        var futureMatch = Regex.Match(lower, @"^(?:(?:from\s+now\s+(?:until|to))|(?:(minutes?|mins?|m|seconds?|secs?|s|hours?|hrs?|h|weeks?|w|days?|d|business\s+days?|work\s+days?|working\s+days?|time)\s+(?:to|until))|until|time\s+to)\s+(?<term>.+)$");
        if (futureMatch.Success)
        {
            string term = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(futureMatch.Groups["term"].Value.Trim());
            return $"From now until {term}";
        }

        var pastMatch = Regex.Match(lower, @"^(?:(?:(minutes?|mins?|m|seconds?|secs?|s|hours?|hrs?|h|weeks?|w|days?|d|business\s+days?|work\s+days?|working\s+days?|time)\s+(?:since|from))|since|time\s+since|time\s+from)\s+(?<term>.+)$");
        if (pastMatch.Success)
        {
            string term = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(pastMatch.Groups["term"].Value.Trim());
            return $"Since {term}";
        }

        var betweenMatch = Regex.Match(lower, @"^(?:(?:hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s|days?|d|weeks?|w|business\s+days?|work\s+days?|working\s+days?|time)\s+between\s+|difference\s+between\s+|between\s+)(.+?)\s+(?:and|to)\s+(.+)$");
        if (betweenMatch.Success)
        {
            return $"Between {betweenMatch.Groups[1].Value.Trim()} and {betweenMatch.Groups[2].Value.Trim()}";
        }

        bool isPast = targetDate < DateTime.Now;
        string termTitle = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(trimmed);

        if (lower.StartsWith("next ") || lower.StartsWith("last ") || lower.StartsWith("this ") || lower.StartsWith("previous ") || lower.StartsWith("past "))
        {
            return isPast
                ? $"Since {termTitle} ({targetDate:MMMM d, yyyy})"
                : $"From now until {termTitle} ({targetDate:MMMM d, yyyy})";
        }

        return isPast
            ? $"Since {termTitle} ({targetDate:MMMM d, yyyy})"
            : $"From now until {termTitle} ({targetDate:MMMM d, yyyy})";
    }

    private static IReadOnlyList<AlternativeMeasurement> GenerateDateSpanAlternatives(TimeSpan span, DateTime targetDate, string queryExpression = "")
    {
        double totalSec = Math.Max(0, span.TotalSeconds);
        double totalDays = Math.Max(0, span.TotalDays);
        double totalHours = Math.Max(0, span.TotalHours);
        double totalMinutes = Math.Max(0, span.TotalMinutes);
        double totalWeeks = totalDays / 7.0;
        int busDays = CalculateBusinessDays(DateTime.Today < targetDate.Date ? DateTime.Today : targetDate.Date, DateTime.Today < targetDate.Date ? targetDate.Date : DateTime.Today);

        string subject = BuildQuerySubject(queryExpression, targetDate);
        string hierarchical = FormatHierarchicalSpan(span);
        string hierarchicalCompact = FormatHierarchicalSpan(span, compact: true);
        string cumulative = FormatCumulativeDaysSpan(span);
        string cumulativeCompact = FormatCumulativeDaysSpan(span, compact: true);

        var list = new List<AlternativeMeasurement>();

        // 0. Standard Date Formats (ISO 8601, Full, Short, Relative, Epoch)
        if (targetDate != DateTime.MinValue)
        {
            bool hasTime = targetDate.TimeOfDay != TimeSpan.Zero;
            string isoFormat = hasTime ? targetDate.ToString("yyyy-MM-dd HH:mm:ss") : targetDate.ToString("yyyy-MM-dd");
            string fullDate = hasTime ? targetDate.ToString("dddd, MMMM d, yyyy h:mm tt") : targetDate.ToString("dddd, MMMM d, yyyy");
            string shortDate = hasTime ? targetDate.ToString("MM/dd/yyyy h:mm tt") : targetDate.ToString("MM/dd/yyyy");
            long epochSec = new DateTimeOffset(targetDate).ToUnixTimeSeconds();
            string relativeCal = targetDate.Date >= DateTime.Today 
                ? (totalDays < 1 ? "Today" : (totalDays < 2 ? "Tomorrow" : $"In {Math.Round(totalDays):N0} days"))
                : (totalDays < 1 ? "Today" : (totalDays < 2 ? "Yesterday" : $"{Math.Round(totalDays):N0} days ago"));

            list.Add(new AlternativeMeasurement(
                "ISO 8601",
                isoFormat,
                "Standard international date format (YYYY-MM-DD)",
                "Date Formats",
                IsCommon: true,
                ConceptTitle: "🌐 ISO 8601 Date",
                ConversationalSentence: $"{targetDate:dddd, MMMM d, yyyy} in ISO 8601 format is {isoFormat}."));

            list.Add(new AlternativeMeasurement(
                "Short Date",
                shortDate,
                "Standard calendar short date format (MM/DD/YYYY)",
                "Date Formats",
                IsCommon: true,
                ConceptTitle: "📅 Short Date",
                ConversationalSentence: $"{targetDate:dddd, MMMM d, yyyy} in short format is {shortDate}."));

            list.Add(new AlternativeMeasurement(
                "Full Date",
                fullDate,
                "Full weekday, month, day, and year",
                "Date Formats",
                IsCommon: true,
                ConceptTitle: "📆 Full Date",
                ConversationalSentence: $"{fullDate}."));

            list.Add(new AlternativeMeasurement(
                "Relative Date",
                relativeCal,
                "Relative calendar time offset from today",
                "Date Formats",
                IsCommon: true,
                ConceptTitle: "⏳ Relative Calendar",
                ConversationalSentence: $"{targetDate:dddd, MMMM d, yyyy} is {relativeCal.ToLowerInvariant()}."));

            list.Add(new AlternativeMeasurement(
                "Unix Timestamp",
                epochSec.ToString(),
                "Elapsed seconds since January 1, 1970 UTC (Epoch)",
                "Date Formats",
                IsCommon: false,
                ConceptTitle: "💻 Unix Epoch Timestamp",
                ConversationalSentence: $"The Unix timestamp for {targetDate:dddd, MMMM d, yyyy} is {epochSec}."));

            list.Add(new AlternativeMeasurement(
                "Day of Year",
                $"Day {targetDate.DayOfYear} of {targetDate.Year}",
                "Ordinal day of the calendar year",
                "Date Formats",
                IsCommon: false,
                ConceptTitle: "🎯 Day of Year",
                ConversationalSentence: $"{targetDate:dddd, MMMM d, yyyy} is day {targetDate.DayOfYear} of {targetDate.Year}."));
        }

        // 1. Common & Precise Breakdowns (Common: Days, Weeks, Hours, Natural Breakdowns)
        list.Add(new AlternativeMeasurement(
            "Days",
            $"{totalDays:N0} days",
            "Total calendar days (24 hours each)",
            "Common",
            IsCommon: true,
            ConceptTitle: "🗓️ Calendar Days",
            ConversationalSentence: $"{subject} is {totalDays:N0} days."));

        list.Add(new AlternativeMeasurement(
            "Time Breakdown",
            hierarchicalCompact,
            "Complete calendar breakdown in weeks, days, hours, minutes, and seconds",
            "Common",
            IsCommon: true,
            ConceptTitle: "⏱️ Time Breakdown (Weeks & Days)",
            ConversationalSentence: $"{subject} is {hierarchical}."));

        list.Add(new AlternativeMeasurement(
            "Weeks",
            $"{totalWeeks:N1} weeks",
            "Full calendar weeks (7 days each)",
            "Common",
            IsCommon: true,
            ConceptTitle: "📆 Total Weeks",
            ConversationalSentence: $"{subject} is {totalWeeks:N1} weeks."));

        list.Add(new AlternativeMeasurement(
            "Hours",
            $"{totalHours:N1} hours",
            "Total elapsed clock hours",
            "Common",
            IsCommon: true,
            ConceptTitle: "⏰ Total Hours",
            ConversationalSentence: $"{subject} is {totalHours:N1} hours."));

        list.Add(new AlternativeMeasurement(
            "Days & Time",
            cumulativeCompact,
            "Total calendar days with remaining hours, minutes, and seconds",
            "Common",
            IsCommon: false,
            ConceptTitle: "📅 Days & Time Breakdown",
            ConversationalSentence: $"{subject} is {cumulative}."));

        list.Add(new AlternativeMeasurement(
            "Minutes",
            $"{totalMinutes:N0} minutes",
            "Total elapsed clock minutes",
            "Common",
            IsCommon: false,
            ConceptTitle: "⏳ Total Minutes",
            ConversationalSentence: $"{subject} is {totalMinutes:N0} minutes."));

        list.Add(new AlternativeMeasurement(
            "Seconds",
            $"{totalSec:N0} seconds",
            "Total elapsed clock seconds",
            "Common",
            IsCommon: false,
            ConceptTitle: "⏱️ Total Seconds",
            ConversationalSentence: $"{subject} is {totalSec:N0} seconds."));

        list.Add(new AlternativeMeasurement(
            "Business Days",
            $"{busDays} business days",
            "Working days (Monday through Friday), excluding weekends",
            "Common",
            IsCommon: false,
            ConceptTitle: "💼 Working Business Days",
            ConversationalSentence: $"{subject} is {busDays} business days (excluding weekends)."));

        list.Add(new AlternativeMeasurement(
            "ISO Date",
            targetDate.ToString("yyyy-MM-dd"),
            "ISO 8601 standard date format",
            "Common",
            IsCommon: false,
            ConceptTitle: "📅 ISO 8601 Date",
            ConversationalSentence: $"{subject} reaches {targetDate:yyyy-MM-dd}."));

        list.Add(new AlternativeMeasurement(
            "Full Date",
            targetDate.ToString("dddd, MMMM d, yyyy"),
            "Full formatted human date",
            "Common",
            IsCommon: false,
            ConceptTitle: "🗓️ Full Formatted Date",
            ConversationalSentence: $"{subject} reaches {targetDate:dddd, MMMM d, yyyy}."));

        // 2. Physics & The Universe
        double lightDistKm = totalSec * 299792.458;
        string lightDistStr = lightDistKm >= 1e12
            ? $"{lightDistKm / 1e12:N2} trillion km (~{lightDistKm / 149597870.7:N0} AU)"
            : lightDistKm >= 1e9
                ? $"{lightDistKm / 1e9:N2} billion km"
                : $"{lightDistKm:N0} km";
        list.Add(new AlternativeMeasurement(
            "Speed of Light Distance",
            lightDistStr,
            "Distance traveled by a photon in vacuum (c = 299,792 km/s), reaching past Pluto.",
            "Physics",
            IsCommon: false,
            ConceptTitle: "⚡ Distance Light Travels",
            ConversationalSentence: $"{subject} is the time it takes light to travel {lightDistStr} in a vacuum."));

        double soundDistM = totalSec * 343.0;
        string soundDistStr = soundDistM >= 1e9
            ? $"{soundDistM / 1e9:N2} billion meters"
            : soundDistM >= 1e6
                ? $"{soundDistM / 1e6:N2} million meters"
                : $"{soundDistM:N0} meters";
        list.Add(new AlternativeMeasurement(
            "Speed of Sound Distance",
            soundDistStr,
            "Distance an acoustic pressure wave travels at sea level (343 m/s, Mach 1).",
            "Physics",
            IsCommon: false,
            ConceptTitle: "🔊 Distance Sound Travels",
            ConversationalSentence: $"{subject} is the time it takes sound to travel {soundDistStr} through air at sea level."));

        double moonLaserTrips = totalSec / 2.56;
        list.Add(new AlternativeMeasurement(
            "Moon Laser Bounces",
            $"{moonLaserTrips:N0} roundtrips",
            "Round-trip travel pulses between Earth observatories and Apollo lunar retroreflectors (2.56s).",
            "Physics",
            IsCommon: false,
            ConceptTitle: "🌕 Moon Laser Retroreflector Bounces",
            ConversationalSentence: $"{subject} is enough time for {moonLaserTrips:N0} roundtrips of laser pulses to the Moon and back."));

        double lightFeet = totalSec * 983571056.0;
        string lightFeetStr = lightFeet >= 1e15 ? $"{lightFeet / 1e15:N2} quadrillion light-feet" : $"{lightFeet / 1e12:N2} trillion light-feet";
        list.Add(new AlternativeMeasurement(
            "Grace Hopper Light-Feet",
            lightFeetStr,
            "Computer pioneer Grace Hopper's physical model of time: light travels ~11.8 inches per nanosecond.",
            "Physics",
            IsCommon: false,
            ConceptTitle: "📏 Grace Hopper Light-Feet",
            ConversationalSentence: $"{subject} represents {lightFeetStr} at the speed of light."));

        double thunderMiles = totalSec / 5.0;
        list.Add(new AlternativeMeasurement(
            "Lightning-Thunder Ratio",
            $"{thunderMiles:N0} miles away",
            "Distance away lightning struck according to the 5-seconds-per-mile storm rule.",
            "Physics",
            IsCommon: false,
            ConceptTitle: "⚡ Lightning Distance (Storm Rule)",
            ConversationalSentence: $"{subject} corresponds to a storm {thunderMiles:N0} miles away using the 5-seconds-per-mile rule."));

        // 3. Space & Astronomy
        double issOrbits = totalSec / 5574.0;
        list.Add(new AlternativeMeasurement(
            "ISS Earth Orbits",
            $"{issOrbits:N1} orbits",
            "Complete 92.9-minute revolutions of the International Space Station around Earth.",
            "Space",
            IsCommon: false,
            ConceptTitle: "🛰️ ISS Earth Revolutions",
            ConversationalSentence: $"{subject} is {issOrbits:N1} complete revolutions of the International Space Station around Earth."));

        double marsSols = totalSec / 88775.244;
        list.Add(new AlternativeMeasurement(
            "Martian Sols",
            $"{marsSols:N2} Sols",
            "One Martian solar day as logged by NASA rovers (24h 39m 35s).",
            "Space",
            IsCommon: false,
            ConceptTitle: "🪐 Martian Solar Days (Sols)",
            ConversationalSentence: $"{subject} equals {marsSols:N2} Sols on the surface of Mars."));

        double lunarCycles = totalDays / 29.5306;
        list.Add(new AlternativeMeasurement(
            "Lunar Cycles",
            $"{lunarCycles:N2} full moons",
            "Complete synodic months from new moon to new moon (29.53 days).",
            "Space",
            IsCommon: false,
            ConceptTitle: "🌑 Synodic Lunar Months",
            ConversationalSentence: $"{subject} is {lunarCycles:N2} complete lunar cycles (new moon to new moon)."));

        double sunTrips = totalSec / 499.0;
        list.Add(new AlternativeMeasurement(
            "Sunlight Travel Time",
            $"{sunTrips:N0} solar trips",
            "Number of times sunlight could travel from the surface of the Sun to Earth (499s).",
            "Space",
            IsCommon: false,
            ConceptTitle: "☀️ Sun-to-Earth Light Trips",
            ConversationalSentence: $"{subject} is the time sunlight could travel from the Sun to Earth {sunTrips:N0} times."));

        double venusDays = totalDays / 243.0;
        list.Add(new AlternativeMeasurement(
            "Venusian Days",
            $"{venusDays:N2} days",
            "Fractions of one single, slow retrograde day on Venus (243 Earth days).",
            "Space",
            IsCommon: false,
            ConceptTitle: "🪐 Venusian Day Fraction",
            ConversationalSentence: $"{subject} represents {venusDays:N2} of a single retrograde day on Venus."));

        double mercuryDays = totalDays / 176.0;
        list.Add(new AlternativeMeasurement(
            "Mercurian Solar Days",
            $"{mercuryDays:N2} days",
            "Sunrise-to-sunrise cycles on the scorched surface of Mercury (176 Earth days).",
            "Space",
            IsCommon: false,
            ConceptTitle: "🪐 Mercurian Solar Days",
            ConversationalSentence: $"{subject} is {mercuryDays:N2} sunrise-to-sunrise cycles on Mercury."));

        double halleyOrbits = totalDays / 27759.0;
        list.Add(new AlternativeMeasurement(
            "Halley's Comet Orbits",
            $"{halleyOrbits:N4} orbits",
            "Orbital fraction of Halley's Comet traveling out past Neptune and back (76 years).",
            "Space",
            IsCommon: false,
            ConceptTitle: "☄️ Halley's Comet Orbital Fraction",
            ConversationalSentence: $"{subject} represents {halleyOrbits:N4} of Halley's Comet's 76-year orbital journey."));

        // 4. Pop Culture & Everyday Life
        double ciscoLoops = totalSec / 342.0;
        list.Add(new AlternativeMeasurement(
            "Cisco Hold Music",
            $"{ciscoLoops:N0} loops",
            "Endless loops of Tim Carleton's default IT corporate hold track Opus No. 1 (5m 42s).",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "📞 Cisco Opus No. 1 Hold Music",
            ConversationalSentence: $"{subject} is {ciscoLoops:N0} uninterrupted loops of Cisco's default corporate hold track."));

        double warhols = totalSec / 900.0;
        list.Add(new AlternativeMeasurement(
            "Warhols of Fame",
            $"{warhols:N1} Warhols",
            "Andy Warhol's promised 15 minutes of global fame per person (900s).",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "🎨 Andy Warhol '15 Minutes of Fame'",
            ConversationalSentence: $"{subject} is {warhols:N1} Warhol units of world fame (15 minutes each)."));

        double rhapsodies = totalSec / 355.0;
        list.Add(new AlternativeMeasurement(
            "Bohemian Rhapsodies",
            $"{rhapsodies:N0} plays",
            "Back-to-back plays of Queen's operatic rock masterpiece (5m 55s).",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "🎸 Queen - Bohemian Rhapsody Plays",
            ConversationalSentence: $"{subject} is {rhapsodies:N0} back-to-back plays of Queen's 'Bohemian Rhapsody'."));

        double rickrolls = totalSec / 213.0;
        list.Add(new AlternativeMeasurement(
            "Rickrolls",
            $"{rickrolls:N0} plays",
            "Uninterrupted loops of Rick Astley's 'Never Gonna Give You Up' (3m 33s).",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "🕺 Rick Astley - Never Gonna Give You Up",
            ConversationalSentence: $"{subject} is {rickrolls:N0} back-to-back plays of Rick Astley's 'Never Gonna Give You Up'."));

        double thereYetCalls = totalSec / 420.0;
        list.Add(new AlternativeMeasurement(
            "Are We There Yet? Calls",
            $"{thereYetCalls:N0} inquiries",
            "Average interval of kids asking backseat questions on road trips (~7 minutes).",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "🚗 'Are We There Yet?' Inquiries",
            ConversationalSentence: $"{subject} spans approximately {thereYetCalls:N0} backseat inquiries on a road trip."));

        double snoozes = totalSec / 540.0;
        list.Add(new AlternativeMeasurement(
            "Snooze Button Hits",
            $"{snoozes:N0} alarms",
            "Classic 9-minute snooze intervals stolen from morning alarms.",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "⏰ Morning Snooze Button Hits",
            ConversationalSentence: $"{subject} equals {snoozes:N0} consecutive 9-minute alarm snoozes."));

        double popcorn = totalSec / 135.0;
        list.Add(new AlternativeMeasurement(
            "Microwave Popcorn Bags",
            $"{popcorn:N0} bags",
            "Time to pop a fresh bag of buttery microwave popcorn (2m 15s).",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "🍿 Microwave Popcorn Batches",
            ConversationalSentence: $"{subject} is enough time to pop {popcorn:N0} fresh bags of microwave popcorn."));

        double coffee = totalSec / 210.0;
        list.Add(new AlternativeMeasurement(
            "Fresh Coffee Brews",
            $"{coffee:N0} cups",
            "Standard brewing cycle for a fresh mug of drip coffee (3.5 minutes).",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "☕ Fresh Drip Coffee Brews",
            ConversationalSentence: $"{subject} is enough time to brew {coffee:N0} mugs of fresh drip coffee."));

        double tiktoks = totalSec / 30.0;
        list.Add(new AlternativeMeasurement(
            "TikTok Videos",
            $"{tiktoks:N0} videos",
            "Standard 30-second short-form videos scrolled through on social media.",
            "PopCulture",
            IsCommon: false,
            ConceptTitle: "📱 Short-Form Social Videos",
            ConversationalSentence: $"{subject} is enough time to scroll through {tiktoks:N0} standard 30-second videos."));

        // 5. Nature & Biological Absurdities
        double beardInches = totalDays * (0.5 / 30.4375);
        list.Add(new AlternativeMeasurement(
            "Wizard Beard Growth",
            $"{beardInches:N2} inches grown",
            "Length of magnificent facial hair grown at average human rate (~0.5 inches/month).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🧙‍♂️ Wizard Beard Growth",
            ConversationalSentence: $"{subject} is enough time to grow {beardInches:N2} inches of a wizard beard."));

        double bananaHalfLife = totalDays / 2.0;
        list.Add(new AlternativeMeasurement(
            "Banana Browning Half-Life",
            $"{bananaHalfLife:N1} generations",
            "The brief 48-hour window between a green banana and brown banana-bread spots.",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🍌 Banana Shelf-Life Half-Lives",
            ConversationalSentence: $"{subject} spans {bananaHalfLife:N1} room-temperature banana shelf-life half-lives."));

        double snailKm = totalDays * 0.0864;
        list.Add(new AlternativeMeasurement(
            "Snail Marathons",
            $"{snailKm:N2} km crawled",
            "Distance a common garden snail can crawl at maximum pace (~1 mm/second).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🐌 Garden Snail Crawl Distance",
            ConversationalSentence: $"{subject} is how far a garden snail crawls ({snailKm:N2} km at maximum pace)."));

        double slothKm = totalDays * 0.1152;
        list.Add(new AlternativeMeasurement(
            "Sloth Treks",
            $"{slothKm:N2} km crawled",
            "Distance a three-toed sloth covers moving at ground speed (~0.003 mph).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🦥 Three-Toed Sloth Treks",
            ConversationalSentence: $"{subject} is how far a three-toed sloth covers ({slothKm:N2} km)."));

        double hamsterPreg = totalDays / 16.0;
        list.Add(new AlternativeMeasurement(
            "Hamster Pregnancies",
            $"{hamsterPreg:N2} generations",
            "Gestation cycle of a golden hamster (16 days).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🐹 Hamster Pregnancy Cycles",
            ConversationalSentence: $"{subject} spans {hamsterPreg:N2} consecutive hamster gestation periods."));

        double elephantPreg = totalDays / 660.0;
        list.Add(new AlternativeMeasurement(
            "Elephant Pregnancies",
            $"{elephantPreg:N2} gestations",
            "Gestation period of an African elephant (22 months / 660 days).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🐘 Elephant Gestation Fraction",
            ConversationalSentence: $"{subject} represents {elephantPreg:N2} African elephant pregnancy cycles."));

        double whaleBeats = totalDays * 5760.0;
        list.Add(new AlternativeMeasurement(
            "Blue Whale Heartbeats",
            $"{whaleBeats:N0} beats",
            "Slow, majestic heartbeats of a deep-diving blue whale (~4 beats/minute).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🐋 Blue Whale Heartbeats",
            ConversationalSentence: $"{subject} spans approximately {whaleBeats:N0} deep-diving blue whale heartbeats."));

        double humanBeats = totalDays * 100800.0;
        list.Add(new AlternativeMeasurement(
            "Human Heartbeats",
            $"{humanBeats:N0} beats",
            "Steady resting pulses of an average human heart (~70 beats/minute).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "❤️ Average Human Heartbeats",
            ConversationalSentence: $"{subject} spans approximately {humanBeats:N0} resting human heartbeats."));

        double mayflies = totalDays;
        list.Add(new AlternativeMeasurement(
            "Mayfly Lifespans",
            $"{mayflies:N1} generations",
            "Entire adult lifetimes of the short-lived mayfly (~24 hours).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "🦟 Mayfly Adult Lifespans",
            ConversationalSentence: $"{subject} equals {mayflies:N1} entire adult mayfly lifespans."));

        double blinks = totalSec / 0.35;
        list.Add(new AlternativeMeasurement(
            "Blinks of an Eye",
            $"{blinks:N0} blinks",
            "Involuntary eyelid blinks performed by human eyes (~350ms each).",
            "Nature",
            IsCommon: false,
            ConceptTitle: "👁️ Human Involuntary Eye Blinks",
            ConversationalSentence: $"{subject} is {blinks:N0} involuntary human eye blinks."));

        // 6. Hacker, Political & Historical Units
        double mooches = totalDays / 10.0;
        list.Add(new AlternativeMeasurement(
            "Scaramuccis (Mooches)",
            $"{mooches:N2} Mooches",
            "Anthony Scaramucci's tenure as White House Communications Director (exactly 10 days).",
            "Historical",
            IsCommon: false,
            ConceptTitle: "🏛️ White House Mooches",
            ConversationalSentence: $"{subject} equals {mooches:N2} Mooches (Anthony Scaramucci's 10-day tenure)."));

        double fortnights = totalDays / 14.0;
        list.Add(new AlternativeMeasurement(
            "Fortnights",
            $"{fortnights:N2} fortnights",
            "Traditional two-week measurement dating back to Old English (14 days).",
            "Historical",
            IsCommon: false,
            ConceptTitle: "⚔️ Traditional Fortnights",
            ConversationalSentence: $"{subject} equals {fortnights:N2} traditional fortnights (14-day periods)."));

        double microfortnights = totalSec / 1.2096;
        list.Add(new AlternativeMeasurement(
            "Microfortnights",
            $"{microfortnights:N0} microfortnights",
            "Classic VMS/Unix hacker unit representing a millionth of a fortnight (1.2096s).",
            "Historical",
            IsCommon: false,
            ConceptTitle: "💾 Hacker Microfortnights",
            ConversationalSentence: $"{subject} is {microfortnights:N0} microfortnights (1.2096s Unix hacker units)."));

        double swatchBeats = totalSec / 86.4;
        list.Add(new AlternativeMeasurement(
            "Internet Time (.beats)",
            $"{swatchBeats:N0} .beats",
            "Swatch's 1998 timezone-free metric time dividing the day into 1,000 beats (86.4s).",
            "Historical",
            IsCommon: false,
            ConceptTitle: "⌚ Swatch Internet Time",
            ConversationalSentence: $"{subject} equals {swatchBeats:N0} Swatch .beats (metric decimal time)."));

        double friedmanUnits = totalDays / 182.5;
        list.Add(new AlternativeMeasurement(
            "Friedman Units",
            $"{friedmanUnits:N2} Friedman units",
            "Columnist Thomas Friedman's perpetual 'the next six months will decide' horizon (182.5 days).",
            "Historical",
            IsCommon: false,
            ConceptTitle: "📰 Friedman Units ('Next 6 Months')",
            ConversationalSentence: $"{subject} equals {friedmanUnits:N2} Friedman units (182.5-day horizons)."));

        double defragPasses = totalSec / 2700.0;
        list.Add(new AlternativeMeasurement(
            "Windows 95 Defrag Passes",
            $"{defragPasses:N1} passes",
            "Mesmerizing sessions of watching colored hard drive clusters get rearranged (45 minutes).",
            "Historical",
            IsCommon: false,
            ConceptTitle: "🖥️ Windows 95 Defrag Runs",
            ConversationalSentence: $"{subject} is {defragPasses:N1} full 45-minute sessions of Windows 95 disk defragmentation."));

        return list;
    }

    private static CalculatorResult? EvaluateBaseConversions(string input)
    {
        var expr = input.Trim();
        var match = Regex.Match(expr, @"^(?:to\s+(hex|bin|dec|oct)\s+(.+)|(.+)\s+to\s+(hex|bin|dec|oct))$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        string targetBase = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[4].Value).ToLowerInvariant();
        string numStr = (match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value).Trim();

        try
        {
            long val;
            if (numStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                val = long.Parse(numStr[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            else if (numStr.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                val = Convert.ToInt64(numStr[2..], 2);
            }
            else if (numStr.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                val = Convert.ToInt64(numStr[2..], 8);
            }
            else
            {
                val = long.Parse(numStr, CultureInfo.InvariantCulture);
            }

            return targetBase switch
            {
                "hex" => new CalculatorResult(expr, $"0x{val:X}", $"Hexadecimal of {val}"),
                "bin" => new CalculatorResult(expr, $"0b{Convert.ToString(val, 2).PadLeft(8, '0')}", $"Binary of {val}"),
                "dec" => new CalculatorResult(expr, val.ToString(), $"Decimal of {numStr}"),
                "oct" => new CalculatorResult(expr, $"0o{Convert.ToString(val, 8)}", $"Octal of {val}"),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static CalculatorResult? EvaluateProgrammerMath(string input)
    {
        try
        {
            var expr = input.Trim();

            // Handle NOT: ~0x0F or ~15
            var notMatch = Regex.Match(expr, @"^~\s*(0x[0-9a-fA-F]+|[0-9]+)$");
            if (notMatch.Success)
            {
                long val = ParseInt(notMatch.Groups[1].Value);
                long res = ~val;
                return new CalculatorResult(expr, $"{res} (0x{res:X})", "Bitwise NOT");
            }

            // Handle shifts: A << B or A >> B
            var shiftMatch = Regex.Match(expr, @"^\s*(0x[0-9a-fA-F]+|[0-9]+)\s*(<<|>>)\s*([0-9]+)\s*$");
            if (shiftMatch.Success)
            {
                long left = ParseInt(shiftMatch.Groups[1].Value);
                string op = shiftMatch.Groups[2].Value;
                int shift = int.Parse(shiftMatch.Groups[3].Value);

                long res = op == "<<" ? left << shift : left >> shift;
                return new CalculatorResult(expr, $"{res} (0x{res:X})", "Bitwise shift (Decimal / Hex)");
            }

            // Handle bitwise AND, OR, XOR: A & B, A | B, A ^ B
            var bitwiseMatch = Regex.Match(expr, @"^\s*(0x[0-9a-fA-F]+|[0-9]+)\s*([&|^])\s*(0x[0-9a-fA-F]+|[0-9]+)\s*$");
            if (bitwiseMatch.Success)
            {
                long left = ParseInt(bitwiseMatch.Groups[1].Value);
                string op = bitwiseMatch.Groups[2].Value;
                long right = ParseInt(bitwiseMatch.Groups[3].Value);

                long res = op switch
                {
                    "&" => left & right,
                    "|" => left | right,
                    "^" => left ^ right,
                    _ => 0
                };
                return new CalculatorResult(expr, $"{res} (0x{res:X})", "Bitwise operation (Decimal / Hex)");
            }

            // Handle simple hex arithmetic: 0xFF + 16
            var hexArithMatch = Regex.Match(expr, @"^\s*(0x[0-9a-fA-F]+|[0-9]+)\s*([+\-*/%])\s*(0x[0-9a-fA-F]+|[0-9]+)\s*$");
            if (hexArithMatch.Success && (expr.Contains("0x", StringComparison.OrdinalIgnoreCase)))
            {
                long left = ParseInt(hexArithMatch.Groups[1].Value);
                string op = hexArithMatch.Groups[2].Value;
                long right = ParseInt(hexArithMatch.Groups[3].Value);

                long res = op switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => right != 0 ? left / right : 0,
                    "%" => right != 0 ? left % right : 0,
                    _ => 0
                };
                return new CalculatorResult(expr, $"{res} (0x{res:X})", "Programmer math (Decimal / Hex)");
            }
        }
        catch { }

        return null;
    }

    private static CalculatorResult? EvaluateMath(string mathExpr, bool explicitMath)
    {
        if (string.IsNullOrWhiteSpace(mathExpr)) return null;

        if (!explicitMath)
        {
            bool hasDigit = false;
            bool hasOp = false;
            bool hasLetters = false;

            foreach (char c in mathExpr)
            {
                if (char.IsDigit(c)) hasDigit = true;
                else if (c is '+' or '-' or '*' or '/' or '^' or '%') hasOp = true;
                else if (char.IsLetter(c)) hasLetters = true;
            }

            // Allow known scientific function names (sin, cos, tan, sqrt, etc.)
            bool hasKnownFunc = Regex.IsMatch(mathExpr, @"\b(sin|cos|tan|asin|acos|atan|sqrt|cbrt|abs|log|ln|pi|e)\b", RegexOptions.IgnoreCase);

            if (!hasKnownFunc && (!hasDigit || !hasOp || hasLetters))
            {
                return null;
            }
        }

        try
        {
            string sanitized = mathExpr;

            // Replace constants
            sanitized = Regex.Replace(sanitized, @"\bpi\b", Math.PI.ToString(CultureInfo.InvariantCulture), RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\be\b", Math.E.ToString(CultureInfo.InvariantCulture), RegexOptions.IgnoreCase);

            // Handle scientific functions
            sanitized = Regex.Replace(sanitized, @"\bsqrt\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                return Math.Sqrt(v).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            sanitized = Regex.Replace(sanitized, @"\bcbrt\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                return Math.Cbrt(v).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            sanitized = Regex.Replace(sanitized, @"\babs\s*\(\s*(-?[0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                return Math.Abs(v).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            sanitized = Regex.Replace(sanitized, @"\blog\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                return Math.Log10(v).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            sanitized = Regex.Replace(sanitized, @"\bln\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                return Math.Log(v).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            // Trigonometry (defaults to degrees for desktop calculator intuitiveness)
            sanitized = Regex.Replace(sanitized, @"\bsin\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                double rad = v * Math.PI / 180.0;
                return Math.Round(Math.Sin(rad), 8).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            sanitized = Regex.Replace(sanitized, @"\bcos\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                double rad = v * Math.PI / 180.0;
                return Math.Round(Math.Cos(rad), 8).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            sanitized = Regex.Replace(sanitized, @"\btan\s*\(\s*([0-9]+(?:\.[0-9]+)?)\s*\)", match =>
            {
                double v = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                double rad = v * Math.PI / 180.0;
                return Math.Round(Math.Tan(rad), 8).ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);

            // Normalize powers: 2^10
            if (sanitized.Contains('^'))
            {
                sanitized = Regex.Replace(sanitized, @"([0-9]+(?:\.[0-9]+)?)\s*\^\s*([0-9]+(?:\.[0-9]+)?)", match =>
                {
                    double b = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    double exp = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    return Math.Pow(b, exp).ToString(CultureInfo.InvariantCulture);
                });
            }

            using var dt = new DataTable();
            var result = dt.Compute(sanitized, null);

            if (result != null && double.TryParse(result.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
            {
                if (double.IsNaN(num) || double.IsInfinity(num)) return null;

                string formatted = FormatDouble(num);
                return new CalculatorResult(mathExpr, formatted, "Calculator result (Enter to paste)");
            }
        }
        catch
        {
            // Invalid math syntax
        }

        return null;
    }

    private static string FormatDouble(double val)
    {
        return val % 1 == 0 
            ? val.ToString("N0", CultureInfo.InvariantCulture) 
            : Math.Round(val, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string NormalizeUnit(string unit)
    {
        var u = unit.ToLowerInvariant().Trim();
        if (u is "mile" or "miles" or "mi") return "mi";
        if (u is "kilometer" or "kilometers" or "km") return "km";
        if (u is "inch" or "inches" or "in") return "in";
        if (u is "centimeter" or "centimeters" or "cm") return "cm";
        if (u is "millimeter" or "millimeters" or "mm") return "mm";
        if (u is "meter" or "meters" or "m") return "m";
        if (u is "foot" or "feet" or "ft") return "ft";
        if (u is "pound" or "pounds" or "lb" or "lbs") return "lbs";
        if (u is "kilogram" or "kilograms" or "kg") return "kg";
        if (u is "gram" or "grams" or "g") return "g";
        if (u is "ounce" or "ounces" or "oz") return "oz";
        if (u is "byte" or "bytes" or "b") return "b";
        if (u is "kilobyte" or "kilobytes" or "kb") return "kb";
        if (u is "megabyte" or "megabytes" or "mb") return "mb";
        if (u is "gigabyte" or "gigabytes" or "gb") return "gb";
        if (u is "terabyte" or "terabytes" or "tb") return "tb";
        if (u is "petabyte" or "petabytes" or "pb") return "pb";
        if (u is "celsius" or "centigrade" or "c") return "c";
        if (u is "fahrenheit" or "f") return "f";
        if (u is "kelvin" or "k") return "k";
        if (u is "kmh" or "km/h" or "kph") return "kmh";
        if (u is "mph") return "mph";
        if (u is "mps" or "m/s") return "mps";
        if (u is "knot" or "knots") return "knot";
        if (u is "ms" or "millisecond" or "milliseconds") return "ms";
        if (u is "s" or "sec" or "second" or "seconds") return "s";
        if (u is "min" or "mins" or "minute" or "minutes") return "min";
        if (u is "h" or "hr" or "hrs" or "hour" or "hours") return "h";
        if (u is "d" or "day" or "days") return "d";
        if (u is "w" or "week" or "weeks") return "w";
        return u;
    }

    private static CalculatorResult? ConvertUnits(double val, string from, string to)
    {
        // Typography / UI: px, rem, pt
        if (from is "px" && to is "rem") return new CalculatorResult($"{val} px to rem", $"{FormatDouble(val / 16.0)}rem", "Based on 16px root");
        if (from is "rem" && to is "px") return new CalculatorResult($"{val} rem to px", $"{FormatDouble(val * 16.0)}px", "Based on 16px root");
        if (from is "pt" && to is "px") return new CalculatorResult($"{val} pt to px", $"{FormatDouble(val * 1.3333)}px", "72pt to 96dpi");
        if (from is "px" && to is "pt") return new CalculatorResult($"{val} px to pt", $"{FormatDouble(val * 0.75)}pt", "96dpi to 72pt");

        // Temperature: C, F, K
        if (from == "c" && to == "f") return new CalculatorResult($"{val} °C to °F", $"{FormatDouble((val * 9.0 / 5.0) + 32.0)}°F", "Temperature conversion");
        if (from == "f" && to == "c") return new CalculatorResult($"{val} °F to °C", $"{FormatDouble((val - 32.0) * 5.0 / 9.0)}°C", "Temperature conversion");
        if (from == "c" && to == "k") return new CalculatorResult($"{val} °C to K", $"{FormatDouble(val + 273.15)} K", "Temperature conversion");
        if (from == "k" && to == "c") return new CalculatorResult($"{val} K to °C", $"{FormatDouble(val - 273.15)} °C", "Temperature conversion");

        // Length to Meters
        var toMeters = new System.Collections.Generic.Dictionary<string, double>
        {
            ["km"] = 1000.0,
            ["m"] = 1.0,
            ["cm"] = 0.01,
            ["mm"] = 0.001,
            ["mi"] = 1609.344,
            ["ft"] = 0.3048,
            ["in"] = 0.0254
        };
        if (toMeters.ContainsKey(from) && toMeters.ContainsKey(to))
        {
            double meters = val * toMeters[from];
            double converted = meters / toMeters[to];
            return new CalculatorResult($"{val} {from} in {to}", $"{FormatDouble(converted)} {to}", "Length conversion");
        }

        // Weight/Mass to Kilograms
        var toKg = new System.Collections.Generic.Dictionary<string, double>
        {
            ["kg"] = 1.0,
            ["g"] = 0.001,
            ["mg"] = 0.000001,
            ["lbs"] = 0.45359237,
            ["oz"] = 0.02834952
        };
        if (toKg.ContainsKey(from) && toKg.ContainsKey(to))
        {
            double kg = val * toKg[from];
            double converted = kg / toKg[to];
            return new CalculatorResult($"{val} {from} in {to}", $"{FormatDouble(converted)} {to}", "Weight / Mass conversion");
        }

        // Speed to km/h
        var toKmh = new System.Collections.Generic.Dictionary<string, double>
        {
            ["kmh"] = 1.0,
            ["mph"] = 1.60934,
            ["mps"] = 3.6,
            ["knot"] = 1.852
        };
        if (toKmh.ContainsKey(from) && toKmh.ContainsKey(to))
        {
            double kmh = val * toKmh[from];
            double converted = kmh / toKmh[to];
            return new CalculatorResult($"{val} {from} in {to}", $"{FormatDouble(converted)} {to}", "Speed conversion");
        }

        // Time to seconds
        var toSec = new System.Collections.Generic.Dictionary<string, double>
        {
            ["ms"] = 0.001,
            ["s"] = 1.0,
            ["min"] = 60.0,
            ["h"] = 3600.0,
            ["d"] = 86400.0,
            ["w"] = 604800.0
        };
        if (toSec.ContainsKey(from) && toSec.ContainsKey(to))
        {
            double s = val * toSec[from];
            double converted = s / toSec[to];
            return new CalculatorResult($"{val} {from} in {to}", $"{FormatDouble(converted)} {to}", "Time conversion");
        }

        // Digital Storage
        var storageOrder = new System.Collections.Generic.List<string> { "b", "kb", "mb", "gb", "tb", "pb" };
        int idxFrom = storageOrder.IndexOf(from);
        int idxTo = storageOrder.IndexOf(to);
        if (idxFrom >= 0 && idxTo >= 0)
        {
            int diff = idxTo - idxFrom;
            double converted = diff switch
            {
                > 0 => val / Math.Pow(1024, diff),
                < 0 => val * Math.Pow(1024, -diff),
                _ => val
            };

            if (converted > 0)
            {
                string formatted = Math.Round(converted, 4).ToString("0.####", CultureInfo.InvariantCulture);
                return new CalculatorResult($"{val} {from.ToUpperInvariant()} to {to.ToUpperInvariant()}", $"{formatted} {to.ToUpperInvariant()}", "Digital storage conversion");
            }
        }

        return null;
    }

    private static long ParseInt(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt64(s[2..], 2);
        }
        return long.Parse(s, CultureInfo.InvariantCulture);
    }
}
