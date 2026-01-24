using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;
using Vibe.Office.Rendering.Skia;
using Vibe.Word.Editor;
using Vibe.Word.Editor.Editing;
using RenderOptions = Vibe.Office.Rendering.RenderOptions;

namespace Vibe.Word.App;

public sealed class DocumentView : Control, ILogicalScrollable
{
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 5f;
    private const float ZoomStep = 0.1f;
    private const int DefaultMultiplePages = 2;
    private const float InkMinDistance = 0.6f;
    private const float InkHitPadding = 6f;
    private const string InkStrokeProgId = "InkStroke";
    private const float CommentBalloonWidth = 220f;
    private const float CommentBalloonPadding = 8f;
    private const float CommentBalloonMargin = 12f;
    private const float CommentBalloonSpacing = 6f;
    private const float CommentBalloonCornerRadius = 4f;
    private const float CommentBalloonFontSize = 12f;

    private static readonly IBrush CommentBalloonFillBrush = new SolidColorBrush(Color.Parse("#FFF7D6"));
    private static readonly IBrush CommentBalloonBorderBrush = new SolidColorBrush(Color.Parse("#D7C284"));
    private static readonly IBrush CommentBalloonTextBrush = new SolidColorBrush(Color.Parse("#303030"));
    private static readonly Typeface CommentBalloonTypeface = new Typeface("Calibri");

    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<DocumentView, Color>(nameof(SurfaceColor), new Color(255, 238, 241, 245));

    private readonly EditorKernel _kernel = new EditorKernel(new LegacyEditorSessionFactory());
    private EditorController _editor;
    private AvaloniaEditorInputAdapter _inputAdapter = null!;
    private readonly SkiaTextMeasurer _textMeasurer = new SkiaTextMeasurer();
    private readonly SkiaDocumentRenderer _renderer = new SkiaDocumentRenderer();
    private SkiaDocumentFontResolver? _fontResolver;
    private readonly RenderOptions _renderOptions = new RenderOptions
    {
        BackgroundColor = new DocColor(238, 241, 245),
        PageColor = DocColor.White,
        PageBorderColor = new DocColor(220, 220, 220),
        PageBorderThickness = 1f,
        HeaderFooterOverlayColor = new DocColor(235, 235, 235, 160),
        HeaderFooterBoundsColor = new DocColor(160, 160, 160),
        HeaderFooterBoundsThickness = 1f,
        TextColor = DocColor.Black,
        SelectionColor = DocColor.SelectionBlue,
        CaretColor = DocColor.Black,
        CaretThickness = 1.5f,
        UseHarfBuzz = true,
        UsePictureCache = true
    };
    private readonly DocColor _defaultCommentHighlightColor;
    private ReviewMarkupMode _reviewMarkupMode = ReviewMarkupMode.All;
    private readonly List<CommentAnchorInfo> _commentAnchors = new();

    private bool _isSelecting;
    private bool _isLoading;
    private Vector _scrollOffset;
    private Size _extent;
    private Size _viewport;
    private float _zoomFactor = 1f;
    private DocumentZoomMode _zoomMode = DocumentZoomMode.Custom;
    private int _multiplePagesPerRow = DefaultMultiplePages;
    private EquationInline? _selectedEquation;
    private long _renderVersion;
    private InkStrokeBuilder? _activeInk;
    private bool _isDrawing;
    private EditorDrawTool _activeDrawTool;
    private HeaderFooterEditSession? _headerFooterSession;
    private HeaderFooterHit? _headerFooterHit;
    private EditorServices? _headerFooterServices;
    private EditorSessionSnapshot? _headerFooterSnapshot;
    private bool _headerFooterDirty;
    private bool _isHeaderFooterSelecting;

    public DocumentView()
    {
        Focusable = true;
        _renderOptions.ZoomFactor = _zoomFactor;
        _renderOptions.SvgRasterizationScale = _zoomFactor;
        _defaultCommentHighlightColor = _renderOptions.CommentHighlightColor;
        _kernel.AddModule(new BasicEditingModule());
        _editor = CreateEditor(DocumentTemplates.CreateDefaultDocument());
        ApplyEditorState();
        UpdateSurfaceColor(SurfaceColor);
    }

    public Color SurfaceColor
    {
        get => GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
    }

    public Document Document => _editor.Document;
    public DocumentLayout Layout => _editor.Layout;
    public TextPosition Caret => _editor.Caret;
    public float ZoomFactor => _zoomFactor;
    public DocumentZoomMode ZoomMode => _zoomMode;
    public Vector ScrollOffset => _scrollOffset;
    public Vector EffectiveScrollOffset => GetEffectiveScrollOffset();

    public EquationInline? SelectedEquation => _selectedEquation;
    public bool IsHeaderFooterEditing => _headerFooterSession is not null;
    public HeaderFooterEditMode HeaderFooterMode => _headerFooterSession?.Mode ?? HeaderFooterEditMode.None;

    public event EventHandler<EquationInline?>? SelectedEquationChanged;
    public event EventHandler? EditorStateChanged;
    public event EventHandler? ZoomChanged;

    public bool ShowInvisibles
    {
        get => _renderOptions.ShowInvisibles;
        set
        {
            if (_renderOptions.ShowInvisibles == value)
            {
                return;
            }

            _renderOptions.ShowInvisibles = value;
            InvalidateAllPages();
        }
    }

    public bool ShowGridlines
    {
        get => _renderOptions.ShowGridlines;
        set
        {
            if (_renderOptions.ShowGridlines == value)
            {
                return;
            }

            _renderOptions.ShowGridlines = value;
            InvalidateAllPages();
        }
    }

    public bool ShowLayout
    {
        get => _renderOptions.ShowLayout;
        set
        {
            if (_renderOptions.ShowLayout == value)
            {
                return;
            }

            _renderOptions.ShowLayout = value;
            InvalidateAllPages();
        }
    }

    public PageFlowDirection PageFlow
    {
        get => _editor.LayoutSettings.PageFlow;
        set
        {
            if (_editor.LayoutSettings.PageFlow == value)
            {
                return;
            }

            _editor.LayoutSettings.PageFlow = value;
            _editor.RefreshLayout();
        }
    }

    public bool UsePagination
    {
        get => _editor.LayoutSettings.UsePagination;
        set
        {
            if (_editor.LayoutSettings.UsePagination == value)
            {
                return;
            }

            _editor.LayoutSettings.UsePagination = value;
            _editor.RefreshLayout();
        }
    }

    public bool UseHarfBuzz
    {
        get => _renderOptions.UseHarfBuzz;
        set
        {
            if (_renderOptions.UseHarfBuzz == value)
            {
                return;
            }

            _renderOptions.UseHarfBuzz = value;
            _textMeasurer.UseHarfBuzz = value;
            _editor.RefreshLayout();
            UpdateScrollMetrics();
            InvalidateVisual();
        }
    }

    public bool UsePictureCache
    {
        get => _renderOptions.UsePictureCache;
        set
        {
            if (_renderOptions.UsePictureCache == value)
            {
                return;
            }

            _renderOptions.UsePictureCache = value;
            InvalidateVisual();
        }
    }

    public ReviewMarkupMode ReviewMarkupMode
    {
        get => _reviewMarkupMode;
        set
        {
            if (_reviewMarkupMode == value)
            {
                return;
            }

            _reviewMarkupMode = value;
            UpdateReviewMarkupMode();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SurfaceColorProperty)
        {
            UpdateSurfaceColor(change.GetNewValue<Color>());
        }
    }

    private void UpdateSurfaceColor(Color color)
    {
        _renderOptions.BackgroundColor = new DocColor(color.R, color.G, color.B);
        InvalidateVisual();
    }

    private void UpdateReviewMarkupMode()
    {
        _renderOptions.CommentHighlightColor = ShouldShowCommentHighlights(_reviewMarkupMode)
            ? _defaultCommentHighlightColor
            : new DocColor(0, 0, 0, 0);
        InvalidateVisual();
    }

    private static bool ShouldShowCommentHighlights(ReviewMarkupMode mode)
    {
        return mode != ReviewMarkupMode.None;
    }

    private static bool ShouldShowCommentBalloons(ReviewMarkupMode mode)
    {
        return mode is ReviewMarkupMode.All or ReviewMarkupMode.Balloons;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_isLoading)
        {
            return;
        }

        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            _editor.UpdateLayout((float)Bounds.Width, (float)Bounds.Height);
            if (_zoomMode != DocumentZoomMode.Custom)
            {
                ApplyZoomMode(_zoomMode, preserveCenter: false);
                return;
            }

