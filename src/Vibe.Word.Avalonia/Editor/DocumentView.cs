using System.Buffers;
using System.Globalization;
using System.Text;
using System.Threading;
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
using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Editor;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Collaboration.UI;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;
using Vibe.Office.Rendering.Skia;
using Vibe.Word.Editor;
using Vibe.Word.Editor.Editing;
using RenderOptions = Vibe.Office.Rendering.RenderOptions;

namespace Vibe.Word.Avalonia;

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
    private const float PresenceTagFontSize = 11f;
    private const float PresenceTagPadding = 4f;
    private const float PresenceTagCornerRadius = 4f;
    private const float PresenceCaretThickness = 1.5f;
    private const float PresenceSelectionOpacity = 0.25f;
    private static readonly TimeSpan PresenceTimeToLive = TimeSpan.FromSeconds(10);
    private const float TableResizeHandleSize = 8f;
    private const float TableResizeMinRowHeight = 8f;
    private const float TableSelectionOutlineThickness = 1f;
    private const float CropHandleSize = 8f;
    private const float CropMinVisibleSize = 12f;
    private const float ShapeHandleSize = 8f;
    private const float ShapeRotateHandleOffset = 18f;
    private const float ShapeMinSize = 8f;
    private const float ShapeRotateSnapDegrees = 15f;
    private const float InlineDragThreshold = 4f;
    private const float FloatingNudgeStep = 1f;
    private const float FloatingNudgeLargeStep = 10f;
    private const int ResizeLayoutDebounceMs = 120;
    private enum FieldUpdateTrigger
    {
        Open,
        Print,
        Manual
    }

    private static readonly IBrush CommentBalloonFillBrush = new SolidColorBrush(Color.Parse("#FFF7D6"));
    private static readonly IBrush CommentBalloonBorderBrush = new SolidColorBrush(Color.Parse("#D7C284"));
    private static readonly IBrush CommentBalloonTextBrush = new SolidColorBrush(Color.Parse("#303030"));
    private static readonly Typeface CommentBalloonTypeface = new Typeface("Calibri");
    private static readonly IBrush RevisionBalloonFillBrush = new SolidColorBrush(Color.Parse("#E8F1FF"));
    private static readonly IBrush RevisionBalloonBorderBrush = new SolidColorBrush(Color.Parse("#A8C3E8"));
    private static readonly IBrush RevisionBalloonTextBrush = new SolidColorBrush(Color.Parse("#203040"));
    private static readonly Typeface RevisionBalloonTypeface = new Typeface("Calibri");
    private static readonly IBrush PresenceTagTextBrush = new SolidColorBrush(Colors.White);
    private static readonly Typeface PresenceTagTypeface = new Typeface("Calibri");
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
    private static readonly Cursor TextCursor = new Cursor(StandardCursorType.Ibeam);

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
    private readonly DocumentAnchorResolver _presenceAnchorResolver = new();
    private readonly Dictionary<Guid, IBrush> _presenceCaretBrushes = new();
    private readonly Dictionary<Guid, IBrush> _presenceSelectionBrushes = new();
    private readonly Dictionary<Guid, Pen> _presenceOutlinePens = new();
    private ICollabUiService? _collabUiService;
    private PresenceSignature? _lastPresenceSignature;
    private CollabEditorSessionCoordinator? _collabCoordinator;
    private ICollabRealtimeSession? _collabSession;
    private Func<Guid, string?>? _collabAuthorResolver;
    private SynchronizationContext? _collabSynchronizationContext;

    private bool _isSelecting;
    private bool _isLoading;
    private Vector _scrollOffset;
    private Size _extent;
    private Size _viewport;
    private float _zoomFactor = 1f;
    private DocumentZoomMode _zoomMode = DocumentZoomMode.Custom;
    private int _multiplePagesPerRow = DefaultMultiplePages;
    private DocumentLayout? _layoutMetricsLayout;
    private bool _layoutContentBoundsValid;
    private float _layoutContentMinX;
    private float _layoutContentMaxX;
    private float _layoutMaxPageWidth;
    private EquationInline? _selectedEquation;
    private long _renderVersion;
    private InkStrokeBuilder? _activeInk;
    private InkReplayState? _inkReplay;
    private DispatcherTimer? _inkReplayTimer;
    private DispatcherTimer? _resizeLayoutTimer;
    private bool _isDrawing;
    private bool _hasPendingLayoutSize;
    private EditorDrawTool _activeDrawTool;
    private HeaderFooterEditSession? _headerFooterSession;
    private HeaderFooterHit? _headerFooterHit;
    private EditorServices? _headerFooterServices;
    private EditorSessionSnapshot? _headerFooterSnapshot;
    private CollabGestureToken? _headerFooterGesture;
    private bool _headerFooterDirty;
    private bool _isHeaderFooterSelecting;
    private ShapeTextEditSession? _shapeTextSession;
    private EditorServices? _shapeTextServices;
    private EditorSessionSnapshot? _shapeTextSnapshot;
    private CollabGestureToken? _shapeTextGesture;
    private bool _shapeTextDirty;
    private bool _isShapeTextSelecting;
    private bool _isShapeTextLayoutUpdating;
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
    private CollabGestureToken? _tableResizeGesture;
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
    private CollabGestureToken? _cropGesture;
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
    private CollabGestureToken? _shapeGesture;
    private bool _shapeDirty;
    private DocRect? _shapePreviewBounds;
    private bool _shapeLayoutRefreshPending;
    private bool _isImageEditing;
    private ShapeDragMode _imageDragMode;
    private ShapeHandleInfo? _activeImageHandle;
    private ShapeHandleInfo? _hoverImageHandle;
    private FloatingObject? _imageFloating;
    private ImageInline? _imageInline;
    private DocPoint _imageStartPoint;
    private DocPoint _imageStartCenter;
    private DocRect _imageStartBounds;
    private float _imageStartRotation;
    private float _imageStartOffsetX;
    private float _imageStartOffsetY;
    private float _imageStartPointerAngle;
    private float _imageStartAspectRatio;
    private EditorSessionSnapshot? _imageSnapshot;
    private CollabGestureToken? _imageGesture;
    private bool _imageDirty;
    private DocRect? _imagePreviewBounds;
    private bool _imageLayoutRefreshPending;
    private bool _isInlineObjectEditing;
    private InlineObjectDragMode _inlineObjectDragMode;
    private ShapeHandleInfo? _activeInlineHandle;
    private ShapeHandleInfo? _hoverInlineHandle;
    private InlineObjectSelectionInfo? _inlineSelection;
    private InlineObjectSelectionInfo? _inlineEditSelection;
    private DocPoint _inlineStartPoint;
    private DocPoint _inlineStartCenter;
    private DocRect _inlineStartBounds;
    private float _inlineStartRotation;
    private float _inlineStartPointerAngle;
    private float _inlineStartAspectRatio;
    private EditorSessionSnapshot? _inlineSnapshot;
    private CollabGestureToken? _inlineGesture;
    private bool _inlineDirty;
    private DocRect? _inlinePreviewBounds;
    private bool _inlineLayoutRefreshPending;
    private Point _inlineDragStartView;
    private bool _inlineDragActive;
    private TextRange _inlineDragRange;
    private bool _isPromotingInlineShape;
    private Size _pendingLayoutSize;
    private bool _acceptsReturn = true;
    private bool _acceptsTab;
    private bool _isReadOnly;
    private bool _isReadOnlyCaretVisible;

    public DocumentView()
    {
        Focusable = true;
        _renderOptions.ZoomFactor = _zoomFactor;
        _renderOptions.SvgRasterizationScale = _zoomFactor;
        _defaultCommentHighlightColor = _renderOptions.CommentHighlightColor;
        _kernel.AddModule(new BasicEditingModule());
        _kernel.Services.Register<IHeaderFooterEditService>(new HeaderFooterEditService(this));
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
    public LayoutSettings LayoutSettingsSnapshot => _editor.LayoutSettings.Clone();
    public TextRange Selection => _editor.Selection;
    public IReadOnlyList<TextRange> SelectionRanges => _editor.SelectionRanges;
    public TextPosition Caret => _editor.Caret;
    public float ZoomFactor => _zoomFactor;
    public DocumentZoomMode ZoomMode => _zoomMode;
    public Vector ScrollOffset => _scrollOffset;
    public Vector EffectiveScrollOffset => GetEffectiveScrollOffset();
    public int CurrentPageIndex => ResolveCurrentPageIndex();

    public EquationInline? SelectedEquation => _selectedEquation;
    public bool IsHeaderFooterEditing => _headerFooterSession is not null;
    public HeaderFooterEditMode HeaderFooterMode => _headerFooterSession?.Mode ?? HeaderFooterEditMode.None;
    public int HeaderFooterSectionIndex => ResolveHeaderFooterSectionIndex();
    public HeaderFooterVariant HeaderFooterVariant => _headerFooterSession?.Target.Variant ?? HeaderFooterVariant.Default;
    public bool HeaderFooterDifferentFirstPage => ResolveHeaderFooterDifferentFirstPage();

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

    public bool AcceptsTab
    {
        get => _acceptsTab;
        set => _acceptsTab = value;
    }

    public bool AcceptsReturn
    {
        get => _acceptsReturn;
        set => _acceptsReturn = value;
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value)
            {
                return;
            }

            _isReadOnly = value;
            if (_isReadOnly)
            {
                SetPictureCropMode(false);
                EndShapeTextEdit();
                EndHeaderFooterEdit();
                _isSelecting = false;
                UpdatePointerCursor(null);
            }

            InvalidateVisual();
        }
    }

    public bool IsReadOnlyCaretVisible
    {
        get => _isReadOnlyCaretVisible;
        set
        {
            if (_isReadOnlyCaretVisible == value)
            {
                return;
            }

            _isReadOnlyCaretVisible = value;
            InvalidateVisual();
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
        _cropGesture = null;
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
            var isInitialSize = _viewport.Width <= 0 || _viewport.Height <= 0;
            UpdateScrollMetrics();
            if (isInitialSize)
            {
                ApplyLayoutForSize(Bounds.Size);
            }
            else
            {
                ScheduleLayoutUpdate(Bounds.Size);
            }
        }
    }

    private void ScheduleLayoutUpdate(Size size)
    {
        _pendingLayoutSize = size;
        _hasPendingLayoutSize = true;
        if (_resizeLayoutTimer is null)
        {
            _resizeLayoutTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ResizeLayoutDebounceMs)
            };
            _resizeLayoutTimer.Tick += (_, _) => ApplyPendingLayout();
        }

        _resizeLayoutTimer.Stop();
        _resizeLayoutTimer.Start();
    }

    private void ApplyPendingLayout()
    {
        if (!_hasPendingLayoutSize)
        {
            _resizeLayoutTimer?.Stop();
            return;
        }

        _hasPendingLayoutSize = false;
        _resizeLayoutTimer?.Stop();
        ApplyLayoutForSize(_pendingLayoutSize);
    }

    private void ApplyLayoutForSize(Size size)
    {
        if (_isLoading)
        {
            return;
        }

        var width = MathF.Max(1f, (float)size.Width);
        var height = MathF.Max(1f, (float)size.Height);
        _editor.UpdateLayout(width, height);
        if (_zoomMode != DocumentZoomMode.Custom)
        {
            ApplyZoomMode(_zoomMode, preserveCenter: false);
            return;
        }

        UpdateScrollMetrics();
        UpdateHeaderFooterSessionLayout();
        UpdateShapeTextSessionLayout();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (HandleTextInputCore(e.Text.AsSpan()))
        {
            e.Handled = true;
        }
    }

    public bool HandleHostedTextInput(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return HandleTextInputCore(text.AsSpan());
    }

    private bool HandleTextInputCore(ReadOnlySpan<char> text)
    {
        if (_isLoading || text.IsEmpty || _isReadOnly)
        {
            return false;
        }

        if (_headerFooterSession is not null)
        {
            if (_headerFooterSession.InputAdapter.HandleTextInput(text, EditorModifiers.None))
            {
                MarkHeaderFooterDirty();
                return true;
            }

            return false;
        }

        if (_shapeTextSession is not null)
        {
            if (_shapeTextSession.InputAdapter.HandleTextInput(text, EditorModifiers.None))
            {
                MarkShapeTextDirty();
                return true;
            }

            return false;
        }

        return _inputAdapter.HandleTextInput(text, EditorModifiers.None);
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

        if (e.Key == Key.F9 && e.KeyModifiers == KeyModifiers.None)
        {
            TriggerFieldUpdate(FieldUpdateTrigger.Manual, recordHistory: true);
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

        if (_shapeTextSession is not null)
        {
            if (e.Key == Key.Escape)
            {
                EndShapeTextEdit();
                e.Handled = true;
                return;
            }

            var isEditKey = IsShapeTextEditKey(e);
            if (_shapeTextSession.InputAdapter.HandleKeyDown(e))
            {
                if (isEditKey)
                {
                    MarkShapeTextDirty();
                }

                e.Handled = true;
            }

            return;
        }

        if (!_isReadOnly && TryHandleFloatingObjectNudge(e))
        {
            e.Handled = true;
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

        if (_shapeTextSession is not null)
        {
            if (TryHandleShapeTextPointerPressed(e))
            {
                return;
            }

            EndShapeTextEdit();
        }

        if (_headerFooterSession is null && !_isReadOnly && TryHandleInkPointerPressed(e))
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
        else if (!_isReadOnly && TryBeginHeaderFooterEditFromPoint(e))
        {
            return;
        }

        if (!_isReadOnly
            && _headerFooterSession is null
            && _shapeTextSession is null
            && TryBeginShapeTextEditFromPoint(e))
        {
            return;
        }

        if (_headerFooterSession is null)
        {
            if (!_isReadOnly && (TryHandlePictureCropPointerPressed(e) || TryHandleTableResizePointerPressed(e)))
            {
                return;
            }

            if (!_isReadOnly
                && (TryHandleShapePointerPressed(e)
                    || TryHandleFloatingImagePointerPressed(e)
                    || TryHandleInlineObjectPointerPressed(e)))
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
        if (_isImageEditing)
        {
            HandleFloatingImagePointerMoved(e);
            return;
        }
        if (_isInlineObjectEditing)
        {
            HandleInlineObjectPointerMoved(e);
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
        if (_isShapeTextSelecting && _shapeTextSession is not null)
        {
            var offset = BuildShapeTextScrollOffset(_shapeTextSession.Metrics);
            var zoom = BuildShapeTextZoomFactor(_shapeTextSession.Metrics);
            if (_shapeTextSession.InputAdapter.HandlePointerMoved(e, offset, zoom, this))
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

        if (_isImageEditing)
        {
            EndFloatingImageEdit(e);
            return;
        }

        if (_isInlineObjectEditing)
        {
            EndInlineObjectEdit(e);
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

        if (_isShapeTextSelecting && _shapeTextSession is not null)
        {
            _isShapeTextSelecting = false;
            var offset = BuildShapeTextScrollOffset(_shapeTextSession.Metrics);
            var zoom = BuildShapeTextZoomFactor(_shapeTextSession.Metrics);
            _shapeTextSession.InputAdapter.HandlePointerReleased(e, offset, zoom, this);
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
        _tableResizeGesture = null;
        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            _tableResizeGesture = gestureRecorder.BeginGesture("table-resize");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
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

        if (_tableResizeDirty)
        {
            if (_tableResizeGesture.HasValue && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
            {
                gestureRecorder.EndGesture(_tableResizeGesture.Value);
            }
            else if (_tableResizeSnapshot.HasValue
                     && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
            {
                history.RecordSnapshot(_tableResizeSnapshot.Value);
            }
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
        _tableResizeGesture = null;
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
        _cropGesture = null;
        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            _cropGesture = gestureRecorder.BeginGesture("image-crop");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
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

        if (_cropDirty)
        {
            if (_cropGesture.HasValue && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
            {
                gestureRecorder.EndGesture(_cropGesture.Value);
            }
            else if (_cropSnapshot.HasValue
                     && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
            {
                history.RecordSnapshot(_cropSnapshot.Value);
            }
        }

        _cropSnapshot = null;
        _cropGesture = null;
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
        _shapeGesture = null;
        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            _shapeGesture = gestureRecorder.BeginGesture("shape-edit");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
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
                var newBounds = ComputeResizedBounds(
                    _shapeStartBounds,
                    _shapeStartCenter,
                    _shapeStartRotation,
                    _activeShapeHandle.Value.Kind,
                    docPoint,
                    _shapeStartAspectRatio,
                    keepAspect);
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

        if (_shapeDirty)
        {
            if (_shapeGesture.HasValue && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
            {
                gestureRecorder.EndGesture(_shapeGesture.Value);
            }
            else if (_shapeSnapshot.HasValue
                     && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
            {
                history.RecordSnapshot(_shapeSnapshot.Value);
            }
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
        _shapeGesture = null;
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

    private bool TryHandleFloatingImagePointerPressed(PointerPressedEventArgs e)
    {
        if (_isPictureCropMode)
        {
            return false;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        var docPoint = GetDocumentPoint(point.Position);
        if (TryGetFloatingImageHandleAtPoint(docPoint, out var handle))
        {
            if (!TryGetSelectedFloatingImageLayout(out var layoutObject, out var image))
            {
                return false;
            }

            var mode = handle.Kind == ShapeHandleKind.Rotate ? ShapeDragMode.Rotate : ShapeDragMode.Resize;
            BeginFloatingImageEdit(mode, handle, layoutObject, image, docPoint);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        if (TryGetFloatingImageAtPoint(docPoint, out var hitLayout, out var hitImage))
        {
            _editor.SetCaretFromPoint(docPoint.X, docPoint.Y, false);
            BeginFloatingImageEdit(ShapeDragMode.Move, null, hitLayout, hitImage, docPoint);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void BeginFloatingImageEdit(
        ShapeDragMode mode,
        ShapeHandleInfo? handle,
        FloatingLayoutObject layoutObject,
        ImageInline image,
        DocPoint startPoint)
    {
        _imageSnapshot = null;
        _imageGesture = null;
        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            _imageGesture = gestureRecorder.BeginGesture("image-edit");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _imageSnapshot = history.CaptureSnapshot();
        }

        _isImageEditing = true;
        _imageDragMode = mode;
        _imageFloating = layoutObject.Object;
        _imageInline = image;
        _imageStartPoint = startPoint;
        _imageStartBounds = layoutObject.Bounds;
        _imageStartCenter = new DocPoint(
            _imageStartBounds.X + _imageStartBounds.Width * 0.5f,
            _imageStartBounds.Y + _imageStartBounds.Height * 0.5f);
        _imageStartRotation = image.Rotation;
        _imageStartOffsetX = layoutObject.Object.Anchor.OffsetX;
        _imageStartOffsetY = layoutObject.Object.Anchor.OffsetY;
        _imageStartPointerAngle = GetAngleDegrees(_imageStartCenter, startPoint);
        _imageStartAspectRatio = image.Height > 0f ? image.Width / image.Height : 1f;
        _imageDirty = false;
        _activeImageHandle = handle;
        _hoverImageHandle = handle;
        _isSelecting = false;
        _imagePreviewBounds = null;

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

    private void HandleFloatingImagePointerMoved(PointerEventArgs e)
    {
        if (!_isImageEditing || _imageFloating is null || _imageInline is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var docPoint = GetDocumentPoint(point.Position);
        var modifiers = e.KeyModifiers;
        switch (_imageDragMode)
        {
            case ShapeDragMode.Move:
            {
                var deltaX = docPoint.X - _imageStartPoint.X;
                var deltaY = docPoint.Y - _imageStartPoint.Y;
                _imageFloating.Anchor.OffsetX = _imageStartOffsetX + deltaX;
                _imageFloating.Anchor.OffsetY = _imageStartOffsetY + deltaY;
                _imagePreviewBounds = new DocRect(
                    _imageStartBounds.X + deltaX,
                    _imageStartBounds.Y + deltaY,
                    _imageStartBounds.Width,
                    _imageStartBounds.Height);
                _imageDirty = true;
                RequestImageLayoutRefresh();
                UpdateDirtyPages(GetAllPages());
                InvalidateVisual();
                break;
            }
            case ShapeDragMode.Resize:
            {
                if (!_activeImageHandle.HasValue)
                {
                    return;
                }

                var keepAspect = modifiers.HasFlag(KeyModifiers.Shift);
                var newBounds = ComputeResizedBounds(
                    _imageStartBounds,
                    _imageStartCenter,
                    _imageStartRotation,
                    _activeImageHandle.Value.Kind,
                    docPoint,
                    _imageStartAspectRatio,
                    keepAspect);
                _imageInline.Width = newBounds.Width;
                _imageInline.Height = newBounds.Height;
                _imageFloating.Anchor.OffsetX = _imageStartOffsetX + (newBounds.X - _imageStartBounds.X);
                _imageFloating.Anchor.OffsetY = _imageStartOffsetY + (newBounds.Y - _imageStartBounds.Y);
                _imagePreviewBounds = newBounds;
                _imageDirty = true;
                RequestImageLayoutRefresh();
                UpdateDirtyPages(GetAllPages());
                InvalidateVisual();
                break;
            }
            case ShapeDragMode.Rotate:
            {
                var angle = GetAngleDegrees(_imageStartCenter, docPoint);
                var delta = NormalizeAngleDelta(angle - _imageStartPointerAngle);
                var rotation = NormalizeRotation(_imageStartRotation + delta);
                if (modifiers.HasFlag(KeyModifiers.Shift))
                {
                    rotation = SnapRotation(rotation, ShapeRotateSnapDegrees);
                }

                if (MathF.Abs(rotation - _imageInline.Rotation) < 0.01f)
                {
                    break;
                }

                _imageInline.Rotation = rotation;
                _imageDirty = true;
                RequestImageLayoutRefresh();
                UpdateDirtyPages(GetAllPages());
                InvalidateVisual();
                break;
            }
        }

        e.Handled = true;
    }

    private void EndFloatingImageEdit(PointerReleasedEventArgs e)
    {
        _isImageEditing = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (_imageDirty)
        {
            if (_imageGesture.HasValue && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
            {
                gestureRecorder.EndGesture(_imageGesture.Value);
            }
            else if (_imageSnapshot.HasValue
                     && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
            {
                history.RecordSnapshot(_imageSnapshot.Value);
            }
        }

        if (_imageDirty || _imageLayoutRefreshPending)
        {
            FlushImageLayoutRefresh();
        }

        ResetFloatingImageEditState();
        UpdatePointerCursor(null);
        InvalidateVisual();
    }

    private void ResetFloatingImageEditState()
    {
        _imageDragMode = ShapeDragMode.None;
        _activeImageHandle = null;
        _hoverImageHandle = null;
        _imageFloating = null;
        _imageInline = null;
        _imageStartPoint = default;
        _imageStartCenter = default;
        _imageStartBounds = default;
        _imageStartRotation = 0f;
        _imageStartOffsetX = 0f;
        _imageStartOffsetY = 0f;
        _imageStartPointerAngle = 0f;
        _imageStartAspectRatio = 0f;
        _imageSnapshot = null;
        _imageGesture = null;
        _imageDirty = false;
        _imagePreviewBounds = null;
        _imageLayoutRefreshPending = false;
    }

    private void RequestImageLayoutRefresh()
    {
        if (_imageLayoutRefreshPending)
        {
            return;
        }

        _imageLayoutRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_imageLayoutRefreshPending)
            {
                return;
            }

            _imageLayoutRefreshPending = false;
            _editor.RefreshLayout();
        }, DispatcherPriority.Render);
    }

    private void FlushImageLayoutRefresh()
    {
        _imageLayoutRefreshPending = false;
        _editor.RefreshLayout();
    }

    private bool TryHandleInlineObjectPointerPressed(PointerPressedEventArgs e)
    {
        if (_isPictureCropMode)
        {
            return false;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        var docPoint = GetDocumentPoint(point.Position);
        if (TryGetInlineObjectHandleAtPoint(docPoint, out var handle))
        {
            if (!TryResolveInlineSelection(out var selection))
            {
                return false;
            }

            var mode = handle.Kind == ShapeHandleKind.Rotate ? InlineObjectDragMode.Rotate : InlineObjectDragMode.Resize;
            BeginInlineObjectEdit(mode, handle, selection, docPoint, point.Position);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        if (TryGetInlineObjectAtPoint(docPoint, out var hit))
        {
            SelectInlineObject(hit);
            BeginInlineObjectMove(hit, point.Position);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void BeginInlineObjectEdit(
        InlineObjectDragMode mode,
        ShapeHandleInfo handle,
        InlineObjectSelectionInfo selection,
        DocPoint startPoint,
        Point startView)
    {
        _inlineSnapshot = null;
        _inlineGesture = null;
        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            _inlineGesture = gestureRecorder.BeginGesture("inline-object-edit");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _inlineSnapshot = history.CaptureSnapshot();
        }

        _isInlineObjectEditing = true;
        _inlineObjectDragMode = mode;
        _activeInlineHandle = handle;
        _hoverInlineHandle = handle;
        _inlineEditSelection = selection;
        _inlineStartPoint = startPoint;
        _inlineStartBounds = selection.Bounds;
        _inlineStartCenter = new DocPoint(
            _inlineStartBounds.X + _inlineStartBounds.Width * 0.5f,
            _inlineStartBounds.Y + _inlineStartBounds.Height * 0.5f);
        _inlineStartRotation = selection.Rotation;
        _inlineStartPointerAngle = GetAngleDegrees(_inlineStartCenter, startPoint);
        _inlineStartAspectRatio = selection.Bounds.Height > 0f ? selection.Bounds.Width / selection.Bounds.Height : 1f;
        _inlinePreviewBounds = null;
        _inlineDirty = false;
        _inlineLayoutRefreshPending = false;
        _inlineDragStartView = startView;
        _inlineDragActive = false;
        _inlineDragRange = default;

        switch (mode)
        {
            case InlineObjectDragMode.Resize:
                UpdatePointerCursor(GetShapeCursor(handle.Kind));
                break;
            case InlineObjectDragMode.Rotate:
                UpdatePointerCursor(ShapeRotateCursor);
                break;
            case InlineObjectDragMode.Move:
                UpdatePointerCursor(ShapeMoveCursor);
                break;
        }
    }

    private void BeginInlineObjectMove(InlineObjectHitInfo hit, Point startView)
    {
        _isInlineObjectEditing = true;
        _inlineObjectDragMode = InlineObjectDragMode.Move;
        _activeInlineHandle = null;
        _hoverInlineHandle = null;
        _inlineEditSelection = hit.Selection;
        _inlineStartPoint = default;
        _inlineStartCenter = default;
        _inlineStartBounds = default;
        _inlineStartRotation = 0f;
        _inlineStartPointerAngle = 0f;
        _inlineStartAspectRatio = 0f;
        _inlineSnapshot = null;
        _inlineGesture = null;
        _inlineDirty = false;
        _inlinePreviewBounds = null;
        _inlineLayoutRefreshPending = false;
        _inlineDragStartView = startView;
        _inlineDragActive = false;
        var start = new TextPosition(hit.Selection.ParagraphIndex, hit.Offset);
        var end = new TextPosition(hit.Selection.ParagraphIndex, hit.Offset + hit.Length);
        _inlineDragRange = new TextRange(start, end);
        UpdatePointerCursor(ShapeMoveCursor);
    }

    private void HandleInlineObjectPointerMoved(PointerEventArgs e)
    {
        if (!_isInlineObjectEditing || !_inlineEditSelection.HasValue)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var docPoint = GetDocumentPoint(point.Position);
        var selection = _inlineEditSelection.Value;
        switch (_inlineObjectDragMode)
        {
            case InlineObjectDragMode.Move:
            {
                var delta = point.Position - _inlineDragStartView;
                if (!_inlineDragActive)
                {
                    if (Math.Abs(delta.X) < InlineDragThreshold && Math.Abs(delta.Y) < InlineDragThreshold)
                    {
                        e.Handled = true;
                        return;
                    }

                    _inlineDragActive = true;
                    if (_inlineGesture is null && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
                    {
                        _inlineGesture = gestureRecorder.BeginGesture("inline-object-move");
                    }
                    else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
                    {
                        _inlineSnapshot = history.CaptureSnapshot();
                    }
                }

                _editor.SetCaretFromPoint(docPoint.X, docPoint.Y, false);
                InvalidateVisual();
                break;
            }
            case InlineObjectDragMode.Resize:
            {
                if (!_activeInlineHandle.HasValue)
                {
                    return;
                }

                var keepAspect = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                var newBounds = ComputeResizedBounds(
                    _inlineStartBounds,
                    _inlineStartCenter,
                    _inlineStartRotation,
                    _activeInlineHandle.Value.Kind,
                    docPoint,
                    _inlineStartAspectRatio,
                    keepAspect);
                if (TryApplyInlineObjectSize(selection.Inline, newBounds.Width, newBounds.Height))
                {
                    _inlinePreviewBounds = newBounds;
                    _inlineDirty = true;
                    RequestInlineLayoutRefresh();
                    UpdateDirtyPages(GetAllPages());
                    InvalidateVisual();
                }

                break;
            }
            case InlineObjectDragMode.Rotate:
            {
                var angle = GetAngleDegrees(_inlineStartCenter, docPoint);
                var deltaAngle = NormalizeAngleDelta(angle - _inlineStartPointerAngle);
                var rotation = NormalizeRotation(_inlineStartRotation + deltaAngle);
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    rotation = SnapRotation(rotation, ShapeRotateSnapDegrees);
                }

                var objectRotation = NormalizeRotation(rotation - selection.BaseRotation);
                if (selection.Inline is ShapeInline shape)
                {
                    if (MathF.Abs(objectRotation - shape.Properties.Rotation) < 0.01f)
                    {
                        break;
                    }

                    shape.Properties.Rotation = objectRotation;
                }
                else if (selection.Inline is ImageInline image)
                {
                    if (MathF.Abs(objectRotation - image.Rotation) < 0.01f)
                    {
                        break;
                    }

                    image.Rotation = objectRotation;
                }
                else
                {
                    break;
                }

                _inlineDirty = true;
                RequestInlineLayoutRefresh();
                UpdateDirtyPages(GetAllPages());
                InvalidateVisual();
                break;
            }
        }

        e.Handled = true;
    }

    private void EndInlineObjectEdit(PointerReleasedEventArgs e)
    {
        _isInlineObjectEditing = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (_inlineObjectDragMode == InlineObjectDragMode.Move)
        {
            if (_inlineDragActive)
            {
                CommitInlineObjectMove();
            }

            EndInlineObjectGesture();
            ResetInlineObjectEditState();
            UpdatePointerCursor(null);
            InvalidateVisual();
            return;
        }

        if (_inlineDirty)
        {
            if (!EndInlineObjectGesture() && _inlineSnapshot.HasValue
                && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
            {
                history.RecordSnapshot(_inlineSnapshot.Value);
            }
        }

        if (_inlineDirty || _inlineLayoutRefreshPending)
        {
            FlushInlineLayoutRefresh();
        }

        ResetInlineObjectEditState();
        UpdatePointerCursor(null);
        InvalidateVisual();
    }

    private void CommitInlineObjectMove()
    {
        if (!_inlineEditSelection.HasValue)
        {
            return;
        }

        var selection = _inlineEditSelection.Value;
        var range = _inlineDragRange.Normalize();
        if (range.IsEmpty)
        {
            return;
        }

        var dropPosition = _editor.Caret;
        if (dropPosition >= range.Start && dropPosition <= range.End)
        {
            SelectInlineObject(new InlineObjectHitInfo(selection, range.Start.Offset, range.End.Offset - range.Start.Offset));
            return;
        }

        var inline = selection.Inline;
        var inlineLength = Math.Max(1, DocumentEditHelpers.GetInlineLength(inline));
        var targetParagraphIndex = Math.Clamp(dropPosition.ParagraphIndex, 0, Math.Max(0, _editor.Document.ParagraphCount - 1));
        var targetOffset = Math.Max(0, dropPosition.Offset);
        if (targetParagraphIndex == range.Start.ParagraphIndex && targetOffset > range.Start.Offset)
        {
            targetOffset = Math.Max(range.Start.Offset, targetOffset - inlineLength);
        }

        var target = new TextPosition(targetParagraphIndex, targetOffset);
        _editor.SetSelection(range, SelectionUpdateMode.Replace);
        _editor.DeleteForward();
        _editor.SetSelection(new TextRange(target, target));
        _editor.InsertInline(inline);
        _editor.SetSelection(new TextRange(target, new TextPosition(targetParagraphIndex, targetOffset + inlineLength)));

        _inlineDirty = true;
        UpdateInlineObjectSelection();
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();

    }

    private void ResetInlineObjectEditState()
    {
        _inlineObjectDragMode = InlineObjectDragMode.None;
        _activeInlineHandle = null;
        _hoverInlineHandle = null;
        _inlineEditSelection = null;
        _inlineStartPoint = default;
        _inlineStartCenter = default;
        _inlineStartBounds = default;
        _inlineStartRotation = 0f;
        _inlineStartPointerAngle = 0f;
        _inlineStartAspectRatio = 0f;
        _inlineSnapshot = null;
        _inlineGesture = null;
        _inlineDirty = false;
        _inlinePreviewBounds = null;
        _inlineLayoutRefreshPending = false;
        _inlineDragStartView = default;
        _inlineDragActive = false;
        _inlineDragRange = default;
    }

    private bool EndInlineObjectGesture()
    {
        if (_inlineGesture.HasValue && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            gestureRecorder.EndGesture(_inlineGesture.Value);
            return true;
        }

        return false;
    }

    private void RequestInlineLayoutRefresh()
    {
        if (_inlineLayoutRefreshPending)
        {
            return;
        }

        _inlineLayoutRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_inlineLayoutRefreshPending)
            {
                return;
            }

            _inlineLayoutRefreshPending = false;
            _editor.RefreshLayout();
        }, DispatcherPriority.Render);
    }

    private void FlushInlineLayoutRefresh()
    {
        _inlineLayoutRefreshPending = false;
        _editor.RefreshLayout();
    }

    private bool TryHandleFloatingObjectNudge(KeyEventArgs e)
    {
        if (_isShapeEditing
            || _isImageEditing
            || _isInlineObjectEditing
            || _isDrawing
            || _isCropping
            || _isTableResizing)
        {
            return false;
        }

        if (!TryGetFloatingNudgeDelta(e, out var deltaX, out var deltaY))
        {
            return false;
        }

        return TryApplyFloatingNudge(deltaX, deltaY);
    }

    private static bool TryGetFloatingNudgeDelta(KeyEventArgs e, out float deltaX, out float deltaY)
    {
        deltaX = 0f;
        deltaY = 0f;

        var modifiers = e.KeyModifiers;
        if ((modifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
        {
            return false;
        }

        var step = modifiers.HasFlag(KeyModifiers.Shift) ? FloatingNudgeLargeStep : FloatingNudgeStep;
        switch (e.Key)
        {
            case Key.Left:
                deltaX = -step;
                return true;
            case Key.Right:
                deltaX = step;
                return true;
            case Key.Up:
                deltaY = -step;
                return true;
            case Key.Down:
                deltaY = step;
                return true;
            default:
                return false;
        }
    }

    private bool TryApplyFloatingNudge(float deltaX, float deltaY)
    {
        var selectedIds = _editor.SelectedFloatingObjectIds;
        if (selectedIds.Count == 0)
        {
            if (!_editor.SelectedFloatingObjectId.HasValue)
            {
                return false;
            }

            selectedIds = new[] { _editor.SelectedFloatingObjectId.Value };
        }

        var selectedSet = new HashSet<Guid>(selectedIds);
        if (selectedSet.Count == 0)
        {
            return false;
        }

        var targets = new List<FloatingObject>();
        for (var paragraphIndex = 0; paragraphIndex < _editor.Document.ParagraphCount; paragraphIndex++)
        {
            var paragraph = _editor.Document.GetParagraph(paragraphIndex);
            var floatingObjects = paragraph.FloatingObjects;
            for (var i = 0; i < floatingObjects.Count; i++)
            {
                var floating = floatingObjects[i];
                if (selectedSet.Contains(floating.Id))
                {
                    targets.Add(floating);
                }
            }
        }

        if (targets.Count == 0)
        {
            return false;
        }

        ICollabGestureRecorder? gestureRecorder = null;
        CollabGestureToken? gesture = null;
        IEditorHistorySnapshotService? history = null;
        EditorSessionSnapshot? snapshot = null;

        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out gestureRecorder))
        {
            gesture = gestureRecorder.BeginGesture("floating-object-nudge");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out history))
        {
            snapshot = history.CaptureSnapshot();
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var anchor = targets[i].Anchor;
            anchor.OffsetX += deltaX;
            anchor.OffsetY += deltaY;
        }

        _editor.RefreshLayout();
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();

        if (gesture.HasValue && gestureRecorder is not null)
        {
            gestureRecorder.EndGesture(gesture.Value);
        }
        else if (snapshot.HasValue && history is not null)
        {
            history.RecordSnapshot(snapshot.Value);
        }

        return true;
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
        float aspectRatio,
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

        if (keepAspect && IsCornerHandle(kind) && aspectRatio > 0f)
        {
            if (newHalfWidth / newHalfHeight > aspectRatio)
            {
                newHalfWidth = newHalfHeight * aspectRatio;
            }
            else
            {
                newHalfHeight = newHalfWidth / aspectRatio;
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
        if (_headerFooterSession is not null || _shapeTextSession is not null)
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
            _hoverImageHandle = null;
            _hoverInlineHandle = null;
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

            if (_hoverInlineHandle.HasValue)
            {
                _hoverInlineHandle = null;
                InvalidateVisual();
            }

            if (_hoverImageHandle.HasValue)
            {
                _hoverImageHandle = null;
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

        if (!_isPictureCropMode && TryGetFloatingImageHandleAtPoint(docPoint, out var imageHandle))
        {
            if (!_hoverImageHandle.HasValue || _hoverImageHandle.Value.Kind != imageHandle.Kind)
            {
                _hoverImageHandle = imageHandle;
                InvalidateVisual();
            }

            if (_hoverInlineHandle.HasValue)
            {
                _hoverInlineHandle = null;
                InvalidateVisual();
            }

            _hoverTableResizeHandle = null;
            UpdatePointerCursor(GetShapeCursor(imageHandle.Kind));
            return;
        }

        if (_hoverImageHandle.HasValue)
        {
            _hoverImageHandle = null;
            InvalidateVisual();
        }

        if (!_isPictureCropMode && TryGetInlineObjectHandleAtPoint(docPoint, out var inlineHandle))
        {
            if (!_hoverInlineHandle.HasValue || _hoverInlineHandle.Value.Kind != inlineHandle.Kind)
            {
                _hoverInlineHandle = inlineHandle;
                InvalidateVisual();
            }

            if (_hoverImageHandle.HasValue)
            {
                _hoverImageHandle = null;
                InvalidateVisual();
            }

            _hoverTableResizeHandle = null;
            UpdatePointerCursor(GetShapeCursor(inlineHandle.Kind));
            return;
        }

        if (_hoverInlineHandle.HasValue)
        {
            _hoverInlineHandle = null;
            InvalidateVisual();
        }

        if (!_isPictureCropMode && TryGetSelectedShapeBounds(out var shapeBounds) && shapeBounds.Contains(docPoint.X, docPoint.Y))
        {
            UpdatePointerCursor(ShapeMoveCursor);
            return;
        }

        if (!_isPictureCropMode && TryGetSelectedFloatingImageBounds(out var imageBounds) && imageBounds.Contains(docPoint.X, docPoint.Y))
        {
            UpdatePointerCursor(ShapeMoveCursor);
            return;
        }

        if (!_isPictureCropMode && TryGetSelectedInlineObjectBounds(out var inlineBounds) && inlineBounds.Contains(docPoint.X, docPoint.Y))
        {
            UpdatePointerCursor(ShapeMoveCursor);
            return;
        }

        if (!_isPictureCropMode && TryGetFloatingShapeAtPoint(docPoint, out _, out _, out _))
        {
            UpdatePointerCursor(ShapeMoveCursor);
            return;
        }

        if (!_isPictureCropMode && TryGetFloatingImageAtPoint(docPoint, out _, out _))
        {
            UpdatePointerCursor(ShapeMoveCursor);
            return;
        }

        if (!_isPictureCropMode && TryGetInlineObjectAtPoint(docPoint, out _))
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

        if (IsOverDocumentPage(docPoint))
        {
            UpdatePointerCursor(TextCursor);
            return;
        }

        UpdatePointerCursor(null);
    }

    private bool IsOverDocumentPage(DocPoint point)
    {
        var pages = _editor.Layout.Pages;
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i].Bounds.Contains(point.X, point.Y))
            {
                return true;
            }
        }

        return false;
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

    private void TriggerFieldUpdate(FieldUpdateTrigger trigger, bool recordHistory)
    {
        if (!TryGetService<IEditorCommandRouter>(out var router))
        {
            return;
        }

        var commandId = trigger switch
        {
            FieldUpdateTrigger.Open => EditorReferencesCommandIds.Fields.UpdateAll,
            FieldUpdateTrigger.Print => EditorReferencesCommandIds.Fields.UpdateAll,
            FieldUpdateTrigger.Manual => EditorReferencesCommandIds.Fields.UpdateCurrent,
            _ => EditorReferencesCommandIds.Fields.UpdateAll
        };

        if (!router.CanExecute(commandId))
        {
            return;
        }

        _ = router.ExecuteAsync(commandId, payload: null, context: null, recordHistory: recordHistory).GetAwaiter().GetResult();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var effectiveOffset = GetEffectiveScrollOffset();
        UpdateHeaderFooterRenderOptions();
        UpdateShapeTextRenderOptions();
        context.Custom(
            new SkiaDrawOperation(
                Bounds,
                _editor,
                _renderer,
                _renderOptions,
                effectiveOffset,
                _isReadOnly,
                _isReadOnlyCaretVisible));
        DrawTableSelectionOverlay(context, effectiveOffset);
        DrawTableResizeHandles(context, effectiveOffset);
        DrawPictureCropOverlay(context, effectiveOffset);
        DrawShapeSelectionOverlay(context, effectiveOffset);
        DrawFloatingImageSelectionOverlay(context, effectiveOffset);
        DrawInlineObjectSelectionOverlay(context, effectiveOffset);
        DrawCollabPresenceOverlay(context, effectiveOffset);
        DrawCommentBalloons(context, effectiveOffset);
        DrawRevisionBalloons(context, effectiveOffset);
        DrawInkPreview(context, effectiveOffset);
        DrawInkReplayOverlay(context, effectiveOffset);
    }

    public void LoadDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EndShapeTextEdit();
        EndHeaderFooterEdit();
        StopInkReplay();
        _editor.Changed -= OnEditorChanged;
        _editor = CreateEditor(document);
        _editor.UpdateLayout((float)Bounds.Width, (float)Bounds.Height);
        RecreateCollaborationCoordinatorIfNeeded();
        ApplyEditorState();
        TriggerFieldUpdate(FieldUpdateTrigger.Open, recordHistory: false);
    }

    public async Task LoadDocumentAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EndShapeTextEdit();
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
        RecreateCollaborationCoordinatorIfNeeded();
        ApplyEditorState();
        TriggerFieldUpdate(FieldUpdateTrigger.Open, recordHistory: false);
    }

    public void UpdateFieldsForPrint()
    {
        TriggerFieldUpdate(FieldUpdateTrigger.Print, recordHistory: false);
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

    public void SelectRange(TextRange range, bool ensureVisible = true)
    {
        _editor.SetSelection(range, SelectionUpdateMode.Replace);
        if (ensureVisible)
        {
            EnsurePositionVisible(range.Start);
        }
    }

    public void SetCaretFromViewPoint(Point point, bool extendSelection = false)
    {
        var docPoint = GetDocumentPoint(point);
        _editor.SetCaretFromPoint(docPoint.X, docPoint.Y, extendSelection);
    }

    public bool IsTextHitAtViewPoint(Point point)
    {
        var docPoint = GetDocumentPoint(point);
        var layout = _editor.Layout;
        var lines = layout.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.ParagraphIndex < 0)
            {
                continue;
            }

            var width = MathF.Max(1f, line.Width);
            var height = MathF.Max(1f, line.LineHeight);
            var left = line.X;
            var top = line.Y;
            var right = left + width;
            var bottom = top + height;
            if (docPoint.X >= left && docPoint.X <= right && docPoint.Y >= top && docPoint.Y <= bottom)
            {
                return true;
            }
        }

        return false;
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
        if (_editor.Document.Comments.Count == 0)
        {
            return;
        }

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
        if (_editor.Document.Revisions.Timeline.Count == 0 && !_editor.Document.TrackChangesEnabled)
        {
            return;
        }

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

            var text = ReviewingHelpers.BuildCommentDisplayText(_editor.Document, comment);
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
        var selections = _editor.TableSelections;
        if (selections.Count == 0)
        {
            return;
        }

        for (var selectionIndex = 0; selectionIndex < selections.Count; selectionIndex++)
        {
            var range = selections[selectionIndex].Normalize();
            if (!TryGetTableLayouts(range.Table, out var layouts))
            {
                continue;
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

    private void DrawFloatingImageSelectionOverlay(DrawingContext context, Vector effectiveOffset)
    {
        if (_headerFooterSession is not null || _isPictureCropMode)
        {
            return;
        }

        if (!TryBuildFloatingImageHandles(out var handles, out var geometry))
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

        var active = _activeImageHandle;
        var hover = _hoverImageHandle;
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

    private void DrawInlineObjectSelectionOverlay(DrawingContext context, Vector effectiveOffset)
    {
        if (_headerFooterSession is not null || _isPictureCropMode)
        {
            return;
        }

        if (!TryBuildInlineObjectHandles(out var handles, out var geometry, out var showRotate))
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

        var active = _activeInlineHandle;
        var hover = _hoverInlineHandle;
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

        if (showRotate && (geometry.RotateHandle.X != 0f || geometry.RotateHandle.Y != 0f))
        {
            var start = DocToView(geometry.TopCenter, effectiveOffset);
            var end = DocToView(geometry.RotateHandle, effectiveOffset);
            context.DrawLine(ShapeSelectionBorderPen, start, end);
        }
    }

    private void DrawCollabPresenceOverlay(DrawingContext context, Vector effectiveOffset)
    {
        if (!_kernel.Services.TryGet<ICollabUiService>(out var collabService))
        {
            return;
        }

        if (!_kernel.Services.TryGet<ICollabIdentityService>(out var identityService))
        {
            return;
        }

        var presence = collabService.Presence;
        if (presence.Count == 0 || _editor.Layout.Lines.Count == 0)
        {
            return;
        }

        var localUserId = identityService.UserId;
        for (var i = 0; i < presence.Count; i++)
        {
            var state = presence[i];
            if (state.UserId == localUserId)
            {
                continue;
            }

            var color = CollabColorPalette.ResolveColor(state.Color, state.UserId);
            var caretBrush = ResolvePresenceCaretBrush(state.UserId, color);
            var selectionBrush = ResolvePresenceSelectionBrush(state.UserId, color);
            var outlinePen = ResolvePresenceOutlinePen(state.UserId, color);

            if (state.SelectionRanges is { Count: > 0 })
            {
                for (var selectionIndex = 0; selectionIndex < state.SelectionRanges.Count; selectionIndex++)
                {
                    var anchorRange = state.SelectionRanges[selectionIndex];
                    if (anchorRange.IsEmpty)
                    {
                        continue;
                    }

                    if (!TryResolveSelection(anchorRange, out var range))
                    {
                        continue;
                    }

                    DrawPresenceSelection(context, range, selectionBrush, effectiveOffset);
                    if (TryBuildInlineSelectionInfo(range, out var inlineSelection))
                    {
                        DrawPresenceInlineSelection(context, inlineSelection, outlinePen, effectiveOffset);
                    }
                }
            }
            else if (state.Selection.HasValue && !state.Selection.Value.IsEmpty)
            {
                if (TryResolveSelection(state.Selection.Value, out var range))
                {
                    DrawPresenceSelection(context, range, selectionBrush, effectiveOffset);
                    if (TryBuildInlineSelectionInfo(range, out var inlineSelection))
                    {
                        DrawPresenceInlineSelection(context, inlineSelection, outlinePen, effectiveOffset);
                    }
                }
            }

            if (state.TableSelections is { Count: > 0 })
            {
                for (var selectionIndex = 0; selectionIndex < state.TableSelections.Count; selectionIndex++)
                {
                    var tableRange = state.TableSelections[selectionIndex].Normalize();
                    if (!TryResolveTableSelection(tableRange, out var resolved))
                    {
                        continue;
                    }

                    DrawPresenceTableSelection(context, resolved, selectionBrush, outlinePen, effectiveOffset);
                }
            }

            if (state.FloatingSelections is { Count: > 0 })
            {
                for (var selectionIndex = 0; selectionIndex < state.FloatingSelections.Count; selectionIndex++)
                {
                    var floatingId = state.FloatingSelections[selectionIndex];
                    if (!TryResolveFloatingSelection(floatingId, out var bounds, out var rotation))
                    {
                        continue;
                    }

                    DrawPresenceOutline(context, bounds, rotation, outlinePen, effectiveOffset);
                }
            }

            if (state.Caret.HasValue && TryResolveAnchor(state.Caret.Value, out var caretPosition))
            {
                DrawPresenceCaret(context, caretPosition, state.DisplayName, caretBrush, effectiveOffset);
            }
        }
    }

    private void UpdateLocalPresence(bool force = false)
    {
        if (!_kernel.Services.TryGet<ICollabUiService>(out var collabService))
        {
            return;
        }

        if (collabService.ConnectionState is CollabConnectionState.Disconnected
            or CollabConnectionState.Offline
            or CollabConnectionState.Error)
        {
            return;
        }

        if (!_kernel.Services.TryGet<ICollabIdentityService>(out var identityService))
        {
            return;
        }

        if (!TryBuildPresence(identityService, out var presence, out var signature))
        {
            return;
        }

        if (!force && _lastPresenceSignature.HasValue && _lastPresenceSignature.Value.Equals(signature))
        {
            return;
        }

        _lastPresenceSignature = signature;
        collabService.UpdatePresence(presence, PresenceTimeToLive);
    }

    private bool TryBuildPresence(
        ICollabIdentityService identityService,
        out PresenceState presence,
        out PresenceSignature signature)
    {
        presence = default!;
        signature = default;

        var caretPosition = _editor.Caret;
        if (!TryCreateAnchor(caretPosition, out var caretAnchor))
        {
            return false;
        }

        AnchorRange? selection = null;
        var normalizedSelection = _editor.Selection.Normalize();
        if (!normalizedSelection.IsEmpty)
        {
            if (!TryCreateAnchor(normalizedSelection.Start, out var selectionStart)
                || !TryCreateAnchor(normalizedSelection.End, out var selectionEnd))
            {
                return false;
            }

            selection = new AnchorRange(selectionStart, selectionEnd);
        }

        List<AnchorRange>? selectionRanges = null;
        var ranges = _editor.SelectionRanges;
        if (ranges.Count > 1)
        {
            selectionRanges = new List<AnchorRange>(ranges.Count);
            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i].Normalize();
                if (range.IsEmpty)
                {
                    continue;
                }

                if (!TryCreateAnchor(range.Start, out var rangeStart)
                    || !TryCreateAnchor(range.End, out var rangeEnd))
                {
                    return false;
                }

                selectionRanges.Add(new AnchorRange(rangeStart, rangeEnd));
            }

            if (selectionRanges.Count == 0)
            {
                selectionRanges = null;
            }
        }

        List<TablePresenceRange>? tableSelections = null;
        var tableRanges = _editor.TableSelections;
        if (tableRanges.Count > 0)
        {
            tableSelections = new List<TablePresenceRange>(tableRanges.Count);
            for (var i = 0; i < tableRanges.Count; i++)
            {
                var range = tableRanges[i].Normalize();
                var tableId = range.Table.NodeId;
                if (tableId == Guid.Empty)
                {
                    continue;
                }

                tableSelections.Add(new TablePresenceRange(
                    tableId,
                    range.RowStart,
                    range.RowEnd,
                    range.ColumnStart,
                    range.ColumnEnd));
            }

            if (tableSelections.Count == 0)
            {
                tableSelections = null;
            }
        }

        List<Guid>? floatingSelections = null;
        var floatingIds = _editor.SelectedFloatingObjectIds;
        if (floatingIds.Count > 0)
        {
            floatingSelections = new List<Guid>(floatingIds.Count);
            for (var i = 0; i < floatingIds.Count; i++)
            {
                floatingSelections.Add(floatingIds[i]);
            }
        }

        presence = new PresenceState(
            identityService.UserId,
            identityService.DisplayName,
            caretAnchor,
            selection,
            DateTimeOffset.UtcNow,
            identityService.Color,
            selectionRanges,
            tableSelections,
            floatingSelections);

        signature = new PresenceSignature(
            caretAnchor,
            selection,
            ComputeAnchorRangeSignature(selectionRanges),
            ComputeTableSelectionSignature(tableSelections),
            ComputeGuidSignature(floatingSelections));
        return true;
    }

    private static ListSignature ComputeAnchorRangeSignature(IReadOnlyList<AnchorRange>? ranges)
    {
        if (ranges is null || ranges.Count == 0)
        {
            return default;
        }

        var hash = new HashCode();
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            hash.Add(range.Start.NodeId);
            hash.Add(range.Start.Offset);
            hash.Add((int)range.Start.Bias);
            hash.Add(range.End.NodeId);
            hash.Add(range.End.Offset);
            hash.Add((int)range.End.Bias);
        }

        return new ListSignature(ranges.Count, hash.ToHashCode());
    }

    private static ListSignature ComputeTableSelectionSignature(IReadOnlyList<TablePresenceRange>? ranges)
    {
        if (ranges is null || ranges.Count == 0)
        {
            return default;
        }

        var hash = new HashCode();
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            hash.Add(range.TableId);
            hash.Add(range.RowStart);
            hash.Add(range.RowEnd);
            hash.Add(range.ColumnStart);
            hash.Add(range.ColumnEnd);
        }

        return new ListSignature(ranges.Count, hash.ToHashCode());
    }

    private static ListSignature ComputeGuidSignature(IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return default;
        }

        var hash = new HashCode();
        for (var i = 0; i < ids.Count; i++)
        {
            hash.Add(ids[i]);
        }

        return new ListSignature(ids.Count, hash.ToHashCode());
    }

    private bool TryCreateAnchor(TextPosition position, out TextAnchor anchor)
    {
        anchor = default;
        try
        {
            var paragraph = _editor.Document.GetParagraph(position.ParagraphIndex);
            var length = DocumentEditHelpers.GetParagraphLength(paragraph);
            var offset = Math.Clamp(position.Offset, 0, length);
            anchor = TextAnchor.Before(paragraph.NodeId, offset);
            return paragraph.NodeId != Guid.Empty;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private bool TryResolveSelection(AnchorRange range, out TextRange selection)
    {
        selection = default;
        if (!TryResolveAnchor(range.Start, out var start))
        {
            return false;
        }

        if (!TryResolveAnchor(range.End, out var end))
        {
            return false;
        }

        selection = new TextRange(start, end);
        return true;
    }

    private bool TryResolveAnchor(TextAnchor anchor, out TextPosition position)
    {
        position = default;
        if (!_presenceAnchorResolver.TryResolveParagraph(_editor.Document, anchor.NodeId, out var paragraph, out var paragraphIndex))
        {
            return false;
        }

        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
        var offset = Math.Clamp(anchor.Offset, 0, length);
        position = new TextPosition(paragraphIndex, offset);
        return true;
    }

    private void DrawPresenceSelection(DrawingContext context, TextRange selection, IBrush brush, Vector effectiveOffset)
    {
        var normalized = selection.Normalize();
        var lines = _editor.Layout.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.ParagraphIndex < normalized.Start.ParagraphIndex || line.ParagraphIndex > normalized.End.ParagraphIndex)
            {
                continue;
            }

            var lineStart = line.StartOffset;
            var lineEnd = line.StartOffset + line.Length;
            var startOffset = line.ParagraphIndex == normalized.Start.ParagraphIndex
                ? Math.Max(lineStart, normalized.Start.Offset)
                : lineStart;
            var endOffset = line.ParagraphIndex == normalized.End.ParagraphIndex
                ? Math.Min(lineEnd, normalized.End.Offset)
                : lineEnd;

            if (endOffset < startOffset)
            {
                continue;
            }

            var startPosition = new TextPosition(line.ParagraphIndex, startOffset);
            var endPosition = new TextPosition(line.ParagraphIndex, endOffset);
            if (!_editor.TryGetCaretPoint(startPosition, out var startPoint, out _))
            {
                continue;
            }

            if (!_editor.TryGetCaretPoint(endPosition, out var endPoint, out _))
            {
                continue;
            }

            var x1 = startPoint.X;
            var x2 = endPoint.X;
            if (MathF.Abs(x2 - x1) < 0.5f)
            {
                x2 = x1 + PresenceCaretThickness;
            }

            var rect = new DocRect(MathF.Min(x1, x2), line.Y, MathF.Abs(x2 - x1), line.LineHeight);
            var viewRect = DocRectToViewRect(rect, effectiveOffset);
            context.FillRectangle(brush, viewRect);
        }
    }

    private bool TryBuildInlineSelectionInfo(TextRange range, out InlineObjectSelectionInfo selection)
    {
        selection = default;
        var normalized = range.Normalize();
        if (normalized.IsEmpty || normalized.Start.ParagraphIndex != normalized.End.ParagraphIndex)
        {
            return false;
        }

        var paragraphIndex = normalized.Start.ParagraphIndex;
        if (paragraphIndex < 0 || paragraphIndex >= _editor.Document.ParagraphCount)
        {
            return false;
        }

        var paragraph = _editor.Document.GetParagraph(paragraphIndex);
        if (!TryGetInlineAtOffset(paragraph, normalized.Start.Offset, out var inline, out var inlineIndex, out var inlineStart, out var inlineLength))
        {
            return false;
        }

        if (inlineStart != normalized.Start.Offset || inlineLength != normalized.End.Offset - normalized.Start.Offset)
        {
            return false;
        }

        return TryBuildInlineSelectionInfo(inline, paragraphIndex, inlineIndex, out selection);
    }

    private void DrawPresenceInlineSelection(DrawingContext context, InlineObjectSelectionInfo selection, Pen pen, Vector effectiveOffset)
    {
        DrawPresenceOutline(context, selection.Bounds, selection.Rotation, pen, effectiveOffset);
    }

    private bool TryResolveTableSelection(TablePresenceRange range, out TableSelectionRange selection)
    {
        selection = default;
        if (!TryGetTableById(range.TableId, out var table))
        {
            return false;
        }

        selection = new TableSelectionRange(table, range.RowStart, range.RowEnd, range.ColumnStart, range.ColumnEnd).Normalize();
        return true;
    }

    private void DrawPresenceTableSelection(
        DrawingContext context,
        TableSelectionRange range,
        IBrush fillBrush,
        Pen outlinePen,
        Vector effectiveOffset)
    {
        if (!TryGetTableLayouts(range.Table, out var layouts))
        {
            return;
        }

        for (var layoutIndex = 0; layoutIndex < layouts.Count; layoutIndex++)
        {
            var layout = layouts[layoutIndex];
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
                context.FillRectangle(fillBrush, rect);
                context.DrawRectangle(outlinePen, rect);
            }
        }
    }

    private bool TryResolveFloatingSelection(Guid floatingId, out DocRect bounds, out float rotation)
    {
        bounds = default;
        rotation = 0f;
        if (!TryGetFloatingLayout(floatingId, out var layoutObject))
        {
            return false;
        }

        bounds = layoutObject.Bounds;
        switch (layoutObject.Object.Content)
        {
            case ShapeInline shape:
                rotation = shape.Properties.Rotation;
                break;
            case ImageInline image:
                rotation = image.Rotation;
                break;
        }

        return true;
    }

    private void DrawPresenceOutline(DrawingContext context, DocRect bounds, float rotation, Pen pen, Vector effectiveOffset)
    {
        if (MathF.Abs(rotation) < 0.01f)
        {
            var rect = DocRectToViewRect(bounds, effectiveOffset);
            context.DrawRectangle(pen, rect);
            return;
        }

        var geometry = BuildShapeSelectionGeometry(bounds, rotation, 0f);
        var topLeft = DocToView(geometry.TopLeft, effectiveOffset);
        var topRight = DocToView(geometry.TopRight, effectiveOffset);
        var bottomRight = DocToView(geometry.BottomRight, effectiveOffset);
        var bottomLeft = DocToView(geometry.BottomLeft, effectiveOffset);
        context.DrawLine(pen, topLeft, topRight);
        context.DrawLine(pen, topRight, bottomRight);
        context.DrawLine(pen, bottomRight, bottomLeft);
        context.DrawLine(pen, bottomLeft, topLeft);
    }

    private bool TryGetTableById(Guid tableId, out TableBlock table)
    {
        table = null!;
        if (tableId == Guid.Empty)
        {
            return false;
        }

        return TryGetTableById(_editor.Document.Blocks, tableId, out table);
    }

    private static bool TryGetTableById(IReadOnlyList<Block> blocks, Guid tableId, out TableBlock table)
    {
        table = null!;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not TableBlock current)
            {
                continue;
            }

            if (current.NodeId == tableId)
            {
                table = current;
                return true;
            }

            for (var rowIndex = 0; rowIndex < current.Rows.Count; rowIndex++)
            {
                var row = current.Rows[rowIndex];
                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    if (TryGetTableById(row.Cells[cellIndex].Blocks, tableId, out table))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool TryGetFloatingLayout(Guid floatingId, out FloatingLayoutObject layoutObject)
    {
        layoutObject = null!;
        var floats = _editor.Layout.FloatingObjects;
        if (floats.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < floats.Count; i++)
        {
            var candidate = floats[i];
            if (candidate.Object.Id == floatingId)
            {
                layoutObject = candidate;
                return true;
            }
        }

        return false;
    }

    private void DrawPresenceCaret(DrawingContext context, TextPosition caret, string displayName, IBrush brush, Vector effectiveOffset)
    {
        if (!_editor.TryGetCaretPoint(caret, out var caretPoint, out var lineIndex))
        {
            return;
        }

        if (lineIndex < 0 || lineIndex >= _editor.Layout.Lines.Count)
        {
            return;
        }

        var line = _editor.Layout.Lines[lineIndex];
        var rect = new DocRect(caretPoint.X, line.Y, PresenceCaretThickness, line.LineHeight);
        var viewRect = DocRectToViewRect(rect, effectiveOffset);
        context.FillRectangle(brush, viewRect);

        DrawPresenceTag(context, displayName, caretPoint, line.LineHeight, brush, effectiveOffset);
    }

    private void DrawPresenceTag(DrawingContext context, string displayName, DocPoint caretPoint, float lineHeight, IBrush brush, Vector effectiveOffset)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var zoom = (float)_zoomFactor;
        var formatted = new FormattedText(
            displayName,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PresenceTagTypeface,
            PresenceTagFontSize * zoom,
            PresenceTagTextBrush);
        formatted.TextAlignment = TextAlignment.Left;

        var padding = PresenceTagPadding * zoom;
        var width = formatted.Width + (2f * padding);
        var height = formatted.Height + (2f * padding);
        var viewX = (caretPoint.X * zoom) - effectiveOffset.X;
        var viewY = (caretPoint.Y * zoom) - effectiveOffset.Y - height - (2f * zoom);
        var rect = new Rect(viewX, viewY, width, height);
        var corner = PresenceTagCornerRadius * zoom;
        context.DrawRectangle(brush, null, rect, corner, corner);
        context.DrawText(formatted, new Point(rect.X + padding, rect.Y + padding));
    }

    private IBrush ResolvePresenceCaretBrush(Guid userId, string color)
    {
        if (_presenceCaretBrushes.TryGetValue(userId, out var brush))
        {
            return brush;
        }

        var resolved = ResolvePresenceColor(color);
        brush = new SolidColorBrush(resolved);
        _presenceCaretBrushes[userId] = brush;
        return brush;
    }

    private IBrush ResolvePresenceSelectionBrush(Guid userId, string color)
    {
        if (_presenceSelectionBrushes.TryGetValue(userId, out var brush))
        {
            return brush;
        }

        var resolved = ResolvePresenceColor(color);
        var alpha = (byte)Math.Clamp(PresenceSelectionOpacity * 255f, 0f, 255f);
        var selectionColor = Color.FromArgb(alpha, resolved.R, resolved.G, resolved.B);
        brush = new SolidColorBrush(selectionColor);
        _presenceSelectionBrushes[userId] = brush;
        return brush;
    }

    private Pen ResolvePresenceOutlinePen(Guid userId, string color)
    {
        if (_presenceOutlinePens.TryGetValue(userId, out var pen))
        {
            return pen;
        }

        var resolved = ResolvePresenceColor(color);
        pen = new Pen(new SolidColorBrush(resolved), 1);
        _presenceOutlinePens[userId] = pen;
        return pen;
    }

    private static Color ResolvePresenceColor(string color)
    {
        try
        {
            return Color.Parse(color);
        }
        catch (Exception)
        {
            return Color.Parse("#2D7DF0");
        }
    }

    private bool TryBuildTableResizeHandles(out List<TableResizeHandle> handles)
    {
        handles = new List<TableResizeHandle>();
        if (!TryGetPrimaryTableSelectionRange(out var range))
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

    private bool TryBuildFloatingImageHandles(out List<ShapeHandleInfo> handles, out ShapeSelectionGeometry geometry)
    {
        handles = new List<ShapeHandleInfo>();
        geometry = default;
        if (!TryResolveFloatingImageSelection(out var selection))
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

    private bool TryBuildInlineObjectHandles(out List<ShapeHandleInfo> handles, out ShapeSelectionGeometry geometry, out bool showRotate)
    {
        handles = new List<ShapeHandleInfo>();
        geometry = default;
        showRotate = false;
        if (!TryResolveInlineSelection(out var selection))
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
        var rotateOffset = selection.SupportsRotate
            ? ShapeRotateHandleOffset / MathF.Max(0.1f, _zoomFactor)
            : 0f;
        geometry = BuildShapeSelectionGeometry(bounds, selection.Rotation, rotateOffset);

        handles.Add(new ShapeHandleInfo(ShapeHandleKind.TopLeft, new DocRect(geometry.TopLeft.X - half, geometry.TopLeft.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Top, new DocRect(geometry.TopCenter.X - half, geometry.TopCenter.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.TopRight, new DocRect(geometry.TopRight.X - half, geometry.TopRight.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Right, new DocRect(geometry.RightCenter.X - half, geometry.RightCenter.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.BottomRight, new DocRect(geometry.BottomRight.X - half, geometry.BottomRight.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Bottom, new DocRect(geometry.BottomCenter.X - half, geometry.BottomCenter.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.BottomLeft, new DocRect(geometry.BottomLeft.X - half, geometry.BottomLeft.Y - half, size, size)));
        handles.Add(new ShapeHandleInfo(ShapeHandleKind.Left, new DocRect(geometry.LeftCenter.X - half, geometry.LeftCenter.Y - half, size, size)));

        showRotate = selection.SupportsRotate;
        if (showRotate)
        {
            handles.Add(new ShapeHandleInfo(ShapeHandleKind.Rotate, new DocRect(geometry.RotateHandle.X - half, geometry.RotateHandle.Y - half, size, size)));
        }

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

    private bool TryGetFloatingImageHandleAtPoint(DocPoint point, out ShapeHandleInfo handle)
    {
        handle = default;
        if (!TryBuildFloatingImageHandles(out var handles, out _))
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

    private bool TryGetInlineObjectHandleAtPoint(DocPoint point, out ShapeHandleInfo handle)
    {
        handle = default;
        if (!TryBuildInlineObjectHandles(out var handles, out _, out _))
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

    private bool TryGetPrimaryTableSelectionRange(out TableSelectionRange range)
    {
        range = default;
        var selections = _editor.TableSelections;
        if (selections.Count == 0)
        {
            return false;
        }

        range = selections[0].Normalize();
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
        if (_editor.SelectedFloatingObjectIds.Count > 1)
        {
            return false;
        }

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

    private bool TryGetInlineObjectAtPoint(DocPoint point, out InlineObjectHitInfo hit)
    {
        hit = default;
        var layout = _editor.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        var lineIndex = layout.LineIndex.FindLineAtY(point.Y);
        if (lineIndex < 0 || lineIndex >= layout.Lines.Count)
        {
            return false;
        }

        var line = layout.Lines[lineIndex];
        var paragraphIndex = line.ParagraphIndex;
        if (paragraphIndex < 0 || paragraphIndex >= _editor.Document.ParagraphCount)
        {
            return false;
        }

        var paragraph = _editor.Document.GetParagraph(paragraphIndex);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);

        bool TryBuildHit(
            InlineObjectKind kind,
            Inline inline,
            float x,
            float width,
            float height,
            float rotation,
            bool supportsRotate,
            out InlineObjectHitInfo info)
        {
            info = default;
            if (!TryFindInlineIndex(paragraph, inline, out var inlineIndex, out var inlineStart, out var inlineLength))
            {
                return false;
            }

            var bounds = ComputeInlineObjectSelectionBounds(line, x, width, height, rotation, out var totalRotation);
            var hitBounds = MathF.Abs(totalRotation) < 0.01f ? bounds : ComputeRotatedBounds(bounds, totalRotation);
            if (!hitBounds.Contains(point.X, point.Y))
            {
                return false;
            }

            var baseRotation = totalRotation - rotation;
            var selection = new InlineObjectSelectionInfo(kind, inline, paragraphIndex, inlineIndex, bounds, totalRotation, baseRotation, supportsRotate, pageIndex);
            info = new InlineObjectHitInfo(selection, inlineStart, inlineLength);
            return true;
        }

        for (var i = line.Shapes.Count - 1; i >= 0; i--)
        {
            var layoutShape = line.Shapes[i];
            var shape = layoutShape.Shape;
            if (TryBuildHit(InlineObjectKind.Shape, shape, layoutShape.X, layoutShape.Width, layoutShape.Height, shape.Properties.Rotation, true, out hit))
            {
                return true;
            }
        }

        for (var i = line.Images.Count - 1; i >= 0; i--)
        {
            var layoutImage = line.Images[i];
            if (TryBuildHit(InlineObjectKind.Image, layoutImage.Image, layoutImage.X, layoutImage.Width, layoutImage.Height, layoutImage.Image.Rotation, true, out hit))
            {
                return true;
            }
        }

        for (var i = line.Charts.Count - 1; i >= 0; i--)
        {
            var layoutChart = line.Charts[i];
            if (TryBuildHit(InlineObjectKind.Chart, layoutChart.Chart, layoutChart.X, layoutChart.Width, layoutChart.Height, 0f, false, out hit))
            {
                return true;
            }
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

    private static bool TryGetInlineAtOffset(
        ParagraphBlock paragraph,
        int offset,
        out Inline inline,
        out int inlineIndex,
        out int inlineStart,
        out int inlineLength)
    {
        inline = null!;
        inlineIndex = -1;
        inlineStart = 0;
        inlineLength = 0;
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var position = 0;
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            var current = paragraph.Inlines[i];
            var length = DocumentEditHelpers.GetInlineLength(current);
            if (offset >= position && offset < position + length)
            {
                inline = current;
                inlineIndex = i;
                inlineStart = position;
                inlineLength = length;
                return true;
            }

            position += length;
        }

        return false;
    }

    private static bool TryFindInlineIndex(
        ParagraphBlock paragraph,
        Inline inline,
        out int inlineIndex,
        out int inlineStart,
        out int inlineLength)
    {
        inlineIndex = -1;
        inlineStart = 0;
        inlineLength = 0;
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var position = 0;
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            var current = paragraph.Inlines[i];
            var length = DocumentEditHelpers.GetInlineLength(current);
            if (ReferenceEquals(current, inline))
            {
                inlineIndex = i;
                inlineStart = position;
                inlineLength = length;
                return true;
            }

            position += length;
        }

        return false;
    }

    private static bool TryApplyInlineObjectSize(Inline inline, float width, float height)
    {
        var clampedWidth = MathF.Max(ShapeMinSize, width);
        var clampedHeight = MathF.Max(ShapeMinSize, height);
        switch (inline)
        {
            case ImageInline image:
                image.Width = clampedWidth;
                image.Height = clampedHeight;
                return true;
            case ShapeInline shape:
                shape.Width = clampedWidth;
                shape.Height = clampedHeight;
                return true;
            case ChartInline chart:
                chart.Width = clampedWidth;
                chart.Height = clampedHeight;
                return true;
            default:
                return false;
        }
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

    private bool TryGetInlineObjectLayout(
        Inline inline,
        int paragraphIndex,
        out DocRect bounds,
        out float rotation,
        out bool supportsRotate,
        out int pageIndex)
    {
        bounds = default;
        rotation = 0f;
        pageIndex = -1;
        supportsRotate = inline is ShapeInline or ImageInline;
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

            if (inline is ImageInline image)
            {
                foreach (var layoutImage in line.Images)
                {
                    if (!ReferenceEquals(layoutImage.Image, image))
                    {
                        continue;
                    }

                    bounds = ComputeInlineObjectSelectionBounds(line, layoutImage.X, layoutImage.Width, layoutImage.Height, image.Rotation, out rotation);
                    pageIndex = layout.LineIndex.GetPageForLine(i);
                    return true;
                }
            }
            else if (inline is ShapeInline shape)
            {
                foreach (var layoutShape in line.Shapes)
                {
                    if (!ReferenceEquals(layoutShape.Shape, shape))
                    {
                        continue;
                    }

                    bounds = ComputeInlineObjectSelectionBounds(line, layoutShape.X, layoutShape.Width, layoutShape.Height, shape.Properties.Rotation, out rotation);
                    pageIndex = layout.LineIndex.GetPageForLine(i);
                    return true;
                }
            }
            else if (inline is ChartInline chart)
            {
                foreach (var layoutChart in line.Charts)
                {
                    if (!ReferenceEquals(layoutChart.Chart, chart))
                    {
                        continue;
                    }

                    bounds = ComputeInlineObjectSelectionBounds(line, layoutChart.X, layoutChart.Width, layoutChart.Height, 0f, out rotation);
                    pageIndex = layout.LineIndex.GetPageForLine(i);
                    return true;
                }
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

    private static DocRect ComputeInlineObjectSelectionBounds(
        LayoutLine line,
        float x,
        float width,
        float height,
        float objectRotation,
        out float totalRotation)
    {
        width = MathF.Max(0f, width);
        height = MathF.Max(0f, height);
        if (!DocTextDirectionHelpers.IsVertical(line.TextDirection))
        {
            totalRotation = objectRotation;
            var baseline = line.Y + line.Ascent;
            return new DocRect(line.X + x, baseline - height, width, height);
        }

        var baseRotation = DocTextDirectionHelpers.GetRotationDegrees(line.TextDirection!.Value);
        totalRotation = baseRotation + objectRotation;
        var centerLocalX = x + width * 0.5f;
        var centerLocalY = line.Ascent - height * 0.5f;

        var radians = baseRotation * (MathF.PI / 180f);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var centerWorld = RotatePoint(centerLocalX, centerLocalY, cos, sin, line.X, line.Y);

        return new DocRect(centerWorld.X - width * 0.5f, centerWorld.Y - height * 0.5f, width, height);
    }

    private static DocRect ComputeRotatedBounds(DocRect bounds, float rotationDegrees)
    {
        if (MathF.Abs(rotationDegrees) < 0.01f)
        {
            return bounds;
        }

        var geometry = BuildShapeSelectionGeometry(bounds, rotationDegrees, 0f);
        var minX = MathF.Min(MathF.Min(geometry.TopLeft.X, geometry.TopRight.X), MathF.Min(geometry.BottomLeft.X, geometry.BottomRight.X));
        var maxX = MathF.Max(MathF.Max(geometry.TopLeft.X, geometry.TopRight.X), MathF.Max(geometry.BottomLeft.X, geometry.BottomRight.X));
        var minY = MathF.Min(MathF.Min(geometry.TopLeft.Y, geometry.TopRight.Y), MathF.Min(geometry.BottomLeft.Y, geometry.BottomRight.Y));
        var maxY = MathF.Max(MathF.Max(geometry.TopLeft.Y, geometry.TopRight.Y), MathF.Max(geometry.BottomLeft.Y, geometry.BottomRight.Y));
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
        if (_editor.SelectedFloatingObjectIds.Count > 1)
        {
            return false;
        }

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

    private bool TryResolveFloatingImageSelection(out ImageSelectionInfo selection)
    {
        selection = default;
        if (!TryGetSelectedFloatingImageLayout(out var layoutObject, out var image))
        {
            return false;
        }

        var bounds = layoutObject.Bounds;
        if (_isImageEditing && _imagePreviewBounds.HasValue && ReferenceEquals(image, _imageInline))
        {
            bounds = _imagePreviewBounds.Value;
        }

        selection = new ImageSelectionInfo(image, bounds, image.Rotation);
        return true;
    }

    private bool TryResolveInlineSelection(out InlineObjectSelectionInfo selection)
    {
        if (_isInlineObjectEditing && _inlineEditSelection.HasValue && _inlineObjectDragMode != InlineObjectDragMode.Move)
        {
            selection = _inlineEditSelection.Value;
            if (selection.Inline is ShapeInline shape)
            {
                selection = selection with { Rotation = selection.BaseRotation + shape.Properties.Rotation };
            }
            else if (selection.Inline is ImageInline image)
            {
                selection = selection with { Rotation = selection.BaseRotation + image.Rotation };
            }

            if (_inlinePreviewBounds.HasValue)
            {
                selection = selection with { Bounds = _inlinePreviewBounds.Value };
            }

            return true;
        }

        if (_inlineSelection.HasValue)
        {
            selection = _inlineSelection.Value;
            return true;
        }

        return TryComputeInlineSelection(out selection);
    }

    private bool TryComputeInlineSelection(out InlineObjectSelectionInfo selection)
    {
        selection = default;
        if (_editor.SelectedFloatingObjectIds.Count > 0 || _editor.SelectedFloatingObjectId.HasValue)
        {
            return false;
        }

        var ranges = _editor.SelectionRanges;
        if (ranges.Count != 1)
        {
            return false;
        }

        var range = ranges[0].Normalize();
        if (range.IsEmpty || range.Start.ParagraphIndex != range.End.ParagraphIndex)
        {
            return false;
        }

        var paragraphIndex = range.Start.ParagraphIndex;
        if (paragraphIndex < 0 || paragraphIndex >= _editor.Document.ParagraphCount)
        {
            return false;
        }

        var paragraph = _editor.Document.GetParagraph(paragraphIndex);
        if (!TryGetInlineAtOffset(paragraph, range.Start.Offset, out var inline, out var inlineIndex, out var inlineStart, out var inlineLength))
        {
            return false;
        }

        if (inlineStart != range.Start.Offset || inlineLength != range.End.Offset - range.Start.Offset)
        {
            return false;
        }

        return TryBuildInlineSelectionInfo(inline, paragraphIndex, inlineIndex, out selection);
    }

    private bool TryBuildInlineSelectionInfo(Inline inline, int paragraphIndex, int inlineIndex, out InlineObjectSelectionInfo selection)
    {
        selection = default;
        if (inline is not ImageInline && inline is not ShapeInline && inline is not ChartInline)
        {
            return false;
        }

        if (!TryGetInlineObjectLayout(inline, paragraphIndex, out var bounds, out var rotation, out var supportsRotate, out var pageIndex))
        {
            return false;
        }

        var kind = inline switch
        {
            ImageInline => InlineObjectKind.Image,
            ShapeInline => InlineObjectKind.Shape,
            ChartInline => InlineObjectKind.Chart,
            _ => InlineObjectKind.Image
        };

        var objectRotation = inline switch
        {
            ShapeInline shape => shape.Properties.Rotation,
            ImageInline image => image.Rotation,
            _ => 0f
        };
        var baseRotation = rotation - objectRotation;
        selection = new InlineObjectSelectionInfo(kind, inline, paragraphIndex, inlineIndex, bounds, rotation, baseRotation, supportsRotate, pageIndex);
        return true;
    }

    private void SelectInlineObject(InlineObjectHitInfo hit)
    {
        var start = new TextPosition(hit.Selection.ParagraphIndex, hit.Offset);
        var end = new TextPosition(hit.Selection.ParagraphIndex, hit.Offset + hit.Length);
        _editor.SetSelection(new TextRange(start, end), SelectionUpdateMode.Replace);
        _inlineSelection = hit.Selection;
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

    private bool TryGetSelectedFloatingImageBounds(out DocRect bounds)
    {
        bounds = default;
        if (!TryResolveFloatingImageSelection(out var selection))
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

    private bool TryGetSelectedInlineObjectBounds(out DocRect bounds)
    {
        bounds = default;
        if (!TryResolveInlineSelection(out var selection))
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

    private bool TryGetFloatingImageAtPoint(DocPoint point, out FloatingLayoutObject layoutObject, out ImageInline image)
    {
        layoutObject = null!;
        image = null!;
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

            if (candidate.Object.Content is ImageInline inlineImage)
            {
                layoutObject = candidate;
                image = inlineImage;
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

    private sealed class ShapeTextEditSession
    {
        public ShapeInline Shape { get; }
        public ShapeTextBox TextBox { get; }
        public Document Document { get; }
        public EditorController Editor { get; }
        public AvaloniaEditorInputAdapter InputAdapter { get; }
        public ShapeTextLayoutMetrics Metrics { get; set; }

        public ShapeTextEditSession(
            ShapeInline shape,
            ShapeTextBox textBox,
            Document document,
            EditorController editor,
            AvaloniaEditorInputAdapter inputAdapter)
        {
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
            TextBox = textBox ?? throw new ArgumentNullException(nameof(textBox));
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Editor = editor ?? throw new ArgumentNullException(nameof(editor));
            InputAdapter = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));
            Metrics = default;
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
            () => HasCopySelection(session),
            () => HasCopySelection(session));
    }

    private static bool HasCopySelection(IEditorSession session)
    {
        if (session.SelectedFloatingObjectIds.Count > 0 || session.SelectedFloatingObjectId.HasValue)
        {
            return true;
        }

        var ranges = session.SelectionRanges;
        for (var i = 0; i < ranges.Count; i++)
        {
            if (!ranges[i].IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private IEditorViewOptionsService CreateViewOptionsService()
    {
        return new EditorViewOptionsService(this);
    }

    private void ConfigureInputPipeline(EditorController editor)
    {
        _kernel.Services.TryGet<IUndoRedoService>(out var undoRedo);
        _kernel.Services.TryGet<IClipboardService>(out var clipboard);
        _kernel.Services.TryGet<ITableSelectionSnapshotProvider>(out var tableSelectionProvider);
        _kernel.Services.TryGet<IContentControlInteractionService>(out var contentControls);
        _kernel.Services.TryGet<IAutoCorrectService>(out var autoCorrect);
        var commandRouter = new EditorCommandInputRouter(
            _kernel.Commands,
            editor,
            undoRedo,
            clipboard,
            tableSelectionProvider,
            contentControls,
            autoCorrect,
            () => _acceptsTab,
            () => _acceptsReturn,
            () => _isReadOnly);
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
        DisableCollaboration();
    }

    private void ApplyEditorState()
    {
        _editor.Changed += OnEditorChanged;
        _scrollOffset = default;
        UpdateDirtyPages(GetAllPages());
        UpdateScrollMetrics();
        UpdateSelectedEquation();
        UpdatePictureCropState();
        UpdateInlineObjectSelection();
        UpdateHeaderFooterSessionLayout();
        UpdateShapeTextSessionLayout();
        UpdateCommentAnchors();
        UpdateRevisionAnchors();
        InvalidateVisual();
        UpdateLocalPresence();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        UpdateScrollMetrics();
        UpdateDirtyPages(_editor.DirtyPages);
        UpdateSelectedEquation();
        UpdatePictureCropState();
        UpdateInlineObjectSelection();
        UpdateHeaderFooterSessionLayout();
        UpdateShapeTextSessionLayout();
        UpdateCommentAnchors();
        UpdateRevisionAnchors();
        InvalidateVisual();
        UpdateLocalPresence();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnableCollaboration(ICollabRealtimeSession session, Func<Guid, string?>? authorResolver = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _collabSession = session;
        _collabAuthorResolver = authorResolver;
        _collabSynchronizationContext ??= SynchronizationContext.Current;
        _collabCoordinator?.Dispose();
        _collabCoordinator = null;
        RecreateCollaborationCoordinatorIfNeeded();
    }

    private void RecreateCollaborationCoordinatorIfNeeded()
    {
        if (_collabSession is null)
        {
            _collabCoordinator?.Dispose();
            _collabCoordinator = null;
            return;
        }

        _collabCoordinator?.Dispose();
        Action? onResyncRequired = null;
        if (_collabUiService is not null)
        {
            onResyncRequired = () => _collabUiService.SetConnectionState(
                CollabConnectionState.Error,
                "Collaboration history is out of date. Please reconnect.");
        }

        _collabCoordinator = new CollabEditorSessionCoordinator(
            _kernel.Services,
            _kernel.Commands,
            _editor,
            _collabSession,
            _collabSynchronizationContext ?? SynchronizationContext.Current,
            _collabAuthorResolver,
            onResyncRequired);
    }

    public void DisableCollaboration()
    {
        _collabCoordinator?.Dispose();
        _collabCoordinator = null;
        _collabSession = null;
        _collabAuthorResolver = null;
        _collabSynchronizationContext = null;
    }

    public bool TryGetService<T>(out T service) where T : class
    {
        if (_shapeTextSession is not null
            && _shapeTextServices is not null
            && _shapeTextServices.TryGet(out service))
        {
            return true;
        }

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
        if (_shapeTextSession is not null
            && _shapeTextServices is not null
            && _shapeTextServices.TryGet(serviceType, out service))
        {
            return true;
        }

        if (_headerFooterSession is not null
            && _headerFooterServices is not null
            && _headerFooterServices.TryGet(serviceType, out service))
        {
            return true;
        }

        return _kernel.Services.TryGet(serviceType, out service);
    }

    public void RegisterService<T>(T service) where T : class
    {
        _kernel.Services.Register(service);
        if (service is ICollabUiService collabService)
        {
            AttachCollaborationService(collabService);
        }
    }

    public bool UnregisterService<T>() where T : class
    {
        var removed = _kernel.Services.Remove<T>();
        if (!removed)
        {
            return false;
        }

        if (typeof(T) == typeof(ICollabUiService))
        {
            DetachCollaborationService();
        }

        return true;
    }

    private void AttachCollaborationService(ICollabUiService collabService)
    {
        if (ReferenceEquals(_collabUiService, collabService))
        {
            return;
        }

        DetachCollaborationService();

        _collabUiService = collabService;
        _collabUiService.StateChanged += OnCollabStateChanged;
    }

    private void DetachCollaborationService()
    {
        if (_collabUiService is null)
        {
            return;
        }

        _collabUiService.StateChanged -= OnCollabStateChanged;
        _collabUiService = null;
    }

    private void OnCollabStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private void InvalidateAllPages()
    {
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
    }

    private void UpdateDirtyPages(IReadOnlyList<int> pages)
    {
        var effectivePages = pages;
        if (effectivePages.Count == 0 && _editor.Layout.Pages.Count > 0)
        {
            effectivePages = GetAllPages();
        }

        _renderVersion++;
        _renderOptions.DirtyPages = effectivePages;
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

    private void UpdateInlineObjectSelection()
    {
        if (_headerFooterSession is not null)
        {
            ClearInlineObjectSelection();
            return;
        }

        if (TryComputeInlineSelection(out var selection))
        {
            _inlineSelection = selection;
            return;
        }

        ClearInlineObjectSelection();
    }

    private void ClearInlineObjectSelection()
    {
        if (_inlineSelection is null && !_hoverInlineHandle.HasValue)
        {
            return;
        }

        _inlineSelection = null;
        _hoverInlineHandle = null;
    }

    private void AutoPromoteInlineShapeIfSelected()
    {
        if (_isPromotingInlineShape || _isShapeEditing || _headerFooterSession is not null)
        {
            return;
        }

        if (HasAnySelection())
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

    private bool HasAnySelection()
    {
        if (_editor.SelectedFloatingObjectIds.Count > 0 || _editor.SelectedFloatingObjectId.HasValue)
        {
            return true;
        }

        var ranges = _editor.SelectionRanges;
        for (var i = 0; i < ranges.Count; i++)
        {
            if (!ranges[i].IsEmpty)
            {
                return true;
            }
        }

        return false;
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

    private void UpdateShapeTextRenderOptions()
    {
        if (_shapeTextSession is null)
        {
            _renderOptions.ShapeTextEditingShapeId = null;
            _renderOptions.ShapeTextSelection = null;
            _renderOptions.ShapeTextSelectionRanges = null;
            _renderOptions.ShapeTextCaret = default;
            _renderOptions.ShowShapeTextCaret = false;
            return;
        }

        _renderOptions.ShapeTextEditingShapeId = _shapeTextSession.Shape.Id;
        _renderOptions.ShapeTextSelection = _shapeTextSession.Editor.Selection.IsEmpty
            ? null
            : _shapeTextSession.Editor.Selection;
        _renderOptions.ShapeTextSelectionRanges = _shapeTextSession.Editor.SelectionRanges;
        _renderOptions.ShapeTextCaret = _shapeTextSession.Editor.Caret;
        _renderOptions.ShowShapeTextCaret = true;
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

    public void SetHeaderFooterDifferentFirstPage(bool value)
    {
        var sectionIndex = ResolveHeaderFooterSectionIndex();
        var section = _editor.Document.GetSection(sectionIndex);
        if (section.Properties.DifferentFirstPageHeaderFooter == value)
        {
            return;
        }

        section.Properties.DifferentFirstPageHeaderFooter = value;
        HeaderFooterVariant? variantOverride = null;
        if (_headerFooterSession is not null)
        {
            if (!value && _headerFooterSession.Target.Variant == HeaderFooterVariant.First)
            {
                variantOverride = HeaderFooterVariant.Default;
            }
            else if (value && _headerFooterSession.Target.Variant == HeaderFooterVariant.Default)
            {
                var pageIndex = ResolveHeaderFooterActivePageIndex();
                if (IsFirstPageOfSection(pageIndex, sectionIndex))
                {
                    variantOverride = HeaderFooterVariant.First;
                }
            }
        }

        ApplyHeaderFooterSettingChange(sectionIndex, variantOverride);
    }

    public void SetHeaderFooterDifferentOddEven(bool value)
    {
        var document = _editor.Document;
        if (document.EvenAndOddHeaders == value)
        {
            return;
        }

        document.EvenAndOddHeaders = value;
        HeaderFooterVariant? variantOverride = null;
        if (_headerFooterSession is not null)
        {
            if (!value && _headerFooterSession.Target.Variant == HeaderFooterVariant.Even)
            {
                variantOverride = HeaderFooterVariant.Default;
            }
            else if (value && _headerFooterSession.Target.Variant == HeaderFooterVariant.Default)
            {
                var sectionIndex = ResolveHeaderFooterSectionIndex();
                var pageIndex = ResolveHeaderFooterActivePageIndex();
                if (IsPageInSection(pageIndex, sectionIndex) && IsEvenPage(pageIndex))
                {
                    variantOverride = HeaderFooterVariant.Even;
                }
            }
        }

        ApplyHeaderFooterSettingChange(ResolveHeaderFooterSectionIndex(), variantOverride);
    }

    public void SetHeaderFooterVariant(HeaderFooterVariant variant)
    {
        if (_headerFooterSession is null)
        {
            return;
        }

        var sectionIndex = ResolveHeaderFooterSectionIndex();
        var settingsChanged = false;
        if (variant == HeaderFooterVariant.First)
        {
            var section = _editor.Document.GetSection(sectionIndex);
            if (section.Properties.DifferentFirstPageHeaderFooter != true)
            {
                section.Properties.DifferentFirstPageHeaderFooter = true;
                settingsChanged = true;
            }
        }
        else if (variant == HeaderFooterVariant.Even)
        {
            if (!_editor.Document.EvenAndOddHeaders)
            {
                _editor.Document.EvenAndOddHeaders = true;
                settingsChanged = true;
            }
        }

        if (settingsChanged)
        {
            _editor.RefreshLayout();
        }

        var pageIndex = ResolveHeaderFooterPageIndexForVariant(sectionIndex, variant);
        if (!TryBuildHeaderFooterHit(pageIndex, _headerFooterSession.Mode, variant, out var hit))
        {
            return;
        }

        BeginHeaderFooterEdit(hit);
        Offset = BuildHeaderFooterScrollOffset(hit);
    }

    public void NavigateHeaderFooterSection(int delta)
    {
        if (_headerFooterSession is null || delta == 0)
        {
            return;
        }

        var currentSection = ResolveHeaderFooterSectionIndex();
        var targetSection = Math.Clamp(currentSection + delta, 0, Math.Max(0, _editor.Document.SectionCount - 1));
        if (targetSection == currentSection)
        {
            return;
        }

        var variant = _headerFooterSession.Target.Variant;
        if (!TryResolveHeaderFooterPageIndex(targetSection, variant, out var pageIndex))
        {
            return;
        }

        if (!TryBuildHeaderFooterHit(pageIndex, _headerFooterSession.Mode, variant, out var hit))
        {
            return;
        }

        BeginHeaderFooterEdit(hit);
        Offset = BuildHeaderFooterScrollOffset(hit);
    }

    private bool BeginHeaderFooterEdit(HeaderFooterHit hit)
    {
        EndShapeTextEdit();
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
        _headerFooterSnapshot = null;
        _headerFooterGesture = null;
        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            _headerFooterGesture = gestureRecorder.BeginGesture("header-footer-edit");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _headerFooterSnapshot = history.CaptureSnapshot();
        }

        UpdateHeaderFooterSessionLayout(hit);
        UpdateDirtyPages(GetShapeTextDirtyPages());
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
        if (_headerFooterDirty)
        {
            if (_headerFooterGesture.HasValue && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
            {
                gestureRecorder.EndGesture(_headerFooterGesture.Value);
            }
            else if (_headerFooterSnapshot.HasValue
                     && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
            {
                history.RecordSnapshot(_headerFooterSnapshot.Value);
            }
        }

        _headerFooterSession = null;
        _headerFooterHit = null;
        _headerFooterServices = null;
        _headerFooterSnapshot = null;
        _headerFooterGesture = null;
        _headerFooterDirty = false;
        _isHeaderFooterSelecting = false;
        UpdateHeaderFooterRenderOptions();
        UpdateDirtyPages(GetShapeTextDirtyPages());
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
        var tableSelectionProvider = services.GetRequired<ITableSelectionSnapshotProvider>();
        if (_kernel.Services.TryGet<IContentControlInteractionService>(out var contentControls))
        {
            services.Register(contentControls);
        }

        services.TryGet<IAutoCorrectService>(out var autoCorrect);
        var inputRouter = new EditorCommandInputRouter(
            dispatcher,
            editor,
            undoRedo,
            clipboard,
            tableSelectionProvider,
            contentControls,
            autoCorrect,
            () => _acceptsTab,
            () => _acceptsReturn,
            () => _isReadOnly);
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

    private void ApplyHeaderFooterSettingChange(int sectionIndex, HeaderFooterVariant? variantOverride)
    {
        _editor.RefreshLayout();
        if (_headerFooterSession is null)
        {
            UpdateDirtyPages(GetAllPages());
            InvalidateVisual();
            EditorStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var variant = variantOverride ?? _headerFooterSession.Target.Variant;
        var pageIndex = ResolveHeaderFooterPageIndexForVariant(sectionIndex, variant);
        if (TryBuildHeaderFooterHit(pageIndex, _headerFooterSession.Mode, variant, out var hit))
        {
            BeginHeaderFooterEdit(hit);
            Offset = BuildHeaderFooterScrollOffset(hit);
            return;
        }

        UpdateHeaderFooterSessionLayout();
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
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

    private bool TryBeginShapeTextEditFromPoint(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }
        if (!TryGetDocumentPoint(e, out var docX, out var docY))
        {
            return false;
        }

        if (!TryResolveShapeTextTarget(
                docX,
                docY,
                out var floating,
                out var shape,
                out var textBox,
                out var shapeBounds,
                out var textBounds))
        {
            return false;
        }

        var isSelected = _editor.SelectedFloatingObjectId.HasValue
                         && _editor.SelectedFloatingObjectId.Value == floating.Id;
        if (e.ClickCount < 2 && !isSelected)
        {
            return false;
        }

        if (!BeginShapeTextEdit(shape, textBox, shapeBounds, textBounds))
        {
            return false;
        }

        return StartShapeTextSelection(e);
    }

    private bool TryHandleShapeTextPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        if (_shapeTextSession is null)
        {
            return false;
        }

        if (!TryGetDocumentPoint(e, out var docX, out var docY))
        {
            return false;
        }

        if (!_shapeTextSession.Metrics.TextBounds.Contains(docX, docY))
        {
            return false;
        }

        return StartShapeTextSelection(e);
    }

    private bool BeginShapeTextEdit(ShapeInline shape, ShapeTextBox textBox, DocRect shapeBounds, DocRect textBounds)
    {
        if (_shapeTextSession is not null && ReferenceEquals(_shapeTextSession.Shape, shape))
        {
            UpdateShapeTextSessionLayout(shape, textBox, shapeBounds, textBounds);
            UpdateShapeTextRenderOptions();
            InvalidateVisual();
            return true;
        }

        EndShapeTextEdit();

        if (!TryCreateShapeTextSession(shape, textBox, out var session, out var services))
        {
            return false;
        }

        _shapeTextSession = session;
        _shapeTextServices = services;
        _shapeTextSession.Editor.Changed += OnShapeTextEditorChanged;
        _shapeTextDirty = false;
        _shapeTextSnapshot = null;
        _shapeTextGesture = null;
        if (_kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
        {
            _shapeTextGesture = gestureRecorder.BeginGesture("shape-text-edit");
        }
        else if (_kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
        {
            _shapeTextSnapshot = history.CaptureSnapshot();
        }

        UpdateShapeTextSessionLayout(shape, textBox, shapeBounds, textBounds);
        UpdateShapeTextRenderOptions();
        UpdateDirtyPages(GetShapeTextDirtyPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void EndShapeTextEdit()
    {
        if (_shapeTextSession is null)
        {
            return;
        }

        _shapeTextSession.Editor.Changed -= OnShapeTextEditorChanged;
        if (_shapeTextDirty)
        {
            if (_shapeTextGesture.HasValue && _kernel.Services.TryGet<ICollabGestureRecorder>(out var gestureRecorder))
            {
                gestureRecorder.EndGesture(_shapeTextGesture.Value);
            }
            else if (_shapeTextSnapshot.HasValue
                     && _kernel.Services.TryGet<IEditorHistorySnapshotService>(out var history))
            {
                history.RecordSnapshot(_shapeTextSnapshot.Value);
            }
        }

        _shapeTextSession = null;
        _shapeTextServices = null;
        _shapeTextSnapshot = null;
        _shapeTextGesture = null;
        _shapeTextDirty = false;
        _isShapeTextSelecting = false;
        UpdateShapeTextRenderOptions();
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnShapeTextEditorChanged(object? sender, EventArgs e)
    {
        if (_shapeTextSession is null)
        {
            return;
        }

        if (_isShapeTextLayoutUpdating && _shapeTextSession.Editor.LastChangeKind != EditorChangeKind.Content)
        {
            return;
        }

        RefreshShapeTextMetrics();
        UpdateShapeTextRenderOptions();

        if (_shapeTextSession.Editor.LastChangeKind == EditorChangeKind.Content)
        {
            MarkShapeTextDirty();
            return;
        }

        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryCreateShapeTextSession(ShapeInline shape, ShapeTextBox textBox, out ShapeTextEditSession session, out EditorServices services)
    {
        session = null!;
        services = null!;
        var document = BuildShapeTextDocument(_editor.Document, textBox);
        var editor = new EditorController(_textMeasurer, document);
        services = new EditorServices();
        var dispatcher = new EditorCommandDispatcher();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));
        var viewOptions = _kernel.Services.TryGet<IEditorViewOptionsService>(out var resolvedViewOptions)
            ? resolvedViewOptions
            : null;
        _ = EditorHomeServiceRegistry.Register(
            services,
            dispatcher,
            editor,
            CreateFontService(editor),
            CreateClipboardService(editor),
            viewOptions,
            documentFactory: DocumentTemplates.CreateDefaultDocument);
        var undoRedo = services.GetRequired<IUndoRedoService>();
        var clipboard = services.GetRequired<IClipboardService>();
        var tableSelectionProvider = services.GetRequired<ITableSelectionSnapshotProvider>();
        if (_kernel.Services.TryGet<IContentControlInteractionService>(out var contentControls))
        {
            services.Register(contentControls);
        }

        services.TryGet<IAutoCorrectService>(out var autoCorrect);
        var inputRouter = new EditorCommandInputRouter(
            dispatcher,
            editor,
            undoRedo,
            clipboard,
            tableSelectionProvider,
            contentControls,
            autoCorrect,
            () => _acceptsTab,
            () => _acceptsReturn,
            () => _isReadOnly);
        var inputAdapter = new AvaloniaEditorInputAdapter(inputRouter);
        session = new ShapeTextEditSession(shape, textBox, document, editor, inputAdapter);
        return true;
    }

    private void MarkShapeTextDirty()
    {
        if (_shapeTextSession is null)
        {
            return;
        }

        _shapeTextDirty = true;
        ApplyShapeTextChanges();
    }

    private void ApplyShapeTextChanges()
    {
        if (_shapeTextSession is null)
        {
            return;
        }

        var textBox = _shapeTextSession.TextBox;
        textBox.Blocks.Clear();
        var blocks = _shapeTextSession.Editor.Document.Blocks;
        if (blocks.Count == 0)
        {
            textBox.Blocks.Add(new ParagraphBlock());
        }
        else
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                textBox.Blocks.Add(DocumentClone.CloneBlock(blocks[i]));
            }
        }

        MergeRevisions(_shapeTextSession.Editor.Document.Revisions, _editor.Document.Revisions);
        UpdateDirtyPages(GetAllPages());
        InvalidateVisual();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateShapeTextSessionLayout()
    {
        if (_shapeTextSession is null)
        {
            return;
        }

        if (!TryResolveShapeTextBounds(_shapeTextSession.Shape, _shapeTextSession.TextBox, out var shapeBounds, out var textBounds))
        {
            EndShapeTextEdit();
            return;
        }

        UpdateShapeTextSessionLayout(_shapeTextSession.Shape, _shapeTextSession.TextBox, shapeBounds, textBounds);
        UpdateShapeTextRenderOptions();
    }

    private void UpdateShapeTextSessionLayout(ShapeInline shape, ShapeTextBox textBox, DocRect shapeBounds, DocRect textBounds)
    {
        if (_shapeTextSession is null || !ReferenceEquals(_shapeTextSession.Shape, shape))
        {
            return;
        }

        if (_isShapeTextLayoutUpdating)
        {
            return;
        }

        try
        {
            _isShapeTextLayoutUpdating = true;
            ApplyShapeTextLayoutSettings(_shapeTextSession.Editor.LayoutSettings, _editor.Layout.Settings, textBounds.Width, textBounds.Height);
            _shapeTextSession.Editor.UpdateLayout(textBounds.Width, textBounds.Height);
            if (ShapeTextLayoutHelper.TryComputeMetrics(_shapeTextSession.Editor.Layout, textBox, textBounds, out var metrics))
            {
                _shapeTextSession.Metrics = metrics;
            }
        }
        finally
        {
            _isShapeTextLayoutUpdating = false;
        }
    }

    private void RefreshShapeTextMetrics()
    {
        if (_shapeTextSession is null)
        {
            return;
        }

        if (_isShapeTextLayoutUpdating)
        {
            return;
        }

        var bounds = _shapeTextSession.Metrics.TextBounds;
        if (bounds.Width <= 1f || bounds.Height <= 1f)
        {
            UpdateShapeTextSessionLayout();
            return;
        }

        if (ShapeTextLayoutHelper.TryComputeMetrics(_shapeTextSession.Editor.Layout, _shapeTextSession.TextBox, bounds, out var metrics))
        {
            _shapeTextSession.Metrics = metrics;
        }
    }

    private bool TryResolveShapeTextTarget(
        float docX,
        float docY,
        out FloatingObject floating,
        out ShapeInline shape,
        out ShapeTextBox textBox,
        out DocRect shapeBounds,
        out DocRect textBounds)
    {
        floating = null!;
        shape = null!;
        textBox = null!;
        shapeBounds = default;
        textBounds = default;

        var docPoint = new DocPoint(docX, docY);
        if (!TryGetFloatingShapeAtPoint(docPoint, out var layoutObject, out var hitFloating, out var hitShape))
        {
            return false;
        }

        floating = hitFloating;
        textBox = hitShape.TextBox ?? new ShapeTextBox();
        if (hitShape.TextBox is null)
        {
            hitShape.TextBox = textBox;
        }

        if (!TryGetShapeTextBounds(hitShape, textBox, layoutObject.Bounds, out textBounds))
        {
            return false;
        }

        if (!textBounds.Contains(docX, docY))
        {
            return false;
        }

        shape = hitShape;
        shapeBounds = layoutObject.Bounds;
        return true;
    }

    private bool TryResolveShapeTextBounds(ShapeInline shape, ShapeTextBox textBox, out DocRect shapeBounds, out DocRect textBounds)
    {
        shapeBounds = default;
        textBounds = default;
        var floats = _editor.Layout.FloatingObjects;
        for (var i = 0; i < floats.Count; i++)
        {
            var floating = floats[i];
            if (floating.Object.Content is ShapeInline candidate && ReferenceEquals(candidate, shape))
            {
                shapeBounds = floating.Bounds;
                return TryGetShapeTextBounds(shape, textBox, shapeBounds, out textBounds);
            }
        }

        return false;
    }

    private static bool TryGetShapeTextBounds(ShapeInline shape, ShapeTextBox textBox, DocRect shapeBounds, out DocRect textBounds)
    {
        textBounds = default;
        if (MathF.Abs(shape.Properties.Rotation) > 0.01f)
        {
            return false;
        }

        var textRect = ShapeGeometryEvaluator.ResolveTextRectangle(shape.Properties, shape.Width, shape.Height);
        var padding = textBox.Properties.Padding;
        var left = shapeBounds.X + textRect.X + padding.Left;
        var top = shapeBounds.Y + textRect.Y + padding.Top;
        var width = textRect.Width - padding.Left - padding.Right;
        var height = textRect.Height - padding.Top - padding.Bottom;
        if (width <= 1f || height <= 1f)
        {
            return false;
        }

        textBounds = new DocRect(left, top, width, height);
        return true;
    }

    private bool StartShapeTextSelection(PointerPressedEventArgs e)
    {
        if (_shapeTextSession is null)
        {
            return false;
        }

        _isSelecting = false;
        _isShapeTextSelecting = true;
        RefreshShapeTextMetrics();
        var offset = BuildShapeTextScrollOffset(_shapeTextSession.Metrics);
        var zoom = BuildShapeTextZoomFactor(_shapeTextSession.Metrics);
        if (_shapeTextSession.InputAdapter.HandlePointerPressed(e, offset, zoom, this))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private Vector BuildShapeTextScrollOffset(ShapeTextLayoutMetrics metrics)
    {
        var effectiveOffset = GetEffectiveScrollOffset();
        var zoom = _zoomFactor <= 0f ? 1f : _zoomFactor;
        return new Vector(
            effectiveOffset.X - metrics.OriginX * zoom,
            effectiveOffset.Y - metrics.OriginY * zoom);
    }

    private float BuildShapeTextZoomFactor(ShapeTextLayoutMetrics metrics)
    {
        var scale = _zoomFactor <= 0f ? 1f : _zoomFactor;
        return scale * MathF.Max(0.01f, metrics.Scale);
    }

    private static void MergeRevisions(DocumentRevisions source, DocumentRevisions target)
    {
        if (source.Timeline.Count == 0)
        {
            return;
        }

        for (var i = 0; i < source.Timeline.Count; i++)
        {
            target.AddOrUpdate(source.Timeline[i]);
        }
    }

    private IReadOnlyList<int> GetShapeTextDirtyPages()
    {
        if (_shapeTextSession is null)
        {
            return GetAllPages();
        }

        var floats = _editor.Layout.FloatingObjects;
        for (var i = 0; i < floats.Count; i++)
        {
            var floating = floats[i];
            if (floating.Object.Content is ShapeInline candidate && ReferenceEquals(candidate, _shapeTextSession.Shape))
            {
                if (floating.PageIndex >= 0)
                {
                    return new[] { floating.PageIndex };
                }

                break;
            }
        }

        return GetAllPages();
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
        return TryBuildHeaderFooterHit(pageIndex, mode, null, out hit);
    }

    private bool TryBuildHeaderFooterHit(
        int pageIndex,
        HeaderFooterEditMode mode,
        HeaderFooterVariant? variantOverride,
        out HeaderFooterHit hit)
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

        if (!TryResolveHeaderFooterTarget(pageIndex, mode, variantOverride, out var target))
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
        return TryResolveHeaderFooterTarget(pageIndex, mode, null, out target);
    }

    private bool TryResolveHeaderFooterTarget(
        int pageIndex,
        HeaderFooterEditMode mode,
        HeaderFooterVariant? variantOverride,
        out HeaderFooterTarget target)
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
            var variant = variantOverride ?? HeaderFooterVariant.Default;
            if (!variantOverride.HasValue)
            {
                if (isFirstPageOfSection && sectionInfo.Properties.DifferentFirstPageHeaderFooter == true)
                {
                    variant = HeaderFooterVariant.First;
                }
                else if (document.EvenAndOddHeaders && isEvenPage)
                {
                    variant = HeaderFooterVariant.Even;
                }
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

    private static bool IsShapeTextEditKey(KeyEventArgs e)
    {
        return IsHeaderFooterEditKey(e);
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

    private static Document BuildShapeTextDocument(Document source, ShapeTextBox textBox)
    {
        var clone = DocumentClone.Clone(source);
        clone.Blocks.Clear();
        if (textBox.Blocks.Count == 0)
        {
            clone.Blocks.Add(new ParagraphBlock());
        }
        else
        {
            for (var i = 0; i < textBox.Blocks.Count; i++)
            {
                clone.Blocks.Add(DocumentClone.CloneBlock(textBox.Blocks[i]));
            }
        }

        if (textBox.Properties.TextDirection.HasValue)
        {
            clone.DefaultParagraphStyleProperties.TextDirection = textBox.Properties.TextDirection;
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

    private static void ApplyShapeTextLayoutSettings(LayoutSettings target, LayoutSettings source, float width, float height)
    {
        ApplyHeaderFooterLayoutSettings(target, source, width, height);
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

    private int ResolveHeaderFooterActivePageIndex()
    {
        if (_headerFooterHit.HasValue)
        {
            return _headerFooterHit.Value.PageIndex;
        }

        return ResolveHeaderFooterPageIndex();
    }

    private int ResolveHeaderFooterSectionIndex()
    {
        var layout = _editor.Layout;
        if (layout.PageSections.Count == 0)
        {
            return 0;
        }

        var pageIndex = ResolveHeaderFooterActivePageIndex();
        if (pageIndex < 0 || pageIndex >= layout.PageSections.Count)
        {
            return 0;
        }

        return layout.PageSections[pageIndex].SectionIndex;
    }

    private bool ResolveHeaderFooterDifferentFirstPage()
    {
        var section = _editor.Document.GetSection(ResolveHeaderFooterSectionIndex());
        return section.Properties.DifferentFirstPageHeaderFooter == true;
    }

    private int ResolveCurrentPageIndex()
    {
        var layout = _editor.Layout;
        if (layout.Pages.Count == 0 || layout.Lines.Count == 0)
        {
            return 0;
        }

        var lineIndex = EditorSelectionService.FindLineIndexForPosition(layout, _editor.Caret, out _);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        if (pageIndex < 0)
        {
            return 0;
        }

        return Math.Min(pageIndex, layout.Pages.Count - 1);
    }

    private int ResolveHeaderFooterPageIndexForVariant(int sectionIndex, HeaderFooterVariant variant)
    {
        var layout = _editor.Layout;
        if (layout.Pages.Count == 0 || layout.PageSections.Count == 0)
        {
            return 0;
        }

        var currentPageIndex = ResolveHeaderFooterActivePageIndex();
        if (IsPageInSection(currentPageIndex, sectionIndex))
        {
            if (variant == HeaderFooterVariant.Default)
            {
                return currentPageIndex;
            }

            if (variant == HeaderFooterVariant.First && IsFirstPageOfSection(currentPageIndex, sectionIndex))
            {
                return currentPageIndex;
            }

            if (variant == HeaderFooterVariant.Even && IsEvenPage(currentPageIndex))
            {
                return currentPageIndex;
            }
        }

        if (TryResolveHeaderFooterPageIndex(sectionIndex, variant, out var pageIndex))
        {
            return pageIndex;
        }

        var maxIndex = Math.Min(layout.Pages.Count, layout.PageSections.Count) - 1;
        return Math.Clamp(currentPageIndex, 0, Math.Max(0, maxIndex));
    }

    private bool TryResolveHeaderFooterPageIndex(int sectionIndex, HeaderFooterVariant variant, out int pageIndex)
    {
        pageIndex = -1;
        var layout = _editor.Layout;
        var maxCount = Math.Min(layout.Pages.Count, layout.PageSections.Count);
        if (maxCount == 0)
        {
            return false;
        }

        var firstIndex = -1;
        var evenIndex = -1;
        for (var i = 0; i < maxCount; i++)
        {
            var section = layout.PageSections[i];
            if (section.SectionIndex != sectionIndex)
            {
                if (firstIndex >= 0)
                {
                    break;
                }

                continue;
            }

            if (firstIndex < 0)
            {
                firstIndex = i;
            }

            if (variant == HeaderFooterVariant.Even && evenIndex < 0)
            {
                var pageNumber = layout.Pages[i].Index + 1;
                if (pageNumber % 2 == 0)
                {
                    evenIndex = i;
                    break;
                }
            }
        }

        if (firstIndex < 0)
        {
            return false;
        }

        if (variant == HeaderFooterVariant.Even && evenIndex >= 0)
        {
            pageIndex = evenIndex;
            return true;
        }

        pageIndex = firstIndex;
        return true;
    }

    private bool IsPageInSection(int pageIndex, int sectionIndex)
    {
        var layout = _editor.Layout;
        if (pageIndex < 0 || pageIndex >= layout.PageSections.Count)
        {
            return false;
        }

        return layout.PageSections[pageIndex].SectionIndex == sectionIndex;
    }

    private bool IsFirstPageOfSection(int pageIndex, int sectionIndex)
    {
        var layout = _editor.Layout;
        if (!IsPageInSection(pageIndex, sectionIndex))
        {
            return false;
        }

        return pageIndex == 0 || layout.PageSections[pageIndex - 1].SectionIndex != sectionIndex;
    }

    private bool IsEvenPage(int pageIndex)
    {
        var layout = _editor.Layout;
        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            return false;
        }

        var pageNumber = layout.Pages[pageIndex].Index + 1;
        return pageNumber % 2 == 0;
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

    private void EnsureLayoutMetrics()
    {
        var layout = _editor.Layout;
        if (ReferenceEquals(_layoutMetricsLayout, layout))
        {
            return;
        }

        _layoutMetricsLayout = layout;
        _layoutContentBoundsValid = false;
        _layoutContentMinX = 0f;
        _layoutContentMaxX = 0f;
        _layoutMaxPageWidth = 0f;

        if (layout.Pages.Count == 0)
        {
            return;
        }

        var bounds = layout.Pages[0].Bounds;
        var minX = bounds.Left;
        var maxX = bounds.Right;
        var maxWidth = bounds.Width;
        for (var i = 1; i < layout.Pages.Count; i++)
        {
            bounds = layout.Pages[i].Bounds;
            if (bounds.Left < minX)
            {
                minX = bounds.Left;
            }

            if (bounds.Right > maxX)
            {
                maxX = bounds.Right;
            }

            if (bounds.Width > maxWidth)
            {
                maxWidth = bounds.Width;
            }
        }

        _layoutContentMinX = minX;
        _layoutContentMaxX = maxX;
        _layoutMaxPageWidth = maxWidth;
        _layoutContentBoundsValid = true;
    }

    private bool TryGetContentHorizontalBounds(out float minX, out float maxX)
    {
        EnsureLayoutMetrics();
        if (!_layoutContentBoundsValid)
        {
            minX = 0f;
            maxX = 0f;
            return false;
        }

        minX = _layoutContentMinX;
        maxX = _layoutContentMaxX;
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
        if (_viewport.Width <= 0)
        {
            return _zoomFactor;
        }

        EnsureLayoutMetrics();
        if (!_layoutContentBoundsValid || _layoutMaxPageWidth <= 0f)
        {
            return _zoomFactor;
        }

        var gap = _editor.Layout.Settings.PageGap;
        var targetWidth = _layoutMaxPageWidth + gap;
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
        if (_viewport.Width <= 0)
        {
            return _zoomFactor;
        }

        EnsureLayoutMetrics();
        if (!_layoutContentBoundsValid || _layoutMaxPageWidth <= 0f)
        {
            return _zoomFactor;
        }

        var gap = _editor.Layout.Settings.PageGap;
        var count = Math.Max(1, pagesPerRow);
        var targetWidth = (_layoutMaxPageWidth * count) + (gap * count);
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
        private readonly bool _isReadOnly;
        private readonly bool _isReadOnlyCaretVisible;

        public SkiaDrawOperation(
            Rect bounds,
            EditorController editor,
            SkiaDocumentRenderer renderer,
            RenderOptions options,
            Vector offset,
            bool isReadOnly,
            bool isReadOnlyCaretVisible)
        {
            _bounds = bounds;
            _editor = editor;
            _renderer = renderer;
            _options = options;
            _offset = offset;
            _isReadOnly = isReadOnly;
            _isReadOnlyCaretVisible = isReadOnlyCaretVisible;
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
            var viewportWidth = MathF.Max(0f, (float)(_bounds.Width / _options.ZoomFactor));
            var viewportHeight = MathF.Max(0f, (float)(_bounds.Height / _options.ZoomFactor));
            _options.VisibleBounds = new DocRect(offsetDocX, offsetDocY, viewportWidth, viewportHeight);

            if (_options.HeaderFooterMode == HeaderFooterEditMode.None && !_options.ShapeTextEditingShapeId.HasValue)
            {
                _options.Caret = _editor.Caret;
                _options.Selection = _editor.Selection.IsEmpty ? null : _editor.Selection;
                _options.SelectionRanges = _editor.SelectionRanges;
                _options.ShowCaret = !_isReadOnly || _isReadOnlyCaretVisible;
            }
            else
            {
                _options.Caret = _editor.Caret;
                _options.Selection = null;
                _options.SelectionRanges = null;
                _options.ShowCaret = false;
            }

            _options.SelectedFloatingObjectId = _editor.SelectedFloatingObjectId;
            _options.SelectedFloatingObjectIds = _editor.SelectedFloatingObjectIds;

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

    private enum InlineObjectKind
    {
        Image,
        Shape,
        Chart
    }

    private enum InlineObjectDragMode
    {
        None,
        Move,
        Resize,
        Rotate
    }

    private readonly record struct InlineObjectSelectionInfo(
        InlineObjectKind Kind,
        Inline Inline,
        int ParagraphIndex,
        int InlineIndex,
        DocRect Bounds,
        float Rotation,
        float BaseRotation,
        bool SupportsRotate,
        int PageIndex);

    private readonly record struct InlineObjectHitInfo(InlineObjectSelectionInfo Selection, int Offset, int Length);

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

    private readonly record struct PresenceSignature(
        TextAnchor? Caret,
        AnchorRange? Selection,
        ListSignature SelectionRanges,
        ListSignature TableSelections,
        ListSignature FloatingSelections);

    private readonly record struct ListSignature(int Count, int Hash);

    private readonly record struct ShapeHandleInfo(ShapeHandleKind Kind, DocRect Bounds);
    private readonly record struct ShapeSelectionInfo(ShapeInline Shape, DocRect Bounds, float Rotation);
    private readonly record struct ImageSelectionInfo(ImageInline Image, DocRect Bounds, float Rotation);
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
