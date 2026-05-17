using System.Windows.Input;
using ProEdit.Controls.Skia;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using ProEdit.Primitives;
using ReactiveUI;

namespace ProEdit.Controls.Skia.Sample.Shared;

/// <summary>
/// Shared sample state used by the Avalonia, Uno, and MAUI document control samples.
/// </summary>
public sealed class DocumentControlSampleViewModel : ReactiveObject
{
    private Document _viewerDocument;
    private Document _editorDocument;
    private double _viewerZoom = 0.9d;
    private double _editorZoom = 1d;
    private ProEditDocumentZoomMode _viewerZoomMode = ProEditDocumentZoomMode.PageWidth;
    private ProEditDocumentZoomMode _editorZoomMode = ProEditDocumentZoomMode.Custom;
    private int _multiplePagesPerRow = 2;
    private bool _showInvisibles;
    private bool _showLayout = true;
    private bool _showGridlines;
    private bool _usePagination = true;
    private bool _isPanEnabled = true;
    private bool _isEditorReadOnly;
    private PageFlowDirection _pageFlow = PageFlowDirection.Vertical;
    private EditorViewMode _viewMode = EditorViewMode.PrintLayout;
    private string _status = "Ready. Type in the editor pane, use Ctrl+wheel to zoom, and drag with the middle mouse button to pan.";

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentControlSampleViewModel"/> class.
    /// </summary>
    public DocumentControlSampleViewModel()
    {
        var document = CreateSampleDocument();
        _viewerDocument = DocumentClone.Clone(document);
        _editorDocument = DocumentClone.Clone(document);

        ResetDocumentsCommand = ReactiveCommand.Create(ResetDocuments);
        ZoomDefaultCommand = ReactiveCommand.Create(ZoomDefault);
        ZoomPageWidthCommand = ReactiveCommand.Create(ZoomPageWidth);
        ZoomWholePageCommand = ReactiveCommand.Create(ZoomWholePage);
        ZoomMultiplePagesCommand = ReactiveCommand.Create(ZoomMultiplePages);
        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        PrintLayoutCommand = ReactiveCommand.Create(UsePrintLayout);
        ReadModeCommand = ReactiveCommand.Create(UseReadMode);
        WebLayoutCommand = ReactiveCommand.Create(UseWebLayout);
    }

    /// <summary>
    /// Gets the read-only document shown by viewer controls.
    /// </summary>
    public Document ViewerDocument
    {
        get => _viewerDocument;
        private set => this.RaiseAndSetIfChanged(ref _viewerDocument, value);
    }

    /// <summary>
    /// Gets the editable document shown by editor controls.
    /// </summary>
    public Document EditorDocument
    {
        get => _editorDocument;
        private set => this.RaiseAndSetIfChanged(ref _editorDocument, value);
    }

    /// <summary>
    /// Gets or sets the viewer zoom factor.
    /// </summary>
    public double ViewerZoom
    {
        get => _viewerZoom;
        set => this.RaiseAndSetIfChanged(ref _viewerZoom, value);
    }

    /// <summary>
    /// Gets or sets the editor zoom factor.
    /// </summary>
    public double EditorZoom
    {
        get => _editorZoom;
        set => this.RaiseAndSetIfChanged(ref _editorZoom, value);
    }

    /// <summary>
    /// Gets or sets the viewer zoom mode.
    /// </summary>
    public ProEditDocumentZoomMode ViewerZoomMode
    {
        get => _viewerZoomMode;
        set => this.RaiseAndSetIfChanged(ref _viewerZoomMode, value);
    }

    /// <summary>
    /// Gets or sets the editor zoom mode.
    /// </summary>
    public ProEditDocumentZoomMode EditorZoomMode
    {
        get => _editorZoomMode;
        set => this.RaiseAndSetIfChanged(ref _editorZoomMode, value);
    }

