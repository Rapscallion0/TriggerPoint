using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TriggerPoint.UI.Services;

/// <summary>
/// A lightweight, theme-adaptive Markdown to FlowDocument renderer for WPF.
/// Converts Markdown text (headings, bullet/numbered lists, inline code, code blocks,
/// bold, italic, quotes, horizontal rules, and hyperlinks) into rich FlowDocument structures.
/// </summary>
public static class MarkdownFlowDocumentRenderer
{
    private static readonly Regex InlineMatcher = new(
        @"(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUrl>[^\)]+)\))|" +
        @"(?<code>`(?<codeText>[^`]+)`)|" +
        @"(?<bold>\*\*(?<boldText>[^*]+)\*\*|__(?<boldText>[^_]+)__)|" +
        @"(?<italic>\*(?<italicText>[^*]+)\*|_(?<italicText>[^_]+)_)",
        RegexOptions.Compiled);

    public static FlowDocument Render(string? markdown)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, sans-serif"),
            FontSize = 12.0,
            LineHeight = 18.0
        };

        doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextPrimaryBrush");

        if (string.IsNullOrWhiteSpace(markdown))
        {
            var emptyPara = new Paragraph(new Run("No changelog available."))
            {
                Margin = new Thickness(0, 4, 0, 4)
            };
            emptyPara.SetResourceReference(Paragraph.ForegroundProperty, "TextSecondaryBrush");
            doc.Blocks.Add(emptyPara);
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // 1. Fenced Code Block
            if (trimmed.StartsWith("```"))
            {
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```"))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                i++; // Skip closing ```

                doc.Blocks.Add(CreateCodeBlock(string.Join("\n", codeLines)));
                continue;
            }

            // 2. Horizontal Rule (---, ***, ___)
            if (trimmed is "---" or "***" or "___" or "----" or "-----")
            {
                doc.Blocks.Add(CreateHorizontalDivider());
                i++;
                continue;
            }

            // 3. Headings (#, ##, ###, ####)
            if (trimmed.StartsWith('#'))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                var headingText = trimmed[level..].TrimStart();

                doc.Blocks.Add(CreateHeading(headingText, level));
                i++;
                continue;
            }

            // 4. Blockquote (> ...)
            if (trimmed.StartsWith('>'))
            {
                var quoteLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    var qLine = lines[i].TrimStart()[1..].TrimStart();
                    quoteLines.Add(qLine);
                    i++;
                }
                doc.Blocks.Add(CreateBlockquote(string.Join(" ", quoteLines)));
                continue;
            }

            // 5. Unordered List (*, -, +)
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
            {
                var list = new System.Windows.Documents.List
                {
                    MarkerStyle = TextMarkerStyle.Disc,
                    Margin = new Thickness(14, 2, 0, 6),
                    Padding = new Thickness(0)
                };

                while (i < lines.Length)
                {
                    var curTrimmed = lines[i].Trim();
                    if (curTrimmed.StartsWith("- ") || curTrimmed.StartsWith("* ") || curTrimmed.StartsWith("+ "))
                    {
                        var itemText = curTrimmed[2..].TrimStart();
                        var listItem = new ListItem();
                        var itemPara = new Paragraph { Margin = new Thickness(0, 1, 0, 2) };
                        AppendInlineFormattedText(itemPara.Inlines, itemText);
                        listItem.Blocks.Add(itemPara);
                        list.ListItems.Add(listItem);
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                doc.Blocks.Add(list);
                continue;
            }

            // 6. Numbered List (1. ...)
            var numMatch = Regex.Match(trimmed, @"^\d+\.\s+");
            if (numMatch.Success)
            {
                var list = new System.Windows.Documents.List
                {
                    MarkerStyle = TextMarkerStyle.Decimal,
                    Margin = new Thickness(14, 2, 0, 6),
                    Padding = new Thickness(0)
                };

                while (i < lines.Length)
                {
                    var curTrimmed = lines[i].Trim();
                    var match = Regex.Match(curTrimmed, @"^\d+\.\s+");
                    if (match.Success)
                    {
                        var itemText = curTrimmed[match.Length..].TrimStart();
                        var listItem = new ListItem();
                        var itemPara = new Paragraph { Margin = new Thickness(0, 1, 0, 2) };
                        AppendInlineFormattedText(itemPara.Inlines, itemText);
                        listItem.Blocks.Add(itemPara);
                        list.ListItems.Add(listItem);
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                doc.Blocks.Add(list);
                continue;
            }

            // 7. Blank lines
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                i++;
                continue;
            }

            // 8. Standard Paragraph
            var paraLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i].Trim()) &&
                   !lines[i].Trim().StartsWith('#') &&
                   !lines[i].Trim().StartsWith("```") &&
                   !lines[i].Trim().StartsWith('>') &&
                   !lines[i].Trim().StartsWith("- ") &&
                   !lines[i].Trim().StartsWith("* ") &&
                   !lines[i].Trim().StartsWith("+ ") &&
                   !Regex.IsMatch(lines[i].Trim(), @"^\d+\.\s+") &&
                   lines[i].Trim() is not ("---" or "***" or "___"))
            {
                paraLines.Add(lines[i].Trim());
                i++;
            }

            var para = new Paragraph
            {
                Margin = new Thickness(0, 3, 0, 5)
            };
            AppendInlineFormattedText(para.Inlines, string.Join(" ", paraLines));
            doc.Blocks.Add(para);
        }

        return doc;
    }

    private static Block CreateHeading(string text, int level)
    {
        var para = new Paragraph
        {
            FontWeight = FontWeights.Bold
        };

        switch (level)
        {
            case 1:
                para.FontSize = 15.0;
                para.Margin = new Thickness(0, 10, 0, 4);
                para.SetResourceReference(Paragraph.ForegroundProperty, "TextPrimaryBrush");
                break;
            case 2:
                para.FontSize = 13.5;
                para.Margin = new Thickness(0, 8, 0, 3);
                para.SetResourceReference(Paragraph.ForegroundProperty, "TextPrimaryBrush");
                break;
            case 3:
                para.FontSize = 12.5;
                para.Margin = new Thickness(0, 6, 0, 2);
                para.SetResourceReference(Paragraph.ForegroundProperty, "AccentBrush");
                break;
            default:
                para.FontSize = 12.0;
                para.Margin = new Thickness(0, 4, 0, 2);
                para.SetResourceReference(Paragraph.ForegroundProperty, "TextPrimaryBrush");
                break;
        }

        AppendInlineFormattedText(para.Inlines, text);
        return para;
    }

    private static Block CreateHorizontalDivider()
    {
        var border = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            SnapsToDevicePixels = true
        };
        border.SetResourceReference(Border.BackgroundProperty, "BorderSubtleBrush");
        return new BlockUIContainer(border);
    }

    private static Block CreateCodeBlock(string code)
    {
        var textBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas, monospace"),
            FontSize = 11.5,
            LineHeight = 16.0,
            TextWrapping = TextWrapping.Wrap
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 6),
            Child = textBlock
        };
        border.SetResourceReference(Border.BackgroundProperty, "BgInputBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");

        return new BlockUIContainer(border);
    }

    private static Block CreateBlockquote(string text)
    {
        var para = new Paragraph
        {
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0)
        };
        para.SetResourceReference(Paragraph.ForegroundProperty, "TextSecondaryBrush");
        AppendInlineFormattedText(para.Inlines, text);

        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(8, 4, 4, 4),
            Margin = new Thickness(0, 4, 0, 6),
            Child = new TextBlock(new InlineUIContainer(new TextBlock { Text = string.Empty })) // placeholder
        };

        // Put paragraph in a flow document section for native flow layout
        var section = new Section
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(8, 2, 0, 2),
            Margin = new Thickness(0, 3, 0, 5)
        };
        section.SetResourceReference(Section.BorderBrushProperty, "AccentBrush");
        section.Blocks.Add(para);
        return section;
    }

    public static void AppendInlineFormattedText(InlineCollection inlines, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        int lastIndex = 0;
        foreach (Match match in InlineMatcher.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                inlines.Add(new Run(text[lastIndex..match.Index]));
            }

            if (match.Groups["link"].Success)
            {
                var label = match.Groups["linkText"].Value;
                var url = match.Groups["linkUrl"].Value;
                var link = new Hyperlink(new Run(label));
                link.SetResourceReference(Hyperlink.ForegroundProperty, "AccentBrush");
                link.ToolTip = url;
                link.Click += (s, e) =>
                {
                    try
                    {
                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        {
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        }
                    }
                    catch { }
                };
                inlines.Add(link);
            }
            else if (match.Groups["code"].Success)
            {
                var codeText = match.Groups["codeText"].Value;
                var span = new Span(new Run(codeText))
                {
                    FontFamily = new FontFamily("Consolas, monospace"),
                    FontSize = 11.0,
                    FontWeight = FontWeights.Medium
                };
                span.SetResourceReference(Span.ForegroundProperty, "AccentBrush");
                span.SetResourceReference(Span.BackgroundProperty, "BgSecondaryBrush");
                inlines.Add(span);
            }
            else if (match.Groups["bold"].Success)
            {
                var boldText = match.Groups["boldText"].Value;
                var bold = new Bold();
                AppendInlineFormattedText(bold.Inlines, boldText);
                inlines.Add(bold);
            }
            else if (match.Groups["italic"].Success)
            {
                var italicText = match.Groups["italicText"].Value;
                var italic = new Italic();
                AppendInlineFormattedText(italic.Inlines, italicText);
                inlines.Add(italic);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            inlines.Add(new Run(text[lastIndex..]));
        }
    }
}
