using System.Text;
using Vibe.Office.FlowDocument;
using Vibe.Office.FlowDocument.IO;
using Xunit;

namespace Vibe.Office.FlowDocument.IO.Tests;

public sealed class FlowDocumentFileConversionServiceTests
{
    [Fact]
    public async Task SaveAndLoadMarkdown_RetainsContent()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.md");
        var original = CreateSimpleDocument("Markdown Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        var markdown = await File.ReadAllTextAsync(path);
        Assert.Contains("Markdown Roundtrip", markdown);
        Assert.Contains("Markdown Roundtrip", ExtractText(loaded));
    }

    [Fact]
    public async Task SaveAndLoadDocx_RetainsContent()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.docx");
        var original = CreateSimpleDocument("Docx Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.Contains("Docx Roundtrip", ExtractText(loaded));
    }

    [Fact]
    public async Task SaveAndLoadDocx_PreservesFlowCompatibleFormatting()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "rich.docx");
        var original = CreateRichFormattingDocument();

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.Contains("Bold Italic", ExtractText(loaded));

        var firstParagraph = Assert.IsType<Paragraph>(loaded.Blocks[0]);
        Assert.True(HasBoldItalicInline(firstParagraph.Inlines, hasBold: false, hasItalic: false));

        var table = Assert.IsType<Table>(loaded.Blocks[1]);
        var firstCell = table.RowGroups[0].Rows[0].Cells[0];
        Assert.True(firstCell.RowSpan >= 2);
        Assert.False(firstCell.Padding.IsEmpty);
        Assert.False(firstCell.BorderThickness.IsEmpty);
        Assert.NotNull(firstCell.BorderBrush);
        Assert.NotNull(firstCell.Background);
    }

    [Fact]
    public async Task SavePdfAndLoadPdf_Succeeds()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.pdf");
        var original = CreateSimpleDocument("Pdf Roundtrip");

        await service.SaveAsync(original, path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task Save_UnsupportedExtension_Throws()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.txt");
        var original = CreateSimpleDocument("Unsupported");

        await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.SaveAsync(original, path));
    }

    [Fact]
    public async Task Load_UnsupportedExtension_Throws()
    {
        using var fixture = new TempDirectoryFixture();
        var service = new FlowDocumentFileConversionService();
        var path = Path.Combine(fixture.Path, "sample.txt");
        await File.WriteAllTextAsync(path, "hello");

        await Assert.ThrowsAsync<FlowDocumentFileFormatException>(() => service.LoadAsync(path));
    }

    [Fact]
    public void CanLoadAndCanSave_SupportConfiguredExtensions()
    {
        var service = new FlowDocumentFileConversionService();

        Assert.True(service.CanLoad(".docx"));
        Assert.True(service.CanLoad("sample.markdown"));
        Assert.True(service.CanLoad(".pdf"));
        Assert.True(service.CanLoad(".pdx"));
        Assert.True(service.CanSave(".docx"));
        Assert.True(service.CanSave(".md"));
        Assert.True(service.CanSave(".pdf"));
        Assert.True(service.CanSave(".pdx"));
        Assert.False(service.CanLoad(".txt"));
        Assert.False(service.CanSave(".txt"));
    }

    private static FlowDocument CreateSimpleDocument(string text)
    {
        var document = new FlowDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new FlowThickness(72, 72, 72, 72)
        };
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run(text));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static FlowDocument CreateRichFormattingDocument()
    {
        var document = new FlowDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new FlowThickness(72, 72, 72, 72)
        };

        var paragraph = new Paragraph();
        var bold = new Bold();
        var italic = new Italic();
        italic.Inlines.Add(new Run("Bold Italic"));
        bold.Inlines.Add(italic);
        paragraph.Inlines.Add(bold);
        document.Blocks.Add(paragraph);

        var table = new Table();
        var group = new TableRowGroup();
        var row1 = new TableRow();
        var row1Cell1 = new TableCell
        {
            RowSpan = 2,
            Padding = new FlowThickness(6, 4, 6, 4),
            BorderThickness = new FlowThickness(1, 1, 1, 1),
            BorderBrush = "#334455",
            Background = "#EEF3FF"
        };
        row1Cell1.Blocks.Add(new Paragraph("R1C1"));
        row1.Cells.Add(row1Cell1);

        var row1Cell2 = new TableCell();
        row1Cell2.Blocks.Add(new Paragraph("R1C2"));
        row1.Cells.Add(row1Cell2);
        group.Rows.Add(row1);

        var row2 = new TableRow();
        var row2Cell = new TableCell();
        row2Cell.Blocks.Add(new Paragraph("R2C2"));
        row2.Cells.Add(row2Cell);
        group.Rows.Add(row2);

        table.RowGroups.Add(group);
        document.Blocks.Add(table);
        return document;
    }

    private static bool HasBoldItalicInline(InlineCollection inlines, bool hasBold, bool hasItalic)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run && hasBold && hasItalic)
            {
                return true;
            }

            if (inline is Span span)
            {
                var nextBold = hasBold || span.FontWeight == FlowFontWeight.Bold;
                var nextItalic = hasItalic || span.FontStyle == FlowFontStyle.Italic;
                if (nextBold && nextItalic && span.Inlines.Count == 0)
                {
                    return true;
                }

                if (HasBoldItalicInline(span.Inlines, nextBold, nextItalic))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ExtractText(FlowDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            AppendBlockText(block, builder);
        }

        return builder.ToString();
    }

    private static void AppendBlockText(Block block, StringBuilder builder)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines)
                {
                    AppendInlineText(inline, builder);
                }
                break;
            case Section section:
                foreach (var child in section.Blocks)
                {
                    AppendBlockText(child, builder);
                }
                break;
            case List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks)
                    {
                        AppendBlockText(child, builder);
                    }
                }
                break;
            case Table table:
                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var child in cell.Blocks)
                            {
                                AppendBlockText(child, builder);
                            }
                        }
                    }
                }
                break;
        }
    }

    private static void AppendInlineText(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case Run run:
                builder.Append(run.Text);
                break;
            case Span span:
                foreach (var child in span.Inlines)
                {
                    AppendInlineText(child, builder);
                }
                break;
            case LineBreak:
                builder.AppendLine();
                break;
            case AnchoredBlock anchored:
                foreach (var block in anchored.Blocks)
                {
                    AppendBlockText(block, builder);
                }
                break;
        }
    }

    private sealed class TempDirectoryFixture : IDisposable
    {
        public TempDirectoryFixture()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // best effort cleanup for test temp directory.
            }
        }
    }
}
