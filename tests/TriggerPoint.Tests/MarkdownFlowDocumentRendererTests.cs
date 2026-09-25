using System;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using TriggerPoint.UI.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class MarkdownFlowDocumentRendererTests
{
    [Fact]
    public void Render_NullOrEmpty_ReturnsFallbackDocument()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                var doc = MarkdownFlowDocumentRenderer.Render(null);
                Assert.NotNull(doc);
                Assert.Single(doc.Blocks);

                var docEmpty = MarkdownFlowDocumentRenderer.Render("   ");
                Assert.NotNull(docEmpty);
                Assert.Single(docEmpty.Blocks);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null) throw caught;
    }

    [Fact]
    public void Render_HeadingsAndDividers_CreatesStyledBlocks()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                string markdown = "# Release v1.5.0\n---\n## Features\n### Performance Improvements";
                var doc = MarkdownFlowDocumentRenderer.Render(markdown);

                Assert.NotNull(doc);
                Assert.Equal(4, doc.Blocks.Count);

                var h1 = Assert.IsType<Paragraph>(doc.Blocks.ElementAt(0));
                Assert.Equal(15.0, h1.FontSize);

                var divider = Assert.IsType<BlockUIContainer>(doc.Blocks.ElementAt(1));
                Assert.NotNull(divider.Child);

                var h2 = Assert.IsType<Paragraph>(doc.Blocks.ElementAt(2));
                Assert.Equal(13.5, h2.FontSize);

                var h3 = Assert.IsType<Paragraph>(doc.Blocks.ElementAt(3));
                Assert.Equal(12.5, h3.FontSize);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null) throw caught;
    }

    [Fact]
    public void Render_Lists_CreatesFormattedLists()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                string markdown = "- First bullet\n- Second bullet with **bold** text\n* Third bullet";
                var doc = MarkdownFlowDocumentRenderer.Render(markdown);

                Assert.NotNull(doc);
                Assert.Single(doc.Blocks);

                var list = Assert.IsType<List>(doc.Blocks.First());
                Assert.Equal(3, list.ListItems.Count);
                Assert.Equal(System.Windows.TextMarkerStyle.Disc, list.MarkerStyle);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null) throw caught;
    }

    [Fact]
    public void Render_InlineFormatting_CreatesRunsBoldsCodesAndHyperlinks()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                string markdown = "Check `git status` for **important** changes at [GitHub](https://github.com/test/repo).";
                var doc = MarkdownFlowDocumentRenderer.Render(markdown);

                Assert.NotNull(doc);
                Assert.Single(doc.Blocks);

                var para = Assert.IsType<Paragraph>(doc.Blocks.First());
                var inlines = para.Inlines.ToList();

                // Contains inline code Span
                Assert.Contains(inlines, i => i is Span s && s.Inlines.OfType<Run>().Any(r => r.Text == "git status"));

                // Contains Bold
                Assert.Contains(inlines, i => i is Bold b && b.Inlines.OfType<Run>().Any(r => r.Text == "important"));

                // Contains Hyperlink
                Assert.Contains(inlines, i => i is Hyperlink h && h.Inlines.OfType<Run>().Any(r => r.Text == "GitHub"));
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null) throw caught;
    }
}
