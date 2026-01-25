using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.App;

public sealed class HorizontalRuler : Control
{
    public static readonly StyledProperty<DocumentView?> EditorViewProperty =
        AvaloniaProperty.Register<HorizontalRuler, DocumentView?>(nameof(EditorView));

    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<HorizontalRuler, Color>(nameof(SurfaceColor), new Color(255, 238, 241, 245));

    public static readonly StyledProperty<TabAlignment> DefaultTabAlignmentProperty =
        AvaloniaProperty.Register<HorizontalRuler, TabAlignment>(nameof(DefaultTabAlignment), TabAlignment.Left);

    private const float MarkerHitSize = 6f;
    private const float TabHitSize = 6f;
    private const float MarkerWidth = 8f;
    private const float MarkerHeight = 6f;
    private const float PreviewEpsilon = 0.25f;

    private static readonly Color BorderColor = new Color(255, 208, 212, 219);
    private static readonly Color TickColor = new Color(255, 94, 94, 94);
    private static readonly Color TextColor = new Color(255, 64, 64, 64);
    private static readonly Color MarkerColor = new Color(255, 58, 110, 165);
    private static readonly Color MarginShadeColor = new Color(255, 226, 229, 234);
    private static readonly Pen TickPen = new Pen(new SolidColorBrush(TickColor), 1);
    private static readonly Pen BorderPen = new Pen(new SolidColorBrush(BorderColor), 1);
    private static readonly Pen MarkerPen = new Pen(new SolidColorBrush(MarkerColor), 1);
    private static readonly IBrush MarkerBrush = new SolidColorBrush(MarkerColor);
    private static readonly IBrush MarginBrush = new SolidColorBrush(MarginShadeColor);
    private static readonly IBrush TextBrush = new SolidColorBrush(TextColor);
    private static readonly Typeface LabelTypeface = new Typeface(FontFamily.Default);

    private IBrush _surfaceBrush = new SolidColorBrush(new Color(255, 238, 241, 245));
    private DocumentStyleResolver? _styleResolver;
    private Document? _styleDocument;
    private DragState? _drag;
    private EditorSessionSnapshot? _previewSnapshot;
    private bool _previewApplied;
    private float _lastPreviewDocX = float.NaN;

    public DocumentView? EditorView
    {
        get => GetValue(EditorViewProperty);
        set => SetValue(EditorViewProperty, value);
    }

    public Color SurfaceColor
    {
        get => GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
    }

    public TabAlignment DefaultTabAlignment
    {
        get => GetValue(DefaultTabAlignmentProperty);
        set => SetValue(DefaultTabAlignmentProperty, value);
    }

    public HorizontalRuler()
    {
        ClipToBounds = true;
        UpdateSurfaceBrush(SurfaceColor);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SurfaceColorProperty)
        {
            UpdateSurfaceBrush(change.GetNewValue<Color>());
        }

