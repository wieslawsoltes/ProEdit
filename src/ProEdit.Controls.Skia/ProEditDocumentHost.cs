using SkiaSharp;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using ProEdit.Primitives;
using ProEdit.Rendering;
using ProEdit.Rendering.Skia;
using ProEdit.Word.Editor;
using ProEdit.Word.Editor.Editing;

namespace ProEdit.Controls.Skia;

/// <summary>
/// Shared Skia-backed rendering and editing host used by platform-specific ProEdit controls.
/// </summary>
public sealed class ProEditDocumentHost : IEditorViewOptionsService, IEditorZoomService, IDisposable
{
    /// <summary>
    /// The minimum supported zoom factor.
    /// </summary>
    public const float MinimumZoom = 0.1f;

    /// <summary>
    /// The maximum supported zoom factor.
    /// </summary>
    public const float MaximumZoom = 5f;

    private const float DefaultViewportWidth = 800f;
    private const float DefaultViewportHeight = 600f;
    private const float ZoomStep = 0.1f;
    private const int DefaultMultiplePages = 2;

    private readonly EditorKernel _kernel;
    private readonly SkiaTextMeasurer _textMeasurer = new();
    private readonly SkiaDocumentRenderer _renderer = new();
    private readonly RenderOptions _renderOptions = CreateDefaultRenderOptions();

    private IEditorMutableSession _session;
    private EditorCommandInputRouter _inputRouter;
    private IEditorCommandRouter _commandRouter = null!;
    private SkiaDocumentFontResolver? _fontResolver;
    private float _viewportWidth = DefaultViewportWidth;
    private float _viewportHeight = DefaultViewportHeight;
    private float _zoom = 1f;
    private ProEditDocumentZoomMode _zoomMode = ProEditDocumentZoomMode.Custom;
    private int _multiplePagesPerRow = DefaultMultiplePages;
    private float _scrollX;
    private float _scrollY;
    private ProEditDocumentExtent _extent;
    private bool _isReadOnly = true;
    private bool _showReadOnlyCaret;
    private bool _acceptsReturn = true;
    private bool _acceptsTab;
    private bool _showRuler = true;
    private bool _showNavigationPane;
    private EditorViewMode _viewMode = EditorViewMode.PrintLayout;
    private float _savedZoom = 1f;
    private ProEditDocumentZoomMode _savedZoomMode = ProEditDocumentZoomMode.Custom;
    private bool _hasSavedZoom;
    private bool _isPanEnabled = true;
    private bool _isPanning;
    private float _panStartX;
    private float _panStartY;
    private float _panStartScrollX;
    private float _panStartScrollY;

    /// <summary>
    /// Raised when document content, selection, layout, or rendering state changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when viewport, zoom, scroll, or extent state changes.
    /// </summary>
    public event EventHandler? ViewportChanged;

    /// <summary>
    /// Raised when view options such as rulers, navigation panes, or view mode change.
    /// </summary>
    public event EventHandler? ViewOptionsChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProEditDocumentHost"/> class with the default editor session factory and editing module.
    /// </summary>
    public ProEditDocumentHost()
        : this(new LegacyEditorSessionFactory())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProEditDocumentHost"/> class.
    /// </summary>
    /// <param name="sessionFactory">The editor session factory.</param>
    /// <param name="modules">Optional editor modules. The basic editing module is registered when this value is null.</param>
    public ProEditDocumentHost(IEditorSessionFactory sessionFactory, IEnumerable<IEditorModule>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);

