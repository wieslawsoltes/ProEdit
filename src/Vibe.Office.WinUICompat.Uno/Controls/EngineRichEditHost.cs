using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SkiaSharp.Views;
using SkiaSharp.Views.Windows;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.FlowDocument.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;
using Vibe.Office.WinUICompat.Text;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Vibe.Office.WinUICompat.Controls;

internal sealed class EngineRichEditHost : UserControl
{
    private const float ScrollStepFallback = 16f;
    private const float ScrollStepLineMultiplier = 3f;
    private const double ScrollBarWidth = 14d;
    private const double MultiClickDistanceThreshold = 6d;
    private static readonly TimeSpan MultiClickTimeThreshold = TimeSpan.FromMilliseconds(500);
    private readonly SKXamlCanvas _canvas;
    private readonly Canvas _embeddedUiOverlay;
    private readonly ScrollBar _verticalScrollBar;
    private readonly Dictionary<string, UIElement> _embeddedElementsById = new(StringComparer.Ordinal);
    private readonly RenderOptions _renderOptions;
    private RichEditTextDocument? _document;
    private DocumentLayout? _embeddedLayoutSnapshot;
    private long _embeddedDirtyVersionSnapshot = long.MinValue;
    private int _embeddedSegmentStartLineSnapshot = -1;
    private int _embeddedSegmentLineCountSnapshot = -1;
    private float _embeddedSegmentStartYSnapshot = float.NaN;
    private float _embeddedSegmentHeightSnapshot = float.NaN;
    private bool _isPointerSelecting;
    private bool _isEmbeddedUiInteractive;
    private bool _autoUpdateViewport = true;
    private bool _isCanvasInputEnabled = true;
    private bool _isScrollEnabled = true;
    private float _verticalScrollOffset;
    private float _contentHeight = 1f;
    private bool _suppressScrollBarValueChange;
    private DateTimeOffset _lastPrimaryPointerDownTimestamp = DateTimeOffset.MinValue;
    private Point _lastPrimaryPointerDownPosition;
    private int _primaryPointerDownCount;
    private bool _hasRenderSegment;
    private int _segmentStartLineIndex;
    private int _segmentLineCount;
    private float _segmentStartY;
    private float _segmentHeight;

    public EngineRichEditHost()
    {
        _renderOptions = new RenderOptions
        {
            BackgroundColor = new DocColor(255, 255, 255),
            PageColor = new DocColor(255, 255, 255),
            PageBorderColor = new DocColor(230, 230, 230),
            PageBorderThickness = 1f,
            TextColor = DocColor.Black,
            SelectionColor = DocColor.SelectionBlue,
            CaretColor = DocColor.Black,
            CaretThickness = 1.5f,
            ShowCaret = true,
            UseHarfBuzz = true,
            UsePictureCache = true,
            ZoomFactor = 1f,
            SvgRasterizationScale = 1f
        };

        _canvas = new SKXamlCanvas();
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerWheelChanged += OnPointerWheelChanged;
        _canvas.KeyDown += OnKeyDown;
        _canvas.SizeChanged += OnCanvasSizeChanged;
        _canvas.IsTabStop = true;
        _canvas.IsHitTestVisible = true;

        _embeddedUiOverlay = new Canvas();
        _embeddedUiOverlay.IsHitTestVisible = false;

        _verticalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = ScrollBarWidth,
            Minimum = 0d,
            Maximum = 0d,
            SmallChange = ScrollStepFallback,
            LargeChange = 120d,
            Visibility = Visibility.Collapsed
        };
        _verticalScrollBar.ValueChanged += OnVerticalScrollBarValueChanged;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var contentHost = new Grid();
        contentHost.Children.Add(_canvas);
        contentHost.Children.Add(_embeddedUiOverlay);
        Grid.SetColumn(contentHost, 0);
        root.Children.Add(contentHost);

        Grid.SetColumn(_verticalScrollBar, 1);
        root.Children.Add(_verticalScrollBar);

