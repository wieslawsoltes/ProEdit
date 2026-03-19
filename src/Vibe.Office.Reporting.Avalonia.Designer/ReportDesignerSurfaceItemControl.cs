using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Interactive surface item that supports selection, move, resize, and drop operations.
/// </summary>
public sealed class ReportDesignerSurfaceItemControl : ContentControl
{
    private const double DragThreshold = 4d;
    private const double HandleHitPadding = 8d;

    private SurfaceInteractionState? _interaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerSurfaceItemControl" /> class.
    /// </summary>
    public ReportDesignerSurfaceItemControl()
    {
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (DataContext is not ReportDesignerCanvasItemViewModel item
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (FindDesignerViewModel() is not { } designer)
        {
            return;
        }

        if (FindStablePointerHost() is InputElement hostElement)
        {
            hostElement.Focus();
        }
        designer.SelectSurfaceItemFromInteraction(item);
        if (item.IsReadOnly)
        {
            return;
        }

        var stablePosition = GetStablePointerPosition(e);
        _interaction = new SurfaceInteractionState(
            item,
            DetermineResizeHandle(e.GetPosition(this), item),
            stablePosition,
            stablePosition);

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (DataContext is ReportDesignerCanvasItemViewModel item && _interaction is null)
        {
            UpdateHoverCursor(e.GetPosition(this), item);
            return;
        }

        if (_interaction is null || FindDesignerViewModel() is not { } designer)
        {
            return;
        }

        var stablePosition = GetStablePointerPosition(e);
        var totalDeltaX = stablePosition.X - _interaction.Value.StartPointerPosition.X;
        var totalDeltaY = stablePosition.Y - _interaction.Value.StartPointerPosition.Y;
        if (!_interaction.Value.HasManipulated
            && Math.Abs(totalDeltaX) < DragThreshold
            && Math.Abs(totalDeltaY) < DragThreshold)
        {
            return;
        }

        var deltaX = stablePosition.X - _interaction.Value.LastPointerPosition.X;
        var deltaY = stablePosition.Y - _interaction.Value.LastPointerPosition.Y;
        var changed = _interaction.Value.Handle == ReportDesignerSurfaceResizeHandle.Move
            ? designer.TryMoveSurfaceItemByDelta(_interaction.Value.Item, deltaX, deltaY)
            : designer.TryResizeSurfaceItemByDelta(_interaction.Value.Item, _interaction.Value.Handle, deltaX, deltaY);

        if (changed)
        {
            _interaction = _interaction.Value with
            {
                LastPointerPosition = stablePosition,
                HasManipulated = true
            };
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndInteraction(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndInteraction(pointer: null);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not ReportDesignerCanvasItemViewModel { IsReadOnly: false }
            || !e.DataTransfer.Contains(ReportDesignerDragDataFormats.Payload))
        {
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ReportDesignerCanvasItemViewModel canvasItem
            || canvasItem.IsReadOnly
            || FindDesignerViewModel() is not { } designer)
        {
            return;
        }

        var payload = ReportDesignerDragDataFormats.Deserialize(
            e.DataTransfer.TryGetValue(ReportDesignerDragDataFormats.Payload),
            designer);
        if (payload is null)
        {
            return;
        }

        var position = TranslateToItemSpace(e.GetPosition(this), canvasItem);
        if (designer.TryApplyDesignerDrop(
                payload,
                canvasItem.Left + Math.Clamp(position.X, 0d, canvasItem.Width),
                canvasItem.Top + Math.Clamp(position.Y, 0d, canvasItem.Height),
                canvasItem))
        {
            if (FindStablePointerHost() is InputElement hostElement)
            {
                hostElement.Focus();
            }

            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void UpdateHoverCursor(Point position, ReportDesignerCanvasItemViewModel item)
    {
        Cursor = DetermineResizeHandle(position, item) switch
        {
            ReportDesignerSurfaceResizeHandle.North or ReportDesignerSurfaceResizeHandle.South =>
                new Cursor(StandardCursorType.SizeNorthSouth),
            ReportDesignerSurfaceResizeHandle.West or ReportDesignerSurfaceResizeHandle.East =>
                new Cursor(StandardCursorType.SizeWestEast),
            ReportDesignerSurfaceResizeHandle.NorthWest or ReportDesignerSurfaceResizeHandle.SouthEast =>
                new Cursor(StandardCursorType.TopLeftCorner),
            ReportDesignerSurfaceResizeHandle.NorthEast or ReportDesignerSurfaceResizeHandle.SouthWest =>
                new Cursor(StandardCursorType.TopRightCorner),
            _ => new Cursor(StandardCursorType.Arrow)
        };
    }

    private ReportDesignerSurfaceResizeHandle DetermineResizeHandle(Point position, ReportDesignerCanvasItemViewModel item)
    {
        if (!item.IsSelected || item.IsReadOnly)
        {
            return ReportDesignerSurfaceResizeHandle.Move;
        }

        var itemPosition = TranslateToItemSpace(position, item);
        var isLeft = itemPosition.X <= HandleHitPadding;
        var isRight = item.Width - itemPosition.X <= HandleHitPadding;
        var isTop = itemPosition.Y <= HandleHitPadding;
        var isBottom = item.Height - itemPosition.Y <= HandleHitPadding;

        if (isLeft && isTop)
        {
            return ReportDesignerSurfaceResizeHandle.NorthWest;
        }

        if (isRight && isTop)
        {
            return ReportDesignerSurfaceResizeHandle.NorthEast;
        }

        if (isLeft && isBottom)
        {
            return ReportDesignerSurfaceResizeHandle.SouthWest;
        }

        if (isRight && isBottom)
        {
            return ReportDesignerSurfaceResizeHandle.SouthEast;
        }

        if (isTop)
        {
            return ReportDesignerSurfaceResizeHandle.North;
        }

        if (isBottom)
        {
            return ReportDesignerSurfaceResizeHandle.South;
        }

        if (isLeft)
        {
            return ReportDesignerSurfaceResizeHandle.West;
        }

        if (isRight)
        {
            return ReportDesignerSurfaceResizeHandle.East;
        }

        return ReportDesignerSurfaceResizeHandle.Move;
    }

    private static Point TranslateToItemSpace(Point hostPosition, ReportDesignerCanvasItemViewModel item)
    {
        return new Point(
            hostPosition.X - item.InteractionPaddingX,
            hostPosition.Y - item.InteractionPaddingY);
    }

    private Point GetStablePointerPosition(PointerEventArgs e)
    {
        return FindStablePointerHost() is { } host
            ? e.GetPosition(host)
            : e.GetPosition(this);
    }

    private Visual? FindStablePointerHost()
    {
        Visual? host = this.GetVisualAncestors().OfType<ReportDesignerSurfaceCanvasControl>().FirstOrDefault();
        return host ?? this.GetVisualAncestors().OfType<ReportDesignerDesignSurface>().FirstOrDefault();
    }

    private ReportDesignerViewModel? FindDesignerViewModel()
    {
        return this.GetVisualAncestors()
            .OfType<ReportDesignerDesignSurface>()
            .FirstOrDefault()?
            .DataContext as ReportDesignerViewModel;
    }

    private void EndInteraction(IPointer? pointer)
    {
        if (_interaction is null)
        {
            pointer?.Capture(null);
            return;
        }

        if (_interaction.Value.HasManipulated && FindDesignerViewModel() is { } designer)
        {
            designer.CompleteSurfaceInteraction(_interaction.Value.Item);
        }

        _interaction = null;
        pointer?.Capture(null);
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private readonly record struct SurfaceInteractionState(
        ReportDesignerCanvasItemViewModel Item,
        ReportDesignerSurfaceResizeHandle Handle,
        Point StartPointerPosition,
        Point LastPointerPosition,
        bool HasManipulated = false);
}
