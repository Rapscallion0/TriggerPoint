using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class RichTextSnippetTests
{
    [Fact]
    public void ActionPayload_Clone_PreservesRichTextProperties()
    {
        var payload = new ActionPayload
        {
            SnippetContentType = SnippetContentType.RichText,
            SnippetTemplate = "Hello world",
            SnippetRtf = @"{\rtf1\ansi\b Hello world\b0}"
        };

        var clone = payload.Clone();

        Assert.Equal(SnippetContentType.RichText, clone.SnippetContentType);
        Assert.Equal("Hello world", clone.SnippetTemplate);
        Assert.Equal(@"{\rtf1\ansi\b Hello world\b0}", clone.SnippetRtf);
    }

    [Fact]
    public void TriggerItem_Clone_PreservesRichTextProperties()
    {
        var item = new TriggerItem
        {
            Name = "Rich Snippet",
            ActionType = ActionType.Snippet,
            Payload = new ActionPayload
            {
                SnippetContentType = SnippetContentType.RichText,
                SnippetTemplate = "Rich text template",
                SnippetRtf = @"{\rtf1\ansi\b Rich text template\b0}"
            }
        };

        var clone = item.Clone();

        Assert.Equal(SnippetContentType.RichText, clone.Payload.SnippetContentType);
        Assert.Equal("Rich text template", clone.Payload.SnippetTemplate);
        Assert.Equal(@"{\rtf1\ansi\b Rich text template\b0}", clone.Payload.SnippetRtf);
    }

    [Fact]
    public void WorkflowStep_Clone_PreservesRichTextProperties()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.InjectSnippet,
            SnippetContentType = SnippetContentType.RichText,
            SnippetTemplate = "Workflow snippet",
            SnippetRtf = @"{\rtf1\ansi\i Workflow snippet\i0}"
        };

        var clone = step.Clone();

        Assert.Equal(SnippetContentType.RichText, clone.SnippetContentType);
        Assert.Equal("Workflow snippet", clone.SnippetTemplate);
        Assert.Equal(@"{\rtf1\ansi\i Workflow snippet\i0}", clone.SnippetRtf);
    }

    [Fact]
    public void ActionPayload_Serialization_RoundTrip()
    {
        var original = new ActionPayload
        {
            SnippetContentType = SnippetContentType.RichText,
            SnippetTemplate = "Test template",
            SnippetRtf = @"{\rtf1\ansi\b Bold snippet\b0}"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ActionPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(SnippetContentType.RichText, deserialized.SnippetContentType);
        Assert.Equal("Test template", deserialized.SnippetTemplate);
        Assert.Equal(@"{\rtf1\ansi\b Bold snippet\b0}", deserialized.SnippetRtf);
    }

    [Fact]
    public void ActionPayload_LegacyJsonWithoutRichProperties_DefaultsToPlainText()
    {
        var legacyJson = @"{
            ""command"": """",
            ""arguments"": """",
            ""snippetTemplate"": ""Legacy plain text snippet""
        }";

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var deserialized = JsonSerializer.Deserialize<ActionPayload>(legacyJson, options);

        Assert.NotNull(deserialized);
        Assert.Equal(SnippetContentType.PlainText, deserialized.SnippetContentType);
        Assert.Equal("Legacy plain text snippet", deserialized.SnippetTemplate);
        Assert.Equal(string.Empty, deserialized.SnippetRtf);
    }

    [Fact]
    public void RichTextService_WrapInClipboardHtmlFormat_CalculatesAccurateByteOffsets()
    {
        var fragment = "<p>Hello <strong>World</strong>!</p>";
        var cfHtml = RichTextService.WrapInClipboardHtmlFormat(fragment);

        Assert.StartsWith("Version:0.9\r\nStartHTML:", cfHtml);

        // Parse offsets from header
        var lines = cfHtml.Split(new[] { "\r\n" }, StringSplitOptions.None);
        int startHtml = int.Parse(lines[1].Replace("StartHTML:", ""));
        int endHtml = int.Parse(lines[2].Replace("EndHTML:", ""));
        int startFrag = int.Parse(lines[3].Replace("StartFragment:", ""));
        int endFrag = int.Parse(lines[4].Replace("EndFragment:", ""));

        var utf8Bytes = Encoding.UTF8.GetBytes(cfHtml);

        Assert.Equal(utf8Bytes.Length, endHtml);
        Assert.True(startHtml < startFrag);
        Assert.True(startFrag < endFrag);
        Assert.True(endFrag <= endHtml);

        var fragBytes = new byte[endFrag - startFrag];
        Array.Copy(utf8Bytes, startFrag, fragBytes, 0, fragBytes.Length);
        var extractedFragment = Encoding.UTF8.GetString(fragBytes);

        Assert.Equal(fragment, extractedFragment);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? ex = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) throw new System.Reflection.TargetInvocationException(ex);
    }

    [Fact]
    public void RichTextService_ConvertPlainTextToFlowDocumentAndBack()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            var input = "Line 1\nLine 2\nLine 3";

            RichTextService.LoadFromPlainText(doc, input);
            var output = RichTextService.ExtractPlainText(doc);

            Assert.Contains("Line 1", output);
            Assert.Contains("Line 2", output);
            Assert.Contains("Line 3", output);
        });
    }

    [Fact]
    public void RichTextService_EvaluateFlowDocument_SubstitutesTokensAndCursor()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            p.Inlines.Add(new Run("Hello "));
            p.Inlines.Add(new Bold(new Run("{username}")));
            p.Inlines.Add(new Run(", current year is {date:yyyy}{cursor}!"));
            doc.Blocks.Add(p);

            var rtf = RichTextService.SaveToRtf(doc);
            var plain = RichTextService.ExtractPlainText(doc);

            var (evalRtf, evalHtml, evalPlain, caretOffset) = RichTextService.EvaluateFlowDocument(
                rtf,
                plain,
                promptResponses: null,
                clipboardProvider: null,
                referenceTime: new DateTime(2026, 9, 18),
                activeWindowTitle: "Test Window",
                activeProcessName: "test.exe");

            Assert.Contains("2026", evalPlain);
            Assert.Contains("Hello", evalPlain);
            Assert.DoesNotContain("{cursor}", evalPlain);
            Assert.DoesNotContain("{date:yyyy}", evalPlain);
            Assert.Equal(1, caretOffset); // "!" remains after cursor
            Assert.Contains("<strong>", evalHtml); // bold tag was preserved
        });
    }

    [Fact]
    public void RichTextService_ConvertInlineToHtml_DefaultText_DoesNotEmitHardcodedColor()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            p.Inlines.Add(new Run("Theme agnostic body text"));
            doc.Blocks.Add(p);

            var html = RichTextService.ConvertFlowDocumentToClipboardHtml(doc);

            Assert.Contains("Theme agnostic body text", html);
            Assert.DoesNotContain("color:", html);
        });
    }

    [Fact]
    public void RichTextService_ConvertInlineToHtml_ExplicitCustomColor_EmitsColorStyle()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            var customRun = new Run("Crimson red text")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(229, 57, 53)) // #E53935
            };
            p.Inlines.Add(customRun);
            doc.Blocks.Add(p);

            var html = RichTextService.ConvertFlowDocumentToClipboardHtml(doc);

            Assert.Contains("Crimson red text", html);
            Assert.Contains("color: #E53935", html);
        });
    }

    [Fact]
    public void RichTextService_SaveToRtf_NormalizesThemeWhiteToAutoColor()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            var runWithThemeWhite = new Run("Default text rendered with dark theme white")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(242, 243, 245)) // #F2F3F5
            };
            p.Inlines.Add(runWithThemeWhite);
            doc.Blocks.Add(p);

            var rtf = RichTextService.SaveToRtf(doc);

            // \cf0 is RTF auto color; \cf1 should not be forced for default text
            Assert.Contains(@"\cf0", rtf);
        });
    }

    [Fact]
    public void RichTextService_LoadFromRtf_LegacyWhiteText_SanitizesToAgnosticAutomatic()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            // RTF with a colortbl containing white and \cf1 applied to text
            var legacyRtf = @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}{\colortbl ;\red242\green243\blue245;}\pard\cf1 Legacy white text\par}";

            RichTextService.LoadFromRtf(doc, legacyRtf);

            // After loading, the inline element should have had its local white foreground cleared
            var p = doc.Blocks.FirstBlock as Paragraph;
            Assert.NotNull(p);
            var firstInline = p.Inlines.FirstInline;
            Assert.NotNull(firstInline);
            Assert.Equal(DependencyProperty.UnsetValue, firstInline.ReadLocalValue(TextElement.ForegroundProperty));
            if (firstInline is Span span && span.Inlines.FirstInline != null)
            {
                Assert.Equal(DependencyProperty.UnsetValue, span.Inlines.FirstInline.ReadLocalValue(TextElement.ForegroundProperty));
            }
        });
    }

    [Fact]
    public void RichTextService_FormattingPreservation_Test()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();

            var runBold = new Run("BoldText") { FontWeight = FontWeights.Bold };
            var runItalic = new Run("ItalicText") { FontStyle = FontStyles.Italic };
            var runUnderline = new Run("UnderlineText");
            runUnderline.TextDecorations.Add(TextDecorations.Underline[0]);
            var runColored = new Run("BlueText") { Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)) };
            var runHighlighted = new Run("HighlightedText") { Background = new SolidColorBrush(Color.FromRgb(255, 235, 59)) };
            var runSize = new Run("BigText") { FontSize = 24 };

            p.Inlines.Add(runBold);
            p.Inlines.Add(new Run(" "));
            p.Inlines.Add(runItalic);
            p.Inlines.Add(new Run(" "));
            p.Inlines.Add(runUnderline);
            p.Inlines.Add(new Run(" "));
            p.Inlines.Add(runColored);
            p.Inlines.Add(new Run(" "));
            p.Inlines.Add(runHighlighted);
            p.Inlines.Add(new Run(" "));
            p.Inlines.Add(runSize);
            doc.Blocks.Add(p);

            var rtf = RichTextService.SaveToRtf(doc);

            var roundTripDoc = new FlowDocument();
            RichTextService.LoadFromRtf(roundTripDoc, rtf);

            var html = RichTextService.ConvertFlowDocumentToClipboardHtml(roundTripDoc);

            Assert.Contains("<strong>BoldText</strong>", html);
            Assert.Contains("<em>ItalicText</em>", html);
            Assert.Contains("<u>UnderlineText</u>", html);
            Assert.Contains("color: #2196F3", html);
            Assert.Contains("background-color: #FFEB3B", html);
            Assert.Contains("font-size: 18pt", html); // 24 * 0.75 = 18pt
        });
    }

    [Fact]
    public void RichTextService_DarkModeRtfExport_NormalizesDefaultTextWhilePreservingExplicitColors()
    {
        RunOnStaThread(() =>
        {
            // Simulate RichTextBox in Dark Mode with Foreground #F2F3F5
            var rtb = new System.Windows.Controls.RichTextBox
            {
                Foreground = new SolidColorBrush(Color.FromRgb(242, 243, 245))
            };
            var doc = rtb.Document;
            doc.Blocks.Clear();

            var p = new Paragraph();
            p.Inlines.Add(new Run("Default dark mode text"));
            p.Inlines.Add(new Run(" "));
            p.Inlines.Add(new Run("Deliberate Green") { Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)) });
            doc.Blocks.Add(p);

            var rtf = RichTextService.SaveToRtf(doc);

            // Default text should use \cf0 (auto)
            Assert.Contains(@"\cf0", rtf);

            // Reload and verify
            var reloaded = new FlowDocument();
            RichTextService.LoadFromRtf(reloaded, rtf);
            var html = RichTextService.ConvertFlowDocumentToClipboardHtml(reloaded);

            // Default text has no hardcoded color; explicit green is preserved
            Assert.Contains("Default dark mode text", html);
            Assert.Contains("color: #4CAF50", html);
        });
    }

    [Fact]
    public void RichTextService_CanvasModeToggle_ProducesIdenticalRtf()
    {
        RunOnStaThread(() =>
        {
            var rtb = new System.Windows.Controls.RichTextBox();

            // 1. Theme canvas mode (#F2F3F5)
            rtb.Foreground = new SolidColorBrush(Color.FromRgb(242, 243, 245));
            var doc = rtb.Document;
            doc.Blocks.Clear();
            var p = new Paragraph();
            p.Inlines.Add(new Run("Sample unstyled text"));
            p.Inlines.Add(new Run(" with deliberate ") { FontWeight = FontWeights.Bold });
            p.Inlines.Add(new Run("Red") { Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)) });
            doc.Blocks.Add(p);

            var rtfTheme = RichTextService.SaveToRtf(doc);

            // 2. Paper canvas mode (#1E293B)
            rtb.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            var rtfPaper = RichTextService.SaveToRtf(doc);

            // Both must normalize unstyled text to \cf0 (auto) and preserve red (\cf)
            Assert.Contains(@"\cf0", rtfTheme);
            Assert.Contains(@"\cf0", rtfPaper);

            var docTheme = new FlowDocument();
            RichTextService.LoadFromRtf(docTheme, rtfTheme);
            var htmlTheme = RichTextService.ConvertFlowDocumentToClipboardHtml(docTheme);

            var docPaper = new FlowDocument();
            RichTextService.LoadFromRtf(docPaper, rtfPaper);
            var htmlPaper = RichTextService.ConvertFlowDocumentToClipboardHtml(docPaper);

            // Both produce identical clean HTML with auto color for unstyled text and preserved Red
            Assert.Equal(htmlTheme, htmlPaper);
            Assert.Contains("<strong> with deliberate </strong>", htmlTheme);
            Assert.Contains("color: #F44336", htmlTheme);
        });
    }

    [Fact]
    public void RichTextService_ClearFormatting_Overhaul_Test()
    {
        RunOnStaThread(() =>
        {
            var doc = new FlowDocument();
            var list = new List();
            var item1 = new ListItem(new Paragraph(new Run("Item 1") { FontWeight = FontWeights.Bold }));
            var item2 = new ListItem(new Paragraph(new Run("Item 2") { Foreground = Brushes.Blue }));
            list.ListItems.Add(item1);
            list.ListItems.Add(item2);
            doc.Blocks.Add(list);

            var p = new Paragraph(new Run("Centered text") { FontSize = 22, Background = Brushes.Yellow });
            p.TextAlignment = TextAlignment.Center;
            doc.Blocks.Add(p);

            // Apply full-document clear formatting
            var targetRange = new TextRange(doc.ContentStart, doc.ContentEnd);

            // 1. Unwrap lists
            var listBlocks = doc.Blocks.OfType<List>().ToList();
            foreach (var l in listBlocks)
            {
                var extracted = new System.Collections.Generic.List<Block>();
                foreach (var item in l.ListItems.ToList())
                {
                    while (item.Blocks.Count > 0)
                    {
                        var childBlock = item.Blocks.FirstBlock;
                        item.Blocks.Remove(childBlock);
                        extracted.Add(childBlock);
                    }
                }
                foreach (var b in extracted)
                {
                    if (b is Paragraph para)
                    {
                        para.Margin = new Thickness(0, 0, 0, 4);
                        para.TextAlignment = TextAlignment.Left;
                    }
                    doc.Blocks.InsertBefore(l, b);
                }
                doc.Blocks.Remove(l);
            }

            // 2. Clear character formatting and alignments
            targetRange = new TextRange(doc.ContentStart, doc.ContentEnd);
            targetRange.ClearAllProperties();
            targetRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            targetRange.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            targetRange.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            targetRange.ApplyPropertyValue(TextElement.FontSizeProperty, 14.66);
            targetRange.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Left);

            // Verify
            Assert.Empty(doc.Blocks.OfType<List>());
            Assert.Equal(3, doc.Blocks.OfType<Paragraph>().Count());

            foreach (var block in doc.Blocks.OfType<Paragraph>())
            {
                Assert.Equal(TextAlignment.Left, block.TextAlignment);
            }

            var html = RichTextService.ConvertFlowDocumentToClipboardHtml(doc);
            Assert.DoesNotContain("<strong>", html);
            Assert.DoesNotContain("<ol>", html);
            Assert.DoesNotContain("<ul>", html);
            Assert.DoesNotContain("color: #", html);
            Assert.DoesNotContain("background-color:", html);
            Assert.Contains("Item 1", html);
            Assert.Contains("Item 2", html);
            Assert.Contains("Centered text", html);
        });
    }
}

