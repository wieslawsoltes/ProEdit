using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using ProEdit.Primitives;
using CoreHost = ProEdit.Controls.Skia.ProEditDocumentHost;

namespace ProEdit.Controls.Skia.Avalonia;

/// <summary>
/// Base class for Avalonia ProEdit document controls.
/// </summary>
public abstract class ProEditDocumentControlBase : Control, ILogicalScrollable
{
    private const float WheelDeltaScale = 48f;
    private const double MultiClickDistanceThreshold = 6d;
    private static readonly TimeSpan MultiClickTimeThreshold = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Defines the <see cref="Document"/> property.
    /// </summary>
    public static readonly StyledProperty<Document?> DocumentProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, Document?>(nameof(Document));

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, double>(nameof(Zoom), 1d);

    /// <summary>
    /// Defines the <see cref="ZoomMode"/> property.
    /// </summary>
    public static readonly StyledProperty<ProEditDocumentZoomMode> ZoomModeProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, ProEditDocumentZoomMode>(nameof(ZoomMode));

    /// <summary>
    /// Defines the <see cref="MultiplePagesPerRow"/> property.
    /// </summary>
    public static readonly StyledProperty<int> MultiplePagesPerRowProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, int>(nameof(MultiplePagesPerRow), 2);

    /// <summary>
    /// Defines the <see cref="ScrollX"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ScrollXProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, double>(nameof(ScrollX), 0d);

    /// <summary>
    /// Defines the <see cref="ScrollY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ScrollYProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, double>(nameof(ScrollY), 0d);

    /// <summary>
    /// Defines the <see cref="IsReadOnly"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(IsReadOnly), true);

    /// <summary>
    /// Defines the <see cref="ShowReadOnlyCaret"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowReadOnlyCaretProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(ShowReadOnlyCaret));

    /// <summary>
    /// Defines the <see cref="AcceptsReturn"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> AcceptsReturnProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(AcceptsReturn), true);

    /// <summary>
    /// Defines the <see cref="AcceptsTab"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> AcceptsTabProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(AcceptsTab));

    /// <summary>
    /// Defines the <see cref="UseHarfBuzz"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> UseHarfBuzzProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(UseHarfBuzz), true);

    /// <summary>
    /// Defines the <see cref="UsePictureCache"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> UsePictureCacheProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(UsePictureCache), true);

    /// <summary>
    /// Defines the <see cref="ShowInvisibles"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowInvisiblesProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(ShowInvisibles));

    /// <summary>
    /// Defines the <see cref="ShowLayout"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowLayoutProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(ShowLayout));

    /// <summary>
    /// Defines the <see cref="ShowGridlines"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowGridlinesProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(ShowGridlines));

    /// <summary>
    /// Defines the <see cref="UsePagination"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> UsePaginationProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(UsePagination), true);

    /// <summary>
    /// Defines the <see cref="PageFlow"/> property.
    /// </summary>
    public static readonly StyledProperty<PageFlowDirection> PageFlowProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, PageFlowDirection>(nameof(PageFlow), PageFlowDirection.Vertical);

    /// <summary>
    /// Defines the <see cref="ShowRuler"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowRulerProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(ShowRuler), true);

    /// <summary>
    /// Defines the <see cref="ShowNavigationPane"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowNavigationPaneProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(ShowNavigationPane));

    /// <summary>
    /// Defines the <see cref="ViewMode"/> property.
    /// </summary>
    public static readonly StyledProperty<EditorViewMode> ViewModeProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, EditorViewMode>(nameof(ViewMode), EditorViewMode.PrintLayout);

    /// <summary>
    /// Defines the <see cref="IsPanEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPanEnabledProperty =
        AvaloniaProperty.Register<ProEditDocumentControlBase, bool>(nameof(IsPanEnabled), true);