            UpdateScrollMetrics();
            UpdateHeaderFooterSessionLayout();
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_isLoading)
        {
            return;
        }

        if (_headerFooterSession is not null)
        {
            if (_headerFooterSession.InputAdapter.HandleTextInput(e))
            {
                MarkHeaderFooterDirty();
                e.Handled = true;
            }

            return;
        }

        if (_inputAdapter.HandleTextInput(e))
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_isLoading)
        {
            return;
        }

        if (HandleZoomShortcut(e))
        {
            e.Handled = true;
            return;
        }

        if (_headerFooterSession is not null)
        {
            if (e.Key == Key.Escape)
            {
                EndHeaderFooterEdit();
                e.Handled = true;
                return;
            }

            var isEditKey = IsHeaderFooterEditKey(e);
            if (_headerFooterSession.InputAdapter.HandleKeyDown(e))
            {
                if (isEditKey)
                {
                    MarkHeaderFooterDirty();
                }

                e.Handled = true;
            }

            return;
        }

        if (_inputAdapter.HandleKeyDown(e))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_isLoading)
        {
            return;
        }

        if (HasZoomModifier(e.KeyModifiers))
        {
            if (e.Delta.Y > 0)
            {
                ZoomIn();
            }
            else if (e.Delta.Y < 0)
            {
                ZoomOut();
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_isLoading)
        {
            return;
        }
        Focus();

        if (_headerFooterSession is null && TryHandleInkPointerPressed(e))
        {
            return;
        }

        if (_headerFooterSession is not null)
        {
            if (TryHandleHeaderFooterPointerPressed(e))
            {
                return;
            }
        }
        else if (TryBeginHeaderFooterEditFromPoint(e))
        {
            return;
        }

        var effectiveOffset = GetEffectiveScrollOffset();
        if (_inputAdapter.HandlePointerPressed(e, effectiveOffset, _zoomFactor, this))
        {
            _isSelecting = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isLoading)
        {
            return;
        }
        if (_isDrawing)
        {
            HandleInkPointerMoved(e);
            return;
        }
        if (_isHeaderFooterSelecting && _headerFooterSession is not null && _headerFooterHit.HasValue)
        {
            var offset = BuildHeaderFooterScrollOffset(_headerFooterHit.Value);
            if (_headerFooterSession.InputAdapter.HandlePointerMoved(e, offset, _zoomFactor, this))
            {
                e.Handled = true;
            }

            return;
        }
        if (!_isSelecting)
        {
            return;
        }

        var current = e.GetCurrentPoint(this);
        if (!current.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var effectiveOffset = GetEffectiveScrollOffset();
        if (_inputAdapter.HandlePointerMoved(e, effectiveOffset, _zoomFactor, this))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isLoading)
        {
            return;
        }

        if (_isDrawing)
        {
            HandleInkPointerReleased(e);
            return;
        }

        if (_isHeaderFooterSelecting && _headerFooterSession is not null && _headerFooterHit.HasValue)
        {
            _isHeaderFooterSelecting = false;
            var offset = BuildHeaderFooterScrollOffset(_headerFooterHit.Value);
            _headerFooterSession.InputAdapter.HandlePointerReleased(e, offset, _zoomFactor, this);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isSelecting)
        {
            _isSelecting = false;
            var effectiveOffset = GetEffectiveScrollOffset();
            _inputAdapter.HandlePointerReleased(e, effectiveOffset, _zoomFactor, this);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var effectiveOffset = GetEffectiveScrollOffset();
        UpdateHeaderFooterRenderOptions();
        context.Custom(new SkiaDrawOperation(Bounds, _editor, _renderer, _renderOptions, effectiveOffset));
        DrawCommentBalloons(context, effectiveOffset);
        DrawInkPreview(context, effectiveOffset);
    }

    public void LoadDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EndHeaderFooterEdit();
        _editor.Changed -= OnEditorChanged;
        _editor = CreateEditor(document);
        _editor.UpdateLayout((float)Bounds.Width, (float)Bounds.Height);
        ApplyEditorState();
    }

    public async Task LoadDocumentAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EndHeaderFooterEdit();
        _editor.Changed -= OnEditorChanged;
        ConfigureMeasurer(document);

        var width = MathF.Max(1f, (float)Bounds.Width);
        var height = MathF.Max(1f, (float)Bounds.Height);

        var editor = await Task.Run(() =>
        {
            var newEditor = new EditorController(_textMeasurer, document);
            newEditor.UpdateLayout(width, height);
            return newEditor;
        }).ConfigureAwait(true);

        _editor = editor;
        EditorHomeServiceRegistry.Register(
            _kernel.Services,
            _kernel.Commands,
            _editor,
            CreateFontService(_editor),
            CreateClipboardService(_editor),
            CreateViewOptionsService());
        ConfigureInputPipeline(editor);
        ApplyEditorState();
    }

    public void RefreshLayout()
    {
        _editor.RefreshLayout();
    }

    public void GoToParagraph(int paragraphIndex)
    {
        if (_editor.Document.ParagraphCount == 0)
        {
            return;
        }

        var index = Math.Clamp(paragraphIndex, 0, _editor.Document.ParagraphCount - 1);
        _editor.SetSelection(new TextRange(new TextPosition(index, 0), new TextPosition(index, 0)));
    }

    public void GoToPosition(TextPosition position, bool ensureVisible = true)
    {
        _editor.SetSelection(new TextRange(position, position));
        if (ensureVisible)
        {
            EnsurePositionVisible(position);
        }
    }

    public void SetLoading(bool isLoading)
    {
        _isLoading = isLoading;
        if (isLoading)
        {
            _isSelecting = false;
            _isDrawing = false;
            _activeInk = null;
        }
    }

    private bool TryHandleInkPointerPressed(PointerPressedEventArgs e)
    {
        if (!TryGetInkTool(out var toolInfo))
        {
            return false;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        _activeDrawTool = toolInfo.Tool;
        _isDrawing = true;
        _isSelecting = false;

        var docPoint = GetDocumentPoint(point.Position);
        if (_activeDrawTool == EditorDrawTool.Eraser)
        {
            e.Pointer.Capture(this);
            EraseInkAtPoint(docPoint);
            e.Handled = true;
            return true;
        }

        _activeInk = new InkStrokeBuilder(toolInfo.Tool, toolInfo.Color, toolInfo.Thickness, toolInfo.BehindText);
        _activeInk.AddPoint(docPoint);
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private void HandleInkPointerMoved(PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var docPoint = GetDocumentPoint(point.Position);
        if (_activeDrawTool == EditorDrawTool.Eraser)
        {
            EraseInkAtPoint(docPoint);
            e.Handled = true;
            return;
        }

        if (_activeInk is null)
        {
            return;
        }

        _activeInk.AddPoint(docPoint);
        InvalidateVisual();
        e.Handled = true;
    }

    private void HandleInkPointerReleased(PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var docPoint = GetDocumentPoint(point.Position);
        if (_activeDrawTool == EditorDrawTool.Eraser)
        {
            EraseInkAtPoint(docPoint);
        }
        else if (_activeInk is not null)
        {
            _activeInk.AddPoint(docPoint);
            CommitInkStroke(_activeInk);
        }

        _activeInk = null;
        _isDrawing = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    private void CommitInkStroke(InkStrokeBuilder stroke)
    {
        if (stroke.Points.Count < 2)
        {
            return;
        }

        var bounds = ComputeStrokeBounds(stroke.Points, stroke.Thickness);
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        ResolveInkAnchor(bounds, out var paragraph, out var page);
        var svgData = BuildInkSvgData(stroke.Points, bounds, stroke.Color, stroke.Thickness);
        var image = new ImageInline(svgData, bounds.Width, bounds.Height, "image/svg+xml")
        {
            EmbeddedObject = new EmbeddedObjectInfo
            {
                ProgId = InkStrokeProgId,
                ContentType = "image/svg+xml"
            }
        };

        var floating = new FloatingObject(image);
        floating.Anchor.HorizontalReference = FloatingHorizontalReference.Page;
        floating.Anchor.VerticalReference = FloatingVerticalReference.Page;
        floating.Anchor.WrapStyle = FloatingWrapStyle.None;
        floating.Anchor.BehindText = stroke.BehindText;

        var pageOriginX = page?.Bounds.Left ?? 0f;
        var pageOriginY = page?.Bounds.Top ?? 0f;
        floating.Anchor.OffsetX = bounds.Left - pageOriginX;
        floating.Anchor.OffsetY = bounds.Top - pageOriginY;
        paragraph.FloatingObjects.Add(floating);
        _editor.RefreshLayout();
    }

    private void ResolveInkAnchor(DocRect bounds, out ParagraphBlock paragraph, out PageLayout? page)
    {
        paragraph = _editor.Document.GetParagraph(0);
        page = _editor.Layout.Pages.Count > 0 ? _editor.Layout.Pages[0] : null;

        var point = new DocPoint(bounds.Left, bounds.Top);
        var layout = _editor.Layout;
        if (layout.Lines.Count > 0)
        {
            var lineIndex = layout.LineIndex.FindLineAtY(point.Y);
            if (lineIndex >= 0 && lineIndex < layout.Lines.Count)
            {
                var line = layout.Lines[lineIndex];
                var paragraphIndex = Math.Clamp(line.ParagraphIndex, 0, Math.Max(0, _editor.Document.ParagraphCount - 1));
                paragraph = _editor.Document.GetParagraph(paragraphIndex);
                var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
                if (pageIndex >= 0 && pageIndex < layout.Pages.Count)
                {
                    page = layout.Pages[pageIndex];
                }
            }
        }
        else if (_editor.Document.ParagraphCount == 0)
        {
            paragraph = new ParagraphBlock();
            _editor.Document.Blocks.Add(paragraph);
        }
    }

    private void EraseInkAtPoint(DocPoint point)
    {
        var layout = _editor.Layout;
        if (layout.FloatingObjects.Count == 0)
        {
            return;
        }

        for (var i = layout.FloatingObjects.Count - 1; i >= 0; i--)
        {
            var floating = layout.FloatingObjects[i];
            if (!IsInkFloatingObject(floating.Object))
            {
                continue;
            }

            var bounds = floating.Bounds;
            if (point.X < bounds.Left - InkHitPadding
                || point.X > bounds.Right + InkHitPadding
                || point.Y < bounds.Top - InkHitPadding
                || point.Y > bounds.Bottom + InkHitPadding)
            {
                continue;
            }

            var paragraphIndex = Math.Clamp(floating.ParagraphIndex, 0, Math.Max(0, _editor.Document.ParagraphCount - 1));
            var paragraph = _editor.Document.GetParagraph(paragraphIndex);
            var index = paragraph.FloatingObjects.IndexOf(floating.Object);
            if (index >= 0)
            {
                paragraph.FloatingObjects.RemoveAt(index);
                _editor.RefreshLayout();
            }

            return;
        }
    }

    private bool TryGetInkTool(out InkToolInfo info)
    {
        info = default;
        if (!TryGetService<IDrawToolService>(out var drawTool))
        {
            return false;
        }

        return drawTool.ActiveTool switch
        {
            EditorDrawTool.Pen => TryBuildInkTool(drawTool.PenColor, drawTool.PenThickness, false, drawTool.ActiveTool, out info),
            EditorDrawTool.Pencil => TryBuildInkTool(drawTool.PencilColor, drawTool.PencilThickness, false, drawTool.ActiveTool, out info),
            EditorDrawTool.Highlighter => TryBuildInkTool(drawTool.HighlighterColor, drawTool.HighlighterThickness, true, drawTool.ActiveTool, out info),
            EditorDrawTool.Eraser => TryBuildInkTool(DocColor.Transparent, InkHitPadding, false, drawTool.ActiveTool, out info),
            _ => false
        };
    }

    private static bool TryBuildInkTool(DocColor color, float thickness, bool behindText, EditorDrawTool tool, out InkToolInfo info)
    {
        if (thickness <= 0f)
        {
            info = default;
            return false;
        }

        info = new InkToolInfo(tool, color, thickness, behindText);
        return true;
    }

    private static bool IsInkFloatingObject(FloatingObject floating)
    {
        return floating.Content is ImageInline image
            && image.EmbeddedObject?.ProgId != null
            && string.Equals(image.EmbeddedObject.ProgId, InkStrokeProgId, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawInkPreview(DrawingContext context, Vector effectiveOffset)
    {
        if (_activeInk is null || _activeInk.Points.Count < 2)
        {
            return;
        }

        var points = _activeInk.Points;
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            var start = DocToView(points[0], effectiveOffset);
            geometryContext.BeginFigure(start, false);
            for (var i = 1; i < points.Count; i++)
            {
                geometryContext.LineTo(DocToView(points[i], effectiveOffset));
            }
        }

        var color = ToAvaloniaColor(_activeInk.Color);
        var pen = new Pen(
            new SolidColorBrush(color),
            _activeInk.Thickness * _zoomFactor,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, geometry);
    }

    private void UpdateCommentAnchors()
    {
        _commentAnchors.Clear();
        var anchors = ReviewingHelpers.BuildCommentAnchors(_editor.Document);
        if (anchors.Count == 0 || _editor.Layout.Lines.Count == 0)
        {
            return;
        }

        foreach (var anchor in anchors)
        {
            var position = anchor.Value;
            var lineIndex = EditorSelectionService.FindLineIndexForPosition(_editor.Layout, position, out var line);
            var pageIndex = _editor.Layout.LineIndex.GetPageForLine(lineIndex);
            if (pageIndex < 0)
            {
                pageIndex = 0;
            }

            _commentAnchors.Add(new CommentAnchorInfo(
                anchor.Key,
                position.ParagraphIndex,
                position.Offset,
                line.Y,
                line.LineHeight,
                pageIndex));
        }

        _commentAnchors.Sort(CompareCommentAnchors);
    }

    private static int CompareCommentAnchors(CommentAnchorInfo left, CommentAnchorInfo right)
    {
        var pageCompare = left.PageIndex.CompareTo(right.PageIndex);
        if (pageCompare != 0)
        {
            return pageCompare;
        }

        var yCompare = left.DocY.CompareTo(right.DocY);
        if (yCompare != 0)
        {
            return yCompare;
        }

        var paragraphCompare = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return left.Offset.CompareTo(right.Offset);
    }

    private void DrawCommentBalloons(DrawingContext context, Vector effectiveOffset)
    {
        if (!ShouldShowCommentBalloons(_reviewMarkupMode))
        {
            return;
        }

        if (_commentAnchors.Count == 0 || _editor.Document.Comments.Count == 0)
        {
            return;
        }

        var pages = _editor.Layout.Pages;
        if (pages.Count == 0)
        {
            return;
        }

        var zoom = _zoomFactor;
        var balloonWidth = CommentBalloonWidth * zoom;
        var padding = CommentBalloonPadding * zoom;
        var spacing = CommentBalloonSpacing * zoom;
        var cornerRadius = CommentBalloonCornerRadius * zoom;
        var borderThickness = Math.Max(1f, zoom * 0.75f);
        var borderPen = new Pen(CommentBalloonBorderBrush, borderThickness);
        var bottomsByPage = new Dictionary<int, double>();

        foreach (var anchor in _commentAnchors)
        {
            if (!_editor.Document.Comments.TryGetValue(anchor.Id, out var comment))
            {
                continue;
            }

            if (anchor.PageIndex < 0 || anchor.PageIndex >= pages.Count)
            {
                continue;
            }

            var page = pages[anchor.PageIndex];
            var docX = page.Bounds.Right + CommentBalloonMargin;
            var viewX = (docX * zoom) - effectiveOffset.X;
            var viewY = (anchor.DocY * zoom) - effectiveOffset.Y;

            var text = ReviewingHelpers.BuildCommentDisplayText(comment);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = $"Comment {anchor.Id}";
            }

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                CommentBalloonTypeface,
                CommentBalloonFontSize * zoom,
                CommentBalloonTextBrush);
            formatted.MaxTextWidth = Math.Max(1f, balloonWidth - (2f * padding));
            formatted.TextAlignment = TextAlignment.Left;

            var height = formatted.Height + (2f * padding);
            if (bottomsByPage.TryGetValue(anchor.PageIndex, out var lastBottom) && viewY < lastBottom + spacing)
            {
                viewY = lastBottom + spacing;
            }

            bottomsByPage[anchor.PageIndex] = viewY + height;

            var rect = new Rect(viewX, viewY, balloonWidth, height);
            context.DrawRectangle(CommentBalloonFillBrush, borderPen, rect, cornerRadius, cornerRadius, default);
            context.DrawText(formatted, new Point(rect.X + padding, rect.Y + padding));
        }
    }

    private readonly record struct CommentAnchorInfo(
        int Id,
        int ParagraphIndex,
        int Offset,
        float DocY,
        float LineHeight,
        int PageIndex);

    private Point DocToView(DocPoint point, Vector effectiveOffset)
    {
        return new Point(point.X * _zoomFactor - effectiveOffset.X, point.Y * _zoomFactor - effectiveOffset.Y);
    }

    private DocPoint GetDocumentPoint(Point point)
    {
        var offset = GetEffectiveScrollOffset();
        var docX = (float)((point.X + offset.X) / _zoomFactor);
        var docY = (float)((point.Y + offset.Y) / _zoomFactor);
        return new DocPoint(docX, docY);
    }

    private static DocRect ComputeStrokeBounds(IReadOnlyList<DocPoint> points, float thickness)
    {
        if (points.Count == 0)
        {
            return new DocRect(0f, 0f, 0f, 0f);
        }

        var minX = points[0].X;
        var minY = points[0].Y;
        var maxX = points[0].X;
        var maxY = points[0].Y;

        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        var half = MathF.Max(0.5f, thickness / 2f);
        minX -= half;
        minY -= half;
        maxX += half;
        maxY += half;

        var width = MathF.Max(1f, maxX - minX);
        var height = MathF.Max(1f, maxY - minY);
        return new DocRect(minX, minY, width, height);
    }

    private static byte[] BuildInkSvgData(
        IReadOnlyList<DocPoint> points,
        DocRect bounds,
        DocColor color,
        float thickness)
    {
        var builder = new StringBuilder(256 + points.Count * 12);
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"");
        AppendFloat(builder, bounds.Width);
        builder.Append("\" height=\"");
        AppendFloat(builder, bounds.Height);
        builder.Append("\" viewBox=\"0 0 ");
        AppendFloat(builder, bounds.Width);
        builder.Append(' ');
        AppendFloat(builder, bounds.Height);
        builder.Append("\">");
        builder.Append("<path d=\"");

        if (points.Count > 0)
        {
            AppendPathPoint(builder, 'M', points[0], bounds);
            for (var i = 1; i < points.Count; i++)
            {
                AppendPathPoint(builder, 'L', points[i], bounds);
            }
        }

        builder.Append("\" fill=\"none\" stroke=\"");
        AppendSvgColor(builder, color);
        builder.Append("\" stroke-width=\"");
        AppendFloat(builder, thickness);
        builder.Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
        if (color.A < 255)
        {
            builder.Append(" stroke-opacity=\"");
            AppendFloat(builder, color.A / 255f);
            builder.Append('"');
        }

        builder.Append(" /></svg>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendPathPoint(StringBuilder builder, char command, DocPoint point, DocRect bounds)
    {
        builder.Append(command);
        builder.Append(' ');
        AppendFloat(builder, point.X - bounds.Left);
        builder.Append(' ');
        AppendFloat(builder, point.Y - bounds.Top);
        builder.Append(' ');
    }

    private static void AppendSvgColor(StringBuilder builder, DocColor color)
    {
        builder.Append("rgb(");
        builder.Append(color.R);
        builder.Append(',');
        builder.Append(color.G);
        builder.Append(',');
        builder.Append(color.B);
        builder.Append(')');
    }

    private static void AppendFloat(StringBuilder builder, float value)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var written, "0.###", CultureInfo.InvariantCulture))
        {
            builder.Append(buffer.Slice(0, written));
            return;
        }

        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static Color ToAvaloniaColor(DocColor color)
    {
        return new Color(color.A, color.R, color.G, color.B);
    }

    private readonly record struct InkToolInfo(EditorDrawTool Tool, DocColor Color, float Thickness, bool BehindText);

    private sealed class InkStrokeBuilder
    {
        public EditorDrawTool Tool { get; }
        public DocColor Color { get; }
        public float Thickness { get; }
        public bool BehindText { get; }
        public List<DocPoint> Points { get; } = new List<DocPoint>(64);

        public InkStrokeBuilder(EditorDrawTool tool, DocColor color, float thickness, bool behindText)
        {
            Tool = tool;
            Color = color;
            Thickness = thickness;
            BehindText = behindText;
        }

        public void AddPoint(DocPoint point)
        {
            if (Points.Count == 0)
            {
                Points.Add(point);
                return;
            }

            var last = Points[^1];
            var dx = point.X - last.X;
            var dy = point.Y - last.Y;
            if (dx * dx + dy * dy < InkMinDistance * InkMinDistance)
            {
                return;
            }

            Points.Add(point);
        }
    }

    private enum HeaderFooterVariant
    {
        Default,
        First,
        Even
    }

    private readonly record struct HeaderFooterTarget(
        int SectionIndex,
        HeaderFooterVariant Variant,
        HeaderFooter Container);

    private sealed class HeaderFooterEditSession
    {
        public HeaderFooterEditMode Mode { get; }
        public HeaderFooterTarget Target { get; }
        public Document Document { get; }
        public EditorController Editor { get; }
        public AvaloniaEditorInputAdapter InputAdapter { get; }

        public HeaderFooterEditSession(
            HeaderFooterEditMode mode,
            HeaderFooterTarget target,
            Document document,
            EditorController editor,
            AvaloniaEditorInputAdapter inputAdapter)
        {
            Mode = mode;
            Target = target;
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Editor = editor ?? throw new ArgumentNullException(nameof(editor));
            InputAdapter = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));
        }
    }

    private readonly record struct HeaderFooterHit(
        HeaderFooterEditMode Mode,
        int PageIndex,
        DocRect Region,
        float OriginX,
        float OriginY,
        float ContentWidth,
        float ContentHeight,
        HeaderFooterTarget Target);

    private sealed class HeaderFooterCommandRouter : IEditorCommandRouter
    {
        private readonly IEditorCommandRouter _inner;
        private readonly EditorCommandHistory? _history;
        private readonly Action _onModified;

        public HeaderFooterCommandRouter(IEditorCommandRouter inner, Action onModified, EditorCommandHistory? history)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _onModified = onModified ?? throw new ArgumentNullException(nameof(onModified));
            _history = history;
        }

        public bool CanExecute(string commandId, object? payload = null, RibbonContextSnapshot? context = null)
        {
            return _inner.CanExecute(commandId, payload, context);
        }

        public async ValueTask<bool> ExecuteAsync(
            string commandId,
            object? payload = null,
            RibbonContextSnapshot? context = null,
            bool recordHistory = true)
        {
            var beforeVersion = _history?.Version ?? -1;
            var result = await _inner.ExecuteAsync(commandId, payload, context, recordHistory).ConfigureAwait(true);
            if (result && (_history is null || _history.Version != beforeVersion))
            {
                _onModified();
            }

            return result;
        }
    }


    private EditorController CreateEditor(Document document)
    {
        ConfigureMeasurer(document);
        var editor = new EditorController(_textMeasurer, document);
        _editor = editor;
        EditorHomeServiceRegistry.Register(
            _kernel.Services,
            _kernel.Commands,
            editor,
            CreateFontService(editor),
            CreateClipboardService(editor),
            CreateViewOptionsService());
        ConfigureInputPipeline(editor);
        return editor;
    }

    private IFontService CreateFontService(IEditorSession session)
    {
        return new SkiaFontServiceAdapter(session);
    }

    private IClipboardService CreateClipboardService(IEditorSession session)
    {
        return new AvaloniaClipboardService(
            () => TopLevel.GetTopLevel(this)?.Clipboard,
            () => !session.Selection.IsEmpty,
            () => !session.Selection.IsEmpty);
    }

    private IEditorViewOptionsService CreateViewOptionsService()
    {
        return new EditorViewOptionsService(this);
    }

    private void ConfigureInputPipeline(EditorController editor)
    {
        _kernel.Services.TryGet<IUndoRedoService>(out var undoRedo);
        _kernel.Services.TryGet<IClipboardService>(out var clipboard);
        _kernel.Services.TryGet<ISelectionTextService>(out var selectionText);
        var commandRouter = new EditorCommandInputRouter(_kernel.Commands, editor, undoRedo, clipboard, selectionText);
        _inputAdapter = new AvaloniaEditorInputAdapter(commandRouter);
    }

    private void ConfigureMeasurer(Document document)
    {
        _textMeasurer.UseHarfBuzz = _renderOptions.UseHarfBuzz;
        _fontResolver?.Dispose();
        _fontResolver = new SkiaDocumentFontResolver(document.Fonts);
        _textMeasurer.TypefaceResolver = _fontResolver;
        _renderer.TypefaceResolver = _fontResolver;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _fontResolver?.Dispose();
        _fontResolver = null;
    }

    private void ApplyEditorState()
    {
        _editor.Changed += OnEditorChanged;
        _scrollOffset = default;
        UpdateDirtyPages(GetAllPages());
        UpdateScrollMetrics();
        UpdateSelectedEquation();
        UpdateHeaderFooterSessionLayout();
        UpdateCommentAnchors();
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        UpdateScrollMetrics();
        UpdateDirtyPages(_editor.DirtyPages);
        UpdateSelectedEquation();
        UpdateHeaderFooterSessionLayout();
        UpdateCommentAnchors();
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryGetService<T>(out T service) where T : class
    {
        if (_headerFooterSession is not null
            && _headerFooterServices is not null
            && _headerFooterServices.TryGet(out service))
        {
            return true;
        }

        return _kernel.Services.TryGet(out service);
    }

    public bool TryGetService(Type serviceType, out object? service)
    {
        if (_headerFooterSession is not null
            && _headerFooterServices is not null
            && _headerFooterServices.TryGet(serviceType, out service))
        {
            return true;
        }

        return _kernel.Services.TryGet(serviceType, out service);
    }

    public void RegisterService<T>(T service) where T : class => _kernel.Services.Register(service);

    private void InvalidateAllPages()
    {
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
    }

    private void UpdateDirtyPages(IReadOnlyList<int> pages)
    {
        _renderVersion++;
        _renderOptions.DirtyPages = pages;
        _renderOptions.DirtyVersion = _renderVersion;
    }

    private IReadOnlyList<int> GetAllPages()
    {
        var count = _editor.Layout.Pages.Count;
        if (count <= 0)
        {
            return Array.Empty<int>();
        }

        return Enumerable.Range(0, count).ToArray();
    }

    private void UpdateSelectedEquation()
    {
        var equation = _editor.GetEquationAtCaret();
        if (ReferenceEquals(equation, _selectedEquation))
        {
            return;
        }

        _selectedEquation = equation;
        SelectedEquationChanged?.Invoke(this, equation);
    }

    private void UpdateHeaderFooterRenderOptions()
    {
        if (_headerFooterSession is null)
        {
            _renderOptions.HeaderFooterMode = HeaderFooterEditMode.None;
            _renderOptions.HeaderFooterSelection = null;
            _renderOptions.HeaderFooterCaret = default;
            _renderOptions.ShowHeaderFooterCaret = false;
            return;
        }

        _renderOptions.HeaderFooterMode = _headerFooterSession.Mode;
        _renderOptions.HeaderFooterSelection = _headerFooterSession.Editor.Selection.IsEmpty
            ? null
            : _headerFooterSession.Editor.Selection;
        _renderOptions.HeaderFooterCaret = _headerFooterSession.Editor.Caret;
        _renderOptions.ShowHeaderFooterCaret = true;
    }

    public void BeginHeaderFooterEdit(HeaderFooterEditMode mode)
    {
        if (mode == HeaderFooterEditMode.None)
        {
            EndHeaderFooterEdit();
            return;
        }

        var pageIndex = ResolveHeaderFooterPageIndex();
        if (!TryBuildHeaderFooterHit(pageIndex, mode, out var hit))
        {
            return;
        }

        BeginHeaderFooterEdit(hit);
    }

    private bool BeginHeaderFooterEdit(HeaderFooterHit hit)
    {
        if (_headerFooterSession is not null && _headerFooterSession.Target.Equals(hit.Target))
        {
            _headerFooterHit = hit;
            UpdateHeaderFooterSessionLayout(hit);
            InvalidateVisual();
            return true;
        }

        EndHeaderFooterEdit();

        if (!TryCreateHeaderFooterSession(hit, out var session, out var services))
        {
            return false;
        }

        _headerFooterSession = session;
        _headerFooterServices = services;
        _headerFooterHit = hit;
        _headerFooterSession.Editor.Changed += OnHeaderFooterEditorChanged;
        _headerFooterDirty = false;

        if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _headerFooterSnapshot = history.CaptureSnapshot();
        }

        UpdateHeaderFooterSessionLayout(hit);
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void EndHeaderFooterEdit()
    {
        if (_headerFooterSession is null)
        {
            return;
        }

        _headerFooterSession.Editor.Changed -= OnHeaderFooterEditorChanged;
        if (_headerFooterDirty && _headerFooterSnapshot.HasValue
            && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            history.RecordSnapshot(_headerFooterSnapshot.Value);
        }

        _headerFooterSession = null;
        _headerFooterHit = null;
        _headerFooterServices = null;
        _headerFooterSnapshot = null;
        _headerFooterDirty = false;
        _isHeaderFooterSelecting = false;
        UpdateHeaderFooterRenderOptions();
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeaderFooterEditorChanged(object? sender, EventArgs e)
    {
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryCreateHeaderFooterSession(HeaderFooterHit hit, out HeaderFooterEditSession session, out EditorServices services)
    {
        session = null!;
        services = null!;
        if (hit.Mode == HeaderFooterEditMode.None)
        {
            return false;
        }

        var document = BuildHeaderFooterDocument(_editor.Document, hit.Target, hit.Mode);
        var editor = new EditorController(_textMeasurer, document);
        services = new EditorServices();
        var dispatcher = new EditorCommandDispatcher();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));
        var viewOptions = _kernel.Services.TryGet<IEditorViewOptionsService>(out var resolvedViewOptions)
            ? resolvedViewOptions
            : null;
        var router = EditorHomeServiceRegistry.Register(
            services,
            dispatcher,
            editor,
            CreateFontService(editor),
            CreateClipboardService(editor),
            viewOptions);
        var undoRedo = services.GetRequired<IUndoRedoService>();
        var history = undoRedo as EditorCommandHistory;
        services.Register<IEditorCommandRouter>(new HeaderFooterCommandRouter(router, MarkHeaderFooterDirty, history));
        var clipboard = services.GetRequired<IClipboardService>();
        var selectionText = services.GetRequired<ISelectionTextService>();
        var inputRouter = new EditorCommandInputRouter(dispatcher, editor, undoRedo, clipboard, selectionText);
        var inputAdapter = new AvaloniaEditorInputAdapter(inputRouter);
        session = new HeaderFooterEditSession(hit.Mode, hit.Target, document, editor, inputAdapter);
        return true;
    }

    private void ApplyHeaderFooterChanges()
    {
        if (_headerFooterSession is null)
        {
            return;
        }

        if (!_kernel.Services.TryGet<IEditorCommandRouter>(out var router))
        {
            return;
        }

        var request = new EditorHeaderFooterUpdateRequest(
            _headerFooterSession.Editor.Document.Blocks,
            _headerFooterSession.Editor.Document,
            _headerFooterSession.Target.Container);
        var commandId = _headerFooterSession.Mode == HeaderFooterEditMode.Footer
            ? EditorInsertCommandIds.HeaderFooter.Footer
            : EditorInsertCommandIds.HeaderFooter.Header;
        _ = router.ExecuteAsync(commandId, request, recordHistory: false);
    }

    private void MarkHeaderFooterDirty()
    {
        if (_headerFooterSession is null)
        {
            return;
        }

        _headerFooterDirty = true;
        ApplyHeaderFooterChanges();
    }

    private void UpdateHeaderFooterSessionLayout()
    {
        if (_headerFooterSession is null)
        {
            return;
        }

        if (_headerFooterHit.HasValue)
        {
            UpdateHeaderFooterSessionLayout(_headerFooterHit.Value);
            return;
        }

        var pageIndex = ResolveHeaderFooterPageIndex();
        if (TryBuildHeaderFooterHit(pageIndex, _headerFooterSession.Mode, out var hit))
        {
            _headerFooterHit = hit;
            UpdateHeaderFooterSessionLayout(hit);
        }
    }

    private void UpdateHeaderFooterSessionLayout(HeaderFooterHit hit)
    {
        if (_headerFooterSession is null)
        {
            return;
        }

        ApplyHeaderFooterLayoutSettings(_headerFooterSession.Editor.LayoutSettings, _editor.Layout.Settings, hit.ContentWidth, hit.ContentHeight);
        _headerFooterSession.Editor.UpdateLayout(hit.ContentWidth, hit.ContentHeight);
    }

    private bool TryBeginHeaderFooterEditFromPoint(PointerPressedEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return false;
        }

        if (!TryGetDocumentPoint(e, out var docX, out var docY))
        {
            return false;
        }

        if (TryResolveHeaderFooterHit(docX, docY, HeaderFooterEditMode.Header, out var hit))
        {
            if (!BeginHeaderFooterEdit(hit))
            {
                return false;
            }

            return StartHeaderFooterSelection(e, hit);
        }

        if (TryResolveHeaderFooterHit(docX, docY, HeaderFooterEditMode.Footer, out hit))
        {
            if (!BeginHeaderFooterEdit(hit))
            {
                return false;
            }

            return StartHeaderFooterSelection(e, hit);
        }

        return false;
    }

    private bool TryHandleHeaderFooterPointerPressed(PointerPressedEventArgs e)
    {
        if (_headerFooterSession is null)
        {
            return false;
        }

        if (!TryGetDocumentPoint(e, out var docX, out var docY))
        {
            return false;
        }

        if (TryResolveHeaderFooterHit(docX, docY, _headerFooterSession.Mode, out var hit))
        {
            if (!BeginHeaderFooterEdit(hit))
            {
                return false;
            }

            return StartHeaderFooterSelection(e, hit);
        }

        EndHeaderFooterEdit();
        return false;
    }

    private bool StartHeaderFooterSelection(PointerPressedEventArgs e, HeaderFooterHit hit)
    {
        if (_headerFooterSession is null)
        {
            return false;
        }

        _isSelecting = false;
        _isHeaderFooterSelecting = true;
        _headerFooterHit = hit;
        UpdateHeaderFooterSessionLayout(hit);
        var offset = BuildHeaderFooterScrollOffset(hit);
        if (_headerFooterSession.InputAdapter.HandlePointerPressed(e, offset, _zoomFactor, this))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private bool TryGetDocumentPoint(PointerEventArgs e, out float docX, out float docY)
    {
        var point = e.GetCurrentPoint(this);
        var position = point.Position;
        var effectiveOffset = GetEffectiveScrollOffset();
        var scale = _zoomFactor <= 0f ? 1f : _zoomFactor;
        docX = (float)((position.X + effectiveOffset.X) / scale);
        docY = (float)((position.Y + effectiveOffset.Y) / scale);
        return true;
    }

    private bool TryResolveHeaderFooterHit(float docX, float docY, HeaderFooterEditMode mode, out HeaderFooterHit hit)
    {
        hit = default;
        if (mode == HeaderFooterEditMode.None)
        {
            return false;
        }

        var pages = _editor.Layout.Pages;
        var pageIndex = FindPageIndex(pages, docX, docY);
        if (pageIndex < 0)
        {
            return false;
        }

        if (!TryBuildHeaderFooterHit(pageIndex, mode, out hit))
        {
            return false;
        }

        return hit.Region.Contains(docX, docY);
    }

    private bool TryBuildHeaderFooterHit(int pageIndex, HeaderFooterEditMode mode, out HeaderFooterHit hit)
    {
        hit = default;
        var layout = _editor.Layout;
        if (mode == HeaderFooterEditMode.None || pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return false;
        }

        if (pageIndex >= layout.PageSections.Count)
        {
            return false;
        }

        if (!TryResolveHeaderFooterTarget(pageIndex, mode, out var target))
        {
            return false;
        }

        var page = layout.Pages[pageIndex];
        var section = layout.PageSections[pageIndex];
        var contentLeft = page.Bounds.X + section.MarginLeft;
        var contentRight = page.Bounds.Right - section.MarginRight;
        var contentWidth = MathF.Max(1f, contentRight - contentLeft);
        var lineHeight = MathF.Max(1f, layout.LineHeight);

        IReadOnlyList<HeaderFooterLine> lines = Array.Empty<HeaderFooterLine>();
        if (TryGetHeaderFooterLayout(layout, pageIndex, out var headerFooter))
        {
            lines = mode == HeaderFooterEditMode.Footer
                ? headerFooter.FooterLines
                : headerFooter.HeaderLines;
        }

        var regionTop = 0f;
        var regionBottom = 0f;
        if (lines.Count > 0)
        {
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var line in lines)
            {
                if (line.Y < minY)
                {
                    minY = line.Y;
                }

                var lineBottom = line.Y + line.LineHeight;
                if (lineBottom > maxY)
                {
                    maxY = lineBottom;
                }
            }

            regionTop = minY;
            regionBottom = maxY;
        }
        else if (mode == HeaderFooterEditMode.Header)
        {
            regionTop = page.Bounds.Y + section.HeaderOffset;
            regionBottom = regionTop + lineHeight;
        }
        else
        {
            regionTop = page.Bounds.Bottom - section.FooterOffset - lineHeight;
            regionBottom = regionTop + lineHeight;
        }

        var regionHeight = MathF.Max(1f, regionBottom - regionTop);
        var region = new DocRect(contentLeft, regionTop, contentWidth, regionHeight);
        hit = new HeaderFooterHit(
            mode,
            pageIndex,
            region,
            contentLeft,
            regionTop,
            contentWidth,
            MathF.Max(lineHeight, regionHeight),
            target);
        return true;
    }

    private bool TryResolveHeaderFooterTarget(int pageIndex, HeaderFooterEditMode mode, out HeaderFooterTarget target)
    {
        target = default;
        var layout = _editor.Layout;
        if (mode == HeaderFooterEditMode.None
            || pageIndex < 0
            || pageIndex >= layout.Pages.Count
            || pageIndex >= layout.PageSections.Count)
        {
            return false;
        }

        static void ResolveEffectiveHeaderFooter(
            HeaderFooter current,
            int currentSectionIndex,
            ref HeaderFooter? effective,
            ref int effectiveSectionIndex)
        {
            if (current.IsDefined || current.Blocks.Count > 0)
            {
                effective = current;
                effectiveSectionIndex = currentSectionIndex;
            }
        }

        var document = _editor.Document;
        HeaderFooter? effectiveDefaultHeader = null;
        HeaderFooter? effectiveDefaultFooter = null;
        HeaderFooter? effectiveFirstHeader = null;
        HeaderFooter? effectiveFirstFooter = null;
        HeaderFooter? effectiveEvenHeader = null;
        HeaderFooter? effectiveEvenFooter = null;
        var effectiveDefaultHeaderSection = -1;
        var effectiveDefaultFooterSection = -1;
        var effectiveFirstHeaderSection = -1;
        var effectiveFirstFooterSection = -1;
        var effectiveEvenHeaderSection = -1;
        var effectiveEvenFooterSection = -1;
        var currentSectionIndex = -1;

        for (var i = 0; i <= pageIndex; i++)
        {
            var sectionSettings = layout.PageSections[i];
            var sectionInfo = document.GetSection(sectionSettings.SectionIndex);
            if (sectionSettings.SectionIndex != currentSectionIndex)
            {
                currentSectionIndex = sectionSettings.SectionIndex;
                ResolveEffectiveHeaderFooter(sectionInfo.Header, currentSectionIndex, ref effectiveDefaultHeader, ref effectiveDefaultHeaderSection);
                ResolveEffectiveHeaderFooter(sectionInfo.Footer, currentSectionIndex, ref effectiveDefaultFooter, ref effectiveDefaultFooterSection);
                ResolveEffectiveHeaderFooter(sectionInfo.FirstHeader, currentSectionIndex, ref effectiveFirstHeader, ref effectiveFirstHeaderSection);
                ResolveEffectiveHeaderFooter(sectionInfo.FirstFooter, currentSectionIndex, ref effectiveFirstFooter, ref effectiveFirstFooterSection);
                ResolveEffectiveHeaderFooter(sectionInfo.EvenHeader, currentSectionIndex, ref effectiveEvenHeader, ref effectiveEvenHeaderSection);
                ResolveEffectiveHeaderFooter(sectionInfo.EvenFooter, currentSectionIndex, ref effectiveEvenFooter, ref effectiveEvenFooterSection);
            }

            if (i != pageIndex)
            {
                continue;
            }

            var isFirstPageOfSection = i == 0 || layout.PageSections[i - 1].SectionIndex != sectionSettings.SectionIndex;
            var pageNumber = layout.Pages[i].Index + 1;
            var isEvenPage = pageNumber % 2 == 0;
            var variant = HeaderFooterVariant.Default;
            if (isFirstPageOfSection && sectionInfo.Properties.DifferentFirstPageHeaderFooter == true)
            {
                variant = HeaderFooterVariant.First;
            }
            else if (document.EvenAndOddHeaders && isEvenPage)
            {
                variant = HeaderFooterVariant.Even;
            }

            HeaderFooter container;
            int containerSectionIndex;
            switch (variant)
            {
                case HeaderFooterVariant.First:
                    if (mode == HeaderFooterEditMode.Footer)
                    {
                        container = effectiveFirstFooter ?? sectionInfo.FirstFooter;
                        containerSectionIndex = effectiveFirstFooter is null ? sectionSettings.SectionIndex : effectiveFirstFooterSection;
                    }
                    else
                    {
                        container = effectiveFirstHeader ?? sectionInfo.FirstHeader;
                        containerSectionIndex = effectiveFirstHeader is null ? sectionSettings.SectionIndex : effectiveFirstHeaderSection;
                    }
                    break;
                case HeaderFooterVariant.Even:
                    if (mode == HeaderFooterEditMode.Footer)
                    {
                        container = effectiveEvenFooter ?? sectionInfo.EvenFooter;
                        containerSectionIndex = effectiveEvenFooter is null ? sectionSettings.SectionIndex : effectiveEvenFooterSection;
                    }
                    else
                    {
                        container = effectiveEvenHeader ?? sectionInfo.EvenHeader;
                        containerSectionIndex = effectiveEvenHeader is null ? sectionSettings.SectionIndex : effectiveEvenHeaderSection;
                    }
                    break;
                default:
                    if (mode == HeaderFooterEditMode.Footer)
                    {
                        container = effectiveDefaultFooter ?? sectionInfo.Footer;
                        containerSectionIndex = effectiveDefaultFooter is null ? sectionSettings.SectionIndex : effectiveDefaultFooterSection;
                    }
                    else
                    {
                        container = effectiveDefaultHeader ?? sectionInfo.Header;
                        containerSectionIndex = effectiveDefaultHeader is null ? sectionSettings.SectionIndex : effectiveDefaultHeaderSection;
                    }
                    break;
            }

            target = new HeaderFooterTarget(containerSectionIndex, variant, container);
            return true;
        }

        return false;
    }

    private static bool TryGetHeaderFooterLayout(DocumentLayout layout, int pageIndex, out HeaderFooterLayout headerFooter)
    {
        foreach (var candidate in layout.HeaderFooters)
        {
            if (candidate.PageIndex == pageIndex)
            {
                headerFooter = candidate;
                return true;
            }
        }

        headerFooter = null!;
        return false;
    }

    private Vector BuildHeaderFooterScrollOffset(HeaderFooterHit hit)
    {
        var effectiveOffset = GetEffectiveScrollOffset();
        var scale = _zoomFactor <= 0f ? 1f : _zoomFactor;
        return new Vector(
            effectiveOffset.X - hit.OriginX * scale,
            effectiveOffset.Y - hit.OriginY * scale);
    }

    private static bool IsHeaderFooterEditKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back:
            case Key.Delete:
            case Key.Enter:
                return true;
        }

        var modifiers = e.KeyModifiers;
        var commandModifier = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        if (!commandModifier || modifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        return e.Key == Key.V || e.Key == Key.X || e.Key == Key.Z || e.Key == Key.Y;
    }

    private static Document BuildHeaderFooterDocument(Document source, HeaderFooterTarget target, HeaderFooterEditMode mode)
    {
        var clone = DocumentClone.Clone(source);
        var headerFooter = ResolveHeaderFooterContainer(clone, target, mode);
        var blocks = headerFooter.Blocks;
        clone.Blocks.Clear();
        if (blocks.Count == 0)
        {
            clone.Blocks.Add(new ParagraphBlock());
        }
        else
        {
            clone.Blocks.AddRange(blocks);
        }

        return clone;
    }

    private static HeaderFooter ResolveHeaderFooterContainer(Document document, HeaderFooterTarget target, HeaderFooterEditMode mode)
    {
        var section = document.GetSection(target.SectionIndex);
        if (target.Variant == HeaderFooterVariant.First)
        {
            return mode == HeaderFooterEditMode.Footer ? section.FirstFooter : section.FirstHeader;
        }

        if (target.Variant == HeaderFooterVariant.Even)
        {
            return mode == HeaderFooterEditMode.Footer ? section.EvenFooter : section.EvenHeader;
        }

        return mode == HeaderFooterEditMode.Footer ? section.Footer : section.Header;
    }

    private static void ApplyHeaderFooterLayoutSettings(LayoutSettings target, LayoutSettings source, float width, float height)
    {
        target.ViewportWidth = width;
        target.ViewportHeight = height;
        target.UsePagination = false;
        target.PageWidth = width;
        target.PageHeight = height;
        target.PageGap = 0f;
        target.PageFlow = PageFlowDirection.Vertical;
        target.MarginLeft = 0f;
        target.MarginRight = 0f;
        target.MarginTop = 0f;
        target.MarginBottom = 0f;
        target.HeaderOffset = 0f;
        target.FooterOffset = 0f;
        target.Gutter = 0f;
        target.ParagraphSpacing = source.ParagraphSpacing;
        target.BlockSpacing = source.BlockSpacing;
        target.ListIndent = source.ListIndent;
        target.ListMarkerGap = source.ListMarkerGap;
        target.DefaultTabWidth = source.DefaultTabWidth;
        target.ColumnGap = source.ColumnGap;
        target.TableCellPadding = source.TableCellPadding;
        target.TableBorderThickness = source.TableBorderThickness;
    }

    private int ResolveHeaderFooterPageIndex()
    {
        var layout = _editor.Layout;
        if (layout.Pages.Count == 0 || layout.Lines.Count == 0)
        {
            return 0;
        }

        var lineIndex = EditorSelectionService.FindLineIndexForPosition(layout, _editor.Caret, out _);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        return pageIndex < 0 ? 0 : pageIndex;
    }

    private static int FindPageIndex(IReadOnlyList<PageLayout> pages, float x, float y)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i].Bounds.Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    public bool CanHorizontallyScroll
    {
        get => _extent.Width > _viewport.Width;
        set { }
    }

    public bool CanVerticallyScroll
    {
        get => true;
        set { }
    }

    public bool IsLogicalScrollEnabled => true;

    public Size ScrollSize => new Size(0, _editor.Layout.LineHeight * _zoomFactor);
    public Size PageScrollSize => new Size(0, Math.Max(0, Bounds.Height));

    public Size Extent => _extent;
    public Size Viewport => _viewport;

    public Vector Offset
    {
        get => _scrollOffset;
        set
        {
            var clamped = ClampOffset(value);
            if (clamped == _scrollOffset)
            {
                return;
            }

            _scrollOffset = clamped;
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public event EventHandler? ScrollInvalidated;

    public bool BringIntoView(Control target, Rect targetRect)
    {
        if (target != this)
        {
            return false;
        }

        if (targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            return false;
        }

        var viewportSize = Bounds.Size;
        if (targetRect.X <= 0.5
            && targetRect.Y <= 0.5
            && targetRect.Width >= viewportSize.Width - 0.5
            && targetRect.Height >= viewportSize.Height - 0.5)
        {
            return false;
        }

        var offsetY = _scrollOffset.Y;
        if (targetRect.Top < _scrollOffset.Y)
        {
            offsetY = targetRect.Top;
        }
        else if (targetRect.Bottom > _scrollOffset.Y + _viewport.Height)
        {
            offsetY = targetRect.Bottom - _viewport.Height;
        }

        Offset = new Vector(_scrollOffset.X, offsetY);
        return true;
    }

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    private void UpdateScrollMetrics(Vector? targetOffset = null)
    {
        _viewport = Bounds.Size;
        var extentHeight = Math.Max(_viewport.Height, _editor.Layout.ContentHeight * _zoomFactor);
        var extentWidth = _viewport.Width;
        if (TryGetContentHorizontalBounds(out var minX, out var maxX))
        {
            var contentWidth = Math.Max(0f, (maxX - minX) * _zoomFactor);
            extentWidth = Math.Max(_viewport.Width, contentWidth);
        }

        _extent = new Size(extentWidth, extentHeight);
        _scrollOffset = ClampOffset(targetOffset ?? _scrollOffset);
        RaiseScrollInvalidated(EventArgs.Empty);
    }

    private Vector ClampOffset(Vector offset)
    {
        var maxX = Math.Max(0, _extent.Width - _viewport.Width);
        var maxY = Math.Max(0, _extent.Height - _viewport.Height);
        var clampedX = Math.Clamp(offset.X, 0, maxX);
        var clampedY = Math.Clamp(offset.Y, 0, maxY);
        return new Vector(clampedX, clampedY);
    }

    private void EnsurePositionVisible(TextPosition position)
    {
        var layout = _editor.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        var lineIndex = EditorSelectionService.FindLineIndexForPosition(layout, position, out var line);
        if (lineIndex < 0)
        {
            return;
        }

        var lineTop = line.Y * _zoomFactor;
        var lineBottom = (line.Y + line.LineHeight) * _zoomFactor;
        var offset = _scrollOffset;

        if (lineTop < offset.Y)
        {
            offset = new Vector(offset.X, lineTop);
        }
        else if (lineBottom > offset.Y + _viewport.Height)
        {
            offset = new Vector(offset.X, lineBottom - _viewport.Height);
        }

        if (_editor.Layout.Settings.PageFlow == PageFlowDirection.Horizontal)
        {
            var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
            if (pageIndex >= 0 && pageIndex < layout.Pages.Count)
            {
                offset = new Vector(layout.Pages[pageIndex].Bounds.Left * _zoomFactor, offset.Y);
            }
        }

        Offset = offset;
    }

    private Vector GetEffectiveScrollOffset()
    {
        var alignmentOffset = GetHorizontalAlignmentOffset();
        if (MathF.Abs(alignmentOffset) < 0.5f)
        {
            return _scrollOffset;
        }

        return new Vector(_scrollOffset.X + alignmentOffset, _scrollOffset.Y);
    }

    private float GetHorizontalAlignmentOffset()
    {
        if (_viewport.Width <= 0 || _editor.Layout.Pages.Count == 0)
        {
            return 0f;
        }

        if (!TryGetContentHorizontalBounds(out var minX, out var maxX))
        {
            return 0f;
        }

        var contentWidth = (maxX - minX) * _zoomFactor;
        if (contentWidth <= 0f)
        {
            return 0f;
        }

        var viewportWidth = (float)_viewport.Width;
        var contentLeft = minX * _zoomFactor;
        if (contentWidth >= viewportWidth - 0.5f)
        {
            return contentLeft;
        }

        var centeredLeft = (viewportWidth - contentWidth) / 2f;
        return contentLeft - centeredLeft;
    }

    private bool TryGetContentHorizontalBounds(out float minX, out float maxX)
    {
        var pages = _editor.Layout.Pages;
        if (pages.Count == 0)
        {
            minX = 0f;
            maxX = 0f;
            return false;
        }

        minX = pages[0].Bounds.Left;
        maxX = pages[0].Bounds.Right;
        for (var i = 1; i < pages.Count; i++)
        {
            var bounds = pages[i].Bounds;
            if (bounds.Left < minX)
            {
                minX = bounds.Left;
            }

            if (bounds.Right > maxX)
            {
                maxX = bounds.Right;
            }
        }

        return true;
    }

    public void ZoomIn()
    {
        SetZoom(_zoomFactor + ZoomStep, DocumentZoomMode.Custom);
    }

    public void ZoomOut()
    {
        SetZoom(_zoomFactor - ZoomStep, DocumentZoomMode.Custom);
    }

    public void ZoomToDefault()
    {
        SetZoom(1f, DocumentZoomMode.Custom);
    }

    public void ZoomToPercent(float percent)
    {
        SetZoom(percent / 100f, DocumentZoomMode.Custom);
    }

    public void ZoomToPageWidth()
    {
        SetZoom(ComputePageWidthZoom(), DocumentZoomMode.PageWidth, preserveCenter: false);
    }

    public void ZoomToWholePage()
    {
        SetZoom(ComputeWholePageZoom(), DocumentZoomMode.WholePage, preserveCenter: false);
    }

    public void ZoomToMultiplePages(int pagesPerRow = DefaultMultiplePages)
    {
        var resolved = Math.Max(1, pagesPerRow);
        _multiplePagesPerRow = resolved;
        SetZoom(ComputeMultiplePagesZoom(resolved), DocumentZoomMode.MultiplePages, preserveCenter: false);
    }

    private void SetZoom(float value, DocumentZoomMode mode, bool preserveCenter = true)
    {
        var clamped = Math.Clamp(value, MinZoom, MaxZoom);
        if (MathF.Abs(_zoomFactor - clamped) < 0.001f && _zoomMode == mode)
        {
            return;
        }

        var previousZoom = _zoomFactor;
        var previousOffset = GetEffectiveScrollOffset();
        var centerDoc = preserveCenter && _viewport.Width > 0 && _viewport.Height > 0
            ? new Point((previousOffset.X + _viewport.Width / 2f) / previousZoom,
                (previousOffset.Y + _viewport.Height / 2f) / previousZoom)
            : new Point(0, 0);

        _zoomFactor = clamped;
        _zoomMode = mode;
        _renderOptions.ZoomFactor = _zoomFactor;
        _renderOptions.SvgRasterizationScale = _zoomFactor;

        if (preserveCenter)
        {
            var targetEffectiveOffset = new Vector(centerDoc.X * _zoomFactor - _viewport.Width / 2f,
                centerDoc.Y * _zoomFactor - _viewport.Height / 2f);

            if (TryGetContentHorizontalBounds(out var minX, out var maxX))
            {
                var contentWidth = (maxX - minX) * _zoomFactor;
                var viewportWidth = (float)_viewport.Width;
                if (contentWidth >= viewportWidth - 0.5f)
                {
                    targetEffectiveOffset = new Vector(minX * _zoomFactor, targetEffectiveOffset.Y);
                }
            }

            var alignmentOffset = GetHorizontalAlignmentOffset();
            var targetOffset = new Vector(targetEffectiveOffset.X - alignmentOffset, targetEffectiveOffset.Y);
            UpdateScrollMetrics(targetOffset);
        }
        else
        {
            UpdateScrollMetrics();
        }

        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyZoomMode(DocumentZoomMode mode, bool preserveCenter)
    {
        switch (mode)
        {
            case DocumentZoomMode.PageWidth:
                SetZoom(ComputePageWidthZoom(), DocumentZoomMode.PageWidth, preserveCenter);
                break;
            case DocumentZoomMode.WholePage:
                SetZoom(ComputeWholePageZoom(), DocumentZoomMode.WholePage, preserveCenter);
                break;
            case DocumentZoomMode.MultiplePages:
                SetZoom(ComputeMultiplePagesZoom(_multiplePagesPerRow), DocumentZoomMode.MultiplePages, preserveCenter);
                break;
            default:
                SetZoom(_zoomFactor, DocumentZoomMode.Custom, preserveCenter);
                break;
        }
    }

    private float ComputePageWidthZoom()
    {
        if (_viewport.Width <= 0 || _editor.Layout.Pages.Count == 0)
        {
            return _zoomFactor;
        }

        var pageWidth = _editor.Layout.Pages.Max(page => page.Bounds.Width);
        if (pageWidth <= 0f)
        {
            return _zoomFactor;
        }

        var gap = _editor.Layout.Settings.PageGap;
        var targetWidth = pageWidth + gap;
        var viewportWidth = (float)_viewport.Width;
        return viewportWidth / Math.Max(1f, targetWidth);
    }

    private float ComputeWholePageZoom()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0 || _editor.Layout.Pages.Count == 0)
        {
            return _zoomFactor;
        }

        var firstPage = _editor.Layout.Pages[0];
        var width = Math.Max(1f, firstPage.Bounds.Width + _editor.Layout.Settings.PageGap);
        var height = Math.Max(1f, firstPage.Bounds.Height + _editor.Layout.Settings.PageGap);
        var viewportWidth = (float)_viewport.Width;
        var viewportHeight = (float)_viewport.Height;
        var zoomX = viewportWidth / width;
        var zoomY = viewportHeight / height;
        return Math.Min(zoomX, zoomY);
    }

    private float ComputeMultiplePagesZoom(int pagesPerRow)
    {
        if (_viewport.Width <= 0 || _editor.Layout.Pages.Count == 0)
        {
            return _zoomFactor;
        }

        var pageWidth = _editor.Layout.Pages.Max(page => page.Bounds.Width);
        if (pageWidth <= 0f)
        {
            return _zoomFactor;
        }

        var gap = _editor.Layout.Settings.PageGap;
        var count = Math.Max(1, pagesPerRow);
        var targetWidth = (pageWidth * count) + (gap * count);
        var viewportWidth = (float)_viewport.Width;
        return viewportWidth / Math.Max(1f, targetWidth);
    }

    public void ScrollToPage(int pageIndex)
    {
        var pages = _editor.Layout.Pages;
        if (pages.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(pageIndex, 0, pages.Count - 1);
        var page = pages[index];
        var offset = _scrollOffset;
        if (_editor.Layout.Settings.PageFlow == PageFlowDirection.Horizontal)
        {
            offset = new Vector(page.Bounds.Left * _zoomFactor, offset.Y);
        }
        else
        {
            offset = new Vector(offset.X, page.Bounds.Top * _zoomFactor);
        }

        Offset = offset;
    }

    private static bool HasZoomModifier(KeyModifiers modifiers)
    {
        return modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
    }

    private bool HandleZoomShortcut(KeyEventArgs e)
    {
        if (!HasZoomModifier(e.KeyModifiers))
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomIn();
                return true;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomOut();
                return true;
            case Key.D0:
            case Key.NumPad0:
                ZoomToDefault();
                return true;
            default:
                return false;
        }
    }

    private sealed class SkiaDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly EditorController _editor;
        private readonly SkiaDocumentRenderer _renderer;
        private readonly RenderOptions _options;
        private readonly Vector _offset;

        public SkiaDrawOperation(Rect bounds, EditorController editor, SkiaDocumentRenderer renderer, RenderOptions options, Vector offset)
        {
            _bounds = bounds;
            _editor = editor;
            _renderer = renderer;
            _options = options;
            _offset = offset;
        }

        public Rect Bounds => _bounds;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.Translate((float)_bounds.X, (float)_bounds.Y);
            canvas.Scale(_options.ZoomFactor);
            var offsetDocX = (float)(_offset.X / _options.ZoomFactor);
            var offsetDocY = (float)(_offset.Y / _options.ZoomFactor);
            canvas.Translate(-offsetDocX, -offsetDocY);

            if (_options.HeaderFooterMode == HeaderFooterEditMode.None)
            {
                _options.Caret = _editor.Caret;
                _options.Selection = _editor.Selection.IsEmpty ? null : _editor.Selection;
                _options.ShowCaret = true;
            }
            else
            {
                _options.Caret = _editor.Caret;
                _options.Selection = null;
                _options.ShowCaret = false;
            }

            _options.SelectedFloatingObjectId = _editor.SelectedFloatingObjectId;

            _renderer.Render(canvas, _editor.Document, _editor.Layout, _options);

            canvas.Restore();
        }

        public bool HitTest(Point p) => _bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose()
        {
        }
    }
}

public enum DocumentZoomMode
{
    Custom,
    PageWidth,
    WholePage,
    MultiplePages
}
