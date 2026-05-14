using SkiaSharp;
using ProEdit.Documents;
using ProEdit.FlowDocument.Documents;
using ProEdit.Primitives;
using ProEdit.Rendering;
using ProEdit.Rendering.Skia;
using ProEdit.WinUICompat.Converters;
using ProEdit.WinUICompat.Text;
using ProEdit.Word.Editor;
using Xunit;
using FlowDocumentModel = ProEdit.FlowDocument.FlowDocument;
using FlowHyperlink = ProEdit.FlowDocument.Hyperlink;
using FlowItalic = ProEdit.FlowDocument.Italic;
using FlowList = ProEdit.FlowDocument.List;
using FlowListItem = ProEdit.FlowDocument.ListItem;
using FlowParagraph = ProEdit.FlowDocument.Paragraph;
using FlowRun = ProEdit.FlowDocument.Run;
using FlowTable = ProEdit.FlowDocument.Table;
using FlowTableCell = ProEdit.FlowDocument.TableCell;
using FlowTableRow = ProEdit.FlowDocument.TableRow;
using FlowTableRowGroup = ProEdit.FlowDocument.TableRowGroup;
using FlowUnderline = ProEdit.FlowDocument.Underline;
using FlowBold = ProEdit.FlowDocument.Bold;

namespace ProEdit.WinUICompat.Tests;

public sealed class RenderingParitySnapshotTests
{
    private const int CanvasWidth = 960;
    private const int CanvasHeight = 640;
    private const int PixelDifferenceThreshold = 16;
    private const double MeanChannelDifferenceThreshold = 8.0;
    private const double DifferentPixelRatioThreshold = 0.30;

    [Fact]
    public void CompatPipeline_RenderedSnapshot_IsCloseToFlowPipeline()
    {
        var fixture = BuildFixtureDocument();

        using var flowBitmap = RenderViaFlowPipeline(fixture, CanvasWidth, CanvasHeight);
        using var compatBitmap = RenderViaCompatPipeline(fixture, CanvasWidth, CanvasHeight);
        using var diffBitmap = BuildDiffBitmap(flowBitmap, compatBitmap, PixelDifferenceThreshold, out var diffStats);

        TryWriteArtifacts(flowBitmap, compatBitmap, diffBitmap, diffStats);

        Assert.True(
            diffStats.MeanChannelDifference <= MeanChannelDifferenceThreshold
            && diffStats.DifferentPixelRatio <= DifferentPixelRatioThreshold,
            $"Rendering drift exceeded threshold. MeanChannelDiff={diffStats.MeanChannelDifference:F2}, DifferentPixelRatio={diffStats.DifferentPixelRatio:P2}");
    }

    private static FlowDocumentModel BuildFixtureDocument()
    {
        var document = new FlowDocumentModel();

        var intro = new FlowParagraph();
        intro.Inlines.Add(new FlowRun("Parity fixture with "));
        var bold = new FlowBold();
        bold.Inlines.Add(new FlowRun("bold"));
        intro.Inlines.Add(bold);
        intro.Inlines.Add(new FlowRun(", "));
        var italic = new FlowItalic();
        italic.Inlines.Add(new FlowRun("italic"));
        intro.Inlines.Add(italic);
        intro.Inlines.Add(new FlowRun(", and "));
        var underline = new FlowUnderline();
        underline.Inlines.Add(new FlowRun("underline"));
        intro.Inlines.Add(underline);
        intro.Inlines.Add(new FlowRun(" inline styles."));
        document.Blocks.Add(intro);

        var linkParagraph = new FlowParagraph();
        linkParagraph.Inlines.Add(new FlowRun("Hyperlink test: "));
        var link = new FlowHyperlink
        {
            NavigateUri = "https://example.com/docs"
        };
        link.Inlines.Add(new FlowRun("example docs"));
        linkParagraph.Inlines.Add(link);
        document.Blocks.Add(linkParagraph);

        var list = new FlowList
        {
            MarkerStyle = ProEdit.FlowDocument.FlowListMarkerStyle.UpperRoman,
            StartIndex = 3
        };
        var item1 = new FlowListItem();
        item1.Blocks.Add(new FlowParagraph("List item one"));
        list.ListItems.Add(item1);
        var item2 = new FlowListItem();
        item2.Blocks.Add(new FlowParagraph("List item two"));
        list.ListItems.Add(item2);
        document.Blocks.Add(list);

        var table = new FlowTable
        {
            CellSpacing = 2
        };
        var group = new FlowTableRowGroup();
        var row1 = new FlowTableRow();
        row1.Cells.Add(new FlowTableCell
        {
            RowSpan = 2,
            Blocks = { new FlowParagraph("Group A") }
        });
        row1.Cells.Add(new FlowTableCell
        {
            Blocks = { new FlowParagraph("Q1") }
        });
        row1.Cells.Add(new FlowTableCell
        {
            Blocks = { new FlowParagraph("120") }
        });
        group.Rows.Add(row1);

        var row2 = new FlowTableRow();
        row2.Cells.Add(new FlowTableCell
        {
            Blocks = { new FlowParagraph("Q2") }
        });
        row2.Cells.Add(new FlowTableCell
        {
            Blocks = { new FlowParagraph("145") }
        });
        group.Rows.Add(row2);
        table.RowGroups.Add(group);
        document.Blocks.Add(table);

        return document;
    }

