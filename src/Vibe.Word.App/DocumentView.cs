using System.Buffers;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
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
    private const int InkReplayFrameMs = 16;
    private const int InkReplayPointsPerTick = 3;
    private const float CommentBalloonWidth = 220f;
    private const float CommentBalloonPadding = 8f;
    private const float CommentBalloonMargin = 12f;
    private const float CommentBalloonSpacing = 6f;
    private const float CommentBalloonCornerRadius = 4f;
    private const float CommentBalloonFontSize = 12f;
    private const float RevisionBalloonWidth = 200f;
    private const float RevisionBalloonPadding = 6f;
    private const float RevisionBalloonMargin = 12f;
    private const float RevisionBalloonSpacing = 6f;
    private const float RevisionBalloonCornerRadius = 4f;
    private const float RevisionBalloonFontSize = 11f;
    private const float TableResizeHandleSize = 8f;
    private const float TableResizeMinRowHeight = 8f;
    private const float TableSelectionOutlineThickness = 1f;
    private const float CropHandleSize = 8f;
    private const float CropMinVisibleSize = 12f;
    private const float ShapeHandleSize = 8f;
    private const float ShapeRotateHandleOffset = 18f;
    private const float ShapeMinSize = 8f;
    private const float ShapeRotateSnapDegrees = 15f;

    private static readonly IBrush CommentBalloonFillBrush = new SolidColorBrush(Color.Parse("#FFF7D6"));
    private static readonly IBrush CommentBalloonBorderBrush = new SolidColorBrush(Color.Parse("#D7C284"));
    private static readonly IBrush CommentBalloonTextBrush = new SolidColorBrush(Color.Parse("#303030"));
    private static readonly Typeface CommentBalloonTypeface = new Typeface("Calibri");
    private static readonly IBrush RevisionBalloonFillBrush = new SolidColorBrush(Color.Parse("#E8F1FF"));
    private static readonly IBrush RevisionBalloonBorderBrush = new SolidColorBrush(Color.Parse("#A8C3E8"));
    private static readonly IBrush RevisionBalloonTextBrush = new SolidColorBrush(Color.Parse("#203040"));
    private static readonly Typeface RevisionBalloonTypeface = new Typeface("Calibri");
    private static readonly IBrush TableSelectionFillBrush = new SolidColorBrush(Color.FromArgb(64, 45, 125, 240));
    private static readonly Pen TableSelectionBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 45, 125, 240)), TableSelectionOutlineThickness);
    private static readonly IBrush TableResizeHandleFillBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly Pen TableResizeHandleBorderPen = new Pen(new SolidColorBrush(Color.Parse("#808080")), 1);
    private static readonly IBrush TableResizeHandleActiveFillBrush = new SolidColorBrush(Color.FromArgb(220, 45, 125, 240));
    private static readonly Pen TableResizeHandleActiveBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 45, 125, 240)), 1);
    private static readonly Pen TableResizeGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 45, 125, 240)), 1);
    private static readonly IBrush CropHandleFillBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly Pen CropHandleBorderPen = new Pen(new SolidColorBrush(Color.Parse("#2D7DF0")), 1);
    private static readonly Pen CropBoundsPen = new Pen(new SolidColorBrush(Color.Parse("#2D7DF0")), 1, dashStyle: new DashStyle(new double[] { 4, 2 }, 0));
    private static readonly Pen ShapeSelectionBorderPen = new Pen(new SolidColorBrush(Color.Parse("#2D7DF0")), 1, dashStyle: new DashStyle(new double[] { 4, 2 }, 0));
    private static readonly IBrush ShapeHandleFillBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly Pen ShapeHandleBorderPen = new Pen(new SolidColorBrush(Color.Parse("#2D7DF0")), 1);
    private static readonly Cursor ColumnResizeCursor = new Cursor(StandardCursorType.SizeWestEast);
    private static readonly Cursor RowResizeCursor = new Cursor(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor CropAllCursor = new Cursor(StandardCursorType.SizeAll);
    private static readonly Cursor CropTopLeftCursor = new Cursor(StandardCursorType.TopLeftCorner);
    private static readonly Cursor CropTopRightCursor = new Cursor(StandardCursorType.TopRightCorner);
    private static readonly Cursor CropBottomLeftCursor = new Cursor(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor CropBottomRightCursor = new Cursor(StandardCursorType.BottomRightCorner);
    private static readonly Cursor ShapeMoveCursor = new Cursor(StandardCursorType.SizeAll);
    private static readonly Cursor ShapeRotateCursor = new Cursor(StandardCursorType.Hand);

    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<DocumentView, Color>(nameof(SurfaceColor), new Color(255, 238, 241, 245));

    private readonly EditorKernel _kernel = new EditorKernel(new LegacyEditorSessionFactory());
    private EditorController _editor;
    private AvaloniaEditorInputAdapter _inputAdapter = null!;
    private readonly SkiaTextMeasurer _textMeasurer = new SkiaTextMeasurer();
    private readonly SkiaDocumentRenderer _renderer = new SkiaDocumentRenderer();
    private readonly SkiaDocumentRenderer _thumbnailRenderer = new SkiaDocumentRenderer();
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
    private RenderOptions? _thumbnailRenderOptions;
    private DocumentLayout? _thumbnailLayout;
    private float _thumbnailScale = 1f;
    private bool _thumbnailUseHarfBuzz;
    private readonly DocColor _defaultCommentHighlightColor;
    private ReviewMarkupMode _reviewMarkupMode = ReviewMarkupMode.All;
    private readonly List<CommentAnchorInfo> _commentAnchors = new();
    private readonly List<RevisionAnchorInfo> _revisionAnchors = new();

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
    private InkReplayState? _inkReplay;
    private DispatcherTimer? _inkReplayTimer;
    private bool _isDrawing;
    private EditorDrawTool _activeDrawTool;
    private HeaderFooterEditSession? _headerFooterSession;
    private HeaderFooterHit? _headerFooterHit;
    private EditorServices? _headerFooterServices;
    private EditorSessionSnapshot? _headerFooterSnapshot;
    private bool _headerFooterDirty;
    private bool _isHeaderFooterSelecting;
    private bool _isTableResizing;
    private TableResizeHandle? _activeTableResizeHandle;
    private TableResizeHandle? _hoverTableResizeHandle;
    private float[]? _tableResizeStartColumnWidths;
    private float[]? _tableResizeWorkingColumnWidths;
    private int _tableResizeColumnCount;
    private float _tableResizeStartPosition;
    private float _tableResizeStartRowHeight;
    private float? _tableResizePreviewPosition;
    private EditorSessionSnapshot? _tableResizeSnapshot;
    private bool _tableResizeDirty;
    private bool _isPictureCropMode;
    private bool _isCropping;
    private CropHandleInfo? _activeCropHandle;
    private CropHandleInfo? _hoverCropHandle;
    private ImageInline? _cropImage;
    private ImageCrop? _cropStartValues;
    private DocPoint _cropStartPoint;
    private DocRect _cropBounds;
    private EditorSessionSnapshot? _cropSnapshot;
    private bool _cropDirty;
    private bool _isShapeEditing;
    private ShapeDragMode _shapeDragMode;
    private ShapeHandleInfo? _activeShapeHandle;
    private ShapeHandleInfo? _hoverShapeHandle;
    private FloatingObject? _shapeFloating;
    private ShapeInline? _shapeInline;
    private DocPoint _shapeStartPoint;
    private DocPoint _shapeStartCenter;
    private DocRect _shapeStartBounds;
    private float _shapeStartRotation;
    private float _shapeStartOffsetX;
    private float _shapeStartOffsetY;
    private float _shapeStartPointerAngle;
    private float _shapeStartAspectRatio;
    private EditorSessionSnapshot? _shapeSnapshot;
    private bool _shapeDirty;
    private DocRect? _shapePreviewBounds;
    private bool _shapeLayoutRefreshPending;
    private bool _isPromotingInlineShape;

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

    public bool IsPictureCropMode => _isPictureCropMode;

    public void SetPictureCropMode(bool enabled)
    {
        if (_isPictureCropMode == enabled)
        {
            return;
        }

        _isPictureCropMode = enabled;
        _isCropping = false;
        _activeCropHandle = null;
        _hoverCropHandle = null;
        _cropImage = null;
        _cropStartValues = null;
        _cropSnapshot = null;
        _cropDirty = false;
        UpdatePointerCursor(null);
        InvalidateVisual();
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

    private static bool ShouldShowRevisionBalloons(ReviewMarkupMode mode)
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

        if (_headerFooterSession is null)
        {
            if (TryHandlePictureCropPointerPressed(e) || TryHandleTableResizePointerPressed(e))
            {
                return;
            }

            if (TryHandleShapePointerPressed(e))
            {
                return;
            }
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
        if (_isShapeEditing)
        {
            HandleShapePointerMoved(e);
            return;
        }
        if (_isDrawing)
        {
            HandleInkPointerMoved(e);
            return;
        }
        if (_isTableResizing)
        {
            HandleTableResizePointerMoved(e);
            return;
        }
        if (_isCropping)
        {
            HandleCropPointerMoved(e);
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
            UpdateHoverState(e);
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

        if (_isShapeEditing)
        {
            EndShapeEdit(e);
            return;
        }

        if (_isDrawing)
        {
            HandleInkPointerReleased(e);
            return;
        }
        if (_isTableResizing)
        {
            EndTableResize(e);
            return;
        }
        if (_isCropping)
        {
            EndCrop(e);
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

    private bool TryHandleTableResizePointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        var docPoint = GetDocumentPoint(point.Position);
        if (!TryGetTableResizeHandleAtPoint(docPoint, out var handle))
        {
            return false;
        }

        _tableResizeSnapshot = null;
        if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _tableResizeSnapshot = history.CaptureSnapshot();
        }

        _activeTableResizeHandle = handle;
        _hoverTableResizeHandle = handle;
        _isTableResizing = true;
        _tableResizeDirty = false;
        _tableResizePreviewPosition = handle.Position;
        _tableResizeStartPosition = handle.Position;

        if (handle.Kind == TableResizeHandleKind.Column)
        {
            var columnCount = Math.Min(handle.Layout.Columns, handle.Layout.ColumnWidths.Count);
            if (columnCount <= 0)
            {
                ResetTableResizeState();
                return false;
            }

            _tableResizeColumnCount = columnCount;
            _tableResizeStartColumnWidths = ArrayPool<float>.Shared.Rent(columnCount);
            _tableResizeWorkingColumnWidths = ArrayPool<float>.Shared.Rent(columnCount);
            for (var i = 0; i < columnCount; i++)
            {
                _tableResizeStartColumnWidths[i] = handle.Layout.ColumnWidths[i];
            }
        }
        else
        {
            if (handle.LocalIndex < 0 || handle.LocalIndex >= handle.Layout.RowHeights.Count)
            {
                ResetTableResizeState();
                return false;
            }

            _tableResizeStartRowHeight = handle.Layout.RowHeights[handle.LocalIndex];
        }

        UpdatePointerCursor(handle.Kind == TableResizeHandleKind.Column ? ColumnResizeCursor : RowResizeCursor);
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    private void HandleTableResizePointerMoved(PointerEventArgs e)
    {
        if (!_activeTableResizeHandle.HasValue)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var docPoint = GetDocumentPoint(point.Position);
        var handle = _activeTableResizeHandle.Value;
        if (handle.Kind == TableResizeHandleKind.Column)
        {
            ApplyTableColumnResize(handle, docPoint.X);
        }
        else
        {
            ApplyTableRowResize(handle, docPoint.Y);
        }

        e.Handled = true;
    }

    private void EndTableResize(PointerReleasedEventArgs e)
    {
        _isTableResizing = false;
        _tableResizePreviewPosition = null;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (_tableResizeDirty && _tableResizeSnapshot.HasValue
            && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            history.RecordSnapshot(_tableResizeSnapshot.Value);
        }

        ResetTableResizeState();
        UpdatePointerCursor(null);
        InvalidateVisual();
    }

    private void ResetTableResizeState()
    {
        if (_tableResizeStartColumnWidths is not null)
        {
            ArrayPool<float>.Shared.Return(_tableResizeStartColumnWidths);
        }

        if (_tableResizeWorkingColumnWidths is not null)
        {
            ArrayPool<float>.Shared.Return(_tableResizeWorkingColumnWidths);
        }

        _tableResizeStartColumnWidths = null;
        _tableResizeWorkingColumnWidths = null;
        _tableResizeColumnCount = 0;
        _activeTableResizeHandle = null;
        _hoverTableResizeHandle = null;
        _tableResizeSnapshot = null;
        _tableResizeDirty = false;
    }

    private void ApplyTableColumnResize(TableResizeHandle handle, float position)
    {
        if (_tableResizeStartColumnWidths is null || _tableResizeWorkingColumnWidths is null)
        {
            return;
        }

        var columnCount = _tableResizeColumnCount;
        if (columnCount <= 0)
        {
            return;
        }

        var baseWidths = _tableResizeStartColumnWidths.AsSpan(0, columnCount);
        var widths = _tableResizeWorkingColumnWidths.AsSpan(0, columnCount);
        baseWidths.CopyTo(widths);

        var delta = position - _tableResizeStartPosition;
        var minWidth = RulerHelpers.MinColumnWidth;
        if (handle.Index < columnCount - 1)
        {
            var leftWidth = baseWidths[handle.Index];
            var rightWidth = baseWidths[handle.Index + 1];
            var minDelta = minWidth - leftWidth;
            var maxDelta = rightWidth - minWidth;
            if (minDelta > maxDelta)
            {
                delta = 0f;
            }
            else
            {
                delta = Math.Clamp(delta, minDelta, maxDelta);
            }

            widths[handle.Index] = leftWidth + delta;
            widths[handle.Index + 1] = rightWidth - delta;
        }
        else if (handle.Index < columnCount)
        {
            var leftWidth = baseWidths[handle.Index];
            var newWidth = MathF.Max(minWidth, leftWidth + delta);
            delta = newWidth - leftWidth;
            widths[handle.Index] = newWidth;
        }

        _tableResizePreviewPosition = _tableResizeStartPosition + delta;
        ExecuteTableCommand(
            EditorTableCommandIds.Layout.ColumnWidthsSet,
            new EditorTableColumnWidthsRequest(_tableResizeWorkingColumnWidths.AsMemory(0, columnCount)),
            recordHistory: false);
        _tableResizeDirty = true;
        InvalidateVisual();
    }

    private void ApplyTableRowResize(TableResizeHandle handle, float position)
    {
        var delta = position - _tableResizeStartPosition;
        var newHeight = MathF.Max(TableResizeMinRowHeight, _tableResizeStartRowHeight + delta);
        delta = newHeight - _tableResizeStartRowHeight;
        _tableResizePreviewPosition = _tableResizeStartPosition + delta;
        ExecuteTableCommand(
            EditorTableCommandIds.Layout.RowHeightSet,
            new EditorTableRowHeightRequest(handle.Index, newHeight, TableRowHeightRule.Exact),
            recordHistory: false);
        _tableResizeDirty = true;
        InvalidateVisual();
    }

    private bool TryHandlePictureCropPointerPressed(PointerPressedEventArgs e)
    {
        if (!_isPictureCropMode)
        {
            return false;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        var docPoint = GetDocumentPoint(point.Position);
        if (!TryGetCropHandleAtPoint(docPoint, out var handle))
        {
            return false;
        }

        if (!TryGetSelectedFloatingImageLayout(out var layoutObject, out var image))
        {
            return false;
        }

        _cropSnapshot = null;
        if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _cropSnapshot = history.CaptureSnapshot();
        }

        _isCropping = true;
        _activeCropHandle = handle;
        _hoverCropHandle = handle;
        _cropImage = image;
        _cropBounds = layoutObject.Bounds;
        _cropStartPoint = docPoint;
        _cropStartValues = image.Crop?.Clone() ?? new ImageCrop();
        _cropDirty = false;
        UpdatePointerCursor(GetCropCursor(handle.Kind));
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    private void HandleCropPointerMoved(PointerEventArgs e)
    {
        if (!_isCropping || _cropImage is null || _cropStartValues is null || !_activeCropHandle.HasValue)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var docPoint = GetDocumentPoint(point.Position);
        var deltaX = docPoint.X - _cropStartPoint.X;
        var deltaY = docPoint.Y - _cropStartPoint.Y;
        var crop = ApplyCropDrag(_cropStartValues, _activeCropHandle.Value.Kind, _cropBounds, deltaX, deltaY);
        _cropImage.Crop = crop.HasValues ? crop : null;
        _cropDirty = true;
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        e.Handled = true;
    }

    private void EndCrop(PointerReleasedEventArgs e)
    {
        _isCropping = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (_cropDirty && _cropSnapshot.HasValue
            && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            history.RecordSnapshot(_cropSnapshot.Value);
        }

        _cropSnapshot = null;
        _cropDirty = false;
        _activeCropHandle = null;
        _hoverCropHandle = null;
        _cropImage = null;
        _cropStartValues = null;
        UpdatePointerCursor(null);
        InvalidateVisual();
    }

    private bool TryHandleShapePointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        var docPoint = GetDocumentPoint(point.Position);
        if (TryGetShapeHandleAtPoint(docPoint, out var handle))
        {
            if (!TryGetSelectedFloatingShapeLayout(out var layoutObject, out var floating, out var shape))
            {
                return false;
            }

            var mode = handle.Kind == ShapeHandleKind.Rotate ? ShapeDragMode.Rotate : ShapeDragMode.Resize;
            BeginShapeEdit(mode, handle, layoutObject, floating, shape, docPoint);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        if (TryGetFloatingShapeAtPoint(docPoint, out var hitLayout, out var hitFloating, out var hitShape))
        {
            _editor.SetCaretFromPoint(docPoint.X, docPoint.Y, false);
            BeginShapeEdit(ShapeDragMode.Move, null, hitLayout, hitFloating, hitShape, docPoint);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void BeginShapeEdit(
        ShapeDragMode mode,
        ShapeHandleInfo? handle,
        FloatingLayoutObject layoutObject,
        FloatingObject floating,
        ShapeInline shape,
        DocPoint startPoint)
    {
        _shapeSnapshot = null;
        if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _shapeSnapshot = history.CaptureSnapshot();
        }

        _isShapeEditing = true;
        _shapeDragMode = mode;
        _shapeFloating = floating;
        _shapeInline = shape;
        _shapeStartPoint = startPoint;
        _shapeStartBounds = layoutObject.Bounds;
        _shapeStartCenter = new DocPoint(
            _shapeStartBounds.X + _shapeStartBounds.Width * 0.5f,
            _shapeStartBounds.Y + _shapeStartBounds.Height * 0.5f);
        _shapeStartRotation = shape.Properties.Rotation;
        _shapeStartOffsetX = floating.Anchor.OffsetX;
        _shapeStartOffsetY = floating.Anchor.OffsetY;
        _shapeStartPointerAngle = GetAngleDegrees(_shapeStartCenter, startPoint);
        _shapeStartAspectRatio = shape.Height > 0f ? shape.Width / shape.Height : 1f;
        _shapeDirty = false;
        _activeShapeHandle = handle;
        _hoverShapeHandle = handle;
        _isSelecting = false;
        _shapePreviewBounds = null;

        switch (mode)
        {
            case ShapeDragMode.Move:
                UpdatePointerCursor(ShapeMoveCursor);
                break;
            case ShapeDragMode.Rotate:
                UpdatePointerCursor(ShapeRotateCursor);
                break;
            case ShapeDragMode.Resize:
                UpdatePointerCursor(handle.HasValue ? GetShapeCursor(handle.Value.Kind) : ShapeMoveCursor);
                break;
        }
    }

    private void HandleShapePointerMoved(PointerEventArgs e)
    {
        if (!_isShapeEditing || _shapeFloating is null || _shapeInline is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var docPoint = GetDocumentPoint(point.Position);
        var modifiers = e.KeyModifiers;
        switch (_shapeDragMode)
        {
            case ShapeDragMode.Move:
            {
                var deltaX = docPoint.X - _shapeStartPoint.X;
                var deltaY = docPoint.Y - _shapeStartPoint.Y;
                _shapeFloating.Anchor.OffsetX = _shapeStartOffsetX + deltaX;
                _shapeFloating.Anchor.OffsetY = _shapeStartOffsetY + deltaY;
                _shapePreviewBounds = new DocRect(
                    _shapeStartBounds.X + deltaX,
                    _shapeStartBounds.Y + deltaY,
                    _shapeStartBounds.Width,
                    _shapeStartBounds.Height);
                _shapeDirty = true;
                RequestShapeLayoutRefresh();
                UpdateDirtyPages(GetAllPages());
                InvalidateVisual();
                break;
            }
            case ShapeDragMode.Resize:
            {
                if (!_activeShapeHandle.HasValue)
                {
                    return;
                }

                var keepAspect = modifiers.HasFlag(KeyModifiers.Shift);
                var newBounds = ComputeResizedBounds(_shapeStartBounds, _shapeStartCenter, _shapeStartRotation, _activeShapeHandle.Value.Kind, docPoint, keepAspect);
                _shapeInline.Width = newBounds.Width;
                _shapeInline.Height = newBounds.Height;
                _shapeFloating.Anchor.OffsetX = _shapeStartOffsetX + (newBounds.X - _shapeStartBounds.X);
                _shapeFloating.Anchor.OffsetY = _shapeStartOffsetY + (newBounds.Y - _shapeStartBounds.Y);
                _shapePreviewBounds = newBounds;
                _shapeDirty = true;
                RequestShapeLayoutRefresh();
                UpdateDirtyPages(GetAllPages());
                InvalidateVisual();
                break;
            }
            case ShapeDragMode.Rotate:
            {
                var angle = GetAngleDegrees(_shapeStartCenter, docPoint);
                var delta = NormalizeAngleDelta(angle - _shapeStartPointerAngle);
                var rotation = NormalizeRotation(_shapeStartRotation + delta);
                if (modifiers.HasFlag(KeyModifiers.Shift))
                {
                    rotation = SnapRotation(rotation, ShapeRotateSnapDegrees);
                }

                if (MathF.Abs(rotation - _shapeInline.Properties.Rotation) < 0.01f)
                {
                    break;
                }

                _shapeInline.Properties.Rotation = rotation;
                _shapeDirty = true;
                RequestShapeLayoutRefresh();
                UpdateDirtyPages(GetAllPages());
                InvalidateVisual();
                break;
            }
        }

        e.Handled = true;
    }

    private void EndShapeEdit(PointerReleasedEventArgs e)
    {
        _isShapeEditing = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (_shapeDirty && _shapeSnapshot.HasValue
            && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            history.RecordSnapshot(_shapeSnapshot.Value);
        }

        if (_shapeDirty || _shapeLayoutRefreshPending)
        {
            FlushShapeLayoutRefresh();
        }

        ResetShapeEditState();
        UpdatePointerCursor(null);
        InvalidateVisual();
    }

    private void ResetShapeEditState()
    {
        _shapeDragMode = ShapeDragMode.None;
        _activeShapeHandle = null;
        _hoverShapeHandle = null;
        _shapeFloating = null;
        _shapeInline = null;
        _shapeStartPoint = default;
        _shapeStartCenter = default;
        _shapeStartBounds = default;
        _shapeStartRotation = 0f;
        _shapeStartOffsetX = 0f;
        _shapeStartOffsetY = 0f;
        _shapeStartPointerAngle = 0f;
        _shapeStartAspectRatio = 0f;
        _shapeSnapshot = null;
        _shapeDirty = false;
        _shapePreviewBounds = null;
        _shapeLayoutRefreshPending = false;
    }

    private void RequestShapeLayoutRefresh()
    {
        if (_shapeLayoutRefreshPending)
        {
            return;
        }

        _shapeLayoutRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_shapeLayoutRefreshPending)
            {
                return;
            }

            _shapeLayoutRefreshPending = false;
            _editor.RefreshLayout();
        }, DispatcherPriority.Render);
    }

    private void FlushShapeLayoutRefresh()
    {
        _shapeLayoutRefreshPending = false;
        _editor.RefreshLayout();
    }

    private static float GetAngleDegrees(DocPoint center, DocPoint point)
    {
        var radians = MathF.Atan2(point.Y - center.Y, point.X - center.X);
        return radians * (180f / MathF.PI);
    }

    private static float NormalizeRotation(float degrees)
    {
        var value = degrees % 360f;
        return value < 0f ? value + 360f : value;
    }

    private static float NormalizeAngleDelta(float degrees)
    {
        var value = degrees % 360f;
        if (value > 180f)
        {
            value -= 360f;
        }
        else if (value < -180f)
        {
            value += 360f;
        }

        return value;
    }

    private static float SnapRotation(float degrees, float step)
    {
        if (step <= 0f)
        {
            return degrees;
        }

        return MathF.Round(degrees / step) * step;
    }

    private DocRect ComputeResizedBounds(
        DocRect start,
        DocPoint center,
        float rotationDegrees,
        ShapeHandleKind kind,
        DocPoint point,
        bool keepAspect)
    {
        var halfWidth = start.Width * 0.5f;
        var halfHeight = start.Height * 0.5f;
        var minHalfWidth = ShapeMinSize * 0.5f;
        var minHalfHeight = ShapeMinSize * 0.5f;

        var radians = rotationDegrees * (MathF.PI / 180f);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var localX = dx * cos + dy * sin;
        var localY = -dx * sin + dy * cos;

        var newHalfWidth = halfWidth;
        var newHalfHeight = halfHeight;

        if (kind is ShapeHandleKind.Left or ShapeHandleKind.TopLeft or ShapeHandleKind.BottomLeft)
        {
            newHalfWidth = MathF.Max(minHalfWidth, -localX);
        }
        else if (kind is ShapeHandleKind.Right or ShapeHandleKind.TopRight or ShapeHandleKind.BottomRight)
        {
            newHalfWidth = MathF.Max(minHalfWidth, localX);
        }

        if (kind is ShapeHandleKind.Top or ShapeHandleKind.TopLeft or ShapeHandleKind.TopRight)
        {
            newHalfHeight = MathF.Max(minHalfHeight, -localY);
        }
        else if (kind is ShapeHandleKind.Bottom or ShapeHandleKind.BottomLeft or ShapeHandleKind.BottomRight)
        {
            newHalfHeight = MathF.Max(minHalfHeight, localY);
        }

        if (keepAspect && IsCornerHandle(kind) && _shapeStartAspectRatio > 0f)
        {
            var ratio = _shapeStartAspectRatio;
            if (newHalfWidth / newHalfHeight > ratio)
            {
                newHalfWidth = newHalfHeight * ratio;
            }
            else
            {
                newHalfHeight = newHalfWidth / ratio;
            }
        }

        var shiftX = 0f;
        var shiftY = 0f;
        if (kind is ShapeHandleKind.Left or ShapeHandleKind.TopLeft or ShapeHandleKind.BottomLeft)
        {
            shiftX = halfWidth - newHalfWidth;
        }
        else if (kind is ShapeHandleKind.Right or ShapeHandleKind.TopRight or ShapeHandleKind.BottomRight)
        {
            shiftX = -(halfWidth - newHalfWidth);
        }

        if (kind is ShapeHandleKind.Top or ShapeHandleKind.TopLeft or ShapeHandleKind.TopRight)
        {
            shiftY = halfHeight - newHalfHeight;
        }
        else if (kind is ShapeHandleKind.Bottom or ShapeHandleKind.BottomLeft or ShapeHandleKind.BottomRight)
        {
            shiftY = -(halfHeight - newHalfHeight);
        }

        var shiftWorldX = shiftX * cos - shiftY * sin;
        var shiftWorldY = shiftX * sin + shiftY * cos;
        var newCenter = new DocPoint(center.X + shiftWorldX, center.Y + shiftWorldY);
        var newWidth = MathF.Max(ShapeMinSize, newHalfWidth * 2f);
        var newHeight = MathF.Max(ShapeMinSize, newHalfHeight * 2f);
        return new DocRect(newCenter.X - newWidth * 0.5f, newCenter.Y - newHeight * 0.5f, newWidth, newHeight);
    }

    private static bool IsCornerHandle(ShapeHandleKind kind)
    {
        return kind is ShapeHandleKind.TopLeft
            or ShapeHandleKind.TopRight
            or ShapeHandleKind.BottomLeft
            or ShapeHandleKind.BottomRight;
    }

    private void UpdateHoverState(PointerEventArgs e)
    {
        if (_headerFooterSession is not null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var docPoint = GetDocumentPoint(point.Position);

        if (_isPictureCropMode && TryGetCropHandleAtPoint(docPoint, out var cropHandle))
        {
            if (!_hoverCropHandle.HasValue || _hoverCropHandle.Value.Kind != cropHandle.Kind)
            {
                _hoverCropHandle = cropHandle;
                InvalidateVisual();
            }

            _hoverShapeHandle = null;
            _hoverTableResizeHandle = null;
            UpdatePointerCursor(GetCropCursor(cropHandle.Kind));
            return;
        }

        if (_hoverCropHandle.HasValue)
        {
            _hoverCropHandle = null;
            InvalidateVisual();
        }

        if (!_isPictureCropMode && TryGetShapeHandleAtPoint(docPoint, out var shapeHandle))
        {
            if (!_hoverShapeHandle.HasValue || _hoverShapeHandle.Value.Kind != shapeHandle.Kind)
            {
                _hoverShapeHandle = shapeHandle;
                InvalidateVisual();
            }

            _hoverTableResizeHandle = null;
            UpdatePointerCursor(GetShapeCursor(shapeHandle.Kind));
            return;
        }

        if (_hoverShapeHandle.HasValue)
        {
            _hoverShapeHandle = null;
            InvalidateVisual();
        }

        if (!_isPictureCropMode && TryGetSelectedShapeBounds(out var shapeBounds) && shapeBounds.Contains(docPoint.X, docPoint.Y))
        {
            UpdatePointerCursor(ShapeMoveCursor);
            return;
        }

        if (TryGetTableResizeHandleAtPoint(docPoint, out var tableHandle))
        {
            if (!_hoverTableResizeHandle.HasValue || !_hoverTableResizeHandle.Value.Equals(tableHandle))
            {
                _hoverTableResizeHandle = tableHandle;
                InvalidateVisual();
            }

            UpdatePointerCursor(tableHandle.Kind == TableResizeHandleKind.Column ? ColumnResizeCursor : RowResizeCursor);
            return;
        }

        if (_hoverTableResizeHandle.HasValue)
        {
            _hoverTableResizeHandle = null;
            InvalidateVisual();
        }

        UpdatePointerCursor(null);
    }

    private bool TryGetTableResizeHandleAtPoint(DocPoint point, out TableResizeHandle handle)
    {
        handle = default;
        if (!TryBuildTableResizeHandles(out var handles))
        {
            return false;
        }

        foreach (var candidate in handles)
        {
            if (candidate.Bounds.Contains(point.X, point.Y))
            {
                handle = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryGetCropHandleAtPoint(DocPoint point, out CropHandleInfo handle)
    {
        handle = default;
        if (!TryBuildCropHandles(out var handles, out _))
        {
            return false;
        }

        foreach (var candidate in handles)
        {
            if (candidate.Bounds.Contains(point.X, point.Y))
            {
                handle = candidate;
                return true;
            }
        }

        return false;
    }

    private static Cursor GetCropCursor(CropHandleKind kind)
    {
        return kind switch
        {
            CropHandleKind.Left => ColumnResizeCursor,
            CropHandleKind.Right => ColumnResizeCursor,
            CropHandleKind.Top => RowResizeCursor,
            CropHandleKind.Bottom => RowResizeCursor,
            CropHandleKind.TopLeft => CropTopLeftCursor,
            CropHandleKind.TopRight => CropTopRightCursor,
            CropHandleKind.BottomRight => CropBottomRightCursor,
            CropHandleKind.BottomLeft => CropBottomLeftCursor,
            _ => CropAllCursor
        };
    }

    private static ImageCrop ApplyCropDrag(ImageCrop start, CropHandleKind kind, DocRect bounds, float deltaX, float deltaY)
    {
        var width = MathF.Max(1f, bounds.Width);
        var height = MathF.Max(1f, bounds.Height);
        var minFractionX = MathF.Min(1f, CropMinVisibleSize / width);
        var minFractionY = MathF.Min(1f, CropMinVisibleSize / height);
        var left = start.Left;
        var right = start.Right;
        var top = start.Top;
        var bottom = start.Bottom;
        var dx = deltaX / width;
        var dy = deltaY / height;

        switch (kind)
        {
            case CropHandleKind.Left:
                left += dx;
                break;
            case CropHandleKind.Right:
                right -= dx;
                break;
            case CropHandleKind.Top:
                top += dy;
                break;
            case CropHandleKind.Bottom:
                bottom -= dy;
                break;
            case CropHandleKind.TopLeft:
                left += dx;
                top += dy;
                break;
            case CropHandleKind.TopRight:
                right -= dx;
                top += dy;
                break;
            case CropHandleKind.BottomRight:
                right -= dx;
                bottom -= dy;
                break;
            case CropHandleKind.BottomLeft:
                left += dx;
                bottom -= dy;
                break;
        }

        left = MathF.Max(0f, left);
        right = MathF.Max(0f, right);
        top = MathF.Max(0f, top);
        bottom = MathF.Max(0f, bottom);

        var maxLeft = MathF.Max(0f, 1f - right - minFractionX);
        left = MathF.Min(left, maxLeft);
        var maxRight = MathF.Max(0f, 1f - left - minFractionX);
        right = MathF.Min(right, maxRight);

        var maxTop = MathF.Max(0f, 1f - bottom - minFractionY);
        top = MathF.Min(top, maxTop);
        var maxBottom = MathF.Max(0f, 1f - top - minFractionY);
        bottom = MathF.Min(bottom, maxBottom);

        return new ImageCrop(left, top, right, bottom);
    }

    private void ExecuteTableCommand(string commandId, object payload, bool recordHistory)
    {
        if (!TryGetService<IEditorCommandRouter>(out var router))
        {
            return;
        }

        _ = router.ExecuteAsync(commandId, payload, null, recordHistory).GetAwaiter().GetResult();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var effectiveOffset = GetEffectiveScrollOffset();
        UpdateHeaderFooterRenderOptions();
        context.Custom(new SkiaDrawOperation(Bounds, _editor, _renderer, _renderOptions, effectiveOffset));
        DrawTableSelectionOverlay(context, effectiveOffset);
        DrawTableResizeHandles(context, effectiveOffset);
        DrawPictureCropOverlay(context, effectiveOffset);
        DrawShapeSelectionOverlay(context, effectiveOffset);
        DrawCommentBalloons(context, effectiveOffset);
        DrawRevisionBalloons(context, effectiveOffset);
        DrawInkPreview(context, effectiveOffset);
        DrawInkReplayOverlay(context, effectiveOffset);
    }

    public void LoadDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EndHeaderFooterEdit();
        StopInkReplay();
        _editor.Changed -= OnEditorChanged;
        _editor = CreateEditor(document);
        _editor.UpdateLayout((float)Bounds.Width, (float)Bounds.Height);
        ApplyEditorState();
    }

    public async Task LoadDocumentAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EndHeaderFooterEdit();
        StopInkReplay();
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
            CreateViewOptionsService(),
            documentFactory: DocumentTemplates.CreateDefaultDocument);
        ConfigureInputPipeline(editor);
        ApplyEditorState();
    }

    public void RefreshLayout()
    {
        _editor.RefreshLayout();
    }

    public bool TryCreatePageThumbnail(int pageIndex, float maxWidth, float maxHeight, out Bitmap? bitmap)
    {
        bitmap = null;
        if (pageIndex < 0 || maxWidth <= 0f || maxHeight <= 0f)
        {
            return false;
        }

        var layout = _editor.Layout;
        if (pageIndex >= layout.Pages.Count)
        {
            return false;
        }

        var page = layout.Pages[pageIndex];
        var pageWidth = MathF.Max(1f, page.Bounds.Width);
        var pageHeight = MathF.Max(1f, page.Bounds.Height);
        var scale = MathF.Min(maxWidth / pageWidth, maxHeight / pageHeight);
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            return false;
        }

        var pixelWidth = Math.Max(1, (int)MathF.Ceiling(pageWidth * scale));
        var pixelHeight = Math.Max(1, (int)MathF.Ceiling(pageHeight * scale));
        if (!EnsureThumbnailCache(layout, scale))
        {
            return false;
        }

        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return false;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(scale);
        canvas.Translate(-page.Bounds.X, -page.Bounds.Y);
        canvas.ClipRect(new SKRect(page.Bounds.X, page.Bounds.Y, page.Bounds.Right, page.Bounds.Bottom));

        if (!_thumbnailRenderer.TryDrawCachedPage(canvas, layout, pageIndex))
        {
            _thumbnailRenderer.Render(canvas, _editor.Document, layout, _thumbnailRenderOptions!);
        }

        canvas.Restore();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        if (data is null)
        {
            return false;
        }

        using var stream = data.AsStream();
        bitmap = new Bitmap(stream);
        return true;
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
            StopInkReplay();
        }
    }

    public ValueTask ReplaySelectedInkAsync()
    {
        if (!TryGetInkReplayTarget(out var layout, out var image))
        {
            return ValueTask.CompletedTask;
        }

        if (!InkStrokeParser.TryParse(image, out var stroke))
        {
            return ValueTask.CompletedTask;
        }

        if (stroke.Points.Count < 2)
        {
            return ValueTask.CompletedTask;
        }

        var points = new List<DocPoint>(stroke.Points.Count);
        var offsetX = layout.Bounds.Left;
        var offsetY = layout.Bounds.Top;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var point = stroke.Points[i];
            points.Add(new DocPoint(point.X + offsetX, point.Y + offsetY));
        }

        StartInkReplay(points, stroke.Color, stroke.Thickness);
        return ValueTask.CompletedTask;
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

    private static bool IsInkImage(ImageInline image)
    {
        return image.EmbeddedObject?.ProgId != null
            && string.Equals(image.EmbeddedObject.ProgId, InkStrokeProgId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInkFloatingObject(FloatingObject floating)
    {
        return floating.Content is ImageInline image && IsInkImage(image);
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

    private void DrawInkReplayOverlay(DrawingContext context, Vector effectiveOffset)
    {
        if (_inkReplay is null || _inkReplay.VisibleCount < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            var start = DocToView(_inkReplay.Points[0], effectiveOffset);
            geometryContext.BeginFigure(start, false);
            for (var i = 1; i < _inkReplay.VisibleCount && i < _inkReplay.Points.Count; i++)
            {
                geometryContext.LineTo(DocToView(_inkReplay.Points[i], effectiveOffset));
            }
        }

        var color = ToAvaloniaColor(_inkReplay.Color);
        var pen = new Pen(
            new SolidColorBrush(color),
            _inkReplay.Thickness * _zoomFactor,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, geometry);
    }

    private void StartInkReplay(List<DocPoint> points, DocColor color, float thickness)
    {
        StopInkReplay();
        _inkReplay = new InkReplayState(points, color, thickness);
        if (_inkReplayTimer is null)
        {
            _inkReplayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(InkReplayFrameMs)
            };
        }

        _inkReplayTimer.Tick += OnInkReplayTick;
        _inkReplayTimer.Start();
        InvalidateVisual();
    }

    private void StopInkReplay()
    {
        if (_inkReplayTimer is not null)
        {
            _inkReplayTimer.Stop();
            _inkReplayTimer.Tick -= OnInkReplayTick;
        }

        _inkReplay = null;
    }

    private void OnInkReplayTick(object? sender, EventArgs e)
    {
        if (_inkReplay is null)
        {
            StopInkReplay();
            return;
        }

        _inkReplay.VisibleCount = Math.Min(_inkReplay.VisibleCount + InkReplayPointsPerTick, _inkReplay.Points.Count);
        if (_inkReplay.VisibleCount >= _inkReplay.Points.Count)
        {
            StopInkReplay();
        }

        InvalidateVisual();
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

    private void UpdateRevisionAnchors()
    {
        _revisionAnchors.Clear();
        var anchors = ReviewingHelpers.BuildRevisionAnchors(_editor.Document);
        if (anchors.Count == 0 || _editor.Layout.Lines.Count == 0)
        {
            return;
        }

        foreach (var anchor in anchors)
        {
            var position = anchor.Anchor;
            var lineIndex = EditorSelectionService.FindLineIndexForPosition(_editor.Layout, position, out var line);
            var pageIndex = _editor.Layout.LineIndex.GetPageForLine(lineIndex);
            if (pageIndex < 0)
            {
                pageIndex = 0;
            }

            _revisionAnchors.Add(new RevisionAnchorInfo(
                anchor.Revision,
                anchor.IsBlock,
                position.ParagraphIndex,
                position.Offset,
                line.Y,
                line.LineHeight,
                pageIndex));
        }

        _revisionAnchors.Sort(CompareRevisionAnchors);
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

    private static int CompareRevisionAnchors(RevisionAnchorInfo left, RevisionAnchorInfo right)
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

    private void DrawRevisionBalloons(DrawingContext context, Vector effectiveOffset)
    {
        if (!ShouldShowRevisionBalloons(_reviewMarkupMode))
        {
            return;
        }

        if (_revisionAnchors.Count == 0)
        {
            return;
        }

        var pages = _editor.Layout.Pages;
        if (pages.Count == 0)
        {
            return;
        }

        var commentPages = new HashSet<int>();
        if (ShouldShowCommentBalloons(_reviewMarkupMode))
        {
            foreach (var anchor in _commentAnchors)
            {
                commentPages.Add(anchor.PageIndex);
            }
        }

        var zoom = _zoomFactor;
        var balloonWidth = RevisionBalloonWidth * zoom;
        var padding = RevisionBalloonPadding * zoom;
        var spacing = RevisionBalloonSpacing * zoom;
        var cornerRadius = RevisionBalloonCornerRadius * zoom;
        var borderThickness = Math.Max(1f, zoom * 0.75f);
        var borderPen = new Pen(RevisionBalloonBorderBrush, borderThickness);
        var bottomsByPage = new Dictionary<int, double>();

        foreach (var anchor in _revisionAnchors)
        {
            if (anchor.PageIndex < 0 || anchor.PageIndex >= pages.Count)
            {
                continue;
            }

            var page = pages[anchor.PageIndex];
            var docX = page.Bounds.Right + CommentBalloonMargin;
            if (commentPages.Contains(anchor.PageIndex))
            {
                docX += CommentBalloonWidth + CommentBalloonMargin;
            }

            var viewX = (docX * zoom) - effectiveOffset.X;
            var viewY = (anchor.DocY * zoom) - effectiveOffset.Y;

            var text = BuildRevisionDisplayText(anchor.Revision);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "Revision";
            }

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                RevisionBalloonTypeface,
                RevisionBalloonFontSize * zoom,
                RevisionBalloonTextBrush);
            formatted.MaxTextWidth = Math.Max(1f, balloonWidth - (2f * padding));
            formatted.TextAlignment = TextAlignment.Left;

            var height = formatted.Height + (2f * padding);
            if (bottomsByPage.TryGetValue(anchor.PageIndex, out var lastBottom) && viewY < lastBottom + spacing)
            {
                viewY = lastBottom + spacing;
            }

            bottomsByPage[anchor.PageIndex] = viewY + height;

            var rect = new Rect(viewX, viewY, balloonWidth, height);
            context.DrawRectangle(RevisionBalloonFillBrush, borderPen, rect, cornerRadius, cornerRadius, default);
            context.DrawText(formatted, new Point(rect.X + padding, rect.Y + padding));
        }
    }

    private static string BuildRevisionDisplayText(RevisionInfo revision)
    {
        var kind = revision.Kind switch
        {
            RevisionKind.Insert => "Inserted",
            RevisionKind.Delete => "Deleted",
            RevisionKind.MoveFrom => "Moved From",
            RevisionKind.MoveTo => "Moved To",
            _ => "Revision"
        };

        var builder = new StringBuilder();
        builder.Append(kind);
        if (!string.IsNullOrWhiteSpace(revision.Author))
        {
            builder.Append(" - ");
            builder.Append(revision.Author);
        }

        if (revision.Date.HasValue)
        {
            builder.Append(' ');
            builder.Append(revision.Date.Value.ToLocalTime().ToString("g"));
        }

        return builder.ToString();
    }

    private readonly record struct CommentAnchorInfo(
        int Id,
        int ParagraphIndex,
        int Offset,
        float DocY,
        float LineHeight,
        int PageIndex);

    private readonly record struct RevisionAnchorInfo(
        RevisionInfo Revision,
        bool IsBlock,
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

    private Rect DocRectToViewRect(DocRect rect, Vector effectiveOffset)
    {
        var topLeft = DocToView(new DocPoint(rect.X, rect.Y), effectiveOffset);
        var bottomRight = DocToView(new DocPoint(rect.Right, rect.Bottom), effectiveOffset);
        return new Rect(topLeft, bottomRight);
    }

    private void UpdatePointerCursor(Cursor? cursor)
    {
        if (Cursor == cursor)
        {
            return;
        }

        Cursor = cursor;
    }

    private void DrawTableSelectionOverlay(DrawingContext context, Vector effectiveOffset)
    {
        if (!TryGetTableSelectionRange(out var range))
        {
            return;
        }

        if (!TryGetTableLayouts(range.Table, out var layouts))
        {
            return;
        }

        foreach (var layout in layouts)
        {
            if (layout.Cells.Count == 0 || layout.Rows <= 0)
            {
                continue;
            }

            var rowMap = BuildTableRowIndexMap(layout);
            foreach (var cell in layout.Cells)
            {
                if (!IsCellInSelection(cell, range, rowMap))
                {
                    continue;
                }

                var rect = DocRectToViewRect(cell.Bounds, effectiveOffset);
                context.FillRectangle(TableSelectionFillBrush, rect);
                context.DrawRectangle(TableSelectionBorderPen, rect);
            }
        }
    }

    private void DrawTableResizeHandles(DrawingContext context, Vector effectiveOffset)
    {
        if (_headerFooterSession is not null || _isPictureCropMode)
        {
            return;
        }

        if (!TryBuildTableResizeHandles(out var handles))
        {
            return;
        }

        var active = _activeTableResizeHandle;
        var hover = _hoverTableResizeHandle;
        foreach (var handle in handles)
        {
            var rect = DocRectToViewRect(handle.Bounds, effectiveOffset);
            var isActive = active.HasValue && handle.Equals(active.Value);
            var isHover = !isActive && hover.HasValue && handle.Equals(hover.Value);
            var fill = isActive || isHover ? TableResizeHandleActiveFillBrush : TableResizeHandleFillBrush;
            var pen = isActive || isHover ? TableResizeHandleActiveBorderPen : TableResizeHandleBorderPen;
            context.FillRectangle(fill, rect);
            context.DrawRectangle(pen, rect);
        }

        if (_isTableResizing && _activeTableResizeHandle.HasValue && _tableResizePreviewPosition.HasValue)
        {
            var handle = _activeTableResizeHandle.Value;
            var position = _tableResizePreviewPosition.Value;
            if (handle.Kind == TableResizeHandleKind.Column)
            {
                var start = DocToView(new DocPoint(position, handle.TableBounds.Top), effectiveOffset);
                var end = DocToView(new DocPoint(position, handle.TableBounds.Bottom), effectiveOffset);
                context.DrawLine(TableResizeGuidePen, start, end);
            }
            else
            {
                var start = DocToView(new DocPoint(handle.TableBounds.Left, position), effectiveOffset);
                var end = DocToView(new DocPoint(handle.TableBounds.Right, position), effectiveOffset);
                context.DrawLine(TableResizeGuidePen, start, end);
            }
        }
    }

    private void DrawPictureCropOverlay(DrawingContext context, Vector effectiveOffset)
    {
        if (!_isPictureCropMode)
        {
            return;
        }

        if (!TryBuildCropHandles(out var handles, out var bounds))
        {
            return;
        }

        var boundsRect = DocRectToViewRect(bounds, effectiveOffset);
        context.DrawRectangle(CropBoundsPen, boundsRect);

        var active = _activeCropHandle;
        var hover = _hoverCropHandle;
        foreach (var handle in handles)
        {
            var rect = DocRectToViewRect(handle.Bounds, effectiveOffset);
            var isActive = active.HasValue && handle.Kind == active.Value.Kind;
            var isHover = !isActive && hover.HasValue && handle.Kind == hover.Value.Kind;
            var fill = isActive || isHover ? TableResizeHandleActiveFillBrush : CropHandleFillBrush;
            var pen = isActive || isHover ? TableResizeHandleActiveBorderPen : CropHandleBorderPen;
            context.FillRectangle(fill, rect);
            context.DrawRectangle(pen, rect);
        }
    }

    private void DrawShapeSelectionOverlay(DrawingContext context, Vector effectiveOffset)
    {
        if (_headerFooterSession is not null || _isPictureCropMode)
        {
            return;
        }

        if (!TryBuildShapeHandles(out var handles, out var geometry))
        {
            return;
        }

        var topLeft = DocToView(geometry.TopLeft, effectiveOffset);
        var topRight = DocToView(geometry.TopRight, effectiveOffset);
        var bottomRight = DocToView(geometry.BottomRight, effectiveOffset);
        var bottomLeft = DocToView(geometry.BottomLeft, effectiveOffset);
        context.DrawLine(ShapeSelectionBorderPen, topLeft, topRight);
        context.DrawLine(ShapeSelectionBorderPen, topRight, bottomRight);
        context.DrawLine(ShapeSelectionBorderPen, bottomRight, bottomLeft);
        context.DrawLine(ShapeSelectionBorderPen, bottomLeft, topLeft);

        var active = _activeShapeHandle;
        var hover = _hoverShapeHandle;
        foreach (var handle in handles)
        {
            var rect = DocRectToViewRect(handle.Bounds, effectiveOffset);
            var isActive = active.HasValue && handle.Kind == active.Value.Kind;
            var isHover = !isActive && hover.HasValue && handle.Kind == hover.Value.Kind;
            var fill = isActive || isHover ? TableResizeHandleActiveFillBrush : ShapeHandleFillBrush;
            var pen = isActive || isHover ? TableResizeHandleActiveBorderPen : ShapeHandleBorderPen;
            context.FillRectangle(fill, rect);
            context.DrawRectangle(pen, rect);
        }

        if (geometry.RotateHandle.X != 0f || geometry.RotateHandle.Y != 0f)
        {
            var start = DocToView(geometry.TopCenter, effectiveOffset);
            var end = DocToView(geometry.RotateHandle, effectiveOffset);
            context.DrawLine(ShapeSelectionBorderPen, start, end);
        }
    }

    private bool TryBuildTableResizeHandles(out List<TableResizeHandle> handles)
    {
        handles = new List<TableResizeHandle>();
        if (!TryGetTableSelectionRange(out var range))
        {
            return false;
        }

        if (!TryGetTableLayouts(range.Table, out var layouts))
        {
            return false;
        }

        var handleSize = TableResizeHandleSize / MathF.Max(0.1f, _zoomFactor);
        foreach (var layout in layouts)
        {
            AppendTableResizeHandles(layout, handleSize, handles);
        }

        return handles.Count > 0;
    }

    private static void AppendTableResizeHandles(TableLayout layout, float handleSize, List<TableResizeHandle> handles)
    {
        if (layout.Columns <= 0 || layout.Rows <= 0)
        {
            return;
        }

        var columnWidths = layout.ColumnWidths;
        if (columnWidths.Count > 0)
        {
            var x = layout.Bounds.Left + layout.CellSpacing;
            var handleY = layout.Bounds.Top - handleSize;
            var maxColumns = Math.Min(layout.Columns, columnWidths.Count);
            for (var column = 0; column < maxColumns; column++)
            {
                x += columnWidths[column];
                var bounds = new DocRect(x - handleSize / 2f, handleY, handleSize, handleSize);
                handles.Add(new TableResizeHandle(TableResizeHandleKind.Column, column, -1, bounds, layout.Bounds, x, layout));
                x += layout.CellSpacing;
            }
        }

        var rowHeights = layout.RowHeights;
        if (rowHeights.Count == 0)
        {
            return;
        }

        var rowMap = BuildTableRowIndexMap(layout);
        var y = layout.Bounds.Top + layout.CellSpacing;
        var handleX = layout.Bounds.Left - handleSize;
        var maxRows = Math.Min(layout.Rows, rowHeights.Count);
        for (var row = 0; row < maxRows; row++)
        {
            var rowHeight = rowHeights[row];
            var boundaryY = y + rowHeight;
            var globalRow = row < rowMap.Length ? rowMap[row] : row;
            if (globalRow >= 0)
            {
                var isLastRow = row == maxRows - 1;
                var nextGlobalRow = row + 1 < rowMap.Length ? rowMap[row + 1] : globalRow + 1;
                var hasBoundary = !isLastRow && nextGlobalRow != globalRow;
                if (hasBoundary || (!layout.ContinuesOnNext && isLastRow))
                {
                    var bounds = new DocRect(handleX, boundaryY - handleSize / 2f, handleSize, handleSize);
                    handles.Add(new TableResizeHandle(TableResizeHandleKind.Row, globalRow, row, bounds, layout.Bounds, boundaryY, layout));
                }
            }

            y = boundaryY + layout.CellSpacing;
        }
    }

    private bool TryBuildCropHandles(out List<CropHandleInfo> handles, out DocRect bounds)
    {
        handles = new List<CropHandleInfo>();
        bounds = default;
        if (!TryGetSelectedFloatingImageLayout(out var layoutObject, out _))
        {
            return false;
        }

        bounds = layoutObject.Bounds;
        var size = CropHandleSize / MathF.Max(0.1f, _zoomFactor);
        var half = size / 2f;

        var left = bounds.Left;
        var right = bounds.Right;
        var top = bounds.Top;
        var bottom = bounds.Bottom;
        var midX = bounds.Left + bounds.Width / 2f;
        var midY = bounds.Top + bounds.Height / 2f;

        handles.Add(new CropHandleInfo(CropHandleKind.TopLeft, new DocRect(left - half, top - half, size, size)));
        handles.Add(new CropHandleInfo(CropHandleKind.Top, new DocRect(midX - half, top - half, size, size)));
        handles.Add(new CropHandleInfo(CropHandleKind.TopRight, new DocRect(right - half, top - half, size, size)));
        handles.Add(new CropHandleInfo(CropHandleKind.Right, new DocRect(right - half, midY - half, size, size)));
        handles.Add(new CropHandleInfo(CropHandleKind.BottomRight, new DocRect(right - half, bottom - half, size, size)));
        handles.Add(new CropHandleInfo(CropHandleKind.Bottom, new DocRect(midX - half, bottom - half, size, size)));
        handles.Add(new CropHandleInfo(CropHandleKind.BottomLeft, new DocRect(left - half, bottom - half, size, size)));
        handles.Add(new CropHandleInfo(CropHandleKind.Left, new DocRect(left - half, midY - half, size, size)));

        return true;
    }

    private bool TryBuildShapeHandles(out List<ShapeHandleInfo> handles, out ShapeSelectionGeometry geometry)
    {
        handles = new List<ShapeHandleInfo>();
        geometry = default;
        if (!TryResolveShapeSelection(out var selection))
        {
            return false;
        }

        var bounds = selection.Bounds;
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return false;
        }

        var size = ShapeHandleSize / MathF.Max(0.1f, _zoomFactor);
        var half = size * 0.5f;
        var rotateOffset = ShapeRotateHandleOffset / MathF.Max(0.1f, _zoomFactor);
        geometry = BuildShapeSelectionGeometry(bounds, selection.Rotation, rotateOffset);

        handles.Add(new ShapeHandleInfo(ShapeHandleKind.TopLeft, new DocRect(geometry.TopLeft.X - half, geometry.TopLeft.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Top, new DocRect(geometry.TopCenter.X - half, geometry.TopCenter.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.TopRight, new DocRect(geometry.TopRight.X - half, geometry.TopRight.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Right, new DocRect(geometry.RightCenter.X - half, geometry.RightCenter.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.BottomRight, new DocRect(geometry.BottomRight.X - half, geometry.BottomRight.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Bottom, new DocRect(geometry.BottomCenter.X - half, geometry.BottomCenter.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.BottomLeft, new DocRect(geometry.BottomLeft.X - half, geometry.BottomLeft.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Left, new DocRect(geometry.LeftCenter.X - half, geometry.LeftCenter.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Rotate, new DocRect(geometry.RotateHandle.X - half, geometry.RotateHandle.Y - half, size, size)));
        return true;
    }

    private bool TryGetShapeHandleAtPoint(DocPoint point, out ShapeHandleInfo handle)
    {
        handle = default;
        if (!TryBuildShapeHandles(out var handles, out _))
        {
            return false;
        }

        foreach (var candidate in handles)
        {
            if (candidate.Bounds.Contains(point.X, point.Y))
            {
                handle = candidate;
                return true;
            }
        }

        return false;
    }

    private static Cursor GetShapeCursor(ShapeHandleKind kind)
    {
        return kind switch
        {
            ShapeHandleKind.Left => ColumnResizeCursor,
            ShapeHandleKind.Right => ColumnResizeCursor,
            ShapeHandleKind.Top => RowResizeCursor,
            ShapeHandleKind.Bottom => RowResizeCursor,
            ShapeHandleKind.TopLeft => CropTopLeftCursor,
            ShapeHandleKind.TopRight => CropTopRightCursor,
            ShapeHandleKind.BottomRight => CropBottomRightCursor,
            ShapeHandleKind.BottomLeft => CropBottomLeftCursor,
            ShapeHandleKind.Rotate => ShapeRotateCursor,
            _ => ShapeMoveCursor
        };
    }

    private static bool IsCellInSelection(TableCellLayout cell, TableSelectionRange range, int[] rowMap)
    {
        if (cell.ColumnIndex > range.ColumnEnd || cell.ColumnIndex + cell.ColumnSpan - 1 < range.ColumnStart)
        {
            return false;
        }

        if (rowMap.Length == 0)
        {
            return false;
        }

        var start = Math.Clamp(cell.RowIndex, 0, rowMap.Length - 1);
        var end = Math.Clamp(cell.RowIndex + Math.Max(1, cell.RowSpan) - 1, 0, rowMap.Length - 1);
        for (var row = start; row <= end; row++)
        {
            var globalRow = rowMap[row];
            if (globalRow >= range.RowStart && globalRow <= range.RowEnd)
            {
                return true;
            }
        }

        return false;
    }

    private static int[] BuildTableRowIndexMap(TableLayout layout)
    {
        if (layout.Rows <= 0)
        {
            return Array.Empty<int>();
        }

        var map = new int[layout.Rows];
        Array.Fill(map, -1);
        foreach (var cell in layout.Cells)
        {
            if (cell.RowIndex < 0 || cell.RowIndex >= map.Length)
            {
                continue;
            }

            var globalRow = cell.MergeOriginRowIndex >= 0 ? cell.MergeOriginRowIndex : cell.RowIndex;
            if (map[cell.RowIndex] < 0 || globalRow < map[cell.RowIndex])
            {
                map[cell.RowIndex] = globalRow;
            }
        }

        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] < 0)
            {
                map[i] = i;
            }
        }

        return map;
    }

    private bool TryGetTableSelectionRange(out TableSelectionRange range)
    {
        range = default;
        if (_editor.Document.ParagraphCount == 0)
        {
            return false;
        }

        var selection = _editor.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _editor.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _editor.Document.ParagraphCount - 1);
        var startLocation = _editor.Document.GetParagraphLocation(startIndex);
        var endLocation = _editor.Document.GetParagraphLocation(endIndex);
        if (!startLocation.IsInTable || !endLocation.IsInTable || startLocation.Table is null || endLocation.Table is null)
        {
            return false;
        }

        if (!ReferenceEquals(startLocation.Table, endLocation.Table))
        {
            return false;
        }

        var rowStart = Math.Min(startLocation.RowIndex, endLocation.RowIndex);
        var rowEnd = Math.Max(startLocation.RowIndex, endLocation.RowIndex);
        var columnStart = Math.Min(startLocation.ColumnIndex, endLocation.ColumnIndex);
        var columnEnd = Math.Max(startLocation.ColumnIndex, endLocation.ColumnIndex);
        range = new TableSelectionRange(startLocation.Table, rowStart, rowEnd, columnStart, columnEnd);
        return true;
    }

    private bool TryGetTableLayouts(TableBlock table, out List<TableLayout> layouts)
    {
        layouts = new List<TableLayout>();
        if (_editor.Layout.Tables.Count == 0)
        {
            return false;
        }

        var tableIndex = -1;
        var seen = 0;
        foreach (var block in _editor.Document.Blocks)
        {
            if (block is not TableBlock current)
            {
                continue;
            }

            if (ReferenceEquals(current, table))
            {
                tableIndex = seen;
                break;
            }

            seen++;
        }

        if (tableIndex < 0)
        {
            return false;
        }

        var currentIndex = -1;
        foreach (var layout in _editor.Layout.Tables)
        {
            if (!layout.ContinuesFromPrevious)
            {
                currentIndex++;
            }

            if (currentIndex == tableIndex)
            {
                layouts.Add(layout);
                continue;
            }

            if (currentIndex > tableIndex)
            {
                break;
            }
        }

        return layouts.Count > 0;
    }

    private bool TryGetSelectedFloatingImageLayout(out FloatingLayoutObject layoutObject, out ImageInline image)
    {
        layoutObject = null!;
        image = null!;
        var selectedId = _editor.SelectedFloatingObjectId;
        if (!selectedId.HasValue)
        {
            return false;
        }

        foreach (var floating in _editor.Layout.FloatingObjects)
        {
            if (floating.Object.Id != selectedId.Value)
            {
                continue;
            }

            if (floating.Object.Content is ImageInline inlineImage)
            {
                layoutObject = floating;
                image = inlineImage;
                return true;
            }

            return false;
        }

        return false;
    }

    private bool TryGetInlineShapeAtPosition(TextPosition position, out ShapeInline shape, out int paragraphIndex, out int inlineIndex)
    {
        shape = null!;
        paragraphIndex = -1;
        inlineIndex = -1;
        if (_editor.Document.ParagraphCount == 0)
        {
            return false;
        }

        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= _editor.Document.ParagraphCount)
        {
            return false;
        }

        var paragraph = _editor.Document.GetParagraph(position.ParagraphIndex);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var offset = 0;
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            var inline = paragraph.Inlines[i];
            var length = DocumentEditHelpers.GetInlineLength(inline);
            if (position.Offset >= offset && position.Offset < offset + length)
            {
                if (inline is ShapeInline inlineShape)
                {
                    shape = inlineShape;
                    paragraphIndex = position.ParagraphIndex;
                    inlineIndex = i;
                    return true;
                }

                return false;
            }

            offset += length;
        }

        return false;
    }

    private bool TryGetInlineShapeLayout(ShapeInline shape, int paragraphIndex, out DocRect bounds, out int pageIndex)
    {
        bounds = default;
        pageIndex = -1;
        var layout = _editor.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < layout.Lines.Count; i++)
        {
            var line = layout.Lines[i];
            if (line.ParagraphIndex != paragraphIndex)
            {
                continue;
            }

            foreach (var layoutShape in line.Shapes)
            {
                if (!ReferenceEquals(layoutShape.Shape, shape))
                {
                    continue;
                }

                bounds = ComputeInlineShapeBounds(line, layoutShape, out _);
                pageIndex = layout.LineIndex.GetPageForLine(i);
                return true;
            }
        }

        return false;
    }

    private static DocRect ComputeInlineShapeBounds(LayoutLine line, LayoutShape shape, out float baseRotation)
    {
        var width = shape.Width;
        var height = shape.Height;
        if (!DocTextDirectionHelpers.IsVertical(line.TextDirection))
        {
            baseRotation = 0f;
            var baseline = line.Y + line.Ascent;
            return new DocRect(line.X + shape.X, baseline - height, width, height);
        }

        baseRotation = DocTextDirectionHelpers.GetRotationDegrees(line.TextDirection!.Value);
        var baselineLocal = line.Ascent;
        var left = shape.X;
        var top = baselineLocal - height;
        var right = left + width;
        var bottom = top + height;

        var radians = baseRotation * (MathF.PI / 180f);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        var p1 = RotatePoint(left, top, cos, sin, line.X, line.Y);
        var p2 = RotatePoint(right, top, cos, sin, line.X, line.Y);
        var p3 = RotatePoint(right, bottom, cos, sin, line.X, line.Y);
        var p4 = RotatePoint(left, bottom, cos, sin, line.X, line.Y);

        var minX = MathF.Min(MathF.Min(p1.X, p2.X), MathF.Min(p3.X, p4.X));
        var maxX = MathF.Max(MathF.Max(p1.X, p2.X), MathF.Max(p3.X, p4.X));
        var minY = MathF.Min(MathF.Min(p1.Y, p2.Y), MathF.Min(p3.Y, p4.Y));
        var maxY = MathF.Max(MathF.Max(p1.Y, p2.Y), MathF.Max(p3.Y, p4.Y));
        return new DocRect(minX, minY, MathF.Max(0f, maxX - minX), MathF.Max(0f, maxY - minY));
    }

    private static DocPoint RotatePoint(float x, float y, float cos, float sin, float originX, float originY)
    {
        var worldX = originX + x * cos - y * sin;
        var worldY = originY + x * sin + y * cos;
        return new DocPoint(worldX, worldY);
    }

    private static DocPoint RotateVector(float x, float y, float cos, float sin, DocPoint origin)
    {
        var worldX = origin.X + x * cos - y * sin;
        var worldY = origin.Y + x * sin + y * cos;
        return new DocPoint(worldX, worldY);
    }

    private static ShapeSelectionGeometry BuildShapeSelectionGeometry(DocRect bounds, float rotationDegrees, float rotateOffset)
    {
        var center = new DocPoint(bounds.Left + bounds.Width * 0.5f, bounds.Top + bounds.Height * 0.5f);
        var halfWidth = bounds.Width * 0.5f;
        var halfHeight = bounds.Height * 0.5f;

        var radians = rotationDegrees * (MathF.PI / 180f);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        var topLeft = RotateVector(-halfWidth, -halfHeight, cos, sin, center);
        var topRight = RotateVector(halfWidth, -halfHeight, cos, sin, center);
        var bottomRight = RotateVector(halfWidth, halfHeight, cos, sin, center);
        var bottomLeft = RotateVector(-halfWidth, halfHeight, cos, sin, center);
        var topCenter = RotateVector(0f, -halfHeight, cos, sin, center);
        var rightCenter = RotateVector(halfWidth, 0f, cos, sin, center);
        var bottomCenter = RotateVector(0f, halfHeight, cos, sin, center);
        var leftCenter = RotateVector(-halfWidth, 0f, cos, sin, center);
        var rotateCenter = RotateVector(0f, -halfHeight - rotateOffset, cos, sin, center);

        return new ShapeSelectionGeometry(
            topLeft,
            topRight,
            bottomRight,
            bottomLeft,
            rotateCenter,
            topCenter,
            rightCenter,
            bottomCenter,
            leftCenter);
    }

    private void PromoteInlineShapeToFloating(int paragraphIndex, int inlineIndex, ShapeInline shape, DocRect bounds, int pageIndex)
    {
        if (paragraphIndex < 0 || paragraphIndex >= _editor.Document.ParagraphCount)
        {
            return;
        }

        var paragraph = _editor.Document.GetParagraph(paragraphIndex);
        if (inlineIndex < 0 || inlineIndex >= paragraph.Inlines.Count)
        {
            return;
        }

        if (!ReferenceEquals(paragraph.Inlines[inlineIndex], shape))
        {
            return;
        }

        _isPromotingInlineShape = true;
        try
        {
            paragraph.Inlines.RemoveAt(inlineIndex);

            var floating = new FloatingObject(shape);
            var anchor = floating.Anchor;
            anchor.HorizontalReference = FloatingHorizontalReference.Page;
            anchor.VerticalReference = FloatingVerticalReference.Page;
            anchor.HorizontalAlignment = FloatingHorizontalAlignment.None;
            anchor.VerticalAlignment = FloatingVerticalAlignment.None;
            anchor.WrapStyle = FloatingWrapStyle.None;
            anchor.WrapSide = FloatingWrapSide.Both;
            anchor.BehindText = false;
            anchor.AnchorOffset = _editor.Caret.Offset;

            if (_editor.Layout.Pages.Count > 0)
            {
                var clamped = Math.Clamp(pageIndex, 0, _editor.Layout.Pages.Count - 1);
                var page = _editor.Layout.Pages[clamped];
                anchor.OffsetX = bounds.X - page.Bounds.X;
                anchor.OffsetY = bounds.Y - page.Bounds.Y;
            }
            else
            {
                anchor.OffsetX = bounds.X;
                anchor.OffsetY = bounds.Y;
            }

            paragraph.FloatingObjects.Add(floating);
            _editor.RefreshLayout();
            _editor.SetCaretFromPoint(bounds.X + MathF.Max(1f, bounds.Width) * 0.5f, bounds.Y + MathF.Max(1f, bounds.Height) * 0.5f, false);
        }
        finally
        {
            _isPromotingInlineShape = false;
        }
    }

    private bool TryGetSelectedFloatingShapeLayout(out FloatingLayoutObject layoutObject, out FloatingObject floating, out ShapeInline shape)
    {
        layoutObject = null!;
        floating = null!;
        shape = null!;
        var selectedId = _editor.SelectedFloatingObjectId;
        if (!selectedId.HasValue)
        {
            return false;
        }

        foreach (var item in _editor.Layout.FloatingObjects)
        {
            if (item.Object.Id != selectedId.Value)
            {
                continue;
            }

            if (item.Object.Content is ShapeInline inlineShape)
            {
                layoutObject = item;
                floating = item.Object;
                shape = inlineShape;
                return true;
            }

            return false;
        }

        return false;
    }

    private bool TryResolveShapeSelection(out ShapeSelectionInfo selection)
    {
        selection = default;
        if (!TryGetSelectedFloatingShapeLayout(out var layoutObject, out _, out var shape))
        {
            return false;
        }

        var bounds = layoutObject.Bounds;
        if (_isShapeEditing && _shapePreviewBounds.HasValue && ReferenceEquals(shape, _shapeInline))
        {
            bounds = _shapePreviewBounds.Value;
        }

        selection = new ShapeSelectionInfo(shape, bounds, shape.Properties.Rotation);
        return true;
    }

    private bool TryGetSelectedShapeBounds(out DocRect bounds)
    {
        bounds = default;
        if (!TryResolveShapeSelection(out var selection))
        {
            return false;
        }

        if (MathF.Abs(selection.Rotation) < 0.01f)
        {
            bounds = selection.Bounds;
            return true;
        }

        var geometry = BuildShapeSelectionGeometry(selection.Bounds, selection.Rotation, 0f);
        var minX = MathF.Min(MathF.Min(geometry.TopLeft.X, geometry.TopRight.X), MathF.Min(geometry.BottomLeft.X, geometry.BottomRight.X));
        var maxX = MathF.Max(MathF.Max(geometry.TopLeft.X, geometry.TopRight.X), MathF.Max(geometry.BottomLeft.X, geometry.BottomRight.X));
        var minY = MathF.Min(MathF.Min(geometry.TopLeft.Y, geometry.TopRight.Y), MathF.Min(geometry.BottomLeft.Y, geometry.BottomRight.Y));
        var maxY = MathF.Max(MathF.Max(geometry.TopLeft.Y, geometry.TopRight.Y), MathF.Max(geometry.BottomLeft.Y, geometry.BottomRight.Y));
        bounds = new DocRect(minX, minY, MathF.Max(0f, maxX - minX), MathF.Max(0f, maxY - minY));
        return true;
    }

    private bool TryGetFloatingShapeAtPoint(DocPoint point, out FloatingLayoutObject layoutObject, out FloatingObject floating, out ShapeInline shape)
    {
        layoutObject = null!;
        floating = null!;
        shape = null!;
        var floats = _editor.Layout.FloatingObjects;
        if (floats.Count == 0)
        {
            return false;
        }

        for (var i = floats.Count - 1; i >= 0; i--)
        {
            var candidate = floats[i];
            if (!candidate.Bounds.Contains(point.X, point.Y))
            {
                continue;
            }

            if (candidate.Object.Content is ShapeInline inlineShape)
            {
                layoutObject = candidate;
                floating = candidate.Object;
                shape = inlineShape;
                return true;
            }
        }

        return false;
    }

    private bool TryGetInkReplayTarget(out FloatingLayoutObject layoutObject, out ImageInline image)
    {
        if (TryGetSelectedFloatingImageLayout(out layoutObject, out image) && IsInkImage(image))
        {
            return true;
        }

        var floats = _editor.Layout.FloatingObjects;
        for (var i = floats.Count - 1; i >= 0; i--)
        {
            var floating = floats[i];
            if (floating.Object.Content is ImageInline inline && IsInkImage(inline))
            {
                layoutObject = floating;
                image = inline;
                return true;
            }
        }

        layoutObject = null!;
        image = null!;
        return false;
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

    private sealed class InkReplayState
    {
        public List<DocPoint> Points { get; }
        public DocColor Color { get; }
        public float Thickness { get; }
        public int VisibleCount { get; set; }

        public InkReplayState(List<DocPoint> points, DocColor color, float thickness)
        {
            Points = points;
            Color = color;
            Thickness = thickness;
            VisibleCount = Math.Min(2, points.Count);
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
            CreateViewOptionsService(),
            documentFactory: DocumentTemplates.CreateDefaultDocument);
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
        _thumbnailRenderer.TypefaceResolver = _fontResolver;
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
        UpdatePictureCropState();
        AutoPromoteInlineShapeIfSelected();
        UpdateHeaderFooterSessionLayout();
        UpdateCommentAnchors();
        UpdateRevisionAnchors();
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        UpdateScrollMetrics();
        UpdateDirtyPages(_editor.DirtyPages);
        UpdateSelectedEquation();
        UpdatePictureCropState();
        AutoPromoteInlineShapeIfSelected();
        UpdateHeaderFooterSessionLayout();
        UpdateCommentAnchors();
        UpdateRevisionAnchors();
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

    private bool EnsureThumbnailCache(DocumentLayout layout, float scale)
    {
        var useHarfBuzz = _renderOptions.UseHarfBuzz;
        var needsRefresh = _thumbnailRenderOptions is null
                           || !ReferenceEquals(layout, _thumbnailLayout)
                           || MathF.Abs(_thumbnailScale - scale) > 0.001f
                           || _thumbnailUseHarfBuzz != useHarfBuzz;

        if (!needsRefresh)
        {
            return true;
        }

        _thumbnailLayout = layout;
        _thumbnailScale = scale;
        _thumbnailUseHarfBuzz = useHarfBuzz;
        _thumbnailRenderOptions = BuildThumbnailRenderOptions(scale);

        using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return false;
        }

        _thumbnailRenderer.Render(surface.Canvas, _editor.Document, layout, _thumbnailRenderOptions);
        return true;
    }

    private RenderOptions BuildThumbnailRenderOptions(float scale)
    {
        return new RenderOptions
        {
            BackgroundColor = DocColor.Transparent,
            PageColor = _renderOptions.PageColor,
            PageBorderColor = _renderOptions.PageBorderColor,
            PageBorderThickness = _renderOptions.PageBorderThickness,
            HeaderFooterOverlayColor = _renderOptions.HeaderFooterOverlayColor,
            HeaderFooterBoundsColor = _renderOptions.HeaderFooterBoundsColor,
            HeaderFooterBoundsThickness = _renderOptions.HeaderFooterBoundsThickness,
            ColumnSeparatorColor = _renderOptions.ColumnSeparatorColor,
            ColumnSeparatorThickness = _renderOptions.ColumnSeparatorThickness,
            TextColor = _renderOptions.TextColor,
            SelectionColor = _renderOptions.SelectionColor,
            FloatingSelectionColor = _renderOptions.FloatingSelectionColor,
            CaretColor = _renderOptions.CaretColor,
            CommentHighlightColor = _renderOptions.CommentHighlightColor,
            PlaceholderFillColor = _renderOptions.PlaceholderFillColor,
            PlaceholderStrokeColor = _renderOptions.PlaceholderStrokeColor,
            PlaceholderTextColor = _renderOptions.PlaceholderTextColor,
            CaretThickness = _renderOptions.CaretThickness,
            ShowLayout = true,
            ShowGridlines = false,
            ShowInvisibles = false,
            UseHarfBuzz = _renderOptions.UseHarfBuzz,
            UsePictureCache = true,
            SvgRenderMode = _renderOptions.SvgRenderMode,
            SvgRasterizationScale = scale,
            SvgRasterBackgroundColor = _renderOptions.SvgRasterBackgroundColor,
            SvgRasterizer = _renderOptions.SvgRasterizer,
            ShowCaret = false,
            HeaderFooterMode = HeaderFooterEditMode.None
        };
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

    private void UpdatePictureCropState()
    {
        if (!_isPictureCropMode)
        {
            return;
        }

        if (TryGetSelectedFloatingImageLayout(out _, out _))
        {
            return;
        }

        SetPictureCropMode(false);
    }

    private void AutoPromoteInlineShapeIfSelected()
    {
        if (_isPromotingInlineShape || _isShapeEditing || _headerFooterSession is not null)
        {
            return;
        }

        if (!_editor.Selection.IsEmpty || _editor.SelectedFloatingObjectId.HasValue)
        {
            return;
        }

        if (!TryGetInlineShapeAtPosition(_editor.Caret, out var shape, out var paragraphIndex, out var inlineIndex))
        {
            return;
        }

        if (!TryGetInlineShapeLayout(shape, paragraphIndex, out var bounds, out var pageIndex))
        {
            return;
        }

        PromoteInlineShapeToFloating(paragraphIndex, inlineIndex, shape, bounds, pageIndex);
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
            viewOptions,
            documentFactory: DocumentTemplates.CreateDefaultDocument);
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
        IReadOnlyList<TableLayout> tables = Array.Empty<TableLayout>();
        if (TryGetHeaderFooterLayout(layout, pageIndex, out var headerFooter))
        {
            if (mode == HeaderFooterEditMode.Footer)
            {
                lines = headerFooter.FooterLines;
                tables = headerFooter.FooterTables;
            }
            else
            {
                lines = headerFooter.HeaderLines;
                tables = headerFooter.HeaderTables;
            }
        }

        var regionTop = 0f;
        var regionBottom = 0f;
        var hasContent = false;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        if (lines.Count > 0)
        {
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

            hasContent = true;
        }

        if (tables.Count > 0)
        {
            foreach (var table in tables)
            {
                var tableBounds = table.Bounds;
                if (tableBounds.Height <= 0f)
                {
                    continue;
                }

                if (tableBounds.Y < minY)
                {
                    minY = tableBounds.Y;
                }

                if (tableBounds.Bottom > maxY)
                {
                    maxY = tableBounds.Bottom;
                }

                hasContent = true;
            }
        }

        if (hasContent)
        {
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

    private enum TableResizeHandleKind
    {
        Column,
        Row
    }

    private readonly record struct TableResizeHandle(
        TableResizeHandleKind Kind,
        int Index,
        int LocalIndex,
        DocRect Bounds,
        DocRect TableBounds,
        float Position,
        TableLayout Layout);

    private readonly record struct TableSelectionRange(
        TableBlock Table,
        int RowStart,
        int RowEnd,
        int ColumnStart,
        int ColumnEnd);

    private enum ShapeDragMode
    {
        None,
        Move,
        Resize,
        Rotate
    }

    private enum ShapeHandleKind
    {
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Rotate
    }

    private readonly record struct ShapeHandleInfo(ShapeHandleKind Kind, DocRect Bounds);
    private readonly record struct ShapeSelectionInfo(ShapeInline Shape, DocRect Bounds, float Rotation);
    private readonly record struct ShapeSelectionGeometry(
        DocPoint TopLeft,
        DocPoint TopRight,
        DocPoint BottomRight,
        DocPoint BottomLeft,
        DocPoint RotateHandle,
        DocPoint TopCenter,
        DocPoint RightCenter,
        DocPoint BottomCenter,
        DocPoint LeftCenter);

    private enum CropHandleKind
    {
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private readonly record struct CropHandleInfo(CropHandleKind Kind, DocRect Bounds);
}

public enum DocumentZoomMode
{
    Custom,
    PageWidth,
    WholePage,
    MultiplePages
}
