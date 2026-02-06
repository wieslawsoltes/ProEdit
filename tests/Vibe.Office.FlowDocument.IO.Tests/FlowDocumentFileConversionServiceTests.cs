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