    private static SKBitmap RenderViaFlowPipeline(FlowDocumentModel flowDocument, int width, int height)
    {
        var converter = new FlowDocumentConverter();
        var engineDocument = converter.Convert(flowDocument);

        var textMeasurer = new SkiaTextMeasurer();
        var renderer = new SkiaDocumentRenderer();
        var editor = new EditorController(textMeasurer, engineDocument);
        editor.UpdateLayout(width, height);

        return RenderEngineDocument(
            engineDocument,
            editor.Layout,
            editor.Caret,
            editor.DirtyPages,
            editor.DirtyVersion,
            renderer,
            width,
            height);
    }

    private static SKBitmap RenderViaCompatPipeline(FlowDocumentModel flowDocument, int width, int height)
    {
        var compatConverter = new CompatFlowDocumentConverter();
        var compatDocument = compatConverter.FromFlowDocument(flowDocument);
        var richEditTextDocument = new RichEditTextDocument();
        richEditTextDocument.SetDocument(compatDocument);
        richEditTextDocument.UpdateViewport(width, height);

        var renderer = new SkiaDocumentRenderer();
        return RenderEngineDocument(
            richEditTextDocument.EditorDocument,
            richEditTextDocument.EditorLayout,
            richEditTextDocument.EditorCaret,
            richEditTextDocument.DirtyPages,
            richEditTextDocument.DirtyVersion,
            renderer,
            width,
            height);
    }

    private static SKBitmap RenderEngineDocument(
        Document document,
        ProEdit.Layout.DocumentLayout layout,
        TextPosition caret,
        IReadOnlyList<int> dirtyPages,
        long dirtyVersion,
        SkiaDocumentRenderer renderer,
        int width,
        int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);

        var options = new RenderOptions
        {
            BackgroundColor = DocColor.White,
            PageColor = DocColor.White,
            PageBorderColor = new DocColor(230, 230, 230),
            PageBorderThickness = 1f,
            TextColor = DocColor.Black,
            SelectionColor = DocColor.SelectionBlue,
            CaretColor = DocColor.Black,
            ShowCaret = false,
            UseHarfBuzz = true,
            UsePictureCache = true,
            ZoomFactor = 1f,
            SvgRasterizationScale = 1f,
            VisibleBounds = new DocRect(0f, 0f, width, height),
            Caret = caret,
            DirtyPages = dirtyPages,
            DirtyVersion = dirtyVersion
        };

        renderer.Render(surface.Canvas, document, layout, options);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static SKBitmap BuildDiffBitmap(
        SKBitmap expected,
        SKBitmap actual,
        int diffThreshold,
        out DiffStats stats)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new InvalidOperationException("Bitmap dimensions must match for diff.");
        }

        var width = expected.Width;
        var height = expected.Height;
        var pixelCount = width * height;
        var diffBitmap = new SKBitmap(width, height, expected.ColorType, expected.AlphaType);

        long totalChannelDelta = 0;
        var differentPixels = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var lhs = expected.GetPixel(x, y);
                var rhs = actual.GetPixel(x, y);
                var dr = Math.Abs(lhs.Red - rhs.Red);
                var dg = Math.Abs(lhs.Green - rhs.Green);
                var db = Math.Abs(lhs.Blue - rhs.Blue);
                var da = Math.Abs(lhs.Alpha - rhs.Alpha);

                var channelDelta = dr + dg + db + da;
                totalChannelDelta += channelDelta;

                var isDifferent = channelDelta > diffThreshold;
                if (isDifferent)
                {
                    differentPixels++;
                }

                diffBitmap.SetPixel(
                    x,
                    y,
                    isDifferent
                        ? new SKColor((byte)Math.Min(255, dr * 4), (byte)Math.Min(255, dg * 4), (byte)Math.Min(255, db * 4), 255)
                        : new SKColor(0, 0, 0, 0));
            }
        }

        stats = new DiffStats(
            TotalPixels: pixelCount,
            DifferentPixels: differentPixels,
            MeanChannelDifference: totalChannelDelta / (double)(pixelCount * 4),
            DifferentPixelRatio: differentPixels / (double)pixelCount);
        return diffBitmap;
    }

    private static void TryWriteArtifacts(SKBitmap flowBitmap, SKBitmap compatBitmap, SKBitmap diffBitmap, DiffStats stats)
    {
        var targetDir = Environment.GetEnvironmentVariable("PROEDIT_RENDER_PARITY_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return;
        }

        Directory.CreateDirectory(targetDir);
        WriteBitmap(flowBitmap, Path.Combine(targetDir, "flow-render.png"));
        WriteBitmap(compatBitmap, Path.Combine(targetDir, "compat-render.png"));
        WriteBitmap(diffBitmap, Path.Combine(targetDir, "render-diff.png"));

        var summary = string.Join(
            Environment.NewLine,
            "WinUICompat Rendering Parity Diff",
            $"TotalPixels={stats.TotalPixels}",
            $"DifferentPixels={stats.DifferentPixels}",
            $"DifferentPixelRatio={stats.DifferentPixelRatio:P4}",
            $"MeanChannelDifference={stats.MeanChannelDifference:F4}");
        File.WriteAllText(Path.Combine(targetDir, "render-diff-summary.txt"), summary);
    }

    private static void WriteBitmap(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private readonly record struct DiffStats(
        int TotalPixels,
        int DifferentPixels,
        double MeanChannelDifference,
        double DifferentPixelRatio);
}
