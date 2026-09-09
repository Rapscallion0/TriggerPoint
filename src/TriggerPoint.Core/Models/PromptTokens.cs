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
    PromptDatePicker
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
}
