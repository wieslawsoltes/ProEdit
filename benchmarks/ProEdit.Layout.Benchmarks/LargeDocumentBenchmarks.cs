using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Order;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;

namespace ProEdit.Layout.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class LargeDocumentBenchmarks
{
    [Params(200, 1000)]
    public int ParagraphCount { get; set; }

    private Document _document = null!;
    private DocumentLayouter _layouter = null!;
    private LayoutSettings _settings = null!;
    private ITextMeasurer _measurer = null!;
    private EditorLayoutService _editorLayout = null!;
    private int _dirtyNearStart;
    private int _dirtyMiddle;
    private string _originalNearStart = string.Empty;
    private string _originalMiddle = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _document = DocumentBenchmarkFactory.CreateLargeDocument(ParagraphCount);
        _document.DefaultTextStyle.Language = "en-US";
        _measurer = new BenchmarkTextMeasurer();
        _settings = CreateLayoutSettings();
        _layouter = new DocumentLayouter();

        _dirtyNearStart = Math.Min(2, ParagraphCount - 1);
        _dirtyMiddle = Math.Min(ParagraphCount / 2, ParagraphCount - 1);
        _originalNearStart = _document.GetParagraph(_dirtyNearStart).Text ?? string.Empty;
        _originalMiddle = _document.GetParagraph(_dirtyMiddle).Text ?? string.Empty;

        _editorLayout = new EditorLayoutService(_document, _measurer);
        ApplySettings(_editorLayout.Settings, _settings);
        _editorLayout.RefreshLayout(null);
    }

    [Benchmark(Baseline = true)]
    public ReflowMetrics FullLayout_LargeDocument()
    {
        var layout = _layouter.Layout(_document, _settings, _measurer);
        return new ReflowMetrics(layout.Lines.Count, layout.Pages.Count, layout.Pages.Count);
    }

    [IterationSetup(Target = nameof(IncrementalReflow_NearStart))]
    public void PrepareNearStart()
    {
        MutateParagraph(_dirtyNearStart, _originalNearStart);
    }

    [Benchmark]
    public ReflowMetrics IncrementalReflow_NearStart()
    {
        var dirtyPages = _editorLayout.RefreshLayout(_dirtyNearStart);
        return new ReflowMetrics(_editorLayout.Layout.Lines.Count, _editorLayout.Layout.Pages.Count, dirtyPages.Count);
    }

    [IterationSetup(Target = nameof(IncrementalReflow_Middle))]
    public void PrepareMiddle()
    {
        MutateParagraph(_dirtyMiddle, _originalMiddle);
    }

    [Benchmark]
    public ReflowMetrics IncrementalReflow_Middle()
    {
        var dirtyPages = _editorLayout.RefreshLayout(_dirtyMiddle);
        return new ReflowMetrics(_editorLayout.Layout.Lines.Count, _editorLayout.Layout.Pages.Count, dirtyPages.Count);
    }

    [IterationCleanup(Target = nameof(IncrementalReflow_NearStart))]
    public void ResetNearStart()
    {
        ResetParagraph(_dirtyNearStart, _originalNearStart);
    }

    [IterationCleanup(Target = nameof(IncrementalReflow_Middle))]
    public void ResetMiddle()
    {
        ResetParagraph(_dirtyMiddle, _originalMiddle);
    }

    private void MutateParagraph(int index, string original)
    {
        var paragraph = _document.GetParagraph(index);
        paragraph.Text = $"{original} edit";
    }

    private void ResetParagraph(int index, string original)
    {
        var paragraph = _document.GetParagraph(index);
        paragraph.Text = original;
        _editorLayout.RefreshLayout(index);
    }

    private static LayoutSettings CreateLayoutSettings()
    {
        return new LayoutSettings
        {
            UsePagination = true,
            PageWidth = 816f,
            PageHeight = 1056f,
            MarginLeft = 72f,
            MarginRight = 72f,
            MarginTop = 72f,
            MarginBottom = 72f
        };
    }

    private static void ApplySettings(LayoutSettings target, LayoutSettings source)
    {
        target.ViewportWidth = source.ViewportWidth;
        target.ViewportHeight = source.ViewportHeight;
        target.UsePagination = source.UsePagination;
        target.PageWidth = source.PageWidth;
        target.PageHeight = source.PageHeight;
        target.PageGap = source.PageGap;
        target.PageFlow = source.PageFlow;
        target.MarginLeft = source.MarginLeft;
        target.MarginTop = source.MarginTop;
        target.MarginRight = source.MarginRight;
        target.MarginBottom = source.MarginBottom;
        target.HeaderOffset = source.HeaderOffset;
        target.FooterOffset = source.FooterOffset;
        target.Gutter = source.Gutter;
        target.ParagraphSpacing = source.ParagraphSpacing;
        target.BlockSpacing = source.BlockSpacing;
        target.ListIndent = source.ListIndent;
        target.ListMarkerGap = source.ListMarkerGap;
        target.DefaultTabWidth = source.DefaultTabWidth;
        target.ColumnGap = source.ColumnGap;
        target.TableCellPadding = source.TableCellPadding;
        target.TableBorderThickness = source.TableBorderThickness;
    }

    public readonly struct ReflowMetrics
    {
        public int Lines { get; }
        public int Pages { get; }
        public int DirtyPages { get; }

        public ReflowMetrics(int lines, int pages, int dirtyPages)
        {
            Lines = lines;
            Pages = pages;
            DirtyPages = dirtyPages;
        }

        public override string ToString()
        {
            return $"Lines={Lines},Pages={Pages},DirtyPages={DirtyPages}";
        }
    }

    private sealed class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddColumnProvider(DefaultColumnProviders.Instance);
            AddExporter(MarkdownExporter.GitHub);
            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }
}
