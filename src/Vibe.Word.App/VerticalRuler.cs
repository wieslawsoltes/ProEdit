using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.App;

public sealed class VerticalRuler : Control
{
    public static readonly StyledProperty<DocumentView?> EditorViewProperty =
        AvaloniaProperty.Register<VerticalRuler, DocumentView?>(nameof(EditorView));

    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<VerticalRuler, Color>(nameof(SurfaceColor), new Color(255, 238, 241, 245));

    private const float MinorTick = RulerHelpers.DipPerInch / 8f;
    private const float MajorTick = RulerHelpers.DipPerInch / 2f;
    private const float LabelTick = RulerHelpers.DipPerInch;
    private const float MarkerHitSize = 6f;
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
    private DragState? _drag;
    private EditorSessionSnapshot? _previewSnapshot;
    private bool _previewApplied;
    private float _lastPreviewDocY = float.NaN;

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

    public VerticalRuler()
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

        var docY = ScreenToDocY(point.Position.Y, context);
        _drag = _drag with { DocY = docY };
        ApplyDragPreview(context, _drag);
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
        if (_previewSnapshot.HasValue)
        {
            ApplyDragPreview(context, drag, force: true);
            CommitPreview();
        }
        else
        {
            CommitDrag(context, drag);
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
        DrawHeaderFooterMarkers(context, ruler);
        DrawBorder(context);
    }

    private void AttachEditorView(DocumentView view)
    {
        view.EditorStateChanged += OnEditorChanged;
        view.ZoomChanged += OnEditorChanged;
        view.ScrollInvalidated += OnEditorChanged;
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
        InvalidateVisual();
    }

    private void UpdateSurfaceBrush(Color color)
    {
        _surfaceBrush = new SolidColorBrush(color);
        InvalidateVisual();
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

        context = new RulerContext(
            view,
            layout,
            page,
            pageSection,
            baseSection,
            pageIndex,
            sectionIndex,
            view.ZoomFactor,
            view.EffectiveScrollOffset);
        return true;
    }

