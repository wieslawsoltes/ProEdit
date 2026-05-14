using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using ProEdit.Primitives;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorLayoutCommandMap
{
    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;

    public EditorLayoutCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public void Register()
    {
        RegisterPageSetupCommands();
        RegisterArrangeCommands();
    }

    private void RegisterPageSetupCommands()
    {
        _router.RegisterAction(EditorLayoutCommandIds.PageSetup.Margins, (_, payload) => SetMargins(payload), (context, _) => HasDocument(context));
        _router.RegisterAction(EditorLayoutCommandIds.PageSetup.Orientation, (_, payload) => SetOrientation(payload), (context, _) => HasDocument(context));
        _router.RegisterAction(EditorLayoutCommandIds.PageSetup.Size, (_, payload) => SetPageSize(payload), (context, _) => HasDocument(context));
        _router.RegisterAction(EditorLayoutCommandIds.PageSetup.Columns, (_, payload) => SetColumns(payload), (context, _) => HasDocument(context));
        _router.RegisterAction(EditorLayoutCommandIds.PageSetup.Breaks, (_, payload) => InsertBreak(payload), (context, _) => HasDocument(context));
    }

    private bool HasDocument(RibbonContextSnapshot? context)
    {
        return _session.Document.ParagraphCount > 0;
    }

    private bool HasFloatingSelection(RibbonContextSnapshot? context)
    {
        if (_session.SelectedFloatingObjectId.HasValue)
        {
            return true;
        }

        return TryGetSelectedInlineObject(out _, out _, out _, out _, out _);
    }

    private void RegisterArrangeCommands()
    {
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Position, (_, payload) => ApplyPosition(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.WrapText, (_, payload) => ApplyWrap(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.WrapSide, (_, payload) => ApplyWrapSide(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Align, (_, payload) => ApplyAlign(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Order, (_, payload) => ApplyOrder(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Rotate, (_, payload) => ApplyRotate(payload), (context, _) => CanRotate(context));
    }

    private bool CanRotate(RibbonContextSnapshot? context)
    {
        if (TryGetSelectedFloatingObject(out var floating))
        {
            return floating.Content is ShapeInline;
        }

        return TryGetSelectedInlineObject(out _, out _, out var inline, out _, out _) && inline is ShapeInline;
    }

    private void SetMargins(object? payload)
    {
        if (payload is not EditorPageMarginsRequest request)
        {
            return;
        }

        var properties = ResolveCurrentSectionProperties();
        properties.MarginLeft = request.Left;
        properties.MarginTop = request.Top;
        properties.MarginRight = request.Right;
        properties.MarginBottom = request.Bottom;

        if (request.HeaderOffset.HasValue)
        {
            properties.HeaderOffset = request.HeaderOffset.Value;
        }

        if (request.FooterOffset.HasValue)
        {
            properties.FooterOffset = request.FooterOffset.Value;
        }

        if (request.Gutter.HasValue)
        {
            properties.Gutter = request.Gutter.Value;
        }

        if (request.MirrorMargins.HasValue)
        {
            _session.Document.MirrorMargins = request.MirrorMargins.Value;
        }

        if (request.GutterAtTop.HasValue)
        {
            _session.Document.GutterAtTop = request.GutterAtTop.Value;
        }

        _session.RefreshLayout();
    }

    private void SetOrientation(object? payload)
    {
        if (payload is not EditorPageOrientationRequest request)
        {
            return;
        }

        var properties = ResolveCurrentSectionProperties();
        var width = properties.PageWidth ?? _session.LayoutSettings.PageWidth;
        var height = properties.PageHeight ?? _session.LayoutSettings.PageHeight;

        if (request.Orientation == PageOrientation.Landscape && width < height)
        {
            (width, height) = (height, width);
        }
        else if (request.Orientation == PageOrientation.Portrait && width > height)
        {
            (width, height) = (height, width);
        }

        properties.PageWidth = width;
        properties.PageHeight = height;
        properties.Orientation = request.Orientation;

        _session.RefreshLayout();
    }

    private void SetPageSize(object? payload)
    {
        if (payload is not EditorPageSizeRequest request)
        {
            return;
        }

        var properties = ResolveCurrentSectionProperties();
        properties.PageWidth = request.Width;
        properties.PageHeight = request.Height;
        properties.Orientation = request.Orientation
            ?? (request.Width >= request.Height ? PageOrientation.Landscape : PageOrientation.Portrait);

        _session.RefreshLayout();
    }

    private void SetColumns(object? payload)
    {
        if (payload is not EditorColumnLayoutRequest request)
        {
            return;
        }

        var properties = ResolveCurrentSectionProperties();
        var count = Math.Max(1, request.ColumnCount);
        properties.ColumnCount = count;
        properties.ColumnEqualWidth = request.EqualWidth ?? count > 1;
        if (request.ColumnGap.HasValue)
        {
            properties.ColumnGap = request.ColumnGap.Value;
        }

        if (request.Separator.HasValue)
        {
            properties.ColumnSeparator = request.Separator.Value;
        }

        properties.ColumnWidths.Clear();
        properties.ColumnGaps.Clear();
        _session.RefreshLayout();
    }

    private void InsertBreak(object? payload)
    {
        if (payload is not EditorBreakRequest request)
        {
            return;
        }

        switch (request.Kind)
        {
            case EditorBreakKind.Page:
                _session.InsertBlock(new PageBreakBlock());
                return;
            case EditorBreakKind.Column:
                _session.InsertBlock(new ColumnBreakBlock());
                return;
            case EditorBreakKind.SectionNextPage:
                InsertSectionBreak(SectionBreakType.NextPage);
                return;
            case EditorBreakKind.SectionContinuous:
                InsertSectionBreak(SectionBreakType.Continuous);
                return;
            case EditorBreakKind.SectionEvenPage:
                InsertSectionBreak(SectionBreakType.EvenPage);
                return;
            case EditorBreakKind.SectionOddPage:
                InsertSectionBreak(SectionBreakType.OddPage);
                return;
            case EditorBreakKind.SectionNextColumn:
                InsertSectionBreak(SectionBreakType.NextColumn);
                return;
            default:
                return;
        }
    }

    private void InsertSectionBreak(SectionBreakType breakType)
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return;
        }

        var currentSectionIndex = ResolveCurrentSectionIndex();
        var currentSection = EnsureSection(currentSectionIndex);
        var newSection = new DocumentSection(
            currentSection.Properties.Clone(),
            _session.Document.Header,
            _session.Document.Footer,
            _session.Document.FirstHeader,
            _session.Document.FirstFooter,
            _session.Document.EvenHeader,
            _session.Document.EvenFooter);
        _session.Document.Sections.Add(newSection);
        var newSectionIndex = _session.Document.Sections.Count - 1;

        _session.InsertBlock(new SectionBreakBlock
        {
            BreakType = breakType,
            SectionIndex = newSectionIndex
        });
    }

    private SectionProperties ResolveCurrentSectionProperties()
    {
        var sectionIndex = ResolveCurrentSectionIndex();
        return EnsureSection(sectionIndex).Properties;
    }

    private int ResolveCurrentSectionIndex()
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return 0;
        }

        var selection = _session.Selection.Normalize();
        var paragraphIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var location = _session.Document.GetParagraphLocation(paragraphIndex);
        var blockIndex = location.BlockIndex;
        var sectionIndex = 0;

        for (var i = 0; i < blockIndex && i < _session.Document.Blocks.Count; i++)
        {
            if (_session.Document.Blocks[i] is SectionBreakBlock sectionBreak && sectionBreak.SectionIndex.HasValue)
            {
                sectionIndex = sectionBreak.SectionIndex.Value;
            }
        }

        return sectionIndex;
    }

    private DocumentSection EnsureSection(int sectionIndex)
    {
        if (_session.Document.Sections.Count == 0)
        {
            _session.Document.Sections.Add(new DocumentSection(
                _session.Document.SectionProperties,
                _session.Document.Header,
                _session.Document.Footer,
                _session.Document.FirstHeader,
                _session.Document.FirstFooter,
                _session.Document.EvenHeader,
                _session.Document.EvenFooter));
        }

        while (_session.Document.Sections.Count <= sectionIndex)
        {
            _session.Document.Sections.Add(new DocumentSection(
                _session.Document.SectionProperties.Clone(),
                _session.Document.Header,
                _session.Document.Footer,
                _session.Document.FirstHeader,
                _session.Document.FirstFooter,
                _session.Document.EvenHeader,
                _session.Document.EvenFooter));
        }

        return _session.Document.Sections[sectionIndex];
    }

    private void ApplyPosition(object? payload)
    {
        if (payload is not EditorFloatingPositionRequest request)
        {
            return;
        }

        if (!TryGetSelectedFloatingObjectOrPromote(out var floating, inline => inline is ShapeInline))
        {
            return;
        }

        var anchor = floating.Anchor;
        anchor.HorizontalReference = FloatingHorizontalReference.Margin;
        anchor.VerticalReference = FloatingVerticalReference.Margin;
        anchor.OffsetX = 0f;
        anchor.OffsetY = 0f;

        switch (request.Kind)
        {
            case EditorFloatingPositionKind.TopLeft:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Left;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Top;
                break;
            case EditorFloatingPositionKind.TopCenter:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Center;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Top;
                break;
            case EditorFloatingPositionKind.TopRight:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Right;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Top;
                break;
            case EditorFloatingPositionKind.MiddleLeft:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Left;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Center;
                break;
            case EditorFloatingPositionKind.MiddleCenter:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Center;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Center;
                break;
            case EditorFloatingPositionKind.MiddleRight:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Right;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Center;
                break;
            case EditorFloatingPositionKind.BottomLeft:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Left;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Bottom;
                break;
            case EditorFloatingPositionKind.BottomCenter:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Center;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Bottom;
                break;
            case EditorFloatingPositionKind.BottomRight:
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Right;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Bottom;
                break;
        }

        _session.RefreshLayout();
    }

    private void ApplyWrap(object? payload)
    {
        if (payload is not EditorFloatingWrapRequest request)
        {
            return;
        }

        if (!TryGetSelectedFloatingObjectOrPromote(out var floating))
        {
            return;
        }

        var anchor = floating.Anchor;
        switch (request.Kind)
        {
            case EditorFloatingWrapKind.Square:
                anchor.WrapStyle = FloatingWrapStyle.Square;
                anchor.BehindText = false;
                break;
            case EditorFloatingWrapKind.Tight:
                anchor.WrapStyle = FloatingWrapStyle.Tight;
                anchor.BehindText = false;
                break;
            case EditorFloatingWrapKind.Through:
                anchor.WrapStyle = FloatingWrapStyle.Through;
                anchor.BehindText = false;
                break;
            case EditorFloatingWrapKind.TopBottom:
                anchor.WrapStyle = FloatingWrapStyle.TopBottom;
                anchor.BehindText = false;
                break;
            case EditorFloatingWrapKind.BehindText:
                anchor.WrapStyle = FloatingWrapStyle.None;
                anchor.BehindText = true;
                break;
            case EditorFloatingWrapKind.InFrontOfText:
                anchor.WrapStyle = FloatingWrapStyle.None;
                anchor.BehindText = false;
                break;
        }

        _session.RefreshLayout();
    }

    private void ApplyAlign(object? payload)
    {
        if (payload is not EditorFloatingAlignRequest request)
        {
            return;
        }

        if (!TryGetSelectedFloatingObjectOrPromote(out var floating))
        {
            return;
        }

        var anchor = floating.Anchor;
        if (request.Target == EditorFloatingAlignTarget.SelectedObjects)
        {
            if (!TryApplyAlignToSelectionBounds(request.Kind, floating))
            {
                return;
            }

            _session.RefreshLayout();
            return;
        }

        var horizontalReference = request.Target == EditorFloatingAlignTarget.Page
            ? FloatingHorizontalReference.Page
            : FloatingHorizontalReference.Margin;
        var verticalReference = request.Target == EditorFloatingAlignTarget.Page
            ? FloatingVerticalReference.Page
            : FloatingVerticalReference.Margin;

        switch (request.Kind)
        {
            case EditorFloatingAlignKind.Left:
                anchor.HorizontalReference = horizontalReference;
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Left;
                anchor.OffsetX = 0f;
                break;
            case EditorFloatingAlignKind.Center:
                anchor.HorizontalReference = horizontalReference;
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Center;
                anchor.OffsetX = 0f;
                break;
            case EditorFloatingAlignKind.Right:
                anchor.HorizontalReference = horizontalReference;
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.Right;
                anchor.OffsetX = 0f;
                break;
            case EditorFloatingAlignKind.Top:
                anchor.VerticalReference = verticalReference;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Top;
                anchor.OffsetY = 0f;
                break;
            case EditorFloatingAlignKind.Middle:
                anchor.VerticalReference = verticalReference;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Center;
                anchor.OffsetY = 0f;
                break;
            case EditorFloatingAlignKind.Bottom:
                anchor.VerticalReference = verticalReference;
                anchor.VerticalAlignment = FloatingVerticalAlignment.Bottom;
                anchor.OffsetY = 0f;
                break;
        }

        _session.RefreshLayout();
    }

    private void ApplyWrapSide(object? payload)
    {
        if (payload is not EditorFloatingWrapSideRequest request)
        {
            return;
        }

        if (!TryGetSelectedFloatingObjectOrPromote(out var floating))
        {
            return;
        }

        floating.Anchor.WrapSide = request.Side;
        _session.RefreshLayout();
    }

    private bool TryApplyAlignToSelectionBounds(EditorFloatingAlignKind kind, FloatingObject floating)
    {
        if (!TryGetSelectedFloatingLayout(out var selectedLayout))
        {
            return false;
        }

        if (_session.Layout.Pages.Count == 0)
        {
            return false;
        }

        var pageIndex = Math.Clamp(selectedLayout.PageIndex, 0, _session.Layout.Pages.Count - 1);
        var pageBounds = _session.Layout.Pages[pageIndex].Bounds;
        var selectionBounds = selectedLayout.Bounds;
        var objectBounds = selectedLayout.Bounds;
        var anchor = floating.Anchor;
        switch (kind)
        {
            case EditorFloatingAlignKind.Left:
                anchor.HorizontalReference = FloatingHorizontalReference.Page;
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.None;
                anchor.OffsetX = selectionBounds.Left - pageBounds.X;
                return true;
            case EditorFloatingAlignKind.Center:
            {
                anchor.HorizontalReference = FloatingHorizontalReference.Page;
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.None;
                var targetCenter = (selectionBounds.Left + selectionBounds.Right) * 0.5f;
                anchor.OffsetX = targetCenter - pageBounds.X - (objectBounds.Width * 0.5f);
                return true;
            }
            case EditorFloatingAlignKind.Right:
                anchor.HorizontalReference = FloatingHorizontalReference.Page;
                anchor.HorizontalAlignment = FloatingHorizontalAlignment.None;
                anchor.OffsetX = selectionBounds.Right - pageBounds.X - objectBounds.Width;
                return true;
            case EditorFloatingAlignKind.Top:
                anchor.VerticalReference = FloatingVerticalReference.Page;
                anchor.VerticalAlignment = FloatingVerticalAlignment.None;
                anchor.OffsetY = selectionBounds.Top - pageBounds.Y;
                return true;
            case EditorFloatingAlignKind.Middle:
            {
                anchor.VerticalReference = FloatingVerticalReference.Page;
                anchor.VerticalAlignment = FloatingVerticalAlignment.None;
                var targetCenter = (selectionBounds.Top + selectionBounds.Bottom) * 0.5f;
                anchor.OffsetY = targetCenter - pageBounds.Y - (objectBounds.Height * 0.5f);
                return true;
            }
            case EditorFloatingAlignKind.Bottom:
                anchor.VerticalReference = FloatingVerticalReference.Page;
                anchor.VerticalAlignment = FloatingVerticalAlignment.None;
                anchor.OffsetY = selectionBounds.Bottom - pageBounds.Y - objectBounds.Height;
                return true;
            default:
                return false;
        }
    }

    private bool TryGetSelectedFloatingLayout(out FloatingLayoutObject selectedLayout)
    {
        selectedLayout = default!;
        var selectedId = _session.SelectedFloatingObjectId;
        if (!selectedId.HasValue)
        {
            return false;
        }

        foreach (var floating in _session.Layout.FloatingObjects)
        {
            if (floating.Object.Id == selectedId.Value)
            {
                selectedLayout = floating;
                return true;
            }
        }

        return false;
    }

    private void ApplyOrder(object? payload)
    {
        if (payload is not EditorFloatingOrderRequest request)
        {
            return;
        }

        if (!TryGetSelectedFloatingObjectOrPromote(out var paragraph, out var floating, out var index))
        {
            return;
        }

        var list = paragraph.FloatingObjects;
        if (list.Count <= 1 || index < 0 || index >= list.Count)
        {
            return;
        }

        switch (request.Kind)
        {
            case EditorFloatingOrderKind.BringForward:
                if (index < list.Count - 1)
                {
                    (list[index], list[index + 1]) = (list[index + 1], list[index]);
                }

                break;
            case EditorFloatingOrderKind.BringToFront:
                list.RemoveAt(index);
                list.Add(floating);
                break;
            case EditorFloatingOrderKind.SendBackward:
                if (index > 0)
                {
                    (list[index], list[index - 1]) = (list[index - 1], list[index]);
                }

                break;
            case EditorFloatingOrderKind.SendToBack:
                list.RemoveAt(index);
                list.Insert(0, floating);
                break;
        }

        _session.RefreshLayout();
    }

    private void ApplyRotate(object? payload)
    {
        if (payload is not EditorFloatingRotateRequest request)
        {
            return;
        }

        if (!TryGetSelectedFloatingObjectOrPromote(out var floating))
        {
            return;
        }

        if (floating.Content is ShapeInline shape)
        {
            switch (request.Kind)
            {
                case EditorFloatingRotateKind.RotateRight90:
                    shape.Properties.Rotation = NormalizeRotation(shape.Properties.Rotation + 90f);
                    break;
                case EditorFloatingRotateKind.RotateLeft90:
                    shape.Properties.Rotation = NormalizeRotation(shape.Properties.Rotation - 90f);
                    break;
                case EditorFloatingRotateKind.FlipHorizontal:
                    shape.Properties.FlipHorizontal = !shape.Properties.FlipHorizontal;
                    break;
                case EditorFloatingRotateKind.FlipVertical:
                    shape.Properties.FlipVertical = !shape.Properties.FlipVertical;
                    break;
            }

            _session.RefreshLayout();
            return;
        }

        if (floating.Content is ImageInline image)
        {
            switch (request.Kind)
            {
                case EditorFloatingRotateKind.RotateRight90:
                    image.Rotation = NormalizeRotation(image.Rotation + 90f);
                    break;
                case EditorFloatingRotateKind.RotateLeft90:
                    image.Rotation = NormalizeRotation(image.Rotation - 90f);
                    break;
                default:
                    return;
            }

            _session.RefreshLayout();
        }

    }

    private static float NormalizeRotation(float degrees)
    {
        var value = degrees % 360f;
        return value < 0f ? value + 360f : value;
    }

    private bool TryGetSelectedFloatingObjectOrPromote(out FloatingObject floating, Func<Inline, bool>? canPromote = null)
    {
        floating = null!;
        return TryGetSelectedFloatingObject(out floating)
               || TryPromoteInlineSelectionToFloating(out _, out floating, out _, canPromote);
    }

    private bool TryGetSelectedFloatingObjectOrPromote(
        out ParagraphBlock paragraph,
        out FloatingObject floating,
        out int index,
        Func<Inline, bool>? canPromote = null)
    {
        paragraph = null!;
        floating = null!;
        index = -1;
        return TryGetSelectedFloatingObject(out paragraph, out floating, out index)
               || TryPromoteInlineSelectionToFloating(out paragraph, out floating, out index, canPromote);
    }

    private bool TryPromoteInlineSelectionToFloating(
        out ParagraphBlock paragraph,
        out FloatingObject floating,
        out int index,
        Func<Inline, bool>? canPromote = null)
    {
        paragraph = null!;
        floating = null!;
        index = -1;

        if (!TryGetSelectedInlineObject(out var paragraphIndex, out paragraph, out var inline, out var inlineIndex, out var inlineStartOffset))
        {
            return false;
        }

        if (canPromote is not null && !canPromote(inline))
        {
            return false;
        }

        if (!TryGetInlineObjectBounds(inline, paragraphIndex, out var bounds, out var pageIndex))
        {
            return false;
        }

        if (inlineIndex < 0 || inlineIndex >= paragraph.Inlines.Count)
        {
            return false;
        }

        if (!ReferenceEquals(paragraph.Inlines[inlineIndex], inline))
        {
            return false;
        }

        paragraph.Inlines.RemoveAt(inlineIndex);

        floating = new FloatingObject(inline);
        var anchor = floating.Anchor;
        anchor.HorizontalReference = FloatingHorizontalReference.Page;
        anchor.VerticalReference = FloatingVerticalReference.Page;
        anchor.HorizontalAlignment = FloatingHorizontalAlignment.None;
        anchor.VerticalAlignment = FloatingVerticalAlignment.None;
        anchor.WrapStyle = FloatingWrapStyle.None;
        anchor.WrapSide = FloatingWrapSide.Both;
        anchor.BehindText = false;
        anchor.AnchorOffset = inlineStartOffset;

        if (_session.Layout.Pages.Count > 0)
        {
            var clamped = Math.Clamp(pageIndex, 0, _session.Layout.Pages.Count - 1);
            var page = _session.Layout.Pages[clamped];
            anchor.OffsetX = bounds.X - page.Bounds.X;
            anchor.OffsetY = bounds.Y - page.Bounds.Y;
        }
        else
        {
            anchor.OffsetX = bounds.X;
            anchor.OffsetY = bounds.Y;
        }

        paragraph.FloatingObjects.Add(floating);
        index = paragraph.FloatingObjects.Count - 1;
        _session.RefreshLayout();

        var centerX = bounds.X + MathF.Max(1f, bounds.Width) * 0.5f;
        var centerY = bounds.Y + MathF.Max(1f, bounds.Height) * 0.5f;
        _session.SetCaretFromPoint(centerX, centerY, SelectionUpdateMode.Replace);
        return true;
    }

    private bool TryGetSelectedInlineObject(
        out int paragraphIndex,
        out ParagraphBlock paragraph,
        out Inline inline,
        out int inlineIndex,
        out int inlineStartOffset)
    {
        paragraphIndex = -1;
        paragraph = null!;
        inline = null!;
        inlineIndex = -1;
        inlineStartOffset = 0;

        if (_session.SelectedFloatingObjectIds.Count > 0 || _session.SelectedFloatingObjectId.HasValue)
        {
            return false;
        }

        var ranges = _session.SelectionRanges;
        if (ranges.Count != 1)
        {
            return false;
        }

        var range = ranges[0].Normalize();
        if (range.IsEmpty || range.Start.ParagraphIndex != range.End.ParagraphIndex)
        {
            return false;
        }

        paragraphIndex = range.Start.ParagraphIndex;
        if (paragraphIndex < 0 || paragraphIndex >= _session.Document.ParagraphCount)
        {
            return false;
        }

        paragraph = _session.Document.GetParagraph(paragraphIndex);
        if (!TryGetInlineAtOffset(paragraph, range.Start.Offset, out inline, out inlineIndex, out inlineStartOffset, out var inlineLength))
        {
            return false;
        }

        if (inlineStartOffset != range.Start.Offset || inlineLength != range.End.Offset - range.Start.Offset)
        {
            return false;
        }

        return inline is ImageInline or ShapeInline or ChartInline;
    }

    private bool TryGetInlineObjectBounds(Inline inline, int paragraphIndex, out DocRect bounds, out int pageIndex)
    {
        bounds = default;
        pageIndex = -1;
        var layout = _session.Layout;
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

                    bounds = ComputeInlineObjectBounds(line, layoutImage.X, layoutImage.Width, layoutImage.Height);
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

                    bounds = ComputeInlineObjectBounds(line, layoutShape.X, layoutShape.Width, layoutShape.Height);
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

                    bounds = ComputeInlineObjectBounds(line, layoutChart.X, layoutChart.Width, layoutChart.Height);
                    pageIndex = layout.LineIndex.GetPageForLine(i);
                    return true;
                }
            }
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

    private static DocRect ComputeInlineObjectBounds(LayoutLine line, float x, float width, float height)
    {
        if (!DocTextDirectionHelpers.IsVertical(line.TextDirection))
        {
            var baseline = line.Y + line.Ascent;
            return new DocRect(line.X + x, baseline - height, width, height);
        }

        var baseRotation = DocTextDirectionHelpers.GetRotationDegrees(line.TextDirection!.Value);
        var baselineLocal = line.Ascent;
        var left = x;
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

    private bool TryGetSelectedFloatingObject(out FloatingObject floating)
    {
        floating = null!;
        return TryGetSelectedFloatingObject(out _, out floating, out _);
    }

    private bool TryGetSelectedFloatingObject(out ParagraphBlock paragraph, out FloatingObject floating, out int index)
    {
        paragraph = null!;
        floating = null!;
        index = -1;

        var selectedId = _session.SelectedFloatingObjectId;
        if (!selectedId.HasValue)
        {
            return false;
        }

        var paragraphCount = _session.Document.ParagraphCount;
        for (var i = 0; i < paragraphCount; i++)
        {
            var candidate = _session.Document.GetParagraph(i);
            var list = candidate.FloatingObjects;
            for (var j = 0; j < list.Count; j++)
            {
                if (list[j].Id == selectedId.Value)
                {
                    paragraph = candidate;
                    floating = list[j];
                    index = j;
                    return true;
                }
            }
        }

        return false;
    }
}
