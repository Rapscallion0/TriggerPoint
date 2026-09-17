using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static class ConditionEvaluator
{
    public static bool Evaluate(string leftValue, ConditionOperator op, string rightValue, bool ignoreCase = true)
    {
        leftValue ??= string.Empty;
        rightValue ??= string.Empty;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        switch (op)
        {
            case ConditionOperator.Equals:
                return string.Equals(leftValue, rightValue, comparison);

            case ConditionOperator.NotEquals:
                return !string.Equals(leftValue, rightValue, comparison);

            case ConditionOperator.Contains:
                return leftValue.IndexOf(rightValue, comparison) >= 0;

            case ConditionOperator.NotContains:
                return leftValue.IndexOf(rightValue, comparison) < 0;

            case ConditionOperator.StartsWith:
                return leftValue.StartsWith(rightValue, comparison);

            case ConditionOperator.EndsWith:
                return leftValue.EndsWith(rightValue, comparison);

            case ConditionOperator.MatchesRegex:
                try
                {
                    var options = RegexOptions.None;
                    if (ignoreCase) options |= RegexOptions.IgnoreCase;
                    return Regex.IsMatch(leftValue, rightValue, options);
                }
                catch
                {
                    return false;
                }

            case ConditionOperator.IsEmpty:
                return string.IsNullOrWhiteSpace(leftValue);

            case ConditionOperator.IsNotEmpty:
                return !string.IsNullOrWhiteSpace(leftValue);

            case ConditionOperator.GreaterThan:
            case ConditionOperator.LessThan:
            case ConditionOperator.GreaterOrEqual:
            case ConditionOperator.LessOrEqual:
            {
                if (double.TryParse(leftValue.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNum) &&
                    double.TryParse(rightValue.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNum))
                {
                    return op switch
                    {
                        ConditionOperator.GreaterThan => leftNum > rightNum,
                        ConditionOperator.LessThan => leftNum < rightNum,
                        ConditionOperator.GreaterOrEqual => leftNum >= rightNum,
                        ConditionOperator.LessOrEqual => leftNum <= rightNum,
                        _ => false
                    };
                }
                // Fallback to lexicographical comparison if not strictly numbers
                int lexComp = string.Compare(leftValue, rightValue, comparison);
                return op switch
                {
                    ConditionOperator.GreaterThan => lexComp > 0,
                    ConditionOperator.LessThan => lexComp < 0,
                    ConditionOperator.GreaterOrEqual => lexComp >= 0,
                    ConditionOperator.LessOrEqual => lexComp <= 0,
                    _ => false
                };
            }

            case ConditionOperator.FileExists:
                try
                {
                    var path = leftValue.Trim().Trim('\"', '\'');
                    return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
                }
                catch
                {
                    return false;
                }

            case ConditionOperator.DirectoryExists:
                try
                {
                    var path = leftValue.Trim().Trim('\"', '\'');
                    return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
                }
                catch
                {
                    return false;
                }

            case ConditionOperator.ProcessIsRunning:
                try
                {
                    var procName = leftValue.Trim().Trim('\"', '\'');
                    if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        procName = procName.Substring(0, procName.Length - 4);
                    }
                    if (string.IsNullOrWhiteSpace(procName)) return false;
                    var processes = Process.GetProcessesByName(procName);
                    return processes != null && processes.Length > 0;
                }
                catch
                {
                    return false;
                }

            default:
                return false;
        }
    }

    public static bool IsUnary(ConditionOperator op) =>
        op is ConditionOperator.IsEmpty
           or ConditionOperator.IsNotEmpty
           or ConditionOperator.FileExists
           or ConditionOperator.DirectoryExists
           or ConditionOperator.ProcessIsRunning;
}
