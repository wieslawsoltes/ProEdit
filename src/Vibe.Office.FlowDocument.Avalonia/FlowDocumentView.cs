using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using Vibe.Office.Documents;
using Vibe.Office.FlowDocument.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using DocumentRenderOptions = Vibe.Office.Rendering.RenderOptions;
using Vibe.Office.Rendering.Skia;

namespace Vibe.Office.FlowDocument.Avalonia;

/// <summary>
/// Renders a FlowDocument using the VibeOffice layout and rendering pipeline.
/// </summary>
public sealed class FlowDocumentView : Panel
{
    /// <summary>
    /// Identifies the <see cref="FlowDocument"/> property.
    /// </summary>
    public static readonly StyledProperty<Vibe.Office.FlowDocument.FlowDocument?> FlowDocumentProperty =
        AvaloniaProperty.Register<FlowDocumentView, Vibe.Office.FlowDocument.FlowDocument?>(nameof(FlowDocument));

    /// <summary>
    /// Identifies the <see cref="UsePagination"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> UsePaginationProperty =
        AvaloniaProperty.Register<FlowDocumentView, bool>(nameof(UsePagination), true);

    /// <summary>
    /// Identifies the <see cref="ZoomFactor"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<FlowDocumentView, double>(nameof(ZoomFactor), 1d);

    /// <summary>
    /// Identifies the <see cref="InlineUiPlaceholderText"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> InlineUiPlaceholderTextProperty =
        AvaloniaProperty.Register<FlowDocumentView, string?>(nameof(InlineUiPlaceholderText));

    /// <summary>
    /// Identifies the <see cref="BlockUiPlaceholderText"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BlockUiPlaceholderTextProperty =
        AvaloniaProperty.Register<FlowDocumentView, string?>(nameof(BlockUiPlaceholderText));

    private readonly DocumentLayouter _layouter = new DocumentLayouter();
    private readonly SkiaTextMeasurer _textMeasurer = new SkiaTextMeasurer();
    private readonly SkiaDocumentRenderer _renderer = new SkiaDocumentRenderer();
    private readonly FlowDocumentSurface _drawingSurface;
    private readonly DocumentRenderOptions _renderOptions = new DocumentRenderOptions
    {
        BackgroundColor = DocColor.Transparent,
        PageColor = DocColor.White,
        PageBorderColor = new DocColor(220, 220, 220),
        PageBorderThickness = 1f,
        TextColor = DocColor.Black,
        UseHarfBuzz = true,
        UsePictureCache = false,
        ShowCaret = false,
        ShowHeaderFooterCaret = false,
        ShowShapeTextCaret = false
    };

    private Document? _document;
    private DocumentLayout? _layout;
    private bool _layoutDirty = true;
    private Rect _layoutBounds;
    private Point _contentOffset;
    private readonly Dictionary<string, Control> _embeddedControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Rect> _embeddedControlBounds = new(StringComparer.Ordinal);
    private string _embeddedUiShapePrefix = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix;

