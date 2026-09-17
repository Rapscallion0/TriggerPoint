using System;
using System.IO;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class ConditionEvaluatorTests
{
    [Theory]
    [InlineData("hello", ConditionOperator.Equals, "hello", true, true)]
    [InlineData("Hello", ConditionOperator.Equals, "hello", true, true)]
    [InlineData("Hello", ConditionOperator.Equals, "hello", false, false)]
    [InlineData("apple", ConditionOperator.NotEquals, "banana", true, true)]
    [InlineData("apple", ConditionOperator.NotEquals, "apple", true, false)]
    [InlineData("banana", ConditionOperator.Contains, "nan", true, true)]
    [InlineData("banana", ConditionOperator.Contains, "NAN", true, true)]
    [InlineData("banana", ConditionOperator.Contains, "NAN", false, false)]
    [InlineData("banana", ConditionOperator.NotContains, "xyz", true, true)]
    [InlineData("https://example.com", ConditionOperator.StartsWith, "https://", true, true)]
    [InlineData("image.png", ConditionOperator.EndsWith, ".PNG", true, true)]
    [InlineData("image.png", ConditionOperator.EndsWith, ".PNG", false, false)]
    public void StringOperators_EvaluateAccurately(string left, ConditionOperator op, string right, bool ignoreCase, bool expected)
    {
        var result = ConditionEvaluator.Evaluate(left, op, right, ignoreCase);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("user@example.com", ConditionOperator.MatchesRegex, @"^[^@]+@[^@]+\.[^@]+$", true, true)]
    [InlineData("invalid-email", ConditionOperator.MatchesRegex, @"^[^@]+@[^@]+\.[^@]+$", true, false)]
    [InlineData("ABC123", ConditionOperator.MatchesRegex, @"^[a-z]+\d+$", true, true)]
    [InlineData("ABC123", ConditionOperator.MatchesRegex, @"^[a-z]+\d+$", false, false)]
    public void RegexOperator_EvaluatesAccurately(string left, ConditionOperator op, string pattern, bool ignoreCase, bool expected)
    {
        var result = ConditionEvaluator.Evaluate(left, op, pattern, ignoreCase);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", ConditionOperator.IsEmpty, true)]
    [InlineData("   ", ConditionOperator.IsEmpty, true)]
    [InlineData(null, ConditionOperator.IsEmpty, true)]
    [InlineData("content", ConditionOperator.IsEmpty, false)]
    [InlineData("", ConditionOperator.IsNotEmpty, false)]
    [InlineData("   ", ConditionOperator.IsNotEmpty, false)]
    [InlineData("content", ConditionOperator.IsNotEmpty, true)]
    public void PresenceOperators_EvaluateAccurately(string? left, ConditionOperator op, bool expected)
    {
        var result = ConditionEvaluator.Evaluate(left!, op, string.Empty);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("10", ConditionOperator.GreaterThan, "5", true)]
    [InlineData("5", ConditionOperator.GreaterThan, "10", false)]
    [InlineData("3.14", ConditionOperator.LessThan, "3.15", true)]
    [InlineData("42", ConditionOperator.GreaterOrEqual, "42", true)]
    [InlineData("41", ConditionOperator.GreaterOrEqual, "42", false)]
    [InlineData("50", ConditionOperator.LessOrEqual, "50", true)]
    [InlineData("51", ConditionOperator.LessOrEqual, "50", false)]
    public void NumericOperators_EvaluateAccurately(string left, ConditionOperator op, string right, bool expected)
    {
        var result = ConditionEvaluator.Evaluate(left, op, right);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SystemOperators_FileAndDirectoryExists()
    {
        var tempFile = Path.GetTempFileName();
        var tempDir = Path.GetTempPath();
        var nonExistent = Path.Combine(tempDir, "DoesNotExist_" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.True(ConditionEvaluator.Evaluate(tempFile, ConditionOperator.FileExists, string.Empty));
            Assert.False(ConditionEvaluator.Evaluate(nonExistent, ConditionOperator.FileExists, string.Empty));

            Assert.True(ConditionEvaluator.Evaluate(tempDir, ConditionOperator.DirectoryExists, string.Empty));
            Assert.False(ConditionEvaluator.Evaluate(nonExistent, ConditionOperator.DirectoryExists, string.Empty));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
