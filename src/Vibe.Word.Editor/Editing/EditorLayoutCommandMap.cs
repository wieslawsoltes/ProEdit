using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Editor.Editing;

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
        if (context.HasValue)
        {
            return context.Value.Selection.Kind == EditorSelectionKind.FloatingObject
                   && _session.SelectedFloatingObjectId.HasValue;
        }

        return _session.SelectedFloatingObjectId.HasValue;
    }

    private void RegisterArrangeCommands()
    {
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Position, (_, payload) => ApplyPosition(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.WrapText, (_, payload) => ApplyWrap(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.WrapSide, (_, payload) => ApplyWrapSide(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Align, (_, payload) => ApplyAlign(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Order, (_, payload) => ApplyOrder(payload), (context, _) => HasFloatingSelection(context));
        _router.RegisterAction(EditorLayoutCommandIds.Arrange.Rotate, (_, payload) => ApplyRotate(payload), (context, _) => HasFloatingSelection(context));
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

        if (!TryGetSelectedFloatingObject(out var floating))
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

        if (!TryGetSelectedFloatingObject(out var floating))
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

        if (!TryGetSelectedFloatingObject(out var floating))
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

        if (!TryGetSelectedFloatingObject(out var floating))
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

        if (!TryGetSelectedFloatingObject(out var paragraph, out var floating, out var index))
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

        if (!TryGetSelectedFloatingObject(out var floating))
        {
            return;
        }

        if (floating.Content is not ShapeInline shape)
        {
            return;
        }

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
    }

    private static float NormalizeRotation(float degrees)
    {
        var value = degrees % 360f;
        return value < 0f ? value + 360f : value;
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
