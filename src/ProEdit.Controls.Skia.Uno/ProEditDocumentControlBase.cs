using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using CoreHost = ProEdit.Controls.Skia.ProEditDocumentHost;

namespace ProEdit.Controls.Skia.Uno;

/// <summary>
/// Base class for Uno Platform ProEdit document controls.
/// </summary>
public abstract class ProEditDocumentControlBase : SKCanvasElement
{
    private const float WheelDeltaScale = 48f;
    private const double MultiClickDistanceThreshold = 6d;
    private static readonly TimeSpan MultiClickTimeThreshold = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Defines the <see cref="Document"/> property.
    /// </summary>
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(Document),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(null, OnDocumentPropertyChanged));

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom),
            typeof(double),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(1d, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ZoomMode"/> property.
    /// </summary>
    public static readonly DependencyProperty ZoomModeProperty =
        DependencyProperty.Register(
            nameof(ZoomMode),
            typeof(ProEditDocumentZoomMode),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(ProEditDocumentZoomMode.Custom, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="MultiplePagesPerRow"/> property.
    /// </summary>
    public static readonly DependencyProperty MultiplePagesPerRowProperty =
        DependencyProperty.Register(
            nameof(MultiplePagesPerRow),
            typeof(int),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(2, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ScrollX"/> property.
    /// </summary>
    public static readonly DependencyProperty ScrollXProperty =
        DependencyProperty.Register(
            nameof(ScrollX),
            typeof(double),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(0d, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ScrollY"/> property.
    /// </summary>
    public static readonly DependencyProperty ScrollYProperty =
        DependencyProperty.Register(
            nameof(ScrollY),
            typeof(double),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(0d, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="IsReadOnly"/> property.
    /// </summary>
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(true, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowReadOnlyCaret"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowReadOnlyCaretProperty =
        DependencyProperty.Register(
            nameof(ShowReadOnlyCaret),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(false, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="AcceptsReturn"/> property.
    /// </summary>
    public static readonly DependencyProperty AcceptsReturnProperty =
        DependencyProperty.Register(
            nameof(AcceptsReturn),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(true, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="AcceptsTab"/> property.
    /// </summary>
    public static readonly DependencyProperty AcceptsTabProperty =
        DependencyProperty.Register(
            nameof(AcceptsTab),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(false, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="UseHarfBuzz"/> property.
    /// </summary>
    public static readonly DependencyProperty UseHarfBuzzProperty =
        DependencyProperty.Register(
            nameof(UseHarfBuzz),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(true, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="UsePictureCache"/> property.
    /// </summary>
    public static readonly DependencyProperty UsePictureCacheProperty =
        DependencyProperty.Register(
            nameof(UsePictureCache),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(true, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowInvisibles"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowInvisiblesProperty =
        DependencyProperty.Register(
            nameof(ShowInvisibles),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(false, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowLayout"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowLayoutProperty =
        DependencyProperty.Register(
            nameof(ShowLayout),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(false, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowGridlines"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowGridlinesProperty =
        DependencyProperty.Register(
            nameof(ShowGridlines),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(false, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="UsePagination"/> property.
    /// </summary>
    public static readonly DependencyProperty UsePaginationProperty =
        DependencyProperty.Register(
            nameof(UsePagination),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(true, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="PageFlow"/> property.
    /// </summary>
    public static readonly DependencyProperty PageFlowProperty =
        DependencyProperty.Register(
            nameof(PageFlow),
            typeof(PageFlowDirection),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(PageFlowDirection.Vertical, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowRuler"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowRulerProperty =
        DependencyProperty.Register(
            nameof(ShowRuler),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(true, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowNavigationPane"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowNavigationPaneProperty =
        DependencyProperty.Register(
            nameof(ShowNavigationPane),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(false, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ViewMode"/> property.
    /// </summary>
    public static readonly DependencyProperty ViewModeProperty =
        DependencyProperty.Register(
            nameof(ViewMode),
            typeof(EditorViewMode),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(EditorViewMode.PrintLayout, OnViewportPropertyChanged));

    /// <summary>
    /// Defines the <see cref="IsPanEnabled"/> property.
    /// </summary>
    public static readonly DependencyProperty IsPanEnabledProperty =
        DependencyProperty.Register(
            nameof(IsPanEnabled),
            typeof(bool),
            typeof(ProEditDocumentControlBase),
            new PropertyMetadata(true, OnViewportPropertyChanged));

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
        IsTabStop = true;
        IsHitTestVisible = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;
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
        InvalidateMeasure();
        InvalidateArrange();
        Invalidate();
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
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        _host.Render(canvas, (int)Math.Ceiling(area.Width), (int)Math.Ceiling(area.Height));
    }

    private static void OnDocumentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProEditDocumentControlBase control)
        {
            return;
        }

        control._host.LoadDocument(e.NewValue as Document);
        control.InvalidateMeasure();
        control.InvalidateArrange();
        control.Invalidate();
    }

    private static void OnViewportPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProEditDocumentControlBase control)
        {
            return;
        }

        if (control._isSynchronizingHostProperties)
        {
            return;
        }

        control.ApplyPropertiesToHost();
        control.InvalidateMeasure();
        control.InvalidateArrange();
        control.Invalidate();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _host.SetViewport((float)e.NewSize.Width, (float)e.NewSize.Height);
        Invalidate();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var modifiers = GetCurrentModifiers();
        var hasCommandModifier = (modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0;
        if (TryMapKey(e.Key, hasCommandModifier, out var editorKey)
            && _host.HandleKey(editorKey, EditorKeyEventKind.Down, modifiers))
        {
            e.Handled = true;
            Invalidate();
            return;
        }

        if ((modifiers & (EditorModifiers.Control | EditorModifiers.Alt | EditorModifiers.Meta)) != 0)
        {
            return;
        }

        if (TryMapPrintableKey(e.Key, modifiers, out var text)
            && _host.HandleTextInput(text.AsSpan(), modifiers))
        {
            e.Handled = true;
            Invalidate();
        }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var modifiers = GetCurrentModifiers();
        var wheelDelta = point.Properties.MouseWheelDelta;
        if (_host.HandleWheel(
            0f,
            (wheelDelta / 120f) * WheelDeltaScale,
            (float)point.Position.X,
            (float)point.Position.Y,
            modifiers))
        {
            SynchronizeViewportProperties();
            e.Handled = true;
            Invalidate();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        CapturePointer(e.Pointer);
        var point = e.GetCurrentPoint(this);
        var button = ResolveButton(point);
        if (button == EditorPointerButton.Middle
            && _host.BeginPan((float)point.Position.X, (float)point.Position.Y))
        {
            e.Handled = true;
            SynchronizeViewportProperties();
            Invalidate();
            return;
        }

        var clickCount = ResolveClickCount(point, button);
        if (HandlePointer(EditorPointerKind.Down, point, button, clickCount))
        {
            e.Handled = true;
            Invalidate();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (_host.UpdatePan((float)point.Position.X, (float)point.Position.Y))
        {
            e.Handled = true;
            SynchronizeViewportProperties();
            Invalidate();
            return;
        }

        if (HandlePointer(EditorPointerKind.Move, point, ResolveButton(point), 0))
        {
            e.Handled = true;
            Invalidate();
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (_host.IsPanning)
        {
            _host.EndPan();
            ReleasePointerCaptures();
            e.Handled = true;
            SynchronizeViewportProperties();
            Invalidate();
            return;
        }

        var handled = HandlePointer(EditorPointerKind.Up, point, ResolveButton(point), 1);
        ReleasePointerCaptures();
        if (handled)
        {
            e.Handled = true;
            Invalidate();
        }
    }

    private bool HandlePointer(EditorPointerKind kind, Microsoft.UI.Input.PointerPoint point, EditorPointerButton button, int clickCount)
    {
        var documentPoint = _host.ScreenToDocument((float)point.Position.X, (float)point.Position.Y);
        var pointerEvent = new EditorPointerEvent(
            kind,
            documentPoint.X,
            documentPoint.Y,
            button,
            GetCurrentModifiers(),
            clickCount);
        return _host.HandlePointer(pointerEvent);
    }

    private int ResolveClickCount(Microsoft.UI.Input.PointerPoint point, EditorPointerButton button)
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
        InvalidateArrange();
        Invalidate();
    }

    private void OnHostViewportChanged(object? sender, EventArgs e)
    {
        SynchronizeViewportProperties();
        InvalidateMeasure();
        InvalidateArrange();
        Invalidate();
    }

    private void OnHostViewOptionsChanged(object? sender, EventArgs e)
    {
        SynchronizeViewportProperties();
        InvalidateMeasure();
        InvalidateArrange();
        Invalidate();
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

    private static bool IsWithinMultiClickDistance(Point current, Point previous)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        return (dx * dx) + (dy * dy) <= MultiClickDistanceThreshold * MultiClickDistanceThreshold;
    }

    private static EditorPointerButton ResolveButton(Microsoft.UI.Input.PointerPoint point)
    {
        var properties = point.Properties;
        if (properties.IsLeftButtonPressed)
        {
            return EditorPointerButton.Primary;
        }

        if (properties.IsRightButtonPressed)
        {
            return EditorPointerButton.Secondary;
        }

        if (properties.IsMiddleButtonPressed)
        {
            return EditorPointerButton.Middle;
        }

        if (properties.IsXButton1Pressed)
        {
            return EditorPointerButton.XButton1;
        }

        if (properties.IsXButton2Pressed)
        {
            return EditorPointerButton.XButton2;
        }

        return EditorPointerButton.None;
    }

    private static bool TryMapKey(VirtualKey key, bool includeCommandShortcuts, out EditorKey mapped)
    {
        mapped = key switch
        {
            VirtualKey.Left => EditorKey.Left,
            VirtualKey.Right => EditorKey.Right,
            VirtualKey.Up => EditorKey.Up,
            VirtualKey.Down => EditorKey.Down,
            VirtualKey.Back => EditorKey.Backspace,
            VirtualKey.Delete => EditorKey.Delete,
            VirtualKey.Enter => EditorKey.Enter,
            VirtualKey.Home => EditorKey.Home,
            VirtualKey.End => EditorKey.End,
            VirtualKey.PageUp => EditorKey.PageUp,
            VirtualKey.PageDown => EditorKey.PageDown,
            VirtualKey.Tab => EditorKey.Tab,
            VirtualKey.A when includeCommandShortcuts => EditorKey.A,
            VirtualKey.Z when includeCommandShortcuts => EditorKey.Z,
            VirtualKey.Y when includeCommandShortcuts => EditorKey.Y,
            VirtualKey.C when includeCommandShortcuts => EditorKey.C,
            VirtualKey.X when includeCommandShortcuts => EditorKey.X,
            VirtualKey.V when includeCommandShortcuts => EditorKey.V,
            _ => EditorKey.Unknown
        };

        return mapped != EditorKey.Unknown;
    }

    private static bool TryMapPrintableKey(VirtualKey key, EditorModifiers modifiers, out string text)
    {
        var isShift = (modifiers & EditorModifiers.Shift) != 0;
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            var value = (char)('A' + ((int)key - (int)VirtualKey.A));
            text = (isShift ? value : char.ToLowerInvariant(value)).ToString();
            return true;
        }

        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            var index = (int)key - (int)VirtualKey.Number0;
            text = isShift
                ? index switch
                {
                    0 => ")",
                    1 => "!",
                    2 => "@",
                    3 => "#",
                    4 => "$",
                    5 => "%",
                    6 => "^",
                    7 => "&",
                    8 => "*",
                    9 => "(",
                    _ => string.Empty
                }
                : ((char)('0' + index)).ToString();
            return text.Length > 0;
        }

        if (key == VirtualKey.Space)
        {
            text = " ";
            return true;
        }

        if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
        {
            var value = (char)('0' + ((int)key - (int)VirtualKey.NumberPad0));
            text = value.ToString();
            return true;
        }

        text = ((int)key) switch
        {
            186 => isShift ? ":" : ";",
            187 => isShift ? "+" : "=",
            188 => isShift ? "<" : ",",
            189 => isShift ? "_" : "-",
            190 => isShift ? ">" : ".",
            191 => isShift ? "?" : "/",
            192 => isShift ? "~" : "`",
            219 => isShift ? "{" : "[",
            220 => isShift ? "|" : "\\",
            221 => isShift ? "}" : "]",
            222 => isShift ? "\"" : "'",
            _ => string.Empty
        };

        return text.Length > 0;
    }

    private static EditorModifiers GetCurrentModifiers()
    {
        var modifiers = EditorModifiers.None;
        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= EditorModifiers.Shift;
        }

        if (IsDown(VirtualKey.Control))
        {
            modifiers |= EditorModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= EditorModifiers.Alt;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= EditorModifiers.Meta;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }
}