    private readonly CoreHost _host;
    private DateTimeOffset _lastPrimaryPointerDownTimestamp = DateTimeOffset.MinValue;
    private Point _lastPrimaryPointerDownPosition;
    private int _primaryPointerDownCount;
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
        Focusable = true;
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
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom factor.
    /// </summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the automatic zoom mode.
    /// </summary>
    public ProEditDocumentZoomMode ZoomMode
    {
        get => GetValue(ZoomModeProperty);
        set => SetValue(ZoomModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the page count used by multiple-pages zoom.
    /// </summary>
    public int MultiplePagesPerRow
    {
        get => GetValue(MultiplePagesPerRowProperty);
        set => SetValue(MultiplePagesPerRowProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll offset in document-space units.
    /// </summary>
    public double ScrollX
    {
        get => GetValue(ScrollXProperty);
        set => SetValue(ScrollXProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll offset in document-space units.
    /// </summary>
    public double ScrollY
    {
        get => GetValue(ScrollYProperty);
        set => SetValue(ScrollYProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether text mutations are blocked.
    /// </summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the caret is rendered in read-only mode.
    /// </summary>
    public bool ShowReadOnlyCaret
    {
        get => GetValue(ShowReadOnlyCaretProperty);
        set => SetValue(ShowReadOnlyCaretProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Enter inserts a paragraph break.
    /// </summary>
    public bool AcceptsReturn
    {
        get => GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Tab inserts a tab character.
    /// </summary>
    public bool AcceptsTab
    {
        get => GetValue(AcceptsTabProperty);
        set => SetValue(AcceptsTabProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether HarfBuzz shaping is used.
    /// </summary>
    public bool UseHarfBuzz
    {
        get => GetValue(UseHarfBuzzProperty);
        set => SetValue(UseHarfBuzzProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether rendered pages are cached as Skia pictures.
    /// </summary>
    public bool UsePictureCache
    {
        get => GetValue(UsePictureCacheProperty);
        set => SetValue(UsePictureCacheProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether invisible characters are rendered.
    /// </summary>
    public bool ShowInvisibles
    {
        get => GetValue(ShowInvisiblesProperty);
        set => SetValue(ShowInvisiblesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether layout guides are rendered.
    /// </summary>
    public bool ShowLayout
    {
        get => GetValue(ShowLayoutProperty);
        set => SetValue(ShowLayoutProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether page gridlines are rendered.
    /// </summary>
    public bool ShowGridlines
    {
        get => GetValue(ShowGridlinesProperty);
        set => SetValue(ShowGridlinesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the document uses paginated layout.
    /// </summary>
    public bool UsePagination
    {
        get => GetValue(UsePaginationProperty);
        set => SetValue(UsePaginationProperty, value);
    }

    /// <summary>
    /// Gets or sets the page flow direction.
    /// </summary>
    public PageFlowDirection PageFlow
    {
        get => GetValue(PageFlowProperty);
        set => SetValue(PageFlowProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether hosting UI should show a ruler.
    /// </summary>
    public bool ShowRuler
    {
        get => GetValue(ShowRulerProperty);
        set => SetValue(ShowRulerProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether hosting UI should show a navigation pane.
    /// </summary>
    public bool ShowNavigationPane
    {
        get => GetValue(ShowNavigationPaneProperty);
        set => SetValue(ShowNavigationPaneProperty, value);
    }

    /// <summary>
    /// Gets or sets the logical editor view mode.
    /// </summary>
    public EditorViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether middle-button panning is enabled.
    /// </summary>
    public bool IsPanEnabled
    {
        get => GetValue(IsPanEnabledProperty);
        set => SetValue(IsPanEnabledProperty, value);
    }

    /// <inheritdoc />
    public bool CanHorizontallyScroll
    {
        get => _host.ScreenExtentWidth > _host.ViewportWidth;
        set { }
    }

    /// <inheritdoc />
    public bool CanVerticallyScroll
    {
        get => true;
        set { }
    }

    /// <inheritdoc />
    public bool IsLogicalScrollEnabled => true;

    /// <inheritdoc />
    public Size ScrollSize => new(0d, Math.Max(1d, _host.Layout.LineHeight * _host.Zoom));

    /// <inheritdoc />
    public Size PageScrollSize => new(0d, Math.Max(0d, Bounds.Height));

    /// <inheritdoc />
    public Size Extent => new(_host.ScreenExtentWidth, _host.ScreenExtentHeight);

    /// <inheritdoc />
    public Size Viewport => new(_host.ViewportWidth, _host.ViewportHeight);

    /// <inheritdoc />
    public Vector Offset
    {
        get => new(_host.ScreenScrollX, _host.ScreenScrollY);
        set
        {
            var oldX = _host.ScreenScrollX;
            var oldY = _host.ScreenScrollY;
            _host.SetScreenScroll((float)value.X, (float)value.Y);
            if (Math.Abs(oldX - _host.ScreenScrollX) < 0.001f
                && Math.Abs(oldY - _host.ScreenScrollY) < 0.001f)
            {
                return;
            }

            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public event EventHandler? ScrollInvalidated;

    /// <inheritdoc />
    public bool BringIntoView(Control target, Rect targetRect)
    {
        if (target != this || targetRect.Width <= 0d || targetRect.Height <= 0d)
        {
            return false;
        }

        var offset = Offset;
        var offsetX = offset.X;
        var offsetY = offset.Y;
        if (targetRect.Left < offset.X)
        {
            offsetX = targetRect.Left;
        }
        else if (targetRect.Right > offset.X + Viewport.Width)
        {
            offsetX = targetRect.Right - Viewport.Width;
        }

        if (targetRect.Top < offset.Y)
        {
            offsetY = targetRect.Top;
        }
        else if (targetRect.Bottom > offset.Y + Viewport.Height)
        {
            offsetY = targetRect.Bottom - Viewport.Height;
        }

        Offset = new Vector(offsetX, offsetY);
        return true;
    }

    /// <inheritdoc />
    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    /// <inheritdoc />
    public void RaiseScrollInvalidated(EventArgs e)
    {
        ScrollInvalidated?.Invoke(this, e);
    }

    /// <summary>
    /// Forces the current document to be rendered again.
    /// </summary>
    public void Refresh()
    {
        _host.EditorSession.RefreshLayout();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            return;
        }

        _host.SetViewport((float)Bounds.Width, (float)Bounds.Height);
        context.Custom(new ProEditDocumentDrawOperation(new Rect(Bounds.Size), _host));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? _host.Extent.Width * _host.Zoom
            : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height)
            ? _host.Extent.Height * _host.Zoom
            : availableSize.Height;
        return new Size(Math.Max(0d, width), Math.Max(0d, height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _host.SetViewport((float)finalSize.Width, (float)finalSize.Height);
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_isSynchronizingHostProperties)
        {
            return;
        }

        if (change.Property == DocumentProperty)
        {
            _host.LoadDocument(Document);
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        if (change.Property == ZoomProperty
            || change.Property == ZoomModeProperty
            || change.Property == MultiplePagesPerRowProperty
            || change.Property == ScrollXProperty
            || change.Property == ScrollYProperty
            || change.Property == IsReadOnlyProperty
            || change.Property == ShowReadOnlyCaretProperty
            || change.Property == AcceptsReturnProperty
            || change.Property == AcceptsTabProperty
            || change.Property == UseHarfBuzzProperty
            || change.Property == UsePictureCacheProperty
            || change.Property == ShowInvisiblesProperty
            || change.Property == ShowLayoutProperty
            || change.Property == ShowGridlinesProperty
            || change.Property == UsePaginationProperty
            || change.Property == PageFlowProperty
            || change.Property == ShowRulerProperty
            || change.Property == ShowNavigationPaneProperty
            || change.Property == ViewModeProperty
            || change.Property == IsPanEnabledProperty)
        {
            ApplyPropertiesToHost();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text)
            && _host.HandleTextInput(e.Text.AsSpan(), EditorModifiers.None))
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var key = MapKey(e.Key);
        if (key != EditorKey.Unknown
            && _host.HandleKey(key, EditorKeyEventKind.Down, MapModifiers(e.KeyModifiers)))
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var modifiers = MapModifiers(e.KeyModifiers);
        var position = e.GetPosition(this);
        if (_host.HandleWheel(
            (float)e.Delta.X * WheelDeltaScale,
            (float)e.Delta.Y * WheelDeltaScale,
            (float)position.X,
            (float)position.Y,
            modifiers))
        {
            SynchronizeViewportProperties();
            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        var button = ResolvePointerButton(point.Properties);
        if (button is EditorPointerButton.Primary or EditorPointerButton.Middle)
        {
            e.Pointer.Capture(this);
        }

        if (button == EditorPointerButton.Middle
            && _host.BeginPan((float)point.Position.X, (float)point.Position.Y))
        {
            e.Handled = true;
            SynchronizeViewportProperties();
            InvalidateVisual();
            return;
        }

        if (HandlePointer(EditorPointerKind.Down, point, e.KeyModifiers))
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);
        if (_host.UpdatePan((float)point.Position.X, (float)point.Position.Y))
        {
            e.Handled = true;
            SynchronizeViewportProperties();
            InvalidateVisual();
            return;
        }

        if (HandlePointer(EditorPointerKind.Move, point, e.KeyModifiers))
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        var point = e.GetCurrentPoint(this);
        if (_host.IsPanning)
        {
            _host.EndPan();
            e.Handled = true;
            SynchronizeViewportProperties();
            InvalidateVisual();
            return;
        }

        if (HandlePointer(EditorPointerKind.Up, point, e.KeyModifiers))
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    private bool HandlePointer(EditorPointerKind kind, PointerPoint point, KeyModifiers modifiers)
    {
        var documentPoint = _host.ScreenToDocument((float)point.Position.X, (float)point.Position.Y);
        var button = ResolvePointerButton(point.Properties);
        var clickCount = kind == EditorPointerKind.Down ? ResolveClickCount(point, button) : 0;
        var pointerEvent = new EditorPointerEvent(
            kind,
            documentPoint.X,
            documentPoint.Y,
            button,
            MapModifiers(modifiers),
            clickCount);
        return _host.HandlePointer(pointerEvent);
    }

    private int ResolveClickCount(PointerPoint point, EditorPointerButton button)
    {
        if (button != EditorPointerButton.Primary)
        {
            _primaryPointerDownCount = 0;
            return 1;
        }

        var now = DateTimeOffset.UtcNow;
        var position = point.Position;
        var isMultiClick =
            _primaryPointerDownCount > 0
            && now - _lastPrimaryPointerDownTimestamp <= MultiClickTimeThreshold
            && IsWithinMultiClickDistance(position, _lastPrimaryPointerDownPosition);

        _primaryPointerDownCount = isMultiClick ? Math.Min(3, _primaryPointerDownCount + 1) : 1;
        _lastPrimaryPointerDownTimestamp = now;
        _lastPrimaryPointerDownPosition = position;
        return _primaryPointerDownCount;
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
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnHostViewportChanged(object? sender, EventArgs e)
    {
        SynchronizeViewportProperties();
        RaiseScrollInvalidated(EventArgs.Empty);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnHostViewOptionsChanged(object? sender, EventArgs e)
    {
        SynchronizeViewportProperties();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SynchronizeViewportProperties()
    {
        _isSynchronizingHostProperties = true;
        try
        {
            SetCurrentValue(ZoomModeProperty, _host.ZoomMode);
            SetCurrentValue(MultiplePagesPerRowProperty, _host.MultiplePagesPerRow);
            SetCurrentValue(ZoomProperty, (double)_host.Zoom);
            SetCurrentValue(ScrollXProperty, (double)_host.ScrollX);
            SetCurrentValue(ScrollYProperty, (double)_host.ScrollY);
            SetCurrentValue(ShowInvisiblesProperty, _host.ShowInvisibles);
            SetCurrentValue(ShowLayoutProperty, _host.ShowLayout);
            SetCurrentValue(ShowGridlinesProperty, _host.ShowGridlines);
            SetCurrentValue(UsePaginationProperty, _host.UsePagination);
            SetCurrentValue(PageFlowProperty, _host.PageFlow);
            SetCurrentValue(ShowRulerProperty, _host.ShowRuler);
            SetCurrentValue(ShowNavigationPaneProperty, _host.ShowNavigationPane);
            SetCurrentValue(ViewModeProperty, _host.ViewMode);
            SetCurrentValue(IsPanEnabledProperty, _host.IsPanEnabled);
        }
        finally
        {
            _isSynchronizingHostProperties = false;
        }
    }

    private static EditorPointerButton ResolvePointerButton(PointerPointProperties properties)
    {
        return properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => EditorPointerButton.Primary,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => EditorPointerButton.Secondary,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => EditorPointerButton.Middle,
            _ when properties.IsLeftButtonPressed => EditorPointerButton.Primary,
            _ when properties.IsRightButtonPressed => EditorPointerButton.Secondary,
            _ when properties.IsMiddleButtonPressed => EditorPointerButton.Middle,
            _ => EditorPointerButton.None
        };
    }

    private static bool IsWithinMultiClickDistance(Point current, Point previous)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        return (dx * dx) + (dy * dy) <= MultiClickDistanceThreshold * MultiClickDistanceThreshold;
    }

    private static EditorModifiers MapModifiers(KeyModifiers modifiers)
    {
        var result = EditorModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= EditorModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= EditorModifiers.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= EditorModifiers.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= EditorModifiers.Meta;
        }

        return result;
    }

    private static EditorKey MapKey(Key key)
    {
        return key switch
        {
            Key.Left => EditorKey.Left,
            Key.Right => EditorKey.Right,
            Key.Up => EditorKey.Up,
            Key.Down => EditorKey.Down,
            Key.Back => EditorKey.Backspace,
            Key.Delete => EditorKey.Delete,
            Key.Enter => EditorKey.Enter,
            Key.Home => EditorKey.Home,
            Key.End => EditorKey.End,
            Key.PageUp => EditorKey.PageUp,
            Key.PageDown => EditorKey.PageDown,
            Key.Tab => EditorKey.Tab,
            Key.A => EditorKey.A,
            Key.Z => EditorKey.Z,
            Key.Y => EditorKey.Y,
            Key.C => EditorKey.C,
            Key.X => EditorKey.X,
            Key.V => EditorKey.V,
            _ => EditorKey.Unknown
        };
    }

    private sealed class ProEditDocumentDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly CoreHost _host;

        public ProEditDocumentDrawOperation(Rect bounds, CoreHost host)
        {
            _bounds = bounds;
            _host = host;
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
            _host.Render(lease.SkCanvas, (int)Math.Ceiling(_bounds.Width), (int)Math.Ceiling(_bounds.Height));
        }

        public bool HitTest(Point p) => _bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }
}
