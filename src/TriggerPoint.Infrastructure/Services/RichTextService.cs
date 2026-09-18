using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;

namespace TriggerPoint.Infrastructure.Services;

public static class RichTextService
{
    private static readonly Regex TokenRegex = new(@"\{(?<tag>[^{}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Loads RTF markup into a FlowDocument, sanitizing any legacy hardcoded theme colors.
    /// </summary>
    public static void LoadFromRtf(FlowDocument doc, string rtf)
    {
        doc.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(rtf)) return;

        try
        {
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
            range.Load(stream, DataFormats.Rtf);
            SanitizeThemeAgnosticElements(doc);
        }
        catch
        {
            // If RTF parsing fails, fallback to loading as plain text
            LoadFromPlainText(doc, rtf);
        }
    }

    /// <summary>
    /// Exports the FlowDocument content to an RTF string, ensuring default text uses automatic color.
    /// </summary>
    public static string SaveToRtf(FlowDocument doc)
    {
        try
        {
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var stream = new MemoryStream();
            range.Save(stream, DataFormats.Rtf);
            var rtf = Encoding.UTF8.GetString(stream.ToArray());
            return NormalizeRtfThemeColors(rtf);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Normalizes RTF markup so that any legacy or default theme foreground color table entry maps to \cf0 (automatic).
    /// </summary>
    public static string NormalizeRtfThemeColors(string rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return rtf;

        // Check if RTF colortbl has entries. WPF may output with or without leading semicolon:
        // {\colortbl\red0\green0\blue0;...} or {\colortbl ;\red0\green0\blue0;...}
        var colorTblMatch = Regex.Match(rtf, @"\{\\colortbl(?<entries>[^}]+)\}");
        if (!colorTblMatch.Success) return rtf;

        var entriesStr = colorTblMatch.Groups["entries"].Value;
        bool hasLeadingSemicolon = entriesStr.TrimStart().StartsWith(";");

        var rawEntries = entriesStr.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var autoColorIndices = new HashSet<int>();

        for (int i = 0; i < rawEntries.Length; i++)
        {
            var entry = rawEntries[i];
            var m = Regex.Match(entry, @"\\red(?<r>\d+)\\green(?<g>\d+)\\blue(?<b>\d+)");
            if (m.Success &&
                int.TryParse(m.Groups["r"].Value, out int r) &&
                int.TryParse(m.Groups["g"].Value, out int g) &&
                int.TryParse(m.Groups["b"].Value, out int b))
            {
                // If the color entry is near-white (theme dark mode text, e.g. #F2F3F5) or paper canvas slate (#1E293B)
                if ((r >= 235 && g >= 235 && b >= 235) || (r == 30 && g == 41 && b == 59))
                {
                    int colorIndex = hasLeadingSemicolon ? (i + 1) : i;
                    autoColorIndices.Add(colorIndex);
                }
            }
        }

        if (autoColorIndices.Count == 0) return rtf;

        // Replace \cfN with \cf0 for the identified autoColorIndices
        var normalizedRtf = rtf;
        foreach (var idx in autoColorIndices)
        {
            if (idx == 0) continue;
            normalizedRtf = Regex.Replace(normalizedRtf, $@"\\cf{idx}(?=\s|\\|;|\b)", "\\cf0");
        }

        return normalizedRtf;
    }

    /// <summary>
    /// Sanitizes all elements in the FlowDocument so default text is theme-agnostic (local Foreground cleared).
    /// </summary>
    public static void SanitizeThemeAgnosticElements(FlowDocument doc)
    {
        doc.ClearValue(TextElement.ForegroundProperty);
        doc.ClearValue(TextElement.BackgroundProperty);

        foreach (var block in doc.Blocks)
        {
            SanitizeBlock(block);
        }
    }

    private static void SanitizeBlock(Block block)
    {
        block.ClearValue(TextElement.ForegroundProperty);

        switch (block)
        {
            case Paragraph p:
                foreach (var inline in p.Inlines)
                {
                    SanitizeInline(inline);
                }
                break;
            case List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks)
                    {
                        SanitizeBlock(child);
                    }
                }
                break;
            case Section sec:
                foreach (var child in sec.Blocks)
                {
                    SanitizeBlock(child);
                }
                break;
        }
    }

    private static void SanitizeInline(Inline inline)
    {
        if (inline is Run run)
        {
            if (IsAutomaticOrThemeColor(run.Foreground, run.Background, run))
            {
                run.ClearValue(TextElement.ForegroundProperty);
            }
        }
        else if (inline is Span span)
        {
            if (IsAutomaticOrThemeColor(span.Foreground, span.Background, span))
            {
                span.ClearValue(TextElement.ForegroundProperty);
            }
            foreach (var child in span.Inlines)
            {
                SanitizeInline(child);
            }
        }
    }