        _kernel = new EditorKernel(sessionFactory);
        RegisterModules(modules ?? CreateDefaultModules());
        var document = new Document();
        ConfigureMeasurer(document);
        _session = _kernel.CreateSession(_textMeasurer, document);
        _commandRouter = RegisterWordEditorServices();
        _inputRouter = CreateInputRouter();
        _session.Changed += OnSessionChanged;
        UpdateLayoutForViewport();
        UpdateExtent();
    }

    /// <summary>
    /// Gets the editor services registered for the current document session.
    /// </summary>
    public EditorServices Services => _kernel.Services;

    /// <summary>
    /// Gets the command dispatcher used by the current document session.
    /// </summary>
    public EditorCommandDispatcher Commands => _kernel.Commands;

    /// <summary>
    /// Gets the command router registered for the current document session.
    /// </summary>
    public IEditorCommandRouter CommandRouter => _commandRouter;

    /// <summary>
    /// Gets the current document.
    /// </summary>
    public Document Document => _session.Document;

    /// <summary>
    /// Gets the current editor session.
    /// </summary>
    public IEditorMutableSession EditorSession => _session;

    /// <summary>
    /// Gets the current document layout.
    /// </summary>
    public DocumentLayout Layout => _session.Layout;

    /// <summary>
    /// Gets the mutable layout settings owned by the current editor session.
    /// </summary>
    public LayoutSettings LayoutSettings => _session.LayoutSettings;

    /// <summary>
    /// Gets the render options used for each render pass.
    /// </summary>
    public RenderOptions RenderOptions => _renderOptions;

    /// <summary>
    /// Gets the Skia text measurer used by layout.
    /// </summary>
    public SkiaTextMeasurer TextMeasurer => _textMeasurer;

    /// <summary>
    /// Gets the Skia document renderer.
    /// </summary>
    public SkiaDocumentRenderer Renderer => _renderer;

    /// <summary>
    /// Gets the document extent in document-space units.
    /// </summary>
    public ProEditDocumentExtent Extent => _extent;

    /// <summary>
    /// Gets the viewport width in screen units.
    /// </summary>
    public float ViewportWidth => _viewportWidth;

    /// <summary>
    /// Gets the viewport height in screen units.
    /// </summary>
    public float ViewportHeight => _viewportHeight;

    /// <summary>
    /// Gets or sets the zoom factor.
    /// </summary>
    public float Zoom
    {
        get => _zoom;
        set => SetZoom(value);
    }

    /// <summary>
    /// Gets the active zoom mode.
    /// </summary>
    public ProEditDocumentZoomMode ZoomMode => _zoomMode;

    /// <summary>
    /// Gets or sets the page count used by <see cref="ProEditDocumentZoomMode.MultiplePages"/>.
    /// </summary>
    public int MultiplePagesPerRow
    {
        get => _multiplePagesPerRow;
        set
        {
            var resolved = Math.Max(1, value);
            if (_multiplePagesPerRow == resolved)
            {
                return;
            }

            _multiplePagesPerRow = resolved;
            if (_zoomMode == ProEditDocumentZoomMode.MultiplePages)
            {
                ApplyZoomMode(ProEditDocumentZoomMode.MultiplePages, preserveCenter: false);
            }
        }
    }

    /// <summary>
    /// Gets or sets the horizontal scroll offset in document-space units.
    /// </summary>
    public float ScrollX
    {
        get => _scrollX;
        set => SetScroll(value, _scrollY);
    }

    /// <summary>
    /// Gets or sets the vertical scroll offset in document-space units.
    /// </summary>
    public float ScrollY
    {
        get => _scrollY;
        set => SetScroll(_scrollX, value);
    }

    /// <summary>
    /// Gets the horizontal scroll offset in screen units.
    /// </summary>
    public float ScreenScrollX => _scrollX * MathF.Max(MinimumZoom, _zoom);

    /// <summary>
    /// Gets the vertical scroll offset in screen units.
    /// </summary>
    public float ScreenScrollY => _scrollY * MathF.Max(MinimumZoom, _zoom);

    /// <summary>
    /// Gets the document extent width in screen units.
    /// </summary>
    public float ScreenExtentWidth => MathF.Max(_viewportWidth, _extent.Width * MathF.Max(MinimumZoom, _zoom));

    /// <summary>
    /// Gets the document extent height in screen units.
    /// </summary>
    public float ScreenExtentHeight => MathF.Max(_viewportHeight, _extent.Height * MathF.Max(MinimumZoom, _zoom));

    /// <summary>
    /// Gets or sets a value indicating whether text mutations are blocked.
    /// </summary>
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
            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the caret is rendered in read-only mode.
    /// </summary>
    public bool ShowReadOnlyCaret
    {
        get => _showReadOnlyCaret;
        set
        {
            if (_showReadOnlyCaret == value)
            {
                return;
            }

            _showReadOnlyCaret = value;
            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether Enter inserts a paragraph break.
    /// </summary>
    public bool AcceptsReturn
    {
        get => _acceptsReturn;
        set => _acceptsReturn = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether Tab inserts a tab character.
    /// </summary>
    public bool AcceptsTab
    {
        get => _acceptsTab;
        set => _acceptsTab = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether HarfBuzz shaping is used.
    /// </summary>
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
            _session.RefreshLayout();
            UpdateExtent();
            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether rendered pages are cached as Skia pictures.
    /// </summary>
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
            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether invisible characters are rendered.
    /// </summary>
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
            OnChanged();
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether layout guides are rendered.
    /// </summary>
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
            OnChanged();
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether page gridlines are rendered.
    /// </summary>
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
            OnChanged();
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets the page flow used by the layout.
    /// </summary>
    public PageFlowDirection PageFlow
    {
        get => _session.LayoutSettings.PageFlow;
        set
        {
            if (_session.LayoutSettings.PageFlow == value)
            {
                return;
            }

            _session.LayoutSettings.PageFlow = value;
            _session.RefreshLayout();
            UpdateExtent();
            CoerceScroll();
            OnChanged();
            OnViewportChanged();
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether page pagination is enabled.
    /// </summary>
    public bool UsePagination
    {
        get => _session.LayoutSettings.UsePagination;
        set
        {
            if (_session.LayoutSettings.UsePagination == value)
            {
                return;
            }

            _session.LayoutSettings.UsePagination = value;
            _session.RefreshLayout();
            UpdateExtent();
            CoerceScroll();
            OnChanged();
            OnViewportChanged();
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the hosting UI should display rulers.
    /// </summary>
    public bool ShowRuler
    {
        get => _showRuler;
        set
        {
            if (_showRuler == value)
            {
                return;
            }

            _showRuler = value;
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the hosting UI should display a navigation pane.
    /// </summary>
    public bool ShowNavigationPane
    {
        get => _showNavigationPane;
        set
        {
            if (_showNavigationPane == value)
            {
                return;
            }

            _showNavigationPane = value;
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets the page movement mode used by editor view commands.
    /// </summary>
    public PageFlowDirection PageMovement
    {
        get => PageFlow;
        set => PageFlow = value;
    }

    /// <summary>
    /// Gets or sets the logical editor view mode.
    /// </summary>
    public EditorViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value)
            {
                return;
            }

            var previous = _viewMode;
            _viewMode = value;
            ApplyViewMode(previous, value);
            OnViewOptionsChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether pointer panning is enabled.
    /// </summary>
    public bool IsPanEnabled
    {
        get => _isPanEnabled;
        set
        {
            if (_isPanEnabled == value)
            {
                return;
            }

            _isPanEnabled = value;
            if (!value)
            {
                EndPan();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a pan gesture is active.
    /// </summary>
    public bool IsPanning => _isPanning;

    /// <summary>
    /// Loads a document into the host.
    /// </summary>
    /// <param name="document">The document to load. A new empty document is used when null.</param>
    public void LoadDocument(Document? document)
    {
        var pageFlow = _session.LayoutSettings.PageFlow;
        var usePagination = _session.LayoutSettings.UsePagination;
        _session.Changed -= OnSessionChanged;
        var resolvedDocument = document ?? new Document();
        ConfigureMeasurer(resolvedDocument);
        _session = _kernel.CreateSession(_textMeasurer, resolvedDocument);
        _session.LayoutSettings.PageFlow = pageFlow;
        _session.LayoutSettings.UsePagination = usePagination;
        _commandRouter = RegisterWordEditorServices();
        _inputRouter = CreateInputRouter();
        _session.Changed += OnSessionChanged;
        _scrollX = 0f;
        _scrollY = 0f;
        UpdateLayoutForViewport();
        UpdateExtent();
        CoerceScroll();
        OnChanged();
        OnViewportChanged();
    }

    /// <summary>
    /// Updates the viewport size in screen units.
    /// </summary>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    public void SetViewport(float width, float height)
    {
        var resolvedWidth = SanitizeViewportDimension(width, DefaultViewportWidth);
        var resolvedHeight = SanitizeViewportDimension(height, DefaultViewportHeight);
        if (NearlyEqual(_viewportWidth, resolvedWidth) && NearlyEqual(_viewportHeight, resolvedHeight))
        {
            return;
        }

        _viewportWidth = resolvedWidth;
        _viewportHeight = resolvedHeight;
        UpdateLayoutForViewport();
        UpdateExtent();
        if (_zoomMode != ProEditDocumentZoomMode.Custom)
        {
            ApplyZoomMode(_zoomMode, preserveCenter: false);
            CoerceScroll();
            OnViewportChanged();
            return;
        }

        CoerceScroll();
        OnViewportChanged();
    }

    /// <summary>
    /// Sets the zoom factor.
    /// </summary>
    /// <param name="zoom">The requested zoom factor.</param>
    public void SetZoom(float zoom)
    {
        SetZoom(zoom, ProEditDocumentZoomMode.Custom);
    }

    /// <summary>
    /// Sets the zoom factor and mode.
    /// </summary>
    /// <param name="zoom">The requested zoom factor.</param>
    /// <param name="mode">The zoom mode.</param>
    /// <param name="preserveCenter">True to keep the viewport center anchored.</param>
    public void SetZoom(float zoom, ProEditDocumentZoomMode mode, bool preserveCenter = true)
    {
        var resolved = Math.Clamp(NormalizeFinite(zoom, 1f), MinimumZoom, MaximumZoom);
        if (NearlyEqual(_zoom, resolved) && _zoomMode == mode)
        {
            return;
        }

        var center = preserveCenter
            ? ScreenToDocument(_viewportWidth / 2f, _viewportHeight / 2f)
            : default;
        _zoom = resolved;
        _zoomMode = mode;
        UpdateLayoutForViewport();
        UpdateExtent();
        if (preserveCenter)
        {
            SetScroll(center.X - (_viewportWidth / 2f) / _zoom, center.Y - (_viewportHeight / 2f) / _zoom);
        }
        else
        {
            CoerceScroll();
        }

        OnChanged();
        OnViewportChanged();
    }

    /// <summary>
    /// Zooms around a screen-space anchor point.
    /// </summary>
    /// <param name="factor">The multiplicative zoom factor.</param>
    /// <param name="anchorX">The anchor x-coordinate in screen units.</param>
    /// <param name="anchorY">The anchor y-coordinate in screen units.</param>
    public void ZoomBy(float factor, float anchorX, float anchorY)
    {
        var oldZoom = _zoom;
        var documentPoint = ScreenToDocument(anchorX, anchorY);
        SetZoom(_zoom * NormalizeFinite(factor, 1f), ProEditDocumentZoomMode.Custom, preserveCenter: false);
        if (NearlyEqual(oldZoom, _zoom))
        {
            return;
        }

        SetScroll(documentPoint.X - anchorX / _zoom, documentPoint.Y - anchorY / _zoom);
    }

    /// <summary>
    /// Sets the scroll offset in document-space units.
    /// </summary>
    /// <param name="x">The horizontal scroll offset.</param>
    /// <param name="y">The vertical scroll offset.</param>
    public void SetScroll(float x, float y)
    {
        var oldX = _scrollX;
        var oldY = _scrollY;
        _scrollX = NormalizeFinite(x, 0f);
        _scrollY = NormalizeFinite(y, 0f);
        CoerceScroll();
        if (NearlyEqual(oldX, _scrollX) && NearlyEqual(oldY, _scrollY))
        {
            return;
        }

        OnChanged();
        OnViewportChanged();
    }

    /// <summary>
    /// Sets the scroll offset in screen units.
    /// </summary>
    /// <param name="x">The horizontal screen-space offset.</param>
    /// <param name="y">The vertical screen-space offset.</param>
    public void SetScreenScroll(float x, float y)
    {
        var zoom = MathF.Max(MinimumZoom, _zoom);
        SetScroll(x / zoom, y / zoom);
    }

    /// <summary>
    /// Scrolls by a delta in document-space units.
    /// </summary>
    /// <param name="deltaX">The horizontal delta.</param>
    /// <param name="deltaY">The vertical delta.</param>
    public void ScrollBy(float deltaX, float deltaY)
    {
        SetScroll(_scrollX + deltaX, _scrollY + deltaY);
    }

    /// <summary>
    /// Scrolls by a delta in screen units.
    /// </summary>
    /// <param name="deltaX">The horizontal screen-space delta.</param>
    /// <param name="deltaY">The vertical screen-space delta.</param>
    public void ScrollScreenBy(float deltaX, float deltaY)
    {
        var zoom = MathF.Max(MinimumZoom, _zoom);
        ScrollBy(deltaX / zoom, deltaY / zoom);
    }

    /// <summary>
    /// Handles a pointer wheel delta using the same zoom and scroll policy as the editor surface.
    /// </summary>
    /// <param name="deltaX">The horizontal wheel delta.</param>
    /// <param name="deltaY">The vertical wheel delta.</param>
    /// <param name="screenX">The wheel location x-coordinate in screen units.</param>
    /// <param name="screenY">The wheel location y-coordinate in screen units.</param>
    /// <param name="modifiers">The active modifiers.</param>
    /// <returns>True when the wheel input was handled.</returns>
    public bool HandleWheel(float deltaX, float deltaY, float screenX, float screenY, EditorModifiers modifiers)
    {
        if ((modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0)
        {
            if (deltaY > 0f)
            {
                ZoomBy(1f + ZoomStep, screenX, screenY);
                return true;
            }

            if (deltaY < 0f)
            {
                ZoomBy(1f / (1f + ZoomStep), screenX, screenY);
                return true;
            }

            return false;
        }

        if ((modifiers & EditorModifiers.Shift) != 0 && NearlyEqual(deltaX, 0f))
        {
            deltaX = deltaY;
            deltaY = 0f;
        }

        ScrollScreenBy(-deltaX, -deltaY);
        return true;
    }

    /// <summary>
    /// Starts a pan gesture from a screen-space point.
    /// </summary>
    /// <param name="screenX">The x-coordinate.</param>
    /// <param name="screenY">The y-coordinate.</param>
    /// <returns>True when panning started.</returns>
    public bool BeginPan(float screenX, float screenY)
    {
        if (!_isPanEnabled)
        {
            return false;
        }

        _isPanning = true;
        _panStartX = screenX;
        _panStartY = screenY;
        _panStartScrollX = _scrollX;
        _panStartScrollY = _scrollY;
        return true;
    }

    /// <summary>
    /// Updates an active pan gesture from a screen-space point.
    /// </summary>
    /// <param name="screenX">The x-coordinate.</param>
    /// <param name="screenY">The y-coordinate.</param>
    /// <returns>True when panning was active.</returns>
    public bool UpdatePan(float screenX, float screenY)
    {
        if (!_isPanning)
        {
            return false;
        }

        var zoom = MathF.Max(MinimumZoom, _zoom);
        SetScroll(
            _panStartScrollX - (screenX - _panStartX) / zoom,
            _panStartScrollY - (screenY - _panStartY) / zoom);
        return true;
    }

    /// <summary>
    /// Ends an active pan gesture.
    /// </summary>
    public void EndPan()
    {
        _isPanning = false;
    }

    /// <summary>
    /// Converts a screen-space point to document-space coordinates.
    /// </summary>
    /// <param name="x">The screen-space x-coordinate.</param>
    /// <param name="y">The screen-space y-coordinate.</param>
    /// <returns>The document-space point.</returns>
    public DocPoint ScreenToDocument(float x, float y)
    {
        var zoom = MathF.Max(MinimumZoom, _zoom);
        return new DocPoint(_scrollX + x / zoom, _scrollY + y / zoom);
    }

    /// <summary>
    /// Converts a document-space point to screen-space coordinates.
    /// </summary>
    /// <param name="x">The document-space x-coordinate.</param>
    /// <param name="y">The document-space y-coordinate.</param>
    /// <returns>The screen-space point.</returns>
    public DocPoint DocumentToScreen(float x, float y)
    {
        return new DocPoint((x - _scrollX) * _zoom, (y - _scrollY) * _zoom);
    }

    /// <summary>
    /// Renders the document into a Skia canvas.
    /// </summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="width">The target width in screen units.</param>
    /// <param name="height">The target height in screen units.</param>
    public void Render(SKCanvas canvas, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        SetViewport(width, height);
        UpdateRenderOptions(width, height);

        canvas.Save();
        canvas.Scale(_zoom);
        canvas.Translate(-_scrollX, -_scrollY);
        _renderer.Render(canvas, _session.Document, _session.Layout, _renderOptions);
        canvas.Restore();
    }

    /// <summary>
    /// Handles text input.
    /// </summary>
    /// <param name="text">The text input.</param>
    /// <param name="modifiers">The active modifiers.</param>
    /// <returns>True when the input was handled.</returns>
    public bool HandleTextInput(ReadOnlySpan<char> text, EditorModifiers modifiers)
    {
        return _inputRouter.HandleTextInput(text, modifiers);
    }

    /// <summary>
    /// Handles a key input event.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="kind">The event kind.</param>
    /// <param name="modifiers">The active modifiers.</param>
    /// <returns>True when the input was handled.</returns>
    public bool HandleKey(EditorKey key, EditorKeyEventKind kind, EditorModifiers modifiers)
    {
        return _inputRouter.HandleKey(key, kind, modifiers);
    }

    /// <summary>
    /// Handles a pointer event whose coordinates are already in document space.
    /// </summary>
    /// <param name="pointerEvent">The pointer event.</param>
    /// <returns>True when the input was handled.</returns>
    public bool HandlePointer(in EditorPointerEvent pointerEvent)
    {
        return _inputRouter.HandlePointer(pointerEvent);
    }

    /// <summary>
    /// Inserts text at the current selection.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    public void InsertText(string text)
    {
        _inputRouter.HandleTextInput((text ?? string.Empty).AsSpan(), EditorModifiers.None);
    }

    /// <summary>
    /// Deletes the character or selection before the caret.
    /// </summary>
    public void Backspace()
    {
        _inputRouter.HandleKey(EditorKey.Backspace, EditorKeyEventKind.Down, EditorModifiers.None);
    }

    /// <summary>
    /// Deletes the character or selection after the caret.
    /// </summary>
    public void DeleteForward()
    {
        _inputRouter.HandleKey(EditorKey.Delete, EditorKeyEventKind.Down, EditorModifiers.None);
    }

    /// <summary>
    /// Selects the whole document.
    /// </summary>
    public void SelectAll()
    {
        _inputRouter.HandleKey(EditorKey.A, EditorKeyEventKind.Down, EditorModifiers.Control);
    }

    /// <summary>
    /// Zooms in by one editor zoom step.
    /// </summary>
    public void ZoomIn()
    {
        SetZoom(_zoom + ZoomStep, ProEditDocumentZoomMode.Custom);
    }

    /// <summary>
    /// Zooms out by one editor zoom step.
    /// </summary>
    public void ZoomOut()
    {
        SetZoom(_zoom - ZoomStep, ProEditDocumentZoomMode.Custom);
    }

    /// <summary>
    /// Restores 100% zoom.
    /// </summary>
    public void ZoomToDefault()
    {
        SetZoom(1f, ProEditDocumentZoomMode.Custom);
    }

    /// <summary>
    /// Sets zoom from a percent value.
    /// </summary>
    /// <param name="percent">The requested percent.</param>
    public void ZoomToPercent(float percent)
    {
        SetZoom(percent / 100f, ProEditDocumentZoomMode.Custom);
    }

    /// <summary>
    /// Fits page width to the viewport.
    /// </summary>
    public void ZoomToPageWidth()
    {
        SetZoom(ComputePageWidthZoom(), ProEditDocumentZoomMode.PageWidth, preserveCenter: false);
    }

    /// <summary>
    /// Fits one whole page to the viewport.
    /// </summary>
    public void ZoomToWholePage()
    {
        SetZoom(ComputeWholePageZoom(), ProEditDocumentZoomMode.WholePage, preserveCenter: false);
    }

    /// <summary>
    /// Fits multiple pages per row to the viewport.
    /// </summary>
    /// <param name="pagesPerRow">The page count per row.</param>
    public void ZoomToMultiplePages(int pagesPerRow = DefaultMultiplePages)
    {
        _multiplePagesPerRow = Math.Max(1, pagesPerRow);
        SetZoom(ComputeMultiplePagesZoom(_multiplePagesPerRow), ProEditDocumentZoomMode.MultiplePages, preserveCenter: false);
    }

    /// <summary>
    /// Scrolls to a page.
    /// </summary>
    /// <param name="pageIndex">The zero-based page index.</param>
    public void ScrollToPage(int pageIndex)
    {
        var pages = _session.Layout.Pages;
        if (pages.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(pageIndex, 0, pages.Count - 1);
        var page = pages[index];
        if (_session.Layout.Settings.PageFlow == PageFlowDirection.Horizontal)
        {
            SetScroll(page.Bounds.Left, _scrollY);
        }
        else
        {
            SetScroll(_scrollX, page.Bounds.Top);
        }
    }

    /// <summary>
    /// Opens a zoom dialog when a hosting UI provides one.
    /// </summary>
    /// <returns>A completed value task for the default host implementation.</returns>
    public ValueTask OpenZoomDialogAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Resolves a registered editor service.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="service">The resolved service.</param>
    /// <returns>True when the service exists.</returns>
    public bool TryGetService<T>(out T service) where T : class
    {
        return _kernel.Services.TryGet(out service);
    }

    /// <summary>
    /// Registers or replaces an editor service.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="service">The service instance.</param>
    public void RegisterService<T>(T service) where T : class
    {
        _kernel.Services.Register(service);
        _inputRouter = CreateInputRouter();
    }

    /// <summary>
    /// Unregisters an editor service.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>True when a service was removed.</returns>
    public bool UnregisterService<T>() where T : class
    {
        var removed = _kernel.Services.Remove<T>();
        if (removed)
        {
            _inputRouter = CreateInputRouter();
        }

        return removed;
    }

    /// <summary>
    /// Releases resources used by the host.
    /// </summary>
    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        _fontResolver?.Dispose();
        _fontResolver = null;
    }

    private static RenderOptions CreateDefaultRenderOptions()
    {
        return new RenderOptions
        {
            BackgroundColor = new DocColor(238, 241, 245),
            PageColor = DocColor.White,
            PageBorderColor = new DocColor(220, 220, 220),
            PageBorderThickness = 1f,
            TextColor = DocColor.Black,
            SelectionColor = DocColor.SelectionBlue,
            CaretColor = DocColor.Black,
            CaretThickness = 1.5f,
            UseHarfBuzz = true,
            UsePictureCache = true
        };
    }

    private EditorCommandInputRouter CreateInputRouter()
    {
        _kernel.Services.TryGet<IUndoRedoService>(out var undoRedo);
        _kernel.Services.TryGet<IClipboardService>(out var clipboard);
        _kernel.Services.TryGet<ITableSelectionSnapshotProvider>(out var tableSelectionProvider);
        _kernel.Services.TryGet<IContentControlInteractionService>(out var contentControls);
        _kernel.Services.TryGet<IAutoCorrectService>(out var autoCorrect);
        return new EditorCommandInputRouter(
            _kernel.Commands,
            _session,
            undoRedo,
            clipboard,
            tableSelectionProvider,
            contentControls,
            autoCorrect,
            acceptsTabProvider: () => _acceptsTab,
            acceptsReturnProvider: () => _acceptsReturn,
            isReadOnlyProvider: () => _isReadOnly);
    }

    private IEditorCommandRouter RegisterWordEditorServices()
    {
        var router = EditorHomeServiceRegistry.Register(
            _kernel.Services,
            _kernel.Commands,
            _session,
            viewOptionsService: this,
            documentFactory: static () => new Document());
        _kernel.Services.Register<IEditorZoomService>(this);
        return router;
    }

    private void ConfigureMeasurer(Document document)
    {
        _fontResolver?.Dispose();
        _fontResolver = new SkiaDocumentFontResolver(document.Fonts);
        _textMeasurer.TypefaceResolver = _fontResolver;
        _renderer.TypefaceResolver = _fontResolver;
    }

    private static IEnumerable<IEditorModule> CreateDefaultModules()
    {
        yield return new BasicEditingModule();
    }

    private void RegisterModules(IEnumerable<IEditorModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var module in modules)
        {
            _kernel.AddModule(module);
        }
    }

    private void UpdateLayoutForViewport()
    {
        var zoom = MathF.Max(MinimumZoom, _zoom);
        _session.UpdateLayout(
            MathF.Max(1f, _viewportWidth / zoom),
            MathF.Max(1f, _viewportHeight / zoom));
    }

    private float ComputePageWidthZoom()
    {
        if (_viewportWidth <= 0f)
        {
            return _zoom;
        }

        if (!TryGetLayoutHorizontalBounds(out _, out _, out var maxPageWidth) || maxPageWidth <= 0f)
        {
            return _zoom;
        }

        var targetWidth = maxPageWidth + _session.Layout.Settings.PageGap;
        return _viewportWidth / MathF.Max(1f, targetWidth);
    }

    private float ComputeWholePageZoom()
    {
        var pages = _session.Layout.Pages;
        if (_viewportWidth <= 0f || _viewportHeight <= 0f || pages.Count == 0)
        {
            return _zoom;
        }

        var firstPage = pages[0];
        var gap = _session.Layout.Settings.PageGap;
        var width = MathF.Max(1f, firstPage.Bounds.Width + gap);
        var height = MathF.Max(1f, firstPage.Bounds.Height + gap);
        return MathF.Min(_viewportWidth / width, _viewportHeight / height);
    }

    private float ComputeMultiplePagesZoom(int pagesPerRow)
    {
        if (_viewportWidth <= 0f)
        {
            return _zoom;
        }

        if (!TryGetLayoutHorizontalBounds(out _, out _, out var maxPageWidth) || maxPageWidth <= 0f)
        {
            return _zoom;
        }

        var count = Math.Max(1, pagesPerRow);
        var gap = _session.Layout.Settings.PageGap;
        var targetWidth = (maxPageWidth * count) + (gap * count);
        return _viewportWidth / MathF.Max(1f, targetWidth);
    }

    private bool TryGetLayoutHorizontalBounds(out float minX, out float maxX, out float maxPageWidth)
    {
        var pages = _session.Layout.Pages;
        if (pages.Count == 0)
        {
            minX = 0f;
            maxX = 0f;
            maxPageWidth = 0f;
            return false;
        }

        var bounds = pages[0].Bounds;
        minX = bounds.Left;
        maxX = bounds.Right;
        maxPageWidth = bounds.Width;
        for (var i = 1; i < pages.Count; i++)
        {
            bounds = pages[i].Bounds;
            minX = MathF.Min(minX, bounds.Left);
            maxX = MathF.Max(maxX, bounds.Right);
            maxPageWidth = MathF.Max(maxPageWidth, bounds.Width);
        }

        return true;
    }

    private void ApplyZoomMode(ProEditDocumentZoomMode mode, bool preserveCenter)
    {
        switch (mode)
        {
            case ProEditDocumentZoomMode.PageWidth:
                SetZoom(ComputePageWidthZoom(), ProEditDocumentZoomMode.PageWidth, preserveCenter);
                break;
            case ProEditDocumentZoomMode.WholePage:
                SetZoom(ComputeWholePageZoom(), ProEditDocumentZoomMode.WholePage, preserveCenter);
                break;
            case ProEditDocumentZoomMode.MultiplePages:
                SetZoom(ComputeMultiplePagesZoom(_multiplePagesPerRow), ProEditDocumentZoomMode.MultiplePages, preserveCenter);
                break;
            default:
                SetZoom(_zoom, ProEditDocumentZoomMode.Custom, preserveCenter);
                break;
        }
    }

    private void ApplyViewMode(EditorViewMode previous, EditorViewMode mode)
    {
        if (previous == EditorViewMode.ReadMode)
        {
            RestoreZoom();
        }

        switch (mode)
        {
            case EditorViewMode.ReadMode:
                SaveZoom();
                UsePagination = true;
                ShowLayout = true;
                PageFlow = PageFlowDirection.Horizontal;
                ZoomToWholePage();
                break;
            case EditorViewMode.PrintLayout:
                UsePagination = true;
                ShowLayout = true;
                PageFlow = PageFlowDirection.Vertical;
                break;
            case EditorViewMode.WebLayout:
            case EditorViewMode.Outline:
            case EditorViewMode.Draft:
                UsePagination = false;
                ShowLayout = false;
                PageFlow = PageFlowDirection.Vertical;
                break;
            default:
                UsePagination = true;
                ShowLayout = true;
                PageFlow = PageFlowDirection.Vertical;
                break;
        }
    }

    private void SaveZoom()
    {
        _savedZoom = _zoom;
        _savedZoomMode = _zoomMode;
        _hasSavedZoom = true;
    }

    private void RestoreZoom()
    {
        if (!_hasSavedZoom)
        {
            return;
        }

        ApplyZoomMode(_savedZoomMode, preserveCenter: false);
        if (_savedZoomMode == ProEditDocumentZoomMode.Custom)
        {
            SetZoom(_savedZoom, ProEditDocumentZoomMode.Custom, preserveCenter: false);
        }

        _hasSavedZoom = false;
    }

    private void UpdateRenderOptions(int width, int height)
    {
        var zoom = MathF.Max(MinimumZoom, _zoom);
        _renderOptions.ZoomFactor = zoom;
        _renderOptions.SvgRasterizationScale = zoom;
        _renderOptions.Caret = _session.Caret;
        _renderOptions.Selection = _session.Selection.IsEmpty ? null : _session.Selection;
        _renderOptions.SelectionRanges = _session.SelectionRanges;
        _renderOptions.ShowCaret = !_isReadOnly || _showReadOnlyCaret;
        _renderOptions.SelectedFloatingObjectId = _session.SelectedFloatingObjectId;
        _renderOptions.SelectedFloatingObjectIds = _session.SelectedFloatingObjectIds;
        _renderOptions.DirtyPages = _session.DirtyPages;
        _renderOptions.DirtyVersion = _session.DirtyVersion;
        _renderOptions.VisibleBounds = new DocRect(
            _scrollX,
            _scrollY,
            MathF.Max(0f, width / zoom),
            MathF.Max(0f, height / zoom));
    }

    private void UpdateExtent()
    {
        var layout = _session.Layout;
        var width = 0f;
        var height = 0f;
        for (var i = 0; i < layout.Pages.Count; i++)
        {
            var bounds = layout.Pages[i].Bounds;
            width = MathF.Max(width, bounds.Right + layout.Settings.PageGap);
            height = MathF.Max(height, bounds.Bottom + layout.Settings.PageGap);
        }

        if (width <= 0f)
        {
            width = layout.Settings.UsePagination
                ? layout.Settings.PageWidth + layout.Settings.PageGap * 2f
                : layout.Settings.ViewportWidth;
        }

        if (height <= 0f)
        {
            height = layout.Settings.UsePagination
                ? layout.Settings.PageHeight + layout.Settings.PageGap * 2f
                : MathF.Max(layout.ContentHeight, layout.Settings.ViewportHeight);
        }

        _extent = new ProEditDocumentExtent(width, height);
    }

    private void CoerceScroll()
    {
        var viewportDocumentWidth = _viewportWidth / MathF.Max(MinimumZoom, _zoom);
        var viewportDocumentHeight = _viewportHeight / MathF.Max(MinimumZoom, _zoom);
        var maxX = MathF.Max(0f, _extent.Width - viewportDocumentWidth);
        var maxY = MathF.Max(0f, _extent.Height - viewportDocumentHeight);
        _scrollX = Math.Clamp(_scrollX, 0f, maxX);
        _scrollY = Math.Clamp(_scrollY, 0f, maxY);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        UpdateExtent();
        CoerceScroll();
        OnChanged();
        OnViewportChanged();
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewportChanged()
    {
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewOptionsChanged()
    {
        ViewOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static float SanitizeViewportDimension(float value, float fallback)
    {
        return MathF.Max(1f, NormalizeFinite(value, fallback));
    }

    private static float NormalizeFinite(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return MathF.Abs(left - right) < 0.001f;
    }
}
