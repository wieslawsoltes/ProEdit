using Microsoft.Maui.Controls;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using CoreHost = ProEdit.Controls.Skia.ProEditDocumentHost;

namespace ProEdit.Controls.Skia.Maui;

/// <summary>
/// Base class for .NET MAUI ProEdit document controls.
/// </summary>
public abstract class ProEditDocumentControlBase : SKCanvasView
{
    private const float WheelDeltaScale = 48f;

    /// <summary>
    /// Defines the <see cref="Document"/> property.
    /// </summary>
    public static readonly BindableProperty DocumentProperty =
        BindableProperty.Create(
            nameof(Document),
            typeof(Document),
            typeof(ProEditDocumentControlBase),
            null,
            propertyChanged: OnDocumentPropertyChanged);

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly BindableProperty ZoomProperty =
        BindableProperty.Create(
            nameof(Zoom),
            typeof(double),
            typeof(ProEditDocumentControlBase),
            1d,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ZoomMode"/> property.
    /// </summary>
    public static readonly BindableProperty ZoomModeProperty =
        BindableProperty.Create(
            nameof(ZoomMode),
            typeof(ProEditDocumentZoomMode),
            typeof(ProEditDocumentControlBase),
            ProEditDocumentZoomMode.Custom,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="MultiplePagesPerRow"/> property.
    /// </summary>
    public static readonly BindableProperty MultiplePagesPerRowProperty =
        BindableProperty.Create(
            nameof(MultiplePagesPerRow),
            typeof(int),
            typeof(ProEditDocumentControlBase),
            2,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ScrollX"/> property.
    /// </summary>
    public static readonly BindableProperty ScrollXProperty =
        BindableProperty.Create(
            nameof(ScrollX),
            typeof(double),
            typeof(ProEditDocumentControlBase),
            0d,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ScrollY"/> property.
    /// </summary>
    public static readonly BindableProperty ScrollYProperty =
        BindableProperty.Create(
            nameof(ScrollY),
            typeof(double),
            typeof(ProEditDocumentControlBase),
            0d,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="IsReadOnly"/> property.
    /// </summary>
    public static readonly BindableProperty IsReadOnlyProperty =
        BindableProperty.Create(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            true,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowReadOnlyCaret"/> property.
    /// </summary>
    public static readonly BindableProperty ShowReadOnlyCaretProperty =
        BindableProperty.Create(
            nameof(ShowReadOnlyCaret),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            false,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="AcceptsReturn"/> property.
    /// </summary>
    public static readonly BindableProperty AcceptsReturnProperty =
        BindableProperty.Create(
            nameof(AcceptsReturn),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            true,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="AcceptsTab"/> property.
    /// </summary>
    public static readonly BindableProperty AcceptsTabProperty =
        BindableProperty.Create(
            nameof(AcceptsTab),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            false,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="UseHarfBuzz"/> property.
    /// </summary>
    public static readonly BindableProperty UseHarfBuzzProperty =
        BindableProperty.Create(
            nameof(UseHarfBuzz),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            true,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="UsePictureCache"/> property.
    /// </summary>
    public static readonly BindableProperty UsePictureCacheProperty =
        BindableProperty.Create(
            nameof(UsePictureCache),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            true,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowInvisibles"/> property.
    /// </summary>
    public static readonly BindableProperty ShowInvisiblesProperty =
        BindableProperty.Create(
            nameof(ShowInvisibles),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            false,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowLayout"/> property.
    /// </summary>
    public static readonly BindableProperty ShowLayoutProperty =
        BindableProperty.Create(
            nameof(ShowLayout),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            false,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowGridlines"/> property.
    /// </summary>
    public static readonly BindableProperty ShowGridlinesProperty =
        BindableProperty.Create(
            nameof(ShowGridlines),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            false,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="UsePagination"/> property.
    /// </summary>
    public static readonly BindableProperty UsePaginationProperty =
        BindableProperty.Create(
            nameof(UsePagination),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            true,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="PageFlow"/> property.
    /// </summary>
    public static readonly BindableProperty PageFlowProperty =
        BindableProperty.Create(
            nameof(PageFlow),
            typeof(PageFlowDirection),
            typeof(ProEditDocumentControlBase),
            PageFlowDirection.Vertical,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowRuler"/> property.
    /// </summary>
    public static readonly BindableProperty ShowRulerProperty =
        BindableProperty.Create(
            nameof(ShowRuler),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            true,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowNavigationPane"/> property.
    /// </summary>
    public static readonly BindableProperty ShowNavigationPaneProperty =
        BindableProperty.Create(
            nameof(ShowNavigationPane),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            false,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ViewMode"/> property.
    /// </summary>
    public static readonly BindableProperty ViewModeProperty =
        BindableProperty.Create(
            nameof(ViewMode),
            typeof(EditorViewMode),
            typeof(ProEditDocumentControlBase),
            EditorViewMode.PrintLayout,
            propertyChanged: OnViewportPropertyChanged);

    /// <summary>
    /// Defines the <see cref="IsPanEnabled"/> property.
    /// </summary>
    public static readonly BindableProperty IsPanEnabledProperty =
        BindableProperty.Create(
            nameof(IsPanEnabled),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            true,
            propertyChanged: OnViewportPropertyChanged);

    private readonly CoreHost _host;
    private bool _isSynchronizingHostProperties;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProEditDocumentControlBase"/> class.
    /// </summary>
    protected ProEditDocumentControlBase()
        : this(new CoreHost())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProEditDocumentControlBase"/> class.
    /// </summary>
    /// <param name="documentHost">The shared document host to render and edit through.</param>
    protected ProEditDocumentControlBase(CoreHost documentHost)
    {
        _host = documentHost ?? throw new ArgumentNullException(nameof(documentHost));
        IgnorePixelScaling = true;
        EnableTouchEvents = true;
        PaintSurface += OnPaintSurface;
        Touch += OnTouch;
        SizeChanged += OnSizeChanged;
        _host.Changed += OnHostChanged;
        _host.ViewportChanged += OnHostViewportChanged;
        _host.ViewOptionsChanged += OnHostViewOptionsChanged;
        ApplyPropertiesToHost();
    }

    /// <summary>
    /// Gets the shared document host.
    /// </summary>
    public CoreHost DocumentHost => _host;

    /// <summary>
    /// Gets or sets the document to display.
    /// </summary>
    public Document? Document
    {
        get => (Document?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom factor.
    /// </summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the automatic zoom mode.
    /// </summary>
    public ProEditDocumentZoomMode ZoomMode
    {
        get => (ProEditDocumentZoomMode)GetValue(ZoomModeProperty);
        set => SetValue(ZoomModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the page count used by multiple-pages zoom.
    /// </summary>
    public int MultiplePagesPerRow
    {
        get => (int)GetValue(MultiplePagesPerRowProperty);
        set => SetValue(MultiplePagesPerRowProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll offset in document-space units.
    /// </summary>
    public double ScrollX
    {
        get => (double)GetValue(ScrollXProperty);
        set => SetValue(ScrollXProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll offset in document-space units.
    /// </summary>
    public double ScrollY
    {
        get => (double)GetValue(ScrollYProperty);
        set => SetValue(ScrollYProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether text mutations are blocked.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the caret is rendered in read-only mode.
    /// </summary>
    public bool ShowReadOnlyCaret
    {
        get => (bool)GetValue(ShowReadOnlyCaretProperty);
        set => SetValue(ShowReadOnlyCaretProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Enter inserts a paragraph break.
    /// </summary>
    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Tab inserts a tab character.
    /// </summary>
    public bool AcceptsTab
    {
        get => (bool)GetValue(AcceptsTabProperty);
        set => SetValue(AcceptsTabProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether HarfBuzz shaping is used.
    /// </summary>
    public bool UseHarfBuzz
    {
        get => (bool)GetValue(UseHarfBuzzProperty);
        set => SetValue(UseHarfBuzzProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether rendered pages are cached as Skia pictures.
    /// </summary>
    public bool UsePictureCache
    {
        get => (bool)GetValue(UsePictureCacheProperty);
        set => SetValue(UsePictureCacheProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether invisible characters are rendered.
    /// </summary>
    public bool ShowInvisibles
    {
        get => (bool)GetValue(ShowInvisiblesProperty);
        set => SetValue(ShowInvisiblesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether layout guides are rendered.
    /// </summary>
    public bool ShowLayout
    {
        get => (bool)GetValue(ShowLayoutProperty);
        set => SetValue(ShowLayoutProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether page gridlines are rendered.
    /// </summary>
    public bool ShowGridlines
    {
        get => (bool)GetValue(ShowGridlinesProperty);
        set => SetValue(ShowGridlinesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the document uses paginated layout.
    /// </summary>
    public bool UsePagination
    {
        get => (bool)GetValue(UsePaginationProperty);
        set => SetValue(UsePaginationProperty, value);
    }

    /// <summary>
    /// Gets or sets the page flow direction.
    /// </summary>
    public PageFlowDirection PageFlow
    {
        get => (PageFlowDirection)GetValue(PageFlowProperty);
        set => SetValue(PageFlowProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether hosting UI should show a ruler.
    /// </summary>
    public bool ShowRuler
    {
        get => (bool)GetValue(ShowRulerProperty);
        set => SetValue(ShowRulerProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether hosting UI should show a navigation pane.
    /// </summary>
    public bool ShowNavigationPane
    {
        get => (bool)GetValue(ShowNavigationPaneProperty);
        set => SetValue(ShowNavigationPaneProperty, value);
    }

    /// <summary>
    /// Gets or sets the logical editor view mode.
    /// </summary>
    public EditorViewMode ViewMode
    {
        get => (EditorViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether middle-button panning is enabled.
    /// </summary>
    public bool IsPanEnabled
    {
        get => (bool)GetValue(IsPanEnabledProperty);
        set => SetValue(IsPanEnabledProperty, value);
    }

    /// <summary>
    /// Forces the current document to be rendered again.
    /// </summary>
    public void Refresh()
    {
        _host.EditorSession.RefreshLayout();
        InvalidateControl();
    }

    /// <summary>
    /// Routes text input into the editor host.
    /// </summary>
    /// <param name="text">The text to input.</param>
    /// <returns>True when the input was handled.</returns>
    public bool HandleTextInput(string text)
    {
        var handled = _host.HandleTextInput((text ?? string.Empty).AsSpan(), EditorModifiers.None);
        if (handled)
        {
            InvalidateControl();
        }

        return handled;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var width = double.IsInfinity(widthConstraint)
            ? _host.Extent.Width * _host.Zoom
            : widthConstraint;
        var height = double.IsInfinity(heightConstraint)
            ? _host.Extent.Height * _host.Zoom
            : heightConstraint;
        return new Size(Math.Max(0d, width), Math.Max(0d, height));
    }

    private static void OnDocumentPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ProEditDocumentControlBase control)
        {
            return;
        }

        control._host.LoadDocument(newValue as Document);
        control.InvalidateControl();
    }

    private static void OnViewportPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ProEditDocumentControlBase control)
        {
            return;
        }

        if (control._isSynchronizingHostProperties)
        {
            return;
        }

        control.ApplyPropertiesToHost();
        control.InvalidateControl();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        _host.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        if (e.ActionType == SKTouchAction.WheelChanged)
        {
            if (_host.HandleWheel(
                0f,
                (e.WheelDelta / 120f) * WheelDeltaScale,
                e.Location.X,
                e.Location.Y,
                EditorModifiers.None))
            {
                SynchronizeViewportProperties();
                e.Handled = true;
                InvalidateControl();
            }

            return;
        }

        var button = ResolveButton(e.MouseButton, e.InContact);
        if (e.ActionType == SKTouchAction.Pressed
            && button == EditorPointerButton.Middle
            && _host.BeginPan(e.Location.X, e.Location.Y))
        {
            e.Handled = true;
            SynchronizeViewportProperties();
            InvalidateControl();
            return;
        }

        if (e.ActionType == SKTouchAction.Moved
            && _host.UpdatePan(e.Location.X, e.Location.Y))
        {
            e.Handled = true;
            SynchronizeViewportProperties();
            InvalidateControl();
            return;
        }

        if ((e.ActionType == SKTouchAction.Released || e.ActionType == SKTouchAction.Cancelled)
            && _host.IsPanning)
        {
            _host.EndPan();
            e.Handled = true;
            SynchronizeViewportProperties();
            InvalidateControl();
            return;
        }

        var kind = e.ActionType switch
        {
            SKTouchAction.Pressed => EditorPointerKind.Down,
            SKTouchAction.Moved => EditorPointerKind.Move,
            SKTouchAction.Released or SKTouchAction.Cancelled => EditorPointerKind.Up,
            _ => (EditorPointerKind?)null
        };

        if (!kind.HasValue)
        {
            return;
        }

        var documentPoint = _host.ScreenToDocument(e.Location.X, e.Location.Y);
        var pointerEvent = new EditorPointerEvent(
            kind.Value,
            documentPoint.X,
            documentPoint.Y,
            button,
            EditorModifiers.None,
            e.ActionType == SKTouchAction.Pressed ? 1 : 0);

        if (_host.HandlePointer(pointerEvent))
        {
            e.Handled = true;
            InvalidateControl();
        }
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        _host.SetViewport((float)Math.Max(1d, Width), (float)Math.Max(1d, Height));
        InvalidateSurface();
    }

    private void ApplyPropertiesToHost()
    {
        _host.IsReadOnly = IsReadOnly;
        _host.ShowReadOnlyCaret = ShowReadOnlyCaret;
        _host.AcceptsReturn = AcceptsReturn;
        _host.AcceptsTab = AcceptsTab;
        _host.UseHarfBuzz = UseHarfBuzz;
        _host.UsePictureCache = UsePictureCache;
        _host.ShowInvisibles = ShowInvisibles;
        _host.ShowLayout = ShowLayout;
        _host.ShowGridlines = ShowGridlines;
        _host.UsePagination = UsePagination;
        _host.PageFlow = PageFlow;
        _host.ShowRuler = ShowRuler;
        _host.ShowNavigationPane = ShowNavigationPane;
        _host.ViewMode = ViewMode;
        _host.IsPanEnabled = IsPanEnabled;
        ApplyZoomToHost();
        _host.SetScroll((float)ScrollX, (float)ScrollY);
    }

    private void ApplyZoomToHost()
    {
        _host.MultiplePagesPerRow = MultiplePagesPerRow;
        switch (ZoomMode)
        {
            case ProEditDocumentZoomMode.PageWidth:
                _host.ZoomToPageWidth();
                break;
            case ProEditDocumentZoomMode.WholePage:
                _host.ZoomToWholePage();
                break;
            case ProEditDocumentZoomMode.MultiplePages:
                _host.ZoomToMultiplePages(MultiplePagesPerRow);
                break;
            default:
                _host.SetZoom((float)Zoom, ProEditDocumentZoomMode.Custom);
                break;
        }
    }

    private void OnHostChanged(object? sender, EventArgs e)
    {
        InvalidateControl();
    }

    private void OnHostViewportChanged(object? sender, EventArgs e)
    {
        SynchronizeViewportProperties();
        InvalidateControl();
    }

    private void OnHostViewOptionsChanged(object? sender, EventArgs e)
    {
        SynchronizeViewportProperties();
        InvalidateControl();
    }

    private void SynchronizeViewportProperties()
    {
        _isSynchronizingHostProperties = true;
        try
        {
            SetValue(ZoomModeProperty, _host.ZoomMode);
            SetValue(MultiplePagesPerRowProperty, _host.MultiplePagesPerRow);
            SetValue(ZoomProperty, (double)_host.Zoom);
            SetValue(ScrollXProperty, (double)_host.ScrollX);
            SetValue(ScrollYProperty, (double)_host.ScrollY);
            SetValue(ShowInvisiblesProperty, _host.ShowInvisibles);
            SetValue(ShowLayoutProperty, _host.ShowLayout);
            SetValue(ShowGridlinesProperty, _host.ShowGridlines);
            SetValue(UsePaginationProperty, _host.UsePagination);
            SetValue(PageFlowProperty, _host.PageFlow);
            SetValue(ShowRulerProperty, _host.ShowRuler);
            SetValue(ShowNavigationPaneProperty, _host.ShowNavigationPane);
            SetValue(ViewModeProperty, _host.ViewMode);
            SetValue(IsPanEnabledProperty, _host.IsPanEnabled);
        }
        finally
        {
            _isSynchronizingHostProperties = false;
        }
    }

    private void InvalidateControl()
    {
        InvalidateMeasure();
        InvalidateSurface();
    }

    private static EditorPointerButton ResolveButton(SKMouseButton button, bool inContact)
    {
        return button switch
        {
            SKMouseButton.Left => EditorPointerButton.Primary,
            SKMouseButton.Right => EditorPointerButton.Secondary,
            SKMouseButton.Middle => EditorPointerButton.Middle,
            _ when inContact => EditorPointerButton.Primary,
            _ => EditorPointerButton.None
        };
    }
}
