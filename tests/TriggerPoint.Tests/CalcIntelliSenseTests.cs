using System.Linq;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class CalcIntelliSenseTests
{
    [Fact]
    public void EmptyInput_ReturnsTopKeywords()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("");
        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Text == "pass");
        Assert.Contains(suggestions, s => s.Text == "phrase");
        Assert.Contains(suggestions, s => s.Text == "guid");
        Assert.Contains(suggestions, s => s.Text == "roll");
    }

    [Fact]
    public void PasswordQuery_SuggestsModifiers()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("= pass ");
        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Text == "nosym");
        Assert.Contains(suggestions, s => s.Text == "leet");
        Assert.Contains(suggestions, s => s.Text == "b4");
        Assert.Contains(suggestions, s => s.Text == "safe");
    }

    [Fact]
    public void PassphraseQuery_SuggestsModifiers()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("= phrase ");
        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Text == "1337" || s.Text == "leet");
        Assert.Contains(suggestions, s => s.Text == "title");
        Assert.Contains(suggestions, s => s.Text.StartsWith("sep"));
    }

    [Fact]
    public void GuidQuery_SuggestsModifiers()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("= guid ");
        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Text == "v7");
        Assert.Contains(suggestions, s => s.Text == "upper");
        Assert.Contains(suggestions, s => s.Text == "nohyphen");
    }

    [Fact]
    public void RollQuery_SuggestsDiceExpressions()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("= roll ");
        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Text == "3d6+2");
        Assert.Contains(suggestions, s => s.Text.Contains("1d20"));
    }

    [Fact]
    public void PartialModifierTyping_FiltersCorrectly()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("= pass no");
        Assert.Contains(suggestions, s => s.Text == "nosym");
    }

    [Fact]
    public void ApplySuggestion_ReplacesPartialToken()
    {
        var item = new CalcIntelliSenseItem
        {
            Text = "nosym",
            DisplayText = "nosym",
            Description = "Exclude special symbols",
            Kind = CalcIntelliSenseKind.Modifier
        };

        var result = CalcIntelliSenseService.ApplySuggestion("= pass no", item);
        Assert.Equal("= pass nosym", result);
    }

    [Fact]
    public void ApplySuggestion_AppendsWhenTrailingSpace()
    {
        var item = new CalcIntelliSenseItem
        {
            Text = "leet",
            DisplayText = "leet",
            Description = "Leetspeak",
            Kind = CalcIntelliSenseKind.Modifier
        };

        var result = CalcIntelliSenseService.ApplySuggestion("= pass 16 ", item);
        Assert.Equal("= pass 16 leet", result);
    }

    [Fact]
    public void Roll_TokenAutocomplete_ReplacesDiceCount()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("=roll 2");
        Assert.NotEmpty(suggestions);
        var item2d6 = suggestions.FirstOrDefault(s => s.Text == "2d6");
        Assert.NotNull(item2d6);
        Assert.Equal("2", item2d6.TokenToReplace);

        var applied = CalcIntelliSenseService.ApplySuggestion("=roll 2", item2d6);
        Assert.Equal("= roll 2d6", applied);
    }

    [Fact]
    public void Roll_TokenAutocomplete_ReplacesDiceWithBonus()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("=roll 3d6+");
        Assert.NotEmpty(suggestions);
        var itemBonus = suggestions.FirstOrDefault(s => s.Text == "3d6+2");
        Assert.NotNull(itemBonus);

        var applied = CalcIntelliSenseService.ApplySuggestion("=roll 3d6+", itemBonus);
        Assert.Equal("= roll 3d6+2", applied);
    }

    [Fact]
    public void Password_TokenAutocomplete_ReplacesLengthAndAppendsSpace()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("=pass 1");
        Assert.NotEmpty(suggestions);
        var item16 = suggestions.FirstOrDefault(s => s.Text == "16");
        Assert.NotNull(item16);
        Assert.Equal("1", item16.TokenToReplace);

        var applied = CalcIntelliSenseService.ApplySuggestion("=pass 1", item16);
        Assert.Equal("= pass 16 ", applied);
    }

    [Fact]
    public void Password_TokenAutocomplete_ReplacesModifier()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("=pass 16 n");
        Assert.NotEmpty(suggestions);
        var itemNosym = suggestions.FirstOrDefault(s => s.Text == "nosym");
        Assert.NotNull(itemNosym);
        Assert.Equal("n", itemNosym.TokenToReplace);

        var applied = CalcIntelliSenseService.ApplySuggestion("=pass 16 n", itemNosym);
        Assert.Equal("= pass 16 nosym ", applied);
    }

    [Fact]
    public void Keyword_Completion_AppendsSpace()
    {
        var suggestions = CalcIntelliSenseService.GetSuggestions("=rol");
        var rollKeyword = suggestions.FirstOrDefault(s => s.Text == "roll");
        Assert.NotNull(rollKeyword);

        var applied = CalcIntelliSenseService.ApplySuggestion("=rol", rollKeyword);
        Assert.Equal("= roll ", applied);
    }

    [Fact]
    public void GetSignature_ReturnsAccurateSignatures()
    {
        Assert.Contains("roll", CalcIntelliSenseService.GetSignature("=roll 2d6"));
        Assert.Contains("pass", CalcIntelliSenseService.GetSignature("=pass 16"));
        Assert.Contains("phrase", CalcIntelliSenseService.GetSignature("=phrase 4"));
        Assert.Contains("guid", CalcIntelliSenseService.GetSignature("=guid"));
    }
}