    public static bool IsAutomaticOrThemeColor(Brush? foreground, Brush? background, DependencyObject element)
    {
        if (element.ReadLocalValue(TextElement.ForegroundProperty) == DependencyProperty.UnsetValue)
            return true;

        if (foreground is not SolidColorBrush fg)
            return true;

        bool hasContrastBackground = background is SolidColorBrush bg && bg.Color.A > 0 && bg.Color != Colors.Transparent;

        if (!hasContrastBackground)
        {
            // If background is transparent/none, near-white (dark mode text) or dark slate (#1E293B, paper canvas text)
            // is ambient canvas text rather than an intentional contrasting choice.
            if ((fg.Color.R >= 235 && fg.Color.G >= 235 && fg.Color.B >= 235) ||
                (fg.Color.R == 30 && fg.Color.G == 41 && fg.Color.B == 59))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts plain text from a FlowDocument.
    /// </summary>
    public static string ExtractPlainText(FlowDocument doc)
    {
        try
        {
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            var text = range.Text ?? string.Empty;
            // WPF TextRange includes trailing \r\n
            return text.EndsWith("\r\n") ? text[..^2] : text;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Converts plain text into a basic FlowDocument with paragraphs and line breaks.
    /// </summary>
    public static void LoadFromPlainText(FlowDocument doc, string plainText)
    {
        doc.Blocks.Clear();
        if (string.IsNullOrEmpty(plainText)) return;

        var normalized = plainText.Replace("\r\n", "\n").Replace("\r", "\n");
        var paragraphs = normalized.Split('\n');

        foreach (var paraText in paragraphs)
        {
            var p = new Paragraph(new Run(paraText))
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            doc.Blocks.Add(p);
        }
    }

    /// <summary>
    /// Evaluates dynamic tokens in a FlowDocument and extracts cursor position if present.
    /// </summary>
    public static (string evaluatedRtf, string evaluatedHtml, string evaluatedPlainText, int caretOffset) EvaluateFlowDocument(
        string rtfContent,
        string plainTextFallback,
        IReadOnlyDictionary<string, string>? promptResponses,
        Func<Task<string>>? clipboardProvider,
        DateTime? referenceTime,
        string? activeWindowTitle,
        string? activeProcessName)
    {
        Func<(string, string, string, int)> evalAction = () =>
        {
            var doc = new FlowDocument();
            if (!string.IsNullOrWhiteSpace(rtfContent))
            {
                LoadFromRtf(doc, rtfContent);
            }
            else
            {
                LoadFromPlainText(doc, plainTextFallback);
            }

            // 1. Gather all tokens in the document
            var plainText = ExtractPlainText(doc);
            var matches = TokenRegex.Matches(plainText);

            // 2. Pre-evaluate token values asynchronously in caller or synchronously here
            var evaluatedTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var now = referenceTime ?? DateTime.Now;

            foreach (Match match in matches)
            {
                var rawTag = match.Groups["tag"].Value.Trim();
                var fullToken = match.Value;

                if (evaluatedTokens.ContainsKey(fullToken)) continue;

                if (string.Equals(rawTag, "cursor", StringComparison.OrdinalIgnoreCase))
                {
                    // Leave placeholder for caret processing
                    continue;
                }

                // Prompt responses
                if (promptResponses != null && promptResponses.TryGetValue(rawTag, out var promptVal))
                {
                    evaluatedTokens[fullToken] = promptVal;
                }
                else
                {
                    // Evaluate standard placeholder parser tokens
                    var evaluated = PlaceholderParser.EvaluateAsync(
                        fullToken,
                        clipboardProvider,
                        promptResponses,
                        now,
                        activeWindowTitle,
                        activeProcessName).GetAwaiter().GetResult();

                    evaluatedTokens[fullToken] = evaluated;
                }
            }

            // 3. Replace evaluated tokens in the FlowDocument
            foreach (var kvp in evaluatedTokens)
            {
                ReplaceTextInDocument(doc, kvp.Key, kvp.Value);
            }

            // 4. Locate and remove {cursor} token if present, calculating offset
            int caretOffset = 0;
            const string cursorToken = PlaceholderParser.CursorToken;
            var docText = ExtractPlainText(doc);
            int cursorIdx = docText.IndexOf(cursorToken, StringComparison.OrdinalIgnoreCase);
            if (cursorIdx >= 0)
            {
                // Caret offset is distance from the end of the text after removing the token
                var textAfter = docText[(cursorIdx + cursorToken.Length)..];
                caretOffset = textAfter.Length;
                ReplaceTextInDocument(doc, cursorToken, string.Empty);
            }

            var finalRtf = SaveToRtf(doc);
            var finalHtml = ConvertFlowDocumentToClipboardHtml(doc);
            var finalPlain = ExtractPlainText(doc);

            return (finalRtf, finalHtml, finalPlain, caretOffset);
        };

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return evalAction();
        }

        (string, string, string, int) result = default;
        Exception? caughtEx = null;
        var staThread = new Thread(() =>
        {
            try
            {
                result = evalAction();
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
        if (caughtEx != null) throw new System.Reflection.TargetInvocationException(caughtEx);
        return result;
    }

    /// <summary>
    /// Replaces occurrences of search text within a FlowDocument while preserving formatting.
    /// </summary>
    public static void ReplaceTextInDocument(FlowDocument doc, string searchText, string replacement)
    {
        if (string.IsNullOrEmpty(searchText)) return;

        // Traverse all text pointers to find and replace
        for (TextPointer start = doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
             start != null && start.CompareTo(doc.ContentEnd) < 0;
             start = start.GetNextInsertionPosition(LogicalDirection.Forward))
        {
            string textRun = start.GetTextInRun(LogicalDirection.Forward);
            int index = textRun.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                TextPointer matchStart = start.GetPositionAtOffset(index);
                TextPointer matchEnd = start.GetPositionAtOffset(index + searchText.Length);

                if (matchStart != null && matchEnd != null)
                {
                    var range = new TextRange(matchStart, matchEnd);
                    range.Text = replacement;
                    // Restart search from start of modified range
                    start = doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                    if (start == null) break;
                }
            }
        }
    }

    /// <summary>
    /// Converts a FlowDocument into clean HTML and wraps it in Windows CF_HTML format.
    /// </summary>
    public static string ConvertFlowDocumentToClipboardHtml(FlowDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family: 'Segoe UI', Arial, sans-serif; font-size: 11pt;\">");

        foreach (var block in doc.Blocks)
        {
            ConvertBlockToHtml(block, sb);
        }

        sb.Append("</div>");
        return WrapInClipboardHtmlFormat(sb.ToString());
    }

    private static void ConvertBlockToHtml(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case Paragraph p:
                sb.Append("<p style=\"margin: 0 0 6px 0;");
                if (p.TextAlignment == TextAlignment.Center) sb.Append(" text-align: center;");
                else if (p.TextAlignment == TextAlignment.Right) sb.Append(" text-align: right;");
                sb.Append("\">");

                foreach (var inline in p.Inlines)
                {
                    ConvertInlineToHtml(inline, sb);
                }
                sb.Append("</p>");
                break;

            case List list:
                var isNumbered = list.MarkerStyle == TextMarkerStyle.Decimal;
                sb.Append(isNumbered ? "<ol style=\"margin: 0 0 6px 0; padding-left: 24px;\">" : "<ul style=\"margin: 0 0 6px 0; padding-left: 24px;\">");

                foreach (var item in list.ListItems)
                {
                    sb.Append("<li>");
                    foreach (var b in item.Blocks)
                    {
                        if (b is Paragraph para)
                        {
                            foreach (var inline in para.Inlines)
                            {
                                ConvertInlineToHtml(inline, sb);
                            }
                        }
                        else
                        {
                            ConvertBlockToHtml(b, sb);
                        }
                    }
                    sb.Append("</li>");
                }

                sb.Append(isNumbered ? "</ol>" : "</ul>");
                break;

            case Section section:
                foreach (var childBlock in section.Blocks)
                {
                    ConvertBlockToHtml(childBlock, sb);
                }
                break;
        }
    }

    private static void ConvertInlineToHtml(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case LineBreak:
                sb.Append("<br/>");
                break;

            case Run run:
                AppendStyledInline(run, WebUtility.HtmlEncode(run.Text), sb, () => { });
                break;

            case Span span:
                AppendStyledInline(span, null, sb, () =>
                {
                    foreach (var child in span.Inlines)
                    {
                        ConvertInlineToHtml(child, sb);
                    }
                });
                break;
        }
    }

    private static void AppendStyledInline(
        Inline inline,
        string? textContent,
        StringBuilder sb,
        Action childrenAction)
    {
        var parentInline = inline.Parent as Inline;

        // Bold
        bool parentIsBold = parentInline != null && (parentInline.FontWeight >= FontWeights.Bold || parentInline is Bold);
        bool isBold = (inline.FontWeight >= FontWeights.Bold || inline is Bold) && !parentIsBold;

        // Italic
        bool parentIsItalic = parentInline != null && (parentInline.FontStyle == FontStyles.Italic || parentInline is Italic);
        bool isItalic = (inline.FontStyle == FontStyles.Italic || inline is Italic) && !parentIsItalic;

        // Underline
        bool parentIsUnderline = parentInline != null && (parentInline is Underline ||
            (parentInline.TextDecorations != null && parentInline.TextDecorations.Contains(TextDecorations.Underline[0])));
        bool isUnderline = (inline is Underline || (inline.TextDecorations != null && inline.TextDecorations.Contains(TextDecorations.Underline[0]))) && !parentIsUnderline;

        // Strikethrough
        bool parentIsStrikethrough = parentInline != null &&
            parentInline.TextDecorations != null && parentInline.TextDecorations.Contains(TextDecorations.Strikethrough[0]);
        bool isStrikethrough = (inline.TextDecorations != null && inline.TextDecorations.Contains(TextDecorations.Strikethrough[0])) && !parentIsStrikethrough;

        var styleSb = new StringBuilder();

        // Font Size (points = pixels * 0.75)
        bool hasLocalFontSize = inline.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue;
        if (hasLocalFontSize && inline.FontSize > 0 && Math.Abs(inline.FontSize - 14.66) > 1.0)
        {
            styleSb.Append($"font-size: {Math.Round(inline.FontSize * 0.75, 1)}pt; ");
        }

        // Foreground Color
        bool hasLocalFg = inline.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue;
        if (hasLocalFg && inline.Foreground is SolidColorBrush fgBrush)
        {
            if (!IsAutomaticOrThemeColor(inline.Foreground, inline.Background, inline))
            {
                styleSb.Append($"color: #{fgBrush.Color.R:X2}{fgBrush.Color.G:X2}{fgBrush.Color.B:X2}; ");
            }
        }

        // Background / Highlight Color
        bool hasLocalBg = inline.ReadLocalValue(TextElement.BackgroundProperty) != DependencyProperty.UnsetValue;
        if (hasLocalBg && inline.Background is SolidColorBrush bgBrush && bgBrush.Color != Colors.Transparent && bgBrush.Color.A > 0)
        {
            styleSb.Append($"background-color: #{bgBrush.Color.R:X2}{bgBrush.Color.G:X2}{bgBrush.Color.B:X2}; ");
        }

        bool hasSpanStyle = styleSb.Length > 0;
        if (hasSpanStyle) sb.Append($"<span style=\"{styleSb.ToString().Trim()}\">");
        if (isBold) sb.Append("<strong>");
        if (isItalic) sb.Append("<em>");
        if (isUnderline) sb.Append("<u>");
        if (isStrikethrough) sb.Append("<s>");

        if (textContent != null)
        {
            sb.Append(textContent);
        }

        childrenAction();

        if (isStrikethrough) sb.Append("</s>");
        if (isUnderline) sb.Append("</u>");
        if (isItalic) sb.Append("</em>");
        if (isBold) sb.Append("</strong>");
        if (hasSpanStyle) sb.Append("</span>");
    }

    /// <summary>
    /// Wraps HTML fragment in standard Windows CF_HTML clipboard format with exact UTF-8 byte offsets.
    /// </summary>
    public static string WrapInClipboardHtmlFormat(string htmlFragment)
    {
        const string header =
            "Version:0.9\r\n" +
            "StartHTML:00000000\r\n" +
            "EndHTML:00000000\r\n" +
            "StartFragment:00000000\r\n" +
            "EndFragment:00000000\r\n";

        const string htmlPrefix = "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">" +
                                  "<html><body><!--StartFragment-->";
        const string htmlSuffix = "<!--EndFragment--></body></html>";

        // Calculate UTF-8 byte offsets
        var headerBytesCount = Encoding.UTF8.GetByteCount(header);
        var prefixBytesCount = Encoding.UTF8.GetByteCount(htmlPrefix);
        var fragmentBytesCount = Encoding.UTF8.GetByteCount(htmlFragment);
        var suffixBytesCount = Encoding.UTF8.GetByteCount(htmlSuffix);

        int startHtml = headerBytesCount;
        int startFragment = headerBytesCount + prefixBytesCount;
        int endFragment = startFragment + fragmentBytesCount;
        int endHtml = endFragment + suffixBytesCount;

        var fullHeader = string.Format(
            "Version:0.9\r\n" +
            "StartHTML:{0:D8}\r\n" +
            "EndHTML:{1:D8}\r\n" +
            "StartFragment:{2:D8}\r\n" +
            "EndFragment:{3:D8}\r\n",
            startHtml, endHtml, startFragment, endFragment);

        return fullHeader + htmlPrefix + htmlFragment + htmlSuffix;
    }
}