    private void DrawMargins(DrawingContext context, RulerContext ruler)
    {
        var width = Bounds.Width;
        var pageTop = DocToScreenY(ruler.PageTop, ruler);
        var pageBottom = DocToScreenY(ruler.PageBottom, ruler);
        var contentTop = DocToScreenY(ruler.ContentTop, ruler);
        var contentBottom = DocToScreenY(ruler.ContentBottom, ruler);

        if (contentTop > pageTop)
        {
            context.FillRectangle(MarginBrush, new Rect(0, pageTop, width, contentTop - pageTop));
        }

        if (pageBottom > contentBottom)
        {
            context.FillRectangle(MarginBrush, new Rect(0, contentBottom, width, pageBottom - contentBottom));
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
        var offsetY = (float)ruler.Offset.Y;
        var viewTopDoc = offsetY / zoom;
        var viewBottomDoc = (offsetY + height) / zoom;
        var origin = ruler.ContentTop;

        var startIndex = (int)MathF.Floor((viewTopDoc - origin) / MinorTick) - 1;
        var endIndex = (int)MathF.Ceiling((viewBottomDoc - origin) / MinorTick) + 1;

        for (var i = startIndex; i <= endIndex; i++)
        {
            var docY = origin + i * MinorTick;
            if (docY < ruler.PageTop - MinorTick || docY > ruler.PageBottom + MinorTick)
            {
                continue;
            }

            var screenY = docY * zoom - offsetY;
            var tickWidth = 3f;
            if (i % (int)(LabelTick / MinorTick) == 0)
            {
                tickWidth = 8f;
            }
            else if (i % (int)(MajorTick / MinorTick) == 0)
            {
                tickWidth = 6f;
            }
            else if (i % 2 == 0)
            {
                tickWidth = 4f;
            }

            context.DrawLine(TickPen, new Point(width - tickWidth, screenY), new Point(width, screenY));

            if (i % (int)(LabelTick / MinorTick) == 0)
            {
                var labelValue = i / (int)(LabelTick / MinorTick);
                DrawLabel(context, labelValue.ToString(CultureInfo.InvariantCulture), 2f, screenY - 6f);
            }
        }
    }

    private void DrawMarginMarkers(DrawingContext context, RulerContext ruler)
    {
        var x = (float)Bounds.Width - 2f;
        DrawTriangle(context, x, DocToScreenY(ruler.ContentTop, ruler), true);
        DrawTriangle(context, x, DocToScreenY(ruler.ContentBottom, ruler), true);
    }

    private void DrawHeaderFooterMarkers(DrawingContext context, RulerContext ruler)
    {
        var width = Bounds.Width;
        var headerY = DocToScreenY(ruler.PageTop + ruler.PageSection.HeaderOffset, ruler);
        var footerY = DocToScreenY(ruler.PageBottom - ruler.PageSection.FooterOffset, ruler);
        context.DrawLine(MarkerPen, new Point(0, headerY), new Point(width, headerY));
        context.DrawLine(MarkerPen, new Point(0, footerY), new Point(width, footerY));
    }

    private void DrawBorder(DrawingContext context)
    {
        var height = Bounds.Height;
        context.DrawLine(BorderPen, new Point(Bounds.Width - 0.5f, 0), new Point(Bounds.Width - 0.5f, height));
    }

    private DragState? HitTest(Point point, RulerContext ruler)
    {
        var contentTopY = DocToScreenY(ruler.ContentTop, ruler);
        var contentBottomY = DocToScreenY(ruler.ContentBottom, ruler);
        var headerY = DocToScreenY(ruler.PageTop + ruler.PageSection.HeaderOffset, ruler);
        var footerY = DocToScreenY(ruler.PageBottom - ruler.PageSection.FooterOffset, ruler);
        var docY = ScreenToDocY(point.Y, ruler);

        if (Math.Abs(point.Y - contentTopY) <= MarkerHitSize)
        {
            return new DragState(DragKind.MarginTop, docY);
        }

        if (Math.Abs(point.Y - contentBottomY) <= MarkerHitSize)
        {
            return new DragState(DragKind.MarginBottom, docY);
        }

        if (Math.Abs(point.Y - headerY) <= MarkerHitSize)
        {
            return new DragState(DragKind.HeaderOffset, docY);
        }

        if (Math.Abs(point.Y - footerY) <= MarkerHitSize)
        {
            return new DragState(DragKind.FooterOffset, docY);
        }

        return null;
    }

    private void CommitDrag(RulerContext ruler, DragState drag)
    {
        if (EditorView is null)
        {
            return;
        }

        switch (drag.Kind)
        {
            case DragKind.MarginTop:
                ApplyMarginTop(ruler, drag.DocY);
                break;
            case DragKind.MarginBottom:
                ApplyMarginBottom(ruler, drag.DocY);
                break;
            case DragKind.HeaderOffset:
                ApplyHeaderOffset(ruler, drag.DocY);
                break;
            case DragKind.FooterOffset:
                ApplyFooterOffset(ruler, drag.DocY);
                break;
        }
    }

    private void ApplyMarginTop(RulerContext ruler, float docY, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        RulerHelpers.ResolveMargins(ruler.BaseSection, ruler.PageIndex, out _, out _, out var currentTop, out var currentBottom);
        var pageHeight = ruler.PageBottom - ruler.PageTop;
        var resolvedTop = Math.Clamp(docY - ruler.PageTop, 0f, pageHeight - RulerHelpers.MinContentSize - currentBottom);
        var baseTop = ruler.BaseSection.MarginTop;
        if (ruler.BaseSection.GutterAtTop)
        {
            baseTop = MathF.Max(0f, resolvedTop - ruler.BaseSection.Gutter);
        }
        else
        {
            baseTop = MathF.Max(0f, resolvedTop);
        }

        var request = new EditorPageMarginsRequest(ruler.BaseSection.MarginLeft, baseTop, ruler.BaseSection.MarginRight, ruler.BaseSection.MarginBottom);
        RulerHelpers.ExecuteCommand(EditorView, EditorLayoutCommandIds.PageSetup.Margins, request, recordHistory);
    }

    private void ApplyMarginBottom(RulerContext ruler, float docY, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        RulerHelpers.ResolveMargins(ruler.BaseSection, ruler.PageIndex, out _, out _, out var currentTop, out var currentBottom);
        var pageHeight = ruler.PageBottom - ruler.PageTop;
        var resolvedBottom = Math.Clamp(ruler.PageBottom - docY, 0f, pageHeight - RulerHelpers.MinContentSize - currentTop);
        var baseBottom = MathF.Max(0f, resolvedBottom);
        var request = new EditorPageMarginsRequest(ruler.BaseSection.MarginLeft, ruler.BaseSection.MarginTop, ruler.BaseSection.MarginRight, baseBottom);
        RulerHelpers.ExecuteCommand(EditorView, EditorLayoutCommandIds.PageSetup.Margins, request, recordHistory);
    }

    private void ApplyHeaderOffset(RulerContext ruler, float docY, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        var offset = MathF.Max(0f, docY - ruler.PageTop);
        var request = new EditorPageMarginsRequest(
            ruler.BaseSection.MarginLeft,
            ruler.BaseSection.MarginTop,
            ruler.BaseSection.MarginRight,
            ruler.BaseSection.MarginBottom,
            HeaderOffset: offset);
        RulerHelpers.ExecuteCommand(EditorView, EditorLayoutCommandIds.PageSetup.Margins, request, recordHistory);
    }

    private void ApplyFooterOffset(RulerContext ruler, float docY, bool recordHistory = true)
    {
        if (EditorView is null)
        {
            return;
        }

        var offset = MathF.Max(0f, ruler.PageBottom - docY);
        var request = new EditorPageMarginsRequest(
            ruler.BaseSection.MarginLeft,
            ruler.BaseSection.MarginTop,
            ruler.BaseSection.MarginRight,
            ruler.BaseSection.MarginBottom,
            FooterOffset: offset);
        RulerHelpers.ExecuteCommand(EditorView, EditorLayoutCommandIds.PageSetup.Margins, request, recordHistory);
    }

    private void ApplyDragPreview(RulerContext ruler, DragState drag, bool force = false)
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
            && !float.IsNaN(_lastPreviewDocY)
            && MathF.Abs(drag.DocY - _lastPreviewDocY) < PreviewEpsilon)
        {
            return;
        }