        if (change.Property == EditorViewProperty)
        {
            if (change.OldValue is DocumentView oldView)
            {
                DetachEditorView(oldView);
            }

            if (change.NewValue is DocumentView newView)
            {
                AttachEditorView(newView);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (EditorView is null)
        {
            return;
        }

        if (!TryBuildContext(out var context))
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (TryHitTabStop(point.Position, context, out var tab))
            {
                var nextAlignment = NextTabAlignment(tab.Alignment);
                var payload = new EditorParagraphTabStopUpdateRequest(
                    tab.Position,
                    tab.Position,
                    nextAlignment,
                    tab.Leader);
                RulerHelpers.ExecuteCommand(EditorView, EditorHomeCommandIds.Paragraph.TabStopUpdate, payload);
                e.Handled = true;
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var drag = HitTest(point.Position, context);
        if (drag is not null)
        {
            _drag = drag;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (TryAddTabStop(point.Position, context))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is null)
        {
            return;
        }

        if (!TryBuildContext(out var context))
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var docX = ScreenToDocX(point.Position.X, context);
        _drag = _drag with { DocX = docX };
        ApplyDragPreview(point.Position, context, _drag);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is null || EditorView is null)
        {
            return;
        }

        if (!TryBuildContext(out var context))
        {
            _drag = null;
            CancelPreview();
            e.Pointer.Capture(null);
            return;
        }

        var drag = _drag;
        _drag = null;
        var point = e.GetCurrentPoint(this);
        if (_previewSnapshot.HasValue)
        {
            ApplyDragPreview(point.Position, context, drag, force: true);
            CommitPreview();
        }
        else
        {
            CommitDrag(point.Position, context, drag);
        }
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        CancelPreview();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(_surfaceBrush, Bounds);

        if (!TryBuildContext(out var ruler))
        {
            DrawBorder(context);
            return;
        }

        DrawMargins(context, ruler);
        DrawTicks(context, ruler);
        DrawMarginMarkers(context, ruler);
        DrawIndentMarkers(context, ruler);
        DrawTabStops(context, ruler);
        DrawTableMarkers(context, ruler);
        DrawBorder(context);
    }

    private void AttachEditorView(DocumentView view)
    {
        view.EditorStateChanged += OnEditorChanged;
        view.ZoomChanged += OnEditorChanged;
        view.ScrollInvalidated += OnEditorChanged;
        EnsureStyleResolver(view.Document);
        InvalidateVisual();
    }

    private void DetachEditorView(DocumentView view)
    {
        view.EditorStateChanged -= OnEditorChanged;
        view.ZoomChanged -= OnEditorChanged;
        view.ScrollInvalidated -= OnEditorChanged;
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (EditorView is not null)
        {
            EnsureStyleResolver(EditorView.Document);
        }

        InvalidateVisual();
    }

    private void UpdateSurfaceBrush(Color color)
    {
        _surfaceBrush = new SolidColorBrush(color);
        InvalidateVisual();
    }

    private void EnsureStyleResolver(Document document)
    {
        if (!ReferenceEquals(_styleDocument, document))
        {
            _styleResolver = new DocumentStyleResolver(document);
            _styleDocument = document;
        }
    }

    private bool TryBuildContext(out RulerContext context)
    {
        context = default;
        var view = EditorView;
        if (view is null)
        {
            return false;
        }

        if (!RulerHelpers.TryGetCurrentPage(view, out var layout, out var page, out var pageSection, out var baseSection, out var pageIndex, out var sectionIndex))
        {
            return false;
        }

        var document = view.Document;
        if (document.ParagraphCount == 0)
        {
            return false;
        }

        EnsureStyleResolver(document);
        var paragraphIndex = Math.Clamp(view.Caret.ParagraphIndex, 0, document.ParagraphCount - 1);
        var paragraph = document.GetParagraph(paragraphIndex);
        var resolvedProperties = _styleResolver?.ResolveParagraphProperties(paragraph) ?? paragraph.Properties;

        var indentLeft = resolvedProperties.IndentLeft ?? 0f;
        var indentRight = resolvedProperties.IndentRight ?? 0f;
        var firstLineIndent = resolvedProperties.FirstLineIndent ?? 0f;

        TableLayout? tableLayout = null;
        if (RulerHelpers.TryGetTableLayout(document, layout, paragraphIndex, out var layoutTable))
        {
            tableLayout = layoutTable;
        }

        context = new RulerContext(
            view,
            document,
            layout,
            page,
            pageSection,
            baseSection,
            paragraph,
            resolvedProperties,
            indentLeft,
            indentRight,
            firstLineIndent,
            page.ContentBounds.Left,
            page.ContentBounds.Right,
            page.Bounds.Left,
            page.Bounds.Right,
            pageIndex,
            sectionIndex,
            view.ZoomFactor,
            view.EffectiveScrollOffset,
            tableLayout);
        return true;
    }

    private void DrawMargins(DrawingContext context, RulerContext ruler)
    {
        var height = Bounds.Height;
        var pageLeft = DocToScreenX(ruler.PageLeft, ruler);
        var pageRight = DocToScreenX(ruler.PageRight, ruler);
        var contentLeft = DocToScreenX(ruler.ContentLeft, ruler);
        var contentRight = DocToScreenX(ruler.ContentRight, ruler);

        if (contentLeft > pageLeft)
        {
            context.FillRectangle(MarginBrush, new Rect(pageLeft, 0, contentLeft - pageLeft, height));
        }

        if (pageRight > contentRight)
        {
            context.FillRectangle(MarginBrush, new Rect(contentRight, 0, pageRight - contentRight, height));
        }
    }

    private void DrawTicks(DrawingContext context, RulerContext ruler)
    {
        var width = (float)Bounds.Width;
        var height = (float)Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var zoom = ruler.Zoom;
        var offsetX = (float)ruler.Offset.X;
        var viewLeftDoc = offsetX / zoom;
        var viewRightDoc = (offsetX + width) / zoom;
        var origin = ruler.ContentLeft;

        var minorTick = RulerHelpers.MinorTick;
        var majorStep = RulerHelpers.MinorTicksPerMajor;
        var labelStep = RulerHelpers.MinorTicksPerLabel;
        var startIndex = (int)MathF.Floor((viewLeftDoc - origin) / minorTick) - 1;
        var endIndex = (int)MathF.Ceiling((viewRightDoc - origin) / minorTick) + 1;

        for (var i = startIndex; i <= endIndex; i++)
        {
            var docX = origin + i * minorTick;
            if (docX < ruler.PageLeft - minorTick || docX > ruler.PageRight + minorTick)
            {
                continue;
            }

            var screenX = docX * zoom - offsetX;
            var tickHeight = 3f;
            if (i % labelStep == 0)
            {
                tickHeight = 8f;
            }
            else if (i % majorStep == 0)
            {
                tickHeight = 6f;
            }
            else if (i % 2 == 0)
            {
                tickHeight = 4f;
            }

            context.DrawLine(TickPen, new Point(screenX, height - tickHeight), new Point(screenX, height));

            if (i % labelStep == 0)
            {
                var labelValue = i / labelStep;
                DrawLabel(context, labelValue.ToString(CultureInfo.InvariantCulture), screenX, 2f);
            }
        }
    }

    private void DrawMarginMarkers(DrawingContext context, RulerContext ruler)
    {
        var y = MarkerHeight + 2f;
        DrawTriangle(context, DocToScreenX(ruler.ContentLeft, ruler), y, true);
        DrawTriangle(context, DocToScreenX(ruler.ContentRight, ruler), y, true);
    }

    private void DrawIndentMarkers(DrawingContext context, RulerContext ruler)
    {
        var height = (float)Bounds.Height;
        var leftIndentX = DocToScreenX(ruler.ContentLeft + ruler.IndentLeft, ruler);
        var rightIndentX = DocToScreenX(ruler.ContentRight - ruler.IndentRight, ruler);
        var firstLineX = DocToScreenX(ruler.ContentLeft + ruler.IndentLeft + ruler.FirstLineIndent, ruler);
        var bottomY = height - MarkerHeight - 1f;
        var upperY = bottomY - MarkerHeight - 2f;

        DrawTriangle(context, leftIndentX, bottomY, false);
        DrawTriangle(context, firstLineX, upperY, true);
        DrawTriangle(context, rightIndentX, bottomY, false);
    }

    private void DrawTabStops(DrawingContext context, RulerContext ruler)
    {
        var tabTop = MarkerHeight + 2f;
        var lineStart = ruler.ContentLeft + ruler.IndentLeft;
        foreach (var tab in ruler.ParagraphProperties.TabStops)
        {
            var screenX = DocToScreenX(lineStart + tab.Position, ruler);
            DrawTabMarker(context, screenX, tabTop, tab.Alignment, MarkerPen, MarkerBrush);
        }

        if (_drag is { Kind: DragKind.TabStop } drag)
        {
            var dragScreenX = DocToScreenX(drag.DocX, ruler);
            DrawTabMarker(context, dragScreenX, tabTop, drag.TabAlignment, MarkerPen, MarkerBrush);
        }
    }

    private void DrawTableMarkers(DrawingContext context, RulerContext ruler)
    {
        if (ruler.TableLayout is null)
        {
            return;
        }

        var table = ruler.TableLayout;
        if (table.ColumnWidths.Count <= 1)
        {
            return;
        }

        var offsets = BuildColumnOffsets(table.ColumnWidths, table.CellSpacing);
        for (var i = 0; i < table.ColumnWidths.Count - 1; i++)
        {
            var boundary = table.Bounds.Left + offsets[i] + table.ColumnWidths[i];
            var screenX = DocToScreenX(boundary, ruler);
            context.DrawLine(MarkerPen, new Point(screenX, 0), new Point(screenX, Bounds.Height));
        }

        if (_drag is { Kind: DragKind.TableColumn } drag)
        {
            var screenX = DocToScreenX(drag.DocX, ruler);
            context.DrawLine(MarkerPen, new Point(screenX, 0), new Point(screenX, Bounds.Height));
        }
    }

    private void DrawBorder(DrawingContext context)
    {
        var height = Bounds.Height;
        var width = Bounds.Width;
        context.DrawLine(BorderPen, new Point(0, height - 0.5f), new Point(width, height - 0.5f));
    }

    private DragState? HitTest(Point point, RulerContext ruler)
    {
        var height = Bounds.Height;
        var contentLeftX = DocToScreenX(ruler.ContentLeft, ruler);
        var contentRightX = DocToScreenX(ruler.ContentRight, ruler);
        var leftIndentX = DocToScreenX(ruler.ContentLeft + ruler.IndentLeft, ruler);
        var rightIndentX = DocToScreenX(ruler.ContentRight - ruler.IndentRight, ruler);
        var firstLineX = DocToScreenX(ruler.ContentLeft + ruler.IndentLeft + ruler.FirstLineIndent, ruler);
        var docX = ScreenToDocX(point.X, ruler);

        if (point.Y <= MarkerHeight + 6f)
        {
            if (Math.Abs(point.X - contentLeftX) <= MarkerHitSize)
            {
                return new DragState(DragKind.MarginLeft, docX);
            }

            if (Math.Abs(point.X - contentRightX) <= MarkerHitSize)
            {
                return new DragState(DragKind.MarginRight, docX);
            }

            if (TryHitTabStop(point, ruler, out var tab))
            {
                return new DragState(DragKind.TabStop, docX)
                {
                    TabPosition = tab.Position,
                    TabAlignment = tab.Alignment,
                    TabLeader = tab.Leader,
                    TabLineStart = ruler.ContentLeft + ruler.IndentLeft
                };
            }
        }

        if (point.Y >= height - MarkerHeight - 8f)
        {
            if (Math.Abs(point.X - leftIndentX) <= MarkerHitSize)
            {
                return new DragState(DragKind.IndentLeft, docX);
            }

            if (Math.Abs(point.X - firstLineX) <= MarkerHitSize)
            {
                return new DragState(DragKind.FirstLineIndent, docX);
            }

            if (Math.Abs(point.X - rightIndentX) <= MarkerHitSize)
            {
                return new DragState(DragKind.IndentRight, docX);
            }
        }

        if (ruler.TableLayout is not null && point.Y > 2f && point.Y < height - 2f)
        {
            if (TryHitTableBoundary(point, ruler, out var boundaryIndex))
            {
                return new DragState(DragKind.TableColumn, docX)
                {
                    TableBoundaryIndex = boundaryIndex,
                    TableColumnWidths = ruler.TableLayout.ColumnWidths.ToArray(),
                    TableLeft = ruler.TableLayout.Bounds.Left,
                    TableCellSpacing = ruler.TableLayout.CellSpacing
                };
            }
        }

        return null;
    }

    private bool TryHitTabStop(Point point, RulerContext ruler, out TabStopHit tab)
    {
        tab = default;
        var lineStart = ruler.ContentLeft + ruler.IndentLeft;
        foreach (var stop in ruler.ParagraphProperties.TabStops)
        {
            var screenX = DocToScreenX(lineStart + stop.Position, ruler);
            if (Math.Abs(point.X - screenX) <= TabHitSize)
            {
                tab = new TabStopHit(stop.Position, stop.Alignment, stop.Leader);
                return true;
            }
        }

        return false;
    }

    private bool TryAddTabStop(Point point, RulerContext ruler)
    {
        if (EditorView is null)
        {
            return false;
        }

        var docX = ScreenToDocX(point.X, ruler);
        if (docX < ruler.ContentLeft || docX > ruler.ContentRight)
        {
            return false;
        }

        var lineStart = ruler.ContentLeft + ruler.IndentLeft;
        if (docX < lineStart)
        {
            return false;
        }

        var max = MathF.Max(0f, ruler.ContentRight - lineStart);
        var position = Math.Clamp(docX - lineStart, 0f, max);
        var payload = new EditorParagraphTabStopRequest(position, DefaultTabAlignment, TabLeader.None);
        return RulerHelpers.ExecuteCommand(EditorView, EditorHomeCommandIds.Paragraph.TabStopAdd, payload);
    }

    private void CommitDrag(Point point, RulerContext ruler, DragState drag)
    {
        if (EditorView is null)
        {
            return;
        }

        switch (drag.Kind)
        {
            case DragKind.MarginLeft:
                ApplyMarginLeft(ruler, drag.DocX);
                break;
            case DragKind.MarginRight:
                ApplyMarginRight(ruler, drag.DocX);
                break;
            case DragKind.IndentLeft:
                ApplyIndentLeft(ruler, drag.DocX);
                break;
            case DragKind.IndentRight:
                ApplyIndentRight(ruler, drag.DocX);
                break;
            case DragKind.FirstLineIndent:
                ApplyFirstLineIndent(ruler, drag.DocX);
                break;
            case DragKind.TabStop:
                ApplyTabStop(point, ruler, drag);
                break;
            case DragKind.TableColumn:
                ApplyTableColumnResize(ruler, drag);
                break;
        }
    }

    private void ApplyMarginLeft(RulerContext ruler, float docX, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        RulerHelpers.ResolveMargins(ruler.BaseSection, ruler.PageIndex, out var currentLeft, out var currentRight, out _, out _);
        var pageWidth = ruler.PageRight - ruler.PageLeft;
        var resolvedLeft = Math.Clamp(docX - ruler.PageLeft, 0f, pageWidth - RulerHelpers.MinContentSize - currentRight);

        var baseLeft = ruler.BaseSection.MarginLeft;
        var baseRight = ruler.BaseSection.MarginRight;
        var isEven = (ruler.PageIndex + 1) % 2 == 0;
        if (ruler.BaseSection.MirrorMargins && isEven)
        {
            baseRight = MathF.Max(0f, resolvedLeft);
        }
        else
        {
            var adjust = !ruler.BaseSection.GutterAtTop ? ruler.BaseSection.Gutter : 0f;
            baseLeft = MathF.Max(0f, resolvedLeft - adjust);
        }

        var request = new EditorPageMarginsRequest(baseLeft, ruler.BaseSection.MarginTop, baseRight, ruler.BaseSection.MarginBottom);
        RulerHelpers.ExecuteCommand(EditorView, EditorLayoutCommandIds.PageSetup.Margins, request, recordHistory);
    }

    private void ApplyMarginRight(RulerContext ruler, float docX, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        RulerHelpers.ResolveMargins(ruler.BaseSection, ruler.PageIndex, out var currentLeft, out var currentRight, out _, out _);
        var pageWidth = ruler.PageRight - ruler.PageLeft;
        var resolvedRight = Math.Clamp(ruler.PageRight - docX, 0f, pageWidth - RulerHelpers.MinContentSize - currentLeft);

        var baseLeft = ruler.BaseSection.MarginLeft;
        var baseRight = ruler.BaseSection.MarginRight;
        var isEven = (ruler.PageIndex + 1) % 2 == 0;
        if (ruler.BaseSection.MirrorMargins && isEven)
        {
            var adjust = !ruler.BaseSection.GutterAtTop ? ruler.BaseSection.Gutter : 0f;
            baseLeft = MathF.Max(0f, resolvedRight - adjust);
        }
        else
        {
            baseRight = MathF.Max(0f, resolvedRight);
        }

        var request = new EditorPageMarginsRequest(baseLeft, ruler.BaseSection.MarginTop, baseRight, ruler.BaseSection.MarginBottom);
        RulerHelpers.ExecuteCommand(EditorView, EditorLayoutCommandIds.PageSetup.Margins, request, recordHistory);
    }

    private void ApplyIndentLeft(RulerContext ruler, float docX, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        var max = MathF.Max(0f, ruler.ContentRight - ruler.ContentLeft - RulerHelpers.MinContentSize);
        var indentLeft = Math.Clamp(docX - ruler.ContentLeft, 0f, max);
        var options = CreateParagraphOptions(indentLeft: indentLeft, indentRight: null, firstLineIndent: null);
        RulerHelpers.ExecuteCommand(EditorView, EditorHomeCommandIds.Paragraph.DialogApply, options, recordHistory);
    }

    private void ApplyIndentRight(RulerContext ruler, float docX, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        var max = MathF.Max(0f, ruler.ContentRight - ruler.ContentLeft - RulerHelpers.MinContentSize);
        var indentRight = Math.Clamp(ruler.ContentRight - docX, 0f, max);
        var options = CreateParagraphOptions(indentLeft: null, indentRight: indentRight, firstLineIndent: null);
        RulerHelpers.ExecuteCommand(EditorView, EditorHomeCommandIds.Paragraph.DialogApply, options, recordHistory);
    }

    private void ApplyFirstLineIndent(RulerContext ruler, float docX, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        var minX = ruler.ContentLeft;
        var maxX = ruler.ContentRight;
        var clamped = Math.Clamp(docX, minX, maxX);
        var firstLineIndent = clamped - (ruler.ContentLeft + ruler.IndentLeft);
        var options = CreateParagraphOptions(indentLeft: null, indentRight: null, firstLineIndent: firstLineIndent);
        RulerHelpers.ExecuteCommand(EditorView, EditorHomeCommandIds.Paragraph.DialogApply, options, recordHistory);
    }

    private void ApplyTabStop(Point point, RulerContext ruler, DragState drag, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        if (point.Y < -6f || point.Y > Bounds.Height + 6f)
        {
            var remove = new EditorParagraphTabStopRemoveRequest(drag.TabPosition);
            RulerHelpers.ExecuteCommand(EditorView, EditorHomeCommandIds.Paragraph.TabStopRemove, remove, recordHistory);
            return;
        }

        var lineStart = drag.TabLineStart;
        var max = MathF.Max(0f, ruler.ContentRight - lineStart);
        var position = Math.Clamp(drag.DocX - lineStart, 0f, max);
        var update = new EditorParagraphTabStopUpdateRequest(drag.TabPosition, position, drag.TabAlignment, drag.TabLeader);
        RulerHelpers.ExecuteCommand(EditorView, EditorHomeCommandIds.Paragraph.TabStopUpdate, update, recordHistory);
    }

    private void ApplyTableColumnResize(RulerContext ruler, DragState drag, bool recordHistory = true)
    {
        if (EditorView is null || drag.TableColumnWidths is null)
        {
            return;
        }

        var widths = drag.TableColumnWidths;
        var boundaryIndex = drag.TableBoundaryIndex;
        if (boundaryIndex < 0 || boundaryIndex >= widths.Length - 1)
        {
            return;
        }

        var offsets = BuildColumnOffsets(widths, drag.TableCellSpacing);
        var boundary = drag.TableLeft + offsets[boundaryIndex] + widths[boundaryIndex];
        var delta = drag.DocX - boundary;
        var leftWidth = widths[boundaryIndex] + delta;
        var rightWidth = widths[boundaryIndex + 1] - delta;

        var min = RulerHelpers.MinColumnWidth;
        if (leftWidth < min)
        {
            var adjust = min - leftWidth;
            leftWidth = min;
            rightWidth -= adjust;
        }

        if (rightWidth < min)
        {
            var adjust = min - rightWidth;
            rightWidth = min;
            leftWidth -= adjust;
        }

        widths[boundaryIndex] = MathF.Max(min, leftWidth);
        widths[boundaryIndex + 1] = MathF.Max(min, rightWidth);

        var payload = new EditorTableColumnWidthsRequest(widths);
        RulerHelpers.ExecuteCommand(EditorView, EditorTableCommandIds.Layout.ColumnWidthsSet, payload, recordHistory);
    }

    private void ApplyDragPreview(Point point, RulerContext ruler, DragState drag, bool force = false)
    {
        if (EditorView is null)
        {
            return;
        }

        if (!EnsurePreviewSnapshot(EditorView))
        {
            return;
        }

        if (!force
            && drag.Kind != DragKind.TabStop
            && !float.IsNaN(_lastPreviewDocX)
            && MathF.Abs(drag.DocX - _lastPreviewDocX) < PreviewEpsilon)
        {
            return;
        }

        _lastPreviewDocX = drag.DocX;
        switch (drag.Kind)
        {
            case DragKind.MarginLeft:
                ApplyMarginLeft(ruler, drag.DocX, recordHistory: false);
                break;
            case DragKind.MarginRight:
                ApplyMarginRight(ruler, drag.DocX, recordHistory: false);
                break;
            case DragKind.IndentLeft:
                ApplyIndentLeft(ruler, drag.DocX, recordHistory: false);
                break;
            case DragKind.IndentRight:
                ApplyIndentRight(ruler, drag.DocX, recordHistory: false);
                break;
            case DragKind.FirstLineIndent:
                ApplyFirstLineIndent(ruler, drag.DocX, recordHistory: false);
                break;
            case DragKind.TabStop:
                ApplyTabStop(point, ruler, drag, recordHistory: false);
                break;
            case DragKind.TableColumn:
                ApplyTableColumnResize(ruler, drag, recordHistory: false);
                break;
        }

        _previewApplied = true;
    }

    private bool EnsurePreviewSnapshot(DocumentView view)
    {
        if (_previewSnapshot.HasValue)
        {
            return true;
        }

        if (!view.TryGetService<IEditorHistorySnapshotService>(out var snapshotService))
        {
            return false;
        }

        _previewSnapshot = snapshotService.CaptureSnapshot();
        _previewApplied = false;
        _lastPreviewDocX = float.NaN;
        return true;
    }

    private void CommitPreview()
    {
        if (EditorView is null)
        {
            ResetPreview();
            return;
        }

        if (!_previewSnapshot.HasValue)
        {
            return;
        }

        if (!EditorView.TryGetService<IEditorHistorySnapshotService>(out var snapshotService))
        {
            ResetPreview();
            return;
        }

        if (_previewApplied)
        {
            snapshotService.RecordSnapshot(_previewSnapshot.Value);
        }
        else
        {
            snapshotService.RestoreSnapshot(_previewSnapshot.Value);
        }

        ResetPreview();
    }

    private void CancelPreview()
    {
        if (EditorView is null || !_previewSnapshot.HasValue)
        {
            ResetPreview();
            return;
        }

        if (EditorView.TryGetService<IEditorHistorySnapshotService>(out var snapshotService))
        {
            snapshotService.RestoreSnapshot(_previewSnapshot.Value);
        }

        ResetPreview();
    }

    private void ResetPreview()
    {
        _previewSnapshot = null;
        _previewApplied = false;
        _lastPreviewDocX = float.NaN;
    }

    private static float DocToScreenX(float docX, RulerContext ruler)
    {
        return docX * ruler.Zoom - (float)ruler.Offset.X;
    }

    private static float ScreenToDocX(double screenX, RulerContext ruler)
    {
        return (float)((screenX + ruler.Offset.X) / ruler.Zoom);
    }

    private static void DrawTriangle(DrawingContext context, float x, float y, bool pointingDown)
    {
        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            if (pointingDown)
            {
                gc.BeginFigure(new Point(x, y), true);
                gc.LineTo(new Point(x - MarkerWidth / 2f, y - MarkerHeight), true);
                gc.LineTo(new Point(x + MarkerWidth / 2f, y - MarkerHeight), true);
            }
            else
            {
                gc.BeginFigure(new Point(x, y), true);
                gc.LineTo(new Point(x - MarkerWidth / 2f, y + MarkerHeight), true);
                gc.LineTo(new Point(x + MarkerWidth / 2f, y + MarkerHeight), true);
            }
        }

        context.DrawGeometry(MarkerBrush, null, geometry);
    }