    /// <summary>
    /// Gets or sets the page count used by multiple-pages zoom.
    /// </summary>
    public int MultiplePagesPerRow
    {
        get => _multiplePagesPerRow;
        set => this.RaiseAndSetIfChanged(ref _multiplePagesPerRow, Math.Max(1, value));
    }

    /// <summary>
    /// Gets or sets a value indicating whether invisible characters are shown.
    /// </summary>
    public bool ShowInvisibles
    {
        get => _showInvisibles;
        set => this.RaiseAndSetIfChanged(ref _showInvisibles, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether layout guides are shown.
    /// </summary>
    public bool ShowLayout
    {
        get => _showLayout;
        set => this.RaiseAndSetIfChanged(ref _showLayout, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether page gridlines are shown.
    /// </summary>
    public bool ShowGridlines
    {
        get => _showGridlines;
        set => this.RaiseAndSetIfChanged(ref _showGridlines, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether pagination is enabled.
    /// </summary>
    public bool UsePagination
    {
        get => _usePagination;
        set => this.RaiseAndSetIfChanged(ref _usePagination, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether middle-button pan is enabled.
    /// </summary>
    public bool IsPanEnabled
    {
        get => _isPanEnabled;
        set => this.RaiseAndSetIfChanged(ref _isPanEnabled, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the editor sample is read-only.
    /// </summary>
    public bool IsEditorReadOnly
    {
        get => _isEditorReadOnly;
        set => this.RaiseAndSetIfChanged(ref _isEditorReadOnly, value);
    }

    /// <summary>
    /// Gets or sets the page flow direction.
    /// </summary>
    public PageFlowDirection PageFlow
    {
        get => _pageFlow;
        set => this.RaiseAndSetIfChanged(ref _pageFlow, value);
    }

    /// <summary>
    /// Gets or sets the logical editor view mode.
    /// </summary>
    public EditorViewMode ViewMode
    {
        get => _viewMode;
        set => this.RaiseAndSetIfChanged(ref _viewMode, value);
    }

    /// <summary>
    /// Gets the current sample status text.
    /// </summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>
    /// Gets a command that resets both sample documents.
    /// </summary>
    public ICommand ResetDocumentsCommand { get; }

    /// <summary>
    /// Gets a command that resets zoom to 100%.
    /// </summary>
    public ICommand ZoomDefaultCommand { get; }

    /// <summary>
    /// Gets a command that fits the page width.
    /// </summary>
    public ICommand ZoomPageWidthCommand { get; }

    /// <summary>
    /// Gets a command that fits the whole page.
    /// </summary>
    public ICommand ZoomWholePageCommand { get; }

    /// <summary>
    /// Gets a command that fits multiple pages.
    /// </summary>
    public ICommand ZoomMultiplePagesCommand { get; }

    /// <summary>
    /// Gets a command that increases explicit zoom.
    /// </summary>
    public ICommand ZoomInCommand { get; }

    /// <summary>
    /// Gets a command that decreases explicit zoom.
    /// </summary>
    public ICommand ZoomOutCommand { get; }

    /// <summary>
    /// Gets a command that switches to print layout.
    /// </summary>
    public ICommand PrintLayoutCommand { get; }

    /// <summary>
    /// Gets a command that switches to read mode.
    /// </summary>
    public ICommand ReadModeCommand { get; }

    /// <summary>
    /// Gets a command that switches to web layout.
    /// </summary>
    public ICommand WebLayoutCommand { get; }

    private void ResetDocuments()
    {
        var document = CreateSampleDocument();
        ViewerDocument = DocumentClone.Clone(document);
        EditorDocument = DocumentClone.Clone(document);
        IsEditorReadOnly = false;
        UsePrintLayout();
        Status = "Sample documents were reset.";
    }

    private void ZoomDefault()
    {
        ViewerZoomMode = ProEditDocumentZoomMode.Custom;
        EditorZoomMode = ProEditDocumentZoomMode.Custom;
        ViewerZoom = 1d;
        EditorZoom = 1d;
        Status = "Zoom reset to 100%.";
    }

    private void ZoomPageWidth()
    {
        ViewerZoomMode = ProEditDocumentZoomMode.PageWidth;
        EditorZoomMode = ProEditDocumentZoomMode.PageWidth;
        Status = "Zoom mode: page width.";
    }

    private void ZoomWholePage()
    {
        ViewerZoomMode = ProEditDocumentZoomMode.WholePage;
        EditorZoomMode = ProEditDocumentZoomMode.WholePage;
        Status = "Zoom mode: whole page.";
    }

    private void ZoomMultiplePages()
    {
        MultiplePagesPerRow = 2;
        ViewerZoomMode = ProEditDocumentZoomMode.MultiplePages;
        EditorZoomMode = ProEditDocumentZoomMode.MultiplePages;
        Status = "Zoom mode: multiple pages.";
    }

    private void ZoomIn()
    {
        ViewerZoomMode = ProEditDocumentZoomMode.Custom;
        EditorZoomMode = ProEditDocumentZoomMode.Custom;
        ViewerZoom = Math.Min(5d, ViewerZoom + 0.1d);
        EditorZoom = Math.Min(5d, EditorZoom + 0.1d);
        Status = $"Zoom: {EditorZoom:P0}.";
    }

    private void ZoomOut()
    {
        ViewerZoomMode = ProEditDocumentZoomMode.Custom;
        EditorZoomMode = ProEditDocumentZoomMode.Custom;
        ViewerZoom = Math.Max(0.1d, ViewerZoom - 0.1d);
        EditorZoom = Math.Max(0.1d, EditorZoom - 0.1d);
        Status = $"Zoom: {EditorZoom:P0}.";
    }

    private void UsePrintLayout()
    {
        ViewMode = EditorViewMode.PrintLayout;
        UsePagination = true;
        ShowLayout = true;
        PageFlow = PageFlowDirection.Vertical;
        ViewerZoomMode = ProEditDocumentZoomMode.PageWidth;
        EditorZoomMode = ProEditDocumentZoomMode.Custom;
        Status = "View mode: print layout.";
    }

    private void UseReadMode()
    {
        ViewMode = EditorViewMode.ReadMode;
        UsePagination = true;
        ShowLayout = true;
        PageFlow = PageFlowDirection.Horizontal;
        ViewerZoomMode = ProEditDocumentZoomMode.WholePage;
        EditorZoomMode = ProEditDocumentZoomMode.WholePage;
        Status = "View mode: read mode.";
    }

    private void UseWebLayout()
    {
        ViewMode = EditorViewMode.WebLayout;
        UsePagination = false;
        ShowLayout = false;
        PageFlow = PageFlowDirection.Vertical;
        ViewerZoomMode = ProEditDocumentZoomMode.Custom;
        EditorZoomMode = ProEditDocumentZoomMode.Custom;
        ViewerZoom = 1d;
        EditorZoom = 1d;
        Status = "View mode: web layout.";
    }

    private static Document CreateSampleDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.SectionProperties.PageBackgroundColor = new DocColor(248, 250, 252);
        document.DefaultTextStyle.FontFamily = "Inter";
        document.DefaultTextStyle.FontSize = 14f;

        document.Blocks.Add(CreateHeading("ProEdit shared document controls"));
        document.Blocks.Add(new ParagraphBlock("This document is rendered by the shared Skia host and edited through the same Word editor services used by the full ProEdit Word app."));
        document.Blocks.Add(new ParagraphBlock("The sample controls expose read-only viewing, editable text input, command routing, page flow, pagination, pan, zoom, selection, and rendering options through framework-native APIs."));
        document.Blocks.Add(new ParagraphBlock("Use the toolbar to switch zoom and view modes. Use Ctrl+wheel for pointer anchored zoom and middle mouse drag for pan."));
        document.Blocks.Add(new ParagraphBlock("Shared host features:", new ListInfo(ListKind.Bullet) { LeftIndent = 32f, HangingIndent = 14f, BulletSymbol = "•" }));
        document.Blocks.Add(new ParagraphBlock("Word editor command maps and services are registered once in the reusable core.", new ListInfo(ListKind.Bullet) { LeftIndent = 32f, HangingIndent = 14f, BulletSymbol = "•" }));
        document.Blocks.Add(new ParagraphBlock("Avalonia, Uno, and MAUI controls adapt platform input without duplicating layout or editing logic.", new ListInfo(ListKind.Bullet) { LeftIndent = 32f, HangingIndent = 14f, BulletSymbol = "•" }));
        document.Blocks.Add(CreateCapabilityTable());
        document.Blocks.Add(new ParagraphBlock("Try editing this pane: click into the editor control and type. Read-only mode blocks text mutations while still allowing navigation and selection."));
        document.Blocks.Add(CreateClosingParagraph());
        return document;
    }

    private static ParagraphBlock CreateHeading(string text)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Properties.SpacingAfter = 14f;
        paragraph.Inlines.Add(new RunInline(text, new TextStyleProperties
        {
            FontSize = 24f,
            FontWeight = DocFontWeight.Bold,
            Color = new DocColor(15, 23, 42)
        }));
        return paragraph;
    }

    private static ParagraphBlock CreateClosingParagraph()
    {
        var paragraph = new ParagraphBlock();
        paragraph.Properties.SpacingBefore = 12f;
        paragraph.Inlines.Add(new RunInline("The same sample document is used in every UI framework. ", new TextStyleProperties
        {
            FontWeight = DocFontWeight.Bold
        }));
        paragraph.Inlines.Add(new RunInline("That keeps rendering and editing behavior comparable across hosts."));
        return paragraph;
    }

    private static TableBlock CreateCapabilityTable()
    {
        var table = new TableBlock();
        table.Properties.Width = 500f;
        table.Properties.WidthUnit = TableWidthUnit.Dxa;
        table.Properties.CellPadding = DocThickness.Uniform(8f);
        table.Properties.Borders.Top = CreateBorder();
        table.Properties.Borders.Bottom = CreateBorder();
        table.Properties.Borders.Left = CreateBorder();
        table.Properties.Borders.Right = CreateBorder();
        table.Properties.Borders.InsideHorizontal = CreateBorder();
        table.Properties.Borders.InsideVertical = CreateBorder();
        table.Properties.ColumnWidths.AddRange([180f, 320f]);

        table.Rows.Add(new TableRow([CreateCell("Feature", true), CreateCell("What the sample exercises", true)]));
        table.Rows.Add(new TableRow([CreateCell("Viewing"), CreateCell("Read-only viewer with page-width and whole-page zoom modes.")]));
        table.Rows.Add(new TableRow([CreateCell("Editing"), CreateCell("Editable control with the same selection, keyboard, undo, clipboard, and proofing service stack.")]));
        table.Rows.Add(new TableRow([CreateCell("Host API"), CreateCell("Framework properties bind into the shared ProEditDocumentHost instead of reimplementing behavior.")]));
        return table;
    }

    private static TableCell CreateCell(string text, bool header = false)
    {
        var cell = new TableCell();
        cell.Properties.Padding = DocThickness.Uniform(8f);
        cell.Properties.ShadingColor = header ? new DocColor(226, 232, 240) : new DocColor(255, 255, 255);
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new RunInline(text, new TextStyleProperties
        {
            FontWeight = header ? DocFontWeight.Bold : null,
            Color = new DocColor(30, 41, 59)
        }));
        cell.Paragraphs.Add(paragraph);
        return cell;
    }

    private static BorderLine CreateBorder()
    {
        return new BorderLine
        {
            Thickness = 1f,
            Color = new DocColor(203, 213, 225)
        };
    }
}