    static FlowDocumentView()
    {
        AffectsRender<FlowDocumentView>(
            FlowDocumentProperty,
            UsePaginationProperty,
            ZoomFactorProperty,
            BackgroundProperty,
            InlineUiPlaceholderTextProperty,
            BlockUiPlaceholderTextProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocumentView"/> class.
    /// </summary>
    public FlowDocumentView()
    {
        _drawingSurface = new FlowDocumentSurface(_renderer, _renderOptions);
        Children.Add(_drawingSurface);
    }

    /// <summary>
    /// Gets or sets the FlowDocument to render.
    /// </summary>
    public Vibe.Office.FlowDocument.FlowDocument? FlowDocument
    {
        get => GetValue(FlowDocumentProperty);
        set => SetValue(FlowDocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether pagination is enabled.
    /// </summary>
    public bool UsePagination
    {
        get => GetValue(UsePaginationProperty);
        set => SetValue(UsePaginationProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom factor.
    /// </summary>
    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text for inline UI containers.
    /// </summary>
    public string? InlineUiPlaceholderText
    {
        get => GetValue(InlineUiPlaceholderTextProperty);
        set => SetValue(InlineUiPlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text for block UI containers.
    /// </summary>
    public string? BlockUiPlaceholderText
    {
        get => GetValue(BlockUiPlaceholderTextProperty);
        set => SetValue(BlockUiPlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets the converted document.
    /// </summary>
    public Document? RenderedDocument => _document;

    /// <summary>
    /// Gets the latest document layout.
    /// </summary>
    public DocumentLayout? Layout => _layout;

    /// <summary>
    /// Forces a layout refresh.
    /// </summary>
    public void Refresh()
    {
        MarkLayoutDirty();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FlowDocumentProperty)
        {
            if (change.OldValue is Vibe.Office.FlowDocument.FlowDocument oldDocument)
            {
                oldDocument.Changed -= OnDocumentChanged;
            }

            if (change.NewValue is Vibe.Office.FlowDocument.FlowDocument newDocument)
            {
                newDocument.Changed += OnDocumentChanged;
            }
            else
            {
                _embeddedControlBounds.Clear();
                ClearEmbeddedControls();
            }

            MarkLayoutDirty();
            return;
        }

        if (change.Property == UsePaginationProperty
            || change.Property == InlineUiPlaceholderTextProperty
            || change.Property == BlockUiPlaceholderTextProperty)
        {
            MarkLayoutDirty();
            return;
        }

        if (change.Property == ZoomFactorProperty)
        {
            UpdateDrawingSurfaceState();
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();
            return;
        }

        if (change.Property == BoundsProperty)
        {
            MarkLayoutDirty();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout();

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        var zoom = Math.Max(0.1d, ZoomFactor);
        var contentWidth = ComputeLayoutWidth() * zoom;
        var contentHeight = ComputeLayoutHeight() * zoom;

        var desiredWidth = double.IsInfinity(availableSize.Width) ? contentWidth : availableSize.Width;
        var desiredHeight = double.IsInfinity(availableSize.Height) ? contentHeight : availableSize.Height;

        return new Size(Math.Max(0d, desiredWidth), Math.Max(0d, desiredHeight));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureLayout();
        UpdateEmbeddedControlBounds();

        _drawingSurface.Arrange(new Rect(finalSize));

        var embeddedSet = new HashSet<Control>(_embeddedControls.Values);
        foreach (var child in Children)
        {
            if (ReferenceEquals(child, _drawingSurface))
            {
                continue;
            }

            if (!embeddedSet.Contains(child))
            {
                child.Arrange(default);
                continue;
            }

            var elementId = FindEmbeddedElementId(child);
            if (elementId is null || !_embeddedControlBounds.TryGetValue(elementId, out var bounds))
            {
                child.Arrange(default);
                continue;
            }

            child.Arrange(bounds);
        }

        return finalSize;
    }

    private void EnsureLayout()
    {
        if (!_layoutDirty)
        {
            return;
        }

        var flowDocument = FlowDocument;
        if (flowDocument is null)
        {
            _document = null;
            _layout = null;
            _layoutBounds = default;
            _contentOffset = default;
            _embeddedControlBounds.Clear();
            ClearEmbeddedControls();
            UpdateDrawingSurfaceState();
            _layoutDirty = false;
            return;
        }

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var converter = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            EnableEmbeddedUiElements = true,
            EmbeddedUiShapePrefix = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix,
            InlineUiPlaceholderText = InlineUiPlaceholderText ?? FlowDocumentConverterOptions.DefaultInlineUiPlaceholderText,
            BlockUiPlaceholderText = BlockUiPlaceholderText ?? FlowDocumentConverterOptions.DefaultBlockUiPlaceholderText
        });
        _embeddedUiShapePrefix = FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix;
        _document = converter.Convert(flowDocument);
        var settings = CreateLayoutSettings(flowDocument, size);
        _layout = _layouter.Layout(_document, settings, _textMeasurer);
        _layoutBounds = ComputeLayoutBounds();
        _contentOffset = ComputeContentOffset(size, _layoutBounds);
        SynchronizeEmbeddedControls(converter.EmbeddedUiElements);
        UpdateEmbeddedControlBounds();
        UpdateDrawingSurfaceState();
        _layoutDirty = false;
    }

    private LayoutSettings CreateLayoutSettings(Vibe.Office.FlowDocument.FlowDocument flowDocument, Size size)
    {
        var settings = new LayoutSettings
        {
            ViewportWidth = (float)size.Width,
            ViewportHeight = (float)size.Height,
            UsePagination = UsePagination
        };

        if (flowDocument.PageWidth.HasValue)
        {
            settings.PageWidth = (float)flowDocument.PageWidth.Value;
        }

        if (flowDocument.PageHeight.HasValue)
        {
            settings.PageHeight = (float)flowDocument.PageHeight.Value;
        }

        if (!flowDocument.PagePadding.IsEmpty)
        {
            settings.MarginLeft = (float)flowDocument.PagePadding.Left;
            settings.MarginTop = (float)flowDocument.PagePadding.Top;
            settings.MarginRight = (float)flowDocument.PagePadding.Right;
            settings.MarginBottom = (float)flowDocument.PagePadding.Bottom;
        }

        if (flowDocument.ColumnGap.HasValue)
        {
            settings.ColumnGap = (float)flowDocument.ColumnGap.Value;
        }

        return settings;
    }

    private void UpdateRenderOptions()
    {
        _renderOptions.ZoomFactor = (float)Math.Max(0.1d, ZoomFactor);

        if (Background is SolidColorBrush brush)
        {
            var color = brush.Color;
            _renderOptions.BackgroundColor = new DocColor(color.R, color.G, color.B, color.A);
        }
    }

    private void UpdateDrawingSurfaceState()
    {
        UpdateRenderOptions();
        _drawingSurface.Document = _document;
        _drawingSurface.Layout = _layout;
        _drawingSurface.LayoutBounds = _layoutBounds;
        _drawingSurface.ContentOffset = _contentOffset;
        _drawingSurface.ZoomFactor = ZoomFactor;
        _drawingSurface.InvalidateVisual();
    }

    private void MarkLayoutDirty()
    {
        _layoutDirty = true;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            MarkLayoutDirty();
            return;
        }

        Dispatcher.UIThread.Post(MarkLayoutDirty);
    }

    private void SynchronizeEmbeddedControls(IReadOnlyList<EmbeddedFlowUiElement> embeddedElements)
    {
        var required = new Dictionary<string, Control>(StringComparer.Ordinal);
        foreach (var element in embeddedElements)
        {
            if (element.Child is not Control control)
            {
                continue;
            }

            if (control.Parent is not null && !ReferenceEquals(control.Parent, this))
            {
                continue;
            }

            required[element.Id] = control;
        }

        if (_embeddedControls.Count > 0)
        {
            var staleIds = new List<string>();
            foreach (var pair in _embeddedControls)
            {
                if (!required.ContainsKey(pair.Key))
                {
                    staleIds.Add(pair.Key);
                }
            }

            foreach (var staleId in staleIds)
            {
                if (!_embeddedControls.TryGetValue(staleId, out var staleControl))
                {
                    continue;
                }

                if (Children.Contains(staleControl))
                {
                    Children.Remove(staleControl);
                }

                _embeddedControls.Remove(staleId);
            }
        }

        foreach (var pair in required)
        {
            _embeddedControls[pair.Key] = pair.Value;
            if (!Children.Contains(pair.Value))
            {
                Children.Add(pair.Value);
            }
        }
    }

    private void ClearEmbeddedControls()
    {
        if (_embeddedControls.Count == 0)
        {
            return;
        }

        foreach (var control in _embeddedControls.Values)
        {
            if (Children.Contains(control))
            {
                Children.Remove(control);
            }
        }

        _embeddedControls.Clear();
    }

    private void UpdateEmbeddedControlBounds()
    {
        _embeddedControlBounds.Clear();
        if (_layout is null || _embeddedControls.Count == 0)
        {
            return;
        }

        var zoom = Math.Max(0.1d, ZoomFactor);
        foreach (var line in _layout.Lines)
        {
            if (line.Shapes.Count == 0)
            {
                continue;
            }

            var baseline = line.Y + line.Ascent;
            foreach (var shape in line.Shapes)
            {
                if (!FlowDocumentConverter.TryParseEmbeddedUiElementId(shape.Shape.Name, _embeddedUiShapePrefix, out var elementId))
                {
                    continue;
                }

                if (!_embeddedControls.ContainsKey(elementId))
                {
                    continue;
                }

                var x = (line.X + shape.X - _layoutBounds.X) * zoom + _contentOffset.X;
                var y = (baseline - shape.Height - _layoutBounds.Y) * zoom + _contentOffset.Y;
                var width = shape.Width * (float)zoom;
                var height = shape.Height * (float)zoom;
                _embeddedControlBounds[elementId] = new Rect(
                    x,
                    y,
                    Math.Max(1d, width),
                    Math.Max(1d, height));
            }
        }
    }

    private string? FindEmbeddedElementId(Control control)
    {
        foreach (var pair in _embeddedControls)
        {
            if (ReferenceEquals(pair.Value, control))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private double ComputeLayoutWidth()
    {
        if (_layout is null || _layout.Pages.Count == 0)
        {
            return Bounds.Width;
        }

        double maxRight = 0d;
        foreach (var page in _layout.Pages)
        {
            var right = page.Bounds.X + page.Bounds.Width;
            if (right > maxRight)
            {
                maxRight = right;
            }
        }

        return maxRight;
    }

    private double ComputeLayoutHeight()
    {
        if (_layout is null || _layout.Pages.Count == 0)
        {
            return Bounds.Height;
        }

        double maxBottom = 0d;
        foreach (var page in _layout.Pages)
        {
            var bottom = page.Bounds.Y + page.Bounds.Height;
            if (bottom > maxBottom)
            {
                maxBottom = bottom;
            }
        }

        return maxBottom;
    }

    private Rect ComputeLayoutBounds()
    {
        if (_layout is null || _layout.Pages.Count == 0)
        {
            return default;
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxRight = double.NegativeInfinity;
        var maxBottom = double.NegativeInfinity;

        foreach (var page in _layout.Pages)
        {
            minX = Math.Min(minX, page.Bounds.X);
            minY = Math.Min(minY, page.Bounds.Y);
            maxRight = Math.Max(maxRight, page.Bounds.X + page.Bounds.Width);
            maxBottom = Math.Max(maxBottom, page.Bounds.Y + page.Bounds.Height);
        }

        if (double.IsInfinity(minX) || double.IsInfinity(minY) || double.IsInfinity(maxRight) || double.IsInfinity(maxBottom))
        {
            return default;
        }

        return new Rect(minX, minY, Math.Max(0d, maxRight - minX), Math.Max(0d, maxBottom - minY));
    }

    private Point ComputeContentOffset(Size viewportSize, Rect layoutBounds)
    {
        if (layoutBounds.Width <= 0d || layoutBounds.Height <= 0d)
        {
            return default;
        }

        var zoom = Math.Max(0.1d, ZoomFactor);
        var contentWidth = layoutBounds.Width * zoom;
        var contentHeight = layoutBounds.Height * zoom;

        var offsetX = (viewportSize.Width - contentWidth) / 2d;
        var offsetY = (viewportSize.Height - contentHeight) / 2d;

        return new Point(offsetX, offsetY);
    }

    private sealed class FlowDocumentSurface : Control
    {
        private readonly SkiaDocumentRenderer _renderer;
        private readonly DocumentRenderOptions _options;

        public FlowDocumentSurface(SkiaDocumentRenderer renderer, DocumentRenderOptions options)
        {
            _renderer = renderer;
            _options = options;
            IsHitTestVisible = false;
        }

        public Document? Document { get; set; }

        public DocumentLayout? Layout { get; set; }

        public Rect LayoutBounds { get; set; }

        public Point ContentOffset { get; set; }

        public double ZoomFactor { get; set; } = 1d;

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (Document is null || Layout is null)
            {
                return;
            }

            context.Custom(new FlowDocumentDrawOperation(
                new Rect(Bounds.Size),
                Document,
                Layout,
                _renderer,
                _options,
                ZoomFactor,
                LayoutBounds,
                ContentOffset));
        }
    }

    private sealed class FlowDocumentDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly Document _document;
        private readonly DocumentLayout _layout;
        private readonly SkiaDocumentRenderer _renderer;
        private readonly DocumentRenderOptions _options;
        private readonly double _zoomFactor;
        private readonly Rect _layoutBounds;
        private readonly Point _contentOffset;

        public FlowDocumentDrawOperation(
            Rect bounds,
            Document document,
            DocumentLayout layout,
            SkiaDocumentRenderer renderer,
            DocumentRenderOptions options,
            double zoomFactor,
            Rect layoutBounds,
            Point contentOffset)
        {
            _bounds = bounds;
            _document = document;
            _layout = layout;
            _renderer = renderer;
            _options = options;
            _zoomFactor = zoomFactor;
            _layoutBounds = layoutBounds;
            _contentOffset = contentOffset;
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
            var zoom = (float)Math.Max(0.1d, _zoomFactor);
            var originX = _contentOffset.X - (_layoutBounds.X * zoom);
            var originY = _contentOffset.Y - (_layoutBounds.Y * zoom);
            canvas.Translate((float)originX, (float)originY);
            canvas.Scale(zoom);

            var viewportWidth = MathF.Max(0f, (float)(_bounds.Width / zoom));
            var viewportHeight = MathF.Max(0f, (float)(_bounds.Height / zoom));
            var visibleX = (float)(_layoutBounds.X - (_contentOffset.X / zoom));
            var visibleY = (float)(_layoutBounds.Y - (_contentOffset.Y / zoom));
            _options.VisibleBounds = new DocRect(visibleX, visibleY, viewportWidth, viewportHeight);

            _renderer.Render(canvas, _document, _layout, _options);
            canvas.Restore();
        }

        public bool HitTest(Point p) => _bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }
}
