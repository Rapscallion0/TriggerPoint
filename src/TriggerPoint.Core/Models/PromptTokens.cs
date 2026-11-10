namespace TriggerPoint.Core.Models;

public sealed record ChoiceOption(string DisplayName, string Value)
{
    public override string ToString() => DisplayName;
}

public enum TokenType
{
    RawText,
    Date,
    Time,
    DateTime,
    Clipboard,
    Cursor,
    PromptText,
    PromptNumber,
    PromptChoice,
    PromptMultiline,
    PromptDatePicker,
    PromptCheckbox
}

public sealed class PromptToken
{
    public TokenType Type { get; set; }
    public string RawTag { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public double? MinNumber { get; set; }
    public double? MaxNumber { get; set; }
    public List<ChoiceOption> Choices { get; set; } = [];
    public string DefaultValue { get; set; } = string.Empty;

    public static bool IsTruthyBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        return v.Equals("true", System.StringComparison.OrdinalIgnoreCase) ||
               v.Equals("1", System.StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", System.StringComparison.OrdinalIgnoreCase) ||
               v.Equals("checked", System.StringComparison.OrdinalIgnoreCase) ||
               v.Equals("on", System.StringComparison.OrdinalIgnoreCase);
    }
}