        _lastPreviewDocY = drag.DocY;
        switch (drag.Kind)
        {
            case DragKind.MarginTop:
                ApplyMarginTop(ruler, drag.DocY, recordHistory: false);
                break;
            case DragKind.MarginBottom:
                ApplyMarginBottom(ruler, drag.DocY, recordHistory: false);
                break;
            case DragKind.HeaderOffset:
                ApplyHeaderOffset(ruler, drag.DocY, recordHistory: false);
                break;
            case DragKind.FooterOffset:
                ApplyFooterOffset(ruler, drag.DocY, recordHistory: false);
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
        _lastPreviewDocY = float.NaN;
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
        _lastPreviewDocY = float.NaN;
    }

    private static float DocToScreenY(float docY, RulerContext ruler)
    {
        return docY * ruler.Zoom - (float)ruler.Offset.Y;
    }

    private static float ScreenToDocY(double screenY, RulerContext ruler)
    {
        return (float)((screenY + ruler.Offset.Y) / ruler.Zoom);
    }

    private static void DrawTriangle(DrawingContext context, float x, float y, bool pointingRight)
    {
        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            if (pointingRight)
            {
                gc.BeginFigure(new Point(x, y), true);
                gc.LineTo(new Point(x - MarkerHeight, y - MarkerWidth / 2f), true);
                gc.LineTo(new Point(x - MarkerHeight, y + MarkerWidth / 2f), true);
            }
            else
            {
                gc.BeginFigure(new Point(x, y), true);
                gc.LineTo(new Point(x + MarkerHeight, y - MarkerWidth / 2f), true);
                gc.LineTo(new Point(x + MarkerHeight, y + MarkerWidth / 2f), true);
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
        context.DrawText(formatted, new Point(x, y));
    }

    private enum DragKind
    {
        MarginTop,
        MarginBottom,
        HeaderOffset,
        FooterOffset
    }

    private sealed record DragState(DragKind Kind, float DocY);

    private readonly record struct RulerContext(
        DocumentView View,
        DocumentLayout Layout,
        PageLayout Page,
        PageSectionSettings PageSection,
        PageSectionSettings BaseSection,
        int PageIndex,
        int SectionIndex,
        float Zoom,
        Vector Offset)
    {
        public float PageTop => Page.Bounds.Top;
        public float PageBottom => Page.Bounds.Bottom;
        public float ContentTop => Page.ContentBounds.Top;
        public float ContentBottom => Page.ContentBounds.Bottom;
    }
}