        IsTabStop = true;
        Content = root;
    }

    public RichEditTextDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value))
            {
                return;
            }

            if (_document is not null)
            {
                _document.Changed -= OnDocumentChanged;
            }

            _document = value;
            ResetEmbeddedUiSnapshot();

            if (_document is not null)
            {
                _document.Changed += OnDocumentChanged;
                UpdateViewport();
                SyncEmbeddedUiLayout(force: true);
            }
            else
            {
                ClearEmbeddedUiElements();
            }

            _canvas.Invalidate();
        }
    }

    public bool ShowCaretWhenReadOnly { get; set; }

    public bool AutoUpdateViewport
    {
        get => _autoUpdateViewport;
        set
        {
            if (_autoUpdateViewport == value)
            {
                return;
            }

            _autoUpdateViewport = value;
            UpdateViewport();
            _canvas.Invalidate();
        }
    }

    public bool IsCanvasInputEnabled
    {
        get => _isCanvasInputEnabled;
        set
        {
            if (_isCanvasInputEnabled == value)
            {
                return;
            }

            _isCanvasInputEnabled = value;
            _canvas.IsHitTestVisible = value;
            _canvas.IsTabStop = value;
        }
    }

    public bool IsEmbeddedUiInteractive
    {
        get => _isEmbeddedUiInteractive;
        set
        {
            if (_isEmbeddedUiInteractive == value)
            {
                return;
            }

            _isEmbeddedUiInteractive = value;
            UpdateEmbeddedUiInteractivity();
        }
    }

    public bool IsScrollEnabled
    {
        get => _isScrollEnabled;
        set
        {
            if (_isScrollEnabled == value)
            {
                return;
            }

            _isScrollEnabled = value;
            if (!_isScrollEnabled)
            {
                SetVerticalScrollOffset(0f);
            }
            else
            {
                ClampVerticalScrollOffset((float)Math.Max(1d, _canvas.ActualHeight));
            }

            UpdateScrollBarState((float)Math.Max(1d, _canvas.ActualHeight));
            _canvas.Invalidate();
        }
    }

    public void SetRenderSegment(int startLineIndex, int lineCount, float startY, float height)
    {
        var normalizedStart = Math.Max(0, startLineIndex);
        var normalizedCount = Math.Max(0, lineCount);
        var normalizedStartY = Math.Max(0f, startY);
        var normalizedHeight = Math.Max(1f, height);

        if (_hasRenderSegment
            && _segmentStartLineIndex == normalizedStart
            && _segmentLineCount == normalizedCount
            && Math.Abs(_segmentStartY - normalizedStartY) < 0.001f
            && Math.Abs(_segmentHeight - normalizedHeight) < 0.001f)
        {
            return;
        }

        _hasRenderSegment = true;
        _segmentStartLineIndex = normalizedStart;
        _segmentLineCount = normalizedCount;
        _segmentStartY = normalizedStartY;
        _segmentHeight = normalizedHeight;
        SetVerticalScrollOffset(0f);
        UpdateScrollBarState((float)Math.Max(1d, _canvas.ActualHeight));
        ResetEmbeddedUiSnapshot();
        _canvas.Invalidate();
    }

    public void ClearRenderSegment()
    {
        if (!_hasRenderSegment)
        {
            return;
        }

        _hasRenderSegment = false;
        _segmentStartLineIndex = 0;
        _segmentLineCount = 0;
        _segmentStartY = 0f;
        _segmentHeight = 0f;
        ClampVerticalScrollOffset((float)Math.Max(1d, _canvas.ActualHeight));
        UpdateScrollBarState((float)Math.Max(1d, _canvas.ActualHeight));
        ResetEmbeddedUiSnapshot();
        _canvas.Invalidate();
    }

    public void InvalidateSurface()
    {
        _canvas.Invalidate();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        RefreshContentHeight();
        var viewportHeight = (float)Math.Max(1d, _canvas.ActualHeight);
        ClampVerticalScrollOffset(viewportHeight);
        EnsureCaretVisible(viewportHeight);
        UpdateScrollBarState(viewportHeight);
        SyncEmbeddedUiLayout(force: false);
        _canvas.Invalidate();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateViewport();
        var viewportHeight = (float)Math.Max(1d, _canvas.ActualHeight);
        ClampVerticalScrollOffset(viewportHeight);
        EnsureCaretVisible(viewportHeight);
        UpdateScrollBarState(viewportHeight);
        SyncEmbeddedUiLayout(force: true);
    }

    private void UpdateViewport()
    {
        if (_document is null || !_autoUpdateViewport)
        {
            return;
        }

        var width = (float)Math.Max(1d, _canvas.ActualWidth);
        var height = (float)Math.Max(1d, _canvas.ActualHeight);
        _document.UpdateViewport(width, height);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_document is null)
        {
            e.Surface.Canvas.Clear(SkiaSharp.SKColors.White);
            ClearEmbeddedUiElements();
            ResetEmbeddedUiSnapshot();
            return;
        }

        _renderOptions.Caret = _document.EditorCaret;
        _renderOptions.SelectionRanges = _document.EditorSelectionRanges;
        _renderOptions.DirtyPages = _document.DirtyPages;
        _renderOptions.DirtyVersion = _document.DirtyVersion;
        _renderOptions.ShowCaret = !_document.IsReadOnly || ShowCaretWhenReadOnly;
        var viewportWidth = (float)Math.Max(1d, _canvas.ActualWidth);
        var viewportHeight = (float)Math.Max(1d, _canvas.ActualHeight);
        RefreshContentHeight();
        ClampVerticalScrollOffset(viewportHeight);
        UpdateScrollBarState(viewportHeight);

        var scaleX = ResolvePixelScale(viewportWidth, e.Info.Width);
        var scaleY = ResolvePixelScale(viewportHeight, e.Info.Height);
        _renderOptions.SvgRasterizationScale = MathF.Max(scaleX, scaleY);

        var windowStartY = GetActiveWindowStartY();
        var visibleHeight = _hasRenderSegment
            ? Math.Max(_segmentHeight, viewportHeight)
            : viewportHeight;
        _renderOptions.VisibleBounds = new DocRect(0f, windowStartY, viewportWidth, visibleHeight);

        e.Surface.Canvas.Save();
        e.Surface.Canvas.Scale(scaleX, scaleY);
        e.Surface.Canvas.Translate(0f, -windowStartY);
        _document.Render(e.Surface.Canvas, _renderOptions);
        e.Surface.Canvas.Restore();

        SyncEmbeddedUiLayout(force: false);
    }

    private void SyncEmbeddedUiLayout(bool force)
    {
        if (_document is null)
        {
            ClearEmbeddedUiElements();
            ResetEmbeddedUiSnapshot();
            return;
        }

        var embeddedElements = _document.EmbeddedUiElementsById;
        if (embeddedElements.Count == 0)
        {
            ClearEmbeddedUiElements();
            _embeddedLayoutSnapshot = _document.EditorLayout;
            _embeddedDirtyVersionSnapshot = _document.DirtyVersion;
            return;
        }

        var layout = _document.EditorLayout;
        if (!force
            && ReferenceEquals(_embeddedLayoutSnapshot, layout)
            && _embeddedDirtyVersionSnapshot == _document.DirtyVersion
            && _embeddedSegmentStartLineSnapshot == _segmentStartLineIndex
            && _embeddedSegmentLineCountSnapshot == _segmentLineCount
            && Math.Abs(_embeddedSegmentStartYSnapshot - _segmentStartY) < 0.001f
            && Math.Abs(_embeddedSegmentHeightSnapshot - _segmentHeight) < 0.001f)
        {
            return;
        }

        _embeddedLayoutSnapshot = layout;
        _embeddedDirtyVersionSnapshot = _document.DirtyVersion;
        _embeddedSegmentStartLineSnapshot = _segmentStartLineIndex;
        _embeddedSegmentLineCountSnapshot = _segmentLineCount;
        _embeddedSegmentStartYSnapshot = _segmentStartY;
        _embeddedSegmentHeightSnapshot = _segmentHeight;

        var segmentStartLineIndex = _hasRenderSegment
            ? Math.Max(0, _segmentStartLineIndex)
            : 0;
        var segmentEndLineIndex = _hasRenderSegment
            ? Math.Min(layout.Lines.Count, _segmentStartLineIndex + Math.Max(0, _segmentLineCount))
            : layout.Lines.Count;

        var windowStartY = GetActiveWindowStartY();
        var windowHeight = _hasRenderSegment
            ? Math.Max(1f, _segmentHeight)
            : (float)Math.Max(1d, _canvas.ActualHeight);
        var windowEndY = windowStartY + windowHeight;

        var placements = CollectEmbeddedUiPlacements(
            layout,
            _document.EmbeddedUiShapePrefix,
            embeddedElements,
            segmentStartLineIndex,
            segmentEndLineIndex,
            windowStartY,
            windowEndY);
        ApplyEmbeddedUiPlacements(placements, embeddedElements);
        UpdateEmbeddedUiInteractivity();
    }

    private static Dictionary<string, Rect> CollectEmbeddedUiPlacements(
        DocumentLayout layout,
        string shapePrefix,
        IReadOnlyDictionary<string, EmbeddedFlowUiElement> embeddedElements,
        int segmentStartLineIndex,
        int segmentEndLineIndex,
        float windowStartY,
        float windowEndY)
    {
        var placements = new Dictionary<string, Rect>(StringComparer.Ordinal);
        AddInlinePlacements(
            layout.Lines,
            shapePrefix,
            embeddedElements,
            placements,
            segmentStartLineIndex,
            segmentEndLineIndex,
            windowStartY,
            windowEndY);
        AddFloatingPlacements(
            layout.FloatingObjects,
            shapePrefix,
            embeddedElements,
            placements,
            windowStartY,
            windowEndY);
        AddFloatingPlacements(
            layout.ExtraFloatingObjects,
            shapePrefix,
            embeddedElements,
            placements,
            windowStartY,
            windowEndY);
        return placements;
    }

    private static void AddInlinePlacements(
        IReadOnlyList<LayoutLine> lines,
        string shapePrefix,
        IReadOnlyDictionary<string, EmbeddedFlowUiElement> embeddedElements,
        Dictionary<string, Rect> placements,
        int segmentStartLineIndex,
        int segmentEndLineIndex,
        float windowStartY,
        float windowEndY)
    {
        var start = Math.Clamp(segmentStartLineIndex, 0, lines.Count);
        var end = Math.Clamp(segmentEndLineIndex, start, lines.Count);
        for (var lineIndex = start; lineIndex < end; lineIndex++)
        {
            var line = lines[lineIndex];
            var shapes = line.Shapes;
            for (var shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
            {
                var shape = shapes[shapeIndex];
                if (!FlowDocumentConverter.TryParseEmbeddedUiElementId(shape.Shape.Name, shapePrefix, out var id)
                    || placements.ContainsKey(id)
                    || !embeddedElements.TryGetValue(id, out var element)
                    || element.Child is not UIElement)
                {
                    continue;
                }

                var bounds = ComputeInlineShapeBounds(line, shape);
                if (!IntersectsVertical(bounds.Top, bounds.Bottom, windowStartY, windowEndY))
                {
                    continue;
                }

                placements[id] = ToViewRect(bounds, windowStartY);
            }
        }
    }

    private static void AddFloatingPlacements(
        IReadOnlyList<FloatingLayoutObject> floatingObjects,
        string shapePrefix,
        IReadOnlyDictionary<string, EmbeddedFlowUiElement> embeddedElements,
        Dictionary<string, Rect> placements,
        float windowStartY,
        float windowEndY)
    {
        for (var i = 0; i < floatingObjects.Count; i++)
        {
            var floating = floatingObjects[i];
            if (floating.Object.Content is not ShapeInline shape)
            {
                continue;
            }

            if (!FlowDocumentConverter.TryParseEmbeddedUiElementId(shape.Name, shapePrefix, out var id)
                || placements.ContainsKey(id)
                || !embeddedElements.TryGetValue(id, out var element)
                || element.Child is not UIElement)
            {
                continue;
            }

            var bounds = floating.Bounds;
            if (!IntersectsVertical(bounds.Top, bounds.Bottom, windowStartY, windowEndY))
            {
                continue;
            }

            // Floating objects are owned by the segment containing their top.
            if (bounds.Top + 0.5f < windowStartY || bounds.Top >= windowEndY)
            {
                continue;
            }

            placements[id] = ToViewRect(bounds, windowStartY);
        }
    }

    private static bool IntersectsVertical(float top, float bottom, float windowTop, float windowBottom)
    {
        return bottom > windowTop && top < windowBottom;
    }

    private static Rect ToViewRect(DocRect bounds, float windowStartY)
    {
        return new Rect(
            bounds.X,
            bounds.Y - windowStartY,
            Math.Max(1d, bounds.Width),
            Math.Max(1d, bounds.Height));
    }

    private void ApplyEmbeddedUiPlacements(
        IReadOnlyDictionary<string, Rect> placements,
        IReadOnlyDictionary<string, EmbeddedFlowUiElement> embeddedElements)
    {
        if (placements.Count == 0)
        {
            ClearEmbeddedUiElements();
            return;
        }

        var staleIds = new List<string>();
        foreach (var id in _embeddedElementsById.Keys)
        {
            if (!placements.ContainsKey(id)
                || !embeddedElements.TryGetValue(id, out var element)
                || element.Child is not UIElement)
            {
                staleIds.Add(id);
            }
        }

        for (var i = 0; i < staleIds.Count; i++)
        {
            RemoveEmbeddedElement(staleIds[i]);
        }

        foreach (var placement in placements)
        {
            if (!embeddedElements.TryGetValue(placement.Key, out var element)
                || element.Child is not UIElement sourceElement)
            {
                continue;
            }

            if (_embeddedElementsById.TryGetValue(placement.Key, out var currentElement)
                && !ReferenceEquals(currentElement, sourceElement))
            {
                RemoveEmbeddedElement(placement.Key);
            }

            if (!_embeddedElementsById.TryGetValue(placement.Key, out var hostedElement))
            {
                hostedElement = sourceElement;
                _embeddedElementsById[placement.Key] = hostedElement;
            }

            AttachEmbeddedElement(hostedElement);

            var bounds = placement.Value;
            Canvas.SetLeft(hostedElement, bounds.X);
            Canvas.SetTop(hostedElement, bounds.Y);
            if (hostedElement is FrameworkElement frameworkElement)
            {
                frameworkElement.Width = Math.Max(1d, bounds.Width);
                frameworkElement.Height = Math.Max(1d, bounds.Height);
            }
        }
    }

    private void AttachEmbeddedElement(UIElement element)
    {
        if (element is FrameworkElement frameworkElement
            && ReferenceEquals(frameworkElement.Parent, _embeddedUiOverlay))
        {
            return;
        }

        DetachElementFromParent(element);
        _embeddedUiOverlay.Children.Add(element);
    }

    private void RemoveEmbeddedElement(string id)
    {
        if (_embeddedElementsById.Remove(id, out var element))
        {
            DetachElementFromParent(element);
        }
    }

    private void ClearEmbeddedUiElements()
    {
        if (_embeddedElementsById.Count == 0)
        {
            return;
        }

        var ids = _embeddedElementsById.Keys.ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            RemoveEmbeddedElement(ids[i]);
        }
    }

    private void UpdateEmbeddedUiInteractivity()
    {
        _embeddedUiOverlay.IsHitTestVisible = _isEmbeddedUiInteractive;
        foreach (var element in _embeddedElementsById.Values)
        {
            element.IsHitTestVisible = _isEmbeddedUiInteractive;
            if (element is Control control)
            {
                control.IsEnabled = _isEmbeddedUiInteractive;
            }
        }
    }

    private void ResetEmbeddedUiSnapshot()
    {
        _embeddedLayoutSnapshot = null;
        _embeddedDirtyVersionSnapshot = long.MinValue;
        _embeddedSegmentStartLineSnapshot = -1;
        _embeddedSegmentLineCountSnapshot = -1;
        _embeddedSegmentStartYSnapshot = float.NaN;
        _embeddedSegmentHeightSnapshot = float.NaN;
    }

    private static void DetachElementFromParent(UIElement element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return;
        }

        switch (frameworkElement.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
            case Border border when ReferenceEquals(border.Child, element):
                border.Child = null;
                break;
            case ContentPresenter presenter when ReferenceEquals(presenter.Content, element):
                presenter.Content = null;
                break;
        }
    }

    private static DocRect ComputeInlineShapeBounds(LayoutLine line, LayoutShape shape)
    {
        var width = shape.Width;
        var height = shape.Height;
        if (!DocTextDirectionHelpers.IsVertical(line.TextDirection))
        {
            var baseline = line.Y + line.Ascent;
            return new DocRect(line.X + shape.X, baseline - height, width, height);
        }

        var baseRotation = DocTextDirectionHelpers.GetRotationDegrees(line.TextDirection!.Value);
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
        return new DocPoint(
            originX + (x * cos) - (y * sin),
            originY + (x * sin) + (y * cos));
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (_document is null || _hasRenderSegment || !_isScrollEnabled)
        {
            return;
        }

        var point = args.GetCurrentPoint(_canvas);
        var wheelDelta = point.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
        {
            return;
        }

        var step = ResolveScrollStep() * ScrollStepLineMultiplier;
        var nextOffset = _verticalScrollOffset - (wheelDelta / 120f) * step;
        if (SetVerticalScrollOffset(nextOffset))
        {
            args.Handled = true;
            _canvas.Invalidate();
        }
    }

    private float ToDocumentY(float hostY)
    {
        return hostY + GetActiveWindowStartY();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_document is null)
        {
            return;
        }

        var modifiers = GetCurrentModifiers();
        var hasCommandModifier = (modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0;
        if (hasCommandModifier
            && (modifiers & EditorModifiers.Alt) == 0
            && TryHandleFormattingShortcut(args.Key))
        {
            EnsureCaretVisible((float)Math.Max(1d, _canvas.ActualHeight));
            args.Handled = true;
            _canvas.Invalidate();
            return;
        }

        if (TryMapKey(args.Key, hasCommandModifier, out var editorKey))
        {
            if (_document.HandleKey(editorKey, modifiers))
            {
                EnsureCaretVisible((float)Math.Max(1d, _canvas.ActualHeight));
                args.Handled = true;
                _canvas.Invalidate();
            }

            return;
        }

        if ((modifiers & (EditorModifiers.Control | EditorModifiers.Alt | EditorModifiers.Meta)) != 0)
        {
            return;
        }

        if (TryMapPrintableKey(args.Key, modifiers, out var text)
            && _document.HandleTextInput(text, modifiers))
        {
            EnsureCaretVisible((float)Math.Max(1d, _canvas.ActualHeight));
            args.Handled = true;
            _canvas.Invalidate();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_document is null)
        {
            return;
        }

        _canvas.Focus(FocusState.Programmatic);
        _canvas.CapturePointer(args.Pointer);

        var point = args.GetCurrentPoint(_canvas);
        var button = ResolveButton(point);
        var clickCount = ResolveClickCount(point, button);
        var handled = _document.HandlePointer(
            EditorPointerKind.Down,
            (float)point.Position.X,
            ToDocumentY((float)point.Position.Y),
            button,
            GetCurrentModifiers(),
            clickCount);
        if (handled)
        {
            EnsureCaretVisible((float)Math.Max(1d, _canvas.ActualHeight));
            args.Handled = true;
            _isPointerSelecting = button == EditorPointerButton.Primary;
            _canvas.Invalidate();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_document is null || !_isPointerSelecting)
        {
            return;
        }

        var point = args.GetCurrentPoint(_canvas);
        var handled = _document.HandlePointer(
            EditorPointerKind.Move,
            (float)point.Position.X,
            ToDocumentY((float)point.Position.Y),
            EditorPointerButton.Primary,
            GetCurrentModifiers(),
            clickCount: 1);
        if (handled)
        {
            EnsureCaretVisible((float)Math.Max(1d, _canvas.ActualHeight));
            args.Handled = true;
            _canvas.Invalidate();
        }
    }

    private void OnVerticalScrollBarValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_suppressScrollBarValueChange || _document is null || _hasRenderSegment || !_isScrollEnabled)
        {
            return;
        }

        if (SetVerticalScrollOffset((float)args.NewValue))
        {
            SyncEmbeddedUiLayout(force: false);
            _canvas.Invalidate();
        }
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

    private static bool IsWithinMultiClickDistance(Point current, Point previous)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        return (dx * dx) + (dy * dy) <= MultiClickDistanceThreshold * MultiClickDistanceThreshold;
    }

    private bool TryHandleFormattingShortcut(VirtualKey key)
    {
        if (_document is null || _document.IsReadOnly)
        {
            return false;
        }

        return key switch
        {
            VirtualKey.B => _document.ToggleBold(),
            VirtualKey.I => _document.ToggleItalic(),
            VirtualKey.U => _document.ToggleUnderline(),
            _ => false
        };
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_document is null)
        {
            return;
        }

        var point = args.GetCurrentPoint(_canvas);
        var handled = _document.HandlePointer(
            EditorPointerKind.Up,
            (float)point.Position.X,
            ToDocumentY((float)point.Position.Y),
            ResolveButton(point),
            GetCurrentModifiers(),
            clickCount: 1);

        _canvas.ReleasePointerCaptures();
        _isPointerSelecting = false;

        if (handled)
        {
            EnsureCaretVisible((float)Math.Max(1d, _canvas.ActualHeight));
            args.Handled = true;
            _canvas.Invalidate();
        }
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

    private float GetActiveWindowStartY()
    {
        if (_hasRenderSegment)
        {
            return _segmentStartY;
        }

        return _isScrollEnabled ? _verticalScrollOffset : 0f;
    }

    private void RefreshContentHeight()
    {
        if (_document is null)
        {
            _contentHeight = 1f;
            return;
        }

        _contentHeight = EstimateLayoutHeight(_document.EditorLayout);
    }

    private static float EstimateLayoutHeight(DocumentLayout layout)
    {
        var maxBottom = 1f;
        var lines = layout.Lines;
        if (lines.Count > 0)
        {
            var last = lines[^1];
            maxBottom = Math.Max(maxBottom, last.Y + last.LineHeight);
        }

        for (var i = 0; i < layout.FloatingObjects.Count; i++)
        {
            maxBottom = Math.Max(maxBottom, layout.FloatingObjects[i].Bounds.Bottom);
        }

        for (var i = 0; i < layout.ExtraFloatingObjects.Count; i++)
        {
            maxBottom = Math.Max(maxBottom, layout.ExtraFloatingObjects[i].Bounds.Bottom);
        }

        return Math.Max(1f, maxBottom);
    }

    private bool ClampVerticalScrollOffset(float viewportHeight)
    {
        var maxOffset = Math.Max(0f, _contentHeight - Math.Max(1f, viewportHeight));
        return SetVerticalScrollOffset(Math.Clamp(_verticalScrollOffset, 0f, maxOffset));
    }

    private bool SetVerticalScrollOffset(float offset)
    {
        var viewportHeight = (float)Math.Max(1d, _canvas.ActualHeight);
        var maxOffset = Math.Max(0f, _contentHeight - viewportHeight);
        var clamped = Math.Clamp(offset, 0f, maxOffset);
        if (Math.Abs(clamped - _verticalScrollOffset) < 0.01f)
        {
            return false;
        }

        _verticalScrollOffset = clamped;
        ResetEmbeddedUiSnapshot();
        SyncScrollBarValue();
        return true;
    }

    private float ResolveScrollStep()
    {
        var lines = _document?.EditorLayout.Lines;
        if (lines is { Count: > 0 })
        {
            return Math.Clamp(lines[0].LineHeight, 12f, 64f);
        }

        return ScrollStepFallback;
    }

    private void UpdateScrollBarState(float viewportHeight)
    {
        if (_hasRenderSegment || !_isScrollEnabled)
        {
            _verticalScrollBar.Visibility = Visibility.Collapsed;
            SyncScrollBarValue();
            return;
        }

        var viewport = Math.Max(1f, viewportHeight);
        var maxOffset = Math.Max(0f, _contentHeight - viewport);
        if (maxOffset <= 0.5f)
        {
            _verticalScrollBar.Visibility = Visibility.Collapsed;
            _verticalScrollBar.Maximum = 0d;
            _verticalScrollBar.ViewportSize = viewport;
            SyncScrollBarValue();
            return;
        }

        _verticalScrollBar.Visibility = Visibility.Visible;
        _verticalScrollBar.Minimum = 0d;
        _verticalScrollBar.Maximum = maxOffset;
        _verticalScrollBar.ViewportSize = viewport;
        _verticalScrollBar.SmallChange = Math.Max(1f, ResolveScrollStep());
        _verticalScrollBar.LargeChange = Math.Max(1f, viewport * 0.9f);
        SyncScrollBarValue();
    }

    private void SyncScrollBarValue()
    {
        var minimum = _verticalScrollBar.Minimum;
        var maximum = Math.Max(minimum, _verticalScrollBar.Maximum);
        var clamped = Math.Clamp((double)_verticalScrollOffset, minimum, maximum);
        _suppressScrollBarValueChange = true;
        try
        {
            _verticalScrollBar.Value = clamped;
        }
        finally
        {
            _suppressScrollBarValueChange = false;
        }
    }

    private void EnsureCaretVisible(float viewportHeight)
    {
        if (_document is null || _hasRenderSegment || !_isScrollEnabled)
        {
            return;
        }

        if (!_document.TryGetCaretPoint(out _, out var caretY, out var lineIndex))
        {
            return;
        }

        var lineHeight = ResolveCaretLineHeight(_document.EditorLayout, lineIndex);
        var caretTop = Math.Max(0f, caretY);
        var caretBottom = caretTop + Math.Max(1f, lineHeight);
        var viewTop = _verticalScrollOffset;
        var viewBottom = viewTop + Math.Max(1f, viewportHeight);

        float targetOffset;
        if (caretTop < viewTop)
        {
            targetOffset = caretTop;
        }
        else if (caretBottom > viewBottom)
        {
            targetOffset = caretBottom - viewportHeight;
        }
        else
        {
            return;
        }

        if (SetVerticalScrollOffset(targetOffset))
        {
            SyncEmbeddedUiLayout(force: true);
        }
    }

    private static float ResolveCaretLineHeight(DocumentLayout layout, int lineIndex)
    {
        var lines = layout.Lines;
        if ((uint)lineIndex < (uint)lines.Count)
        {
            return lines[lineIndex].LineHeight;
        }

        if (lines.Count > 0)
        {
            return lines[^1].LineHeight;
        }

        return ScrollStepFallback;
    }

    private static float ResolvePixelScale(float logicalSize, int pixelSize)
    {
        var logical = Math.Max(1f, logicalSize);
        var scale = pixelSize / logical;
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
        {
            return 1f;
        }

        return scale;
    }
}
