using System;
using System.Linq;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class CalculatorGeneratorTests
{
    #region GUID Tests

    [Fact]
    public void Guid_Standard_ReturnsValidHyphenatedGuid()
    {
        var result = QuickCalculatorService.TryEvaluate("guid");
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result.FormattedResult, out var parsedGuid));
        Assert.Equal(36, result.FormattedResult.Length);
        Assert.Equal(result.FormattedResult.ToLowerInvariant(), result.FormattedResult);
        Assert.NotNull(result.Alternatives);
        Assert.True(result.Alternatives.Count >= 9); // Formats + candidates
    }

    [Fact]
    public void Guid_Uppercase_HasOnlyUppercaseLettersAndHyphens()
    {
        var result = QuickCalculatorService.TryEvaluate("guid upper");
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result.FormattedResult, out _));
        Assert.Equal(result.FormattedResult.ToUpperInvariant(), result.FormattedResult);
    }

    [Fact]
    public void Guid_NoHyphens_HasExact32HexChars()
    {
        var result = QuickCalculatorService.TryEvaluate("guid nohyphens");
        Assert.NotNull(result);
        Assert.Equal(32, result.FormattedResult.Length);
        Assert.DoesNotContain("-", result.FormattedResult);
    }

    [Fact]
    public void Guid_Braces_HasCurlyBraces()
    {
        var result = QuickCalculatorService.TryEvaluate("guid braces");
        Assert.NotNull(result);
        Assert.StartsWith("{", result.FormattedResult);
        Assert.EndsWith("}", result.FormattedResult);
        Assert.Equal(38, result.FormattedResult.Length);
    }

    [Fact]
    public void Guid_Base64_Returns22CharUrlSafeString()
    {
        var result = QuickCalculatorService.TryEvaluate("guid base64");
        Assert.NotNull(result);
        Assert.Equal(22, result.FormattedResult.Length);
        Assert.DoesNotContain("/", result.FormattedResult);
        Assert.DoesNotContain("+", result.FormattedResult);
    }

    [Fact]
    public void Guid_UuidV7_ReturnsValidGuid()
    {
        var result = QuickCalculatorService.TryEvaluate("guid v7");
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result.FormattedResult, out _));
        // In UUIDv7, version nibble at index 14 is '7'
        Assert.Equal('7', result.FormattedResult[14]);
    }

    [Fact]
    public void Guid_CustomCandidateCount_GeneratesRequestedCandidates()
    {
        var settings = new AppSettings { GeneratorCandidateCount = 5 };
        var result = QuickCalculatorService.TryEvaluate("guid count 4", settings);
        Assert.NotNull(result);
        Assert.NotNull(result.Alternatives);
        var candidates = result.Alternatives.Where(a => a.Category == "Candidates").ToList();
        Assert.Equal(3, candidates.Count); // 1 primary + 3 alternatives = 4 total candidates
    }

    #endregion

    #region Password Tests

    [Fact]
    public void Password_Default_ReturnsConfiguredLengthWithEntropy()
    {
        var settings = new AppSettings { DefaultPasswordLength = 16 };
        var result = QuickCalculatorService.TryEvaluate("pass", settings);
        Assert.NotNull(result);
        Assert.Equal(16, result.FormattedResult.Length);
        Assert.Contains("Entropy:", result.Description);
        Assert.NotNull(result.Alternatives);
        Assert.Equal(9, result.Alternatives.Count); // 1 primary + 9 alternatives = 10 candidates
    }

    [Fact]
    public void Password_CustomLength_MatchesRequestedLength()
    {
        var result = QuickCalculatorService.TryEvaluate("pass 24");
        Assert.NotNull(result);
        Assert.Equal(24, result.FormattedResult.Length);
    }

    [Fact]
    public void Password_NoSymbols_ContainsOnlyAlphanumeric()
    {
        for (int i = 0; i < 5; i++)
        {
            var result = QuickCalculatorService.TryEvaluate("pass 32 nosym");
            Assert.NotNull(result);
            Assert.True(result.FormattedResult.All(char.IsLetterOrDigit));
        }
    }

    [Fact]
    public void Password_Safe_ExcludesAmbiguousGlyphs()
    {
        char[] ambiguous = ['0', 'O', 'o', '1', 'l', 'I', '|'];
        for (int i = 0; i < 10; i++)
        {
            var result = QuickCalculatorService.TryEvaluate("pass 64 safe");
            Assert.NotNull(result);
            foreach (char c in ambiguous)
            {
                Assert.DoesNotContain(c, result.FormattedResult);
            }
        }
    }

    [Fact]
    public void Password_Pin_ContainsOnlyDigits()
    {
        var result = QuickCalculatorService.TryEvaluate("pin 6");
        Assert.NotNull(result);
        Assert.Equal(6, result.FormattedResult.Length);
        Assert.True(result.FormattedResult.All(char.IsDigit));
    }

    [Fact]
    public void Password_CandidateBatch_AllCandidatesMatchLengthAndRules()
    {
        var settings = new AppSettings { GeneratorCandidateCount = 10 };
        var result = QuickCalculatorService.TryEvaluate("pass 20 nosym", settings);
        Assert.NotNull(result);
        Assert.NotNull(result.Alternatives);
        Assert.Equal(9, result.Alternatives.Count);

        foreach (var alt in result.Alternatives)
        {
            Assert.Equal(20, alt.FormattedValue.Length);
            Assert.True(alt.FormattedValue.All(char.IsLetterOrDigit));
        }
    }

    #endregion

    #region Passphrase Tests

    [Fact]
    public void Passphrase_Default_ReturnsConfiguredWordCount()
    {
        var settings = new AppSettings { DefaultPassphraseWordCount = 4 };
        var result = QuickCalculatorService.TryEvaluate("passphrase", settings);
        Assert.NotNull(result);
        string[] parts = result.FormattedResult.Split('-');
        Assert.Equal(4, parts.Length);
        Assert.NotNull(result.Alternatives);
        Assert.Equal(9, result.Alternatives.Count);
    }

    [Fact]
    public void Phrase_ExplicitEqualsAnd1337_GeneratesExpectedResult()
    {
        var result = QuickCalculatorService.TryEvaluate("= phrase 4 1337");
        Assert.NotNull(result);
        Assert.Equal("phrase 4 1337", result.Expression);
        string[] parts = result.FormattedResult.Split('-');
        Assert.Equal(4, parts.Length);
        // Leet mode is active in description
        Assert.Contains("Leet: Visual", result.Description);
    }

    [Fact]
    public void Phrase_DirectAlias_WorksIdenticallyToPassphrase()
    {
        var result = QuickCalculatorService.TryEvaluate("phrase 5");
        Assert.NotNull(result);
        string[] parts = result.FormattedResult.Split('-');
        Assert.Equal(5, parts.Length);
    }

    [Fact]
    public void Phrase_PhoneticB4_SetsPhoneticLeetMode()
    {
        var result = QuickCalculatorService.TryEvaluate("phrase 3 b4");
        Assert.NotNull(result);
        Assert.Contains("Leet: Phonetic", result.Description);
    }

    [Fact]
    public void Passphrase_Max12Words_Generates12Words()
    {
        var result = QuickCalculatorService.TryEvaluate("passphrase 12");
        Assert.NotNull(result);
        string[] parts = result.FormattedResult.Split('-');
        Assert.Equal(12, parts.Length);
    }

    [Fact]
    public void Passphrase_Separators_SpaceAndUnderscore()
    {
        var spaceRes = QuickCalculatorService.TryEvaluate("passphrase 3 space");
        Assert.NotNull(spaceRes);
        Assert.Equal(3, spaceRes.FormattedResult.Split(' ').Length);

        var underRes = QuickCalculatorService.TryEvaluate("passphrase 3 _");
        Assert.NotNull(underRes);
        Assert.Equal(3, underRes.FormattedResult.Split('_').Length);
    }

    [Fact]
    public void Passphrase_TitleCase_CapitalizesWords()
    {
        var result = QuickCalculatorService.TryEvaluate("passphrase 3 title");
        Assert.NotNull(result);
        string[] parts = result.FormattedResult.Split('-');
        Assert.All(parts, p => Assert.True(char.IsUpper(p[0])));
    }

    [Fact]
    public void Passphrase_LeetVisual_SubstitutesLettersWithNumbers()
    {
        var config = new GeneratorEngine.PassphraseConfig(
            WordCount: 5,
            Separator: PassphraseSeparatorOption.Hyphen,
            Capitalize: false,
            IncludeNumber: false,
            LeetMode: PassphraseLeetMode.Visual,
            LeetIntensity: PassphraseLeetIntensity.Aggressive);

        string phrase = GeneratorEngine.GenerateSinglePassphrase(config);
        Assert.NotNull(phrase);
        // Under aggressive visual leet, any word with a, e, i, o, s, t, b will have numbers
        Assert.Contains(phrase, char.IsDigit);
    }

    [Fact]
    public void Passphrase_LeetPhonetic_SubstitutesPhonetics()
    {
        string sub = GeneratorEngine.ApplyLeetspeak("before", PassphraseLeetMode.Phonetic, PassphraseLeetIntensity.Aggressive);
        Assert.Equal("b4", sub);

        string gr8 = GeneratorEngine.ApplyLeetspeak("great", PassphraseLeetMode.Phonetic, PassphraseLeetIntensity.Aggressive);
        Assert.Equal("gr8", gr8);
    }

    #endregion

    #region Hashes & Encodings Tests

    [Fact]
    public void Hashes_Sha256_ComputesKnownDigest()
    {
        var result = QuickCalculatorService.TryEvaluate("sha256 hello");
        Assert.NotNull(result);
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", result.FormattedResult);
        Assert.NotNull(result.Alternatives);
        Assert.Contains(result.Alternatives, a => a.Label == "MD5");
        Assert.Contains(result.Alternatives, a => a.Label == "CRC32");
    }

    [Fact]
    public void Hashes_Md5_ComputesKnownDigest()
    {
        var result = QuickCalculatorService.TryEvaluate("md5 hello");
        Assert.NotNull(result);
        Assert.Equal("5d41402abc4b2a76b9719d911017c592", result.FormattedResult);
    }

    [Fact]
    public void Encoding_Base64_EncodeAndDecode()
    {
        var enc = QuickCalculatorService.TryEvaluate("base64 hello world");
        Assert.NotNull(enc);
        Assert.Equal("aGVsbG8gd29ybGQ=", enc.FormattedResult);

        var dec = QuickCalculatorService.TryEvaluate("base64 decode aGVsbG8gd29ybGQ=");
        Assert.NotNull(dec);
        Assert.Equal("hello world", dec.FormattedResult);
    }

    [Fact]
    public void Encoding_Slug_CreatesUrlSafeSlug()
    {
        var result = QuickCalculatorService.TryEvaluate("slug My Awesome Article Title!");
        Assert.NotNull(result);
        Assert.Equal("my-awesome-article-title", result.FormattedResult);
    }

    #endregion

    #region Random Numbers & Dice Tests

    [Fact]
    public void Dice_Roll2d6_ReturnsValueBetween2And12()
    {
        for (int i = 0; i < 10; i++)
        {
            var result = QuickCalculatorService.TryEvaluate("roll 2d6");
            Assert.NotNull(result);
            int val = int.Parse(result.FormattedResult);
            Assert.InRange(val, 2, 12);
            Assert.Contains("Roll 2d6:", result.Description);
        }
    }

    [Fact]
    public void Dice_RollWithPositiveModifier_CalculatesCorrectly()
    {
        for (int i = 0; i < 10; i++)
        {
            // 3d6 base is 3..18, +2 makes it 5..20
            var result = QuickCalculatorService.TryEvaluate("= roll 3d6+2");
            Assert.NotNull(result);
            int val = int.Parse(result.FormattedResult);
            Assert.InRange(val, 5, 20);
            Assert.Contains("+ 2 =", result.Description);
            Assert.NotNull(result.Alternatives);
            Assert.Equal(10, result.Alternatives.Count);
        }
    }

    [Fact]
    public void Dice_RollWithNegativeModifier_CalculatesCorrectly()
    {
        for (int i = 0; i < 10; i++)
        {
            // 1d20 base is 1..20, -4 makes it -3..16
            var result = QuickCalculatorService.TryEvaluate("roll 1d20-4");
            Assert.NotNull(result);
            int val = int.Parse(result.FormattedResult);
            Assert.InRange(val, -3, 16);
            Assert.Contains("- 4 =", result.Description);
        }
    }

    [Fact]
    public void Random_Range_ReturnsWithinBounds()
    {
        for (int i = 0; i < 10; i++)
        {
            var result = QuickCalculatorService.TryEvaluate("rand 50 75");
            Assert.NotNull(result);
            int val = int.Parse(result.FormattedResult);
            Assert.InRange(val, 50, 75);
        }
    }

    [Fact]
    public void Coin_Flip_ReturnsHeadsOrTails()
    {
        var result = QuickCalculatorService.TryEvaluate("coin");
        Assert.NotNull(result);
        Assert.True(result.FormattedResult is "Heads" or "Tails");
    }

    #endregion

    #region Mock Data & Lorem Tests

    [Fact]
    public void MockData_FakeEmail_ContainsAtAndDot()
    {
        var result = QuickCalculatorService.TryEvaluate("fake email");
        Assert.NotNull(result);
        Assert.Contains("@", result.FormattedResult);
        Assert.Contains(".", result.FormattedResult);
    }

    [Fact]
    public void MockData_FakeIp_ContainsFourOctets()
    {
        var result = QuickCalculatorService.TryEvaluate("fake ipv4");
        Assert.NotNull(result);
        string[] octets = result.FormattedResult.Split('.');
        Assert.Equal(4, octets.Length);
        Assert.All(octets, o => Assert.True(int.TryParse(o, out int b) && b >= 0 && b <= 255));
    }

    [Fact]
    public void Lorem_Words_ReturnsExactWordCount()
    {
        var result = QuickCalculatorService.TryEvaluate("lorem 25 words");
        Assert.NotNull(result);
        string[] words = result.FormattedResult.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(25, words.Length);
    }

    [Fact]
    public void Lorem_Paragraphs_SeparatedByDoubleNewlines()
    {
        var result = QuickCalculatorService.TryEvaluate("lorem 3 paragraphs");
        Assert.NotNull(result);
        string[] paras = result.FormattedResult.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, paras.Length);
    }

    #endregion
}