    private static void DrawLabel(DrawingContext context, string text, float x, float y)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            TextBrush);
        context.DrawText(formatted, new Point(x - formatted.Width / 2f, y));
    }

    private static void DrawTabMarker(DrawingContext context, float x, float y, TabAlignment alignment, Pen pen, IBrush brush)
    {
        var bottom = y + MarkerHeight;
        switch (alignment)
        {
            case TabAlignment.Right:
                context.DrawLine(pen, new Point(x, y), new Point(x, bottom));
                context.DrawLine(pen, new Point(x, bottom), new Point(x - MarkerWidth / 2f, bottom));
                break;
            case TabAlignment.Center:
                context.DrawLine(pen, new Point(x, y), new Point(x, bottom));
                context.DrawLine(pen, new Point(x - MarkerWidth / 2f, bottom), new Point(x + MarkerWidth / 2f, bottom));
                break;
            case TabAlignment.Decimal:
                context.DrawLine(pen, new Point(x, y), new Point(x, bottom));
                context.DrawEllipse(brush, null, new Point(x + MarkerWidth / 3f, bottom - 1f), 1f, 1f);
                break;
            default:
                context.DrawLine(pen, new Point(x, y), new Point(x, bottom));
                context.DrawLine(pen, new Point(x, bottom), new Point(x + MarkerWidth / 2f, bottom));
                break;
        }
    }

    private static TabAlignment NextTabAlignment(TabAlignment alignment)
    {
        return alignment switch
        {
            TabAlignment.Left => TabAlignment.Center,
            TabAlignment.Center => TabAlignment.Right,
            TabAlignment.Right => TabAlignment.Decimal,
            _ => TabAlignment.Left
        };
    }

    private static float[] BuildColumnOffsets(IReadOnlyList<float> widths, float spacing)
    {
        var offsets = new float[widths.Count];
        var current = spacing;
        for (var i = 0; i < widths.Count; i++)
        {
            offsets[i] = current;
            current += widths[i] + spacing;
        }

        return offsets;
    }

    private bool TryHitTableBoundary(Point point, RulerContext ruler, out int boundaryIndex)
    {
        boundaryIndex = -1;
        if (ruler.TableLayout is null || ruler.TableLayout.ColumnWidths.Count <= 1)
        {
            return false;
        }

        var table = ruler.TableLayout;
        var offsets = BuildColumnOffsets(table.ColumnWidths, table.CellSpacing);
        for (var i = 0; i < table.ColumnWidths.Count - 1; i++)
        {
            var boundary = table.Bounds.Left + offsets[i] + table.ColumnWidths[i];
            var screenX = DocToScreenX(boundary, ruler);
            if (Math.Abs(point.X - screenX) <= MarkerHitSize)
            {
                boundaryIndex = i;
                return true;
            }
        }

        return false;
    }

    private static EditorParagraphDialogOptions CreateParagraphOptions(
        float? indentLeft,
        float? indentRight,
        float? firstLineIndent)
    {
        return new EditorParagraphDialogOptions(
            Alignment: null,
            IndentLeft: indentLeft,
            IndentRight: indentRight,
            FirstLineIndent: firstLineIndent,
            SpacingBefore: null,
            SpacingAfter: null,
            LineSpacing: null,
            LineSpacingRule: null,
            ContextualSpacing: null,
            KeepWithNext: null,
            KeepLinesTogether: null,
            WidowControl: null,
            PageBreakBefore: null,
            SuppressLineNumbers: null,
            Bidi: null,
            TextDirection: null);
    }

    private enum DragKind
    {
        MarginLeft,
        MarginRight,
        IndentLeft,
        IndentRight,
        FirstLineIndent,
        TabStop,
        TableColumn
    }

    private sealed record DragState(DragKind Kind, float DocX)
    {
        public float TabPosition { get; init; }
        public TabAlignment TabAlignment { get; init; }
        public TabLeader TabLeader { get; init; }
        public float TabLineStart { get; init; }
        public int TableBoundaryIndex { get; init; }
        public float[]? TableColumnWidths { get; init; }
        public float TableLeft { get; init; }
        public float TableCellSpacing { get; init; }
    }

    private readonly record struct TabStopHit(float Position, TabAlignment Alignment, TabLeader Leader);

    private readonly record struct RulerContext(
        DocumentView View,
        Document Document,
        DocumentLayout Layout,
        PageLayout Page,
        PageSectionSettings PageSection,
        PageSectionSettings BaseSection,
        ParagraphBlock Paragraph,
        ParagraphProperties ParagraphProperties,
        float IndentLeft,
        float IndentRight,
        float FirstLineIndent,
        float ContentLeft,
        float ContentRight,
        float PageLeft,
        float PageRight,
        int PageIndex,
        int SectionIndex,
        float Zoom,
        Vector Offset,
        TableLayout? TableLayout);
}
