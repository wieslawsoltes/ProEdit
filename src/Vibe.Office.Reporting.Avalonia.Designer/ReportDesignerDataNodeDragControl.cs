using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Drag source wrapper for Report Data nodes.
/// </summary>
public sealed class ReportDesignerDataNodeDragControl : ContentControl
{
    private const double DragThreshold = 4d;

    private Point? _dragStartPoint;
    private bool _isDragging;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerDataNodeDragControl" /> class.
    /// </summary>
    public ReportDesignerDataNodeDragControl()
    {
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (DataContext is not ReportDesignerDataNodeViewModel node
            || ReportDesignerDragPayloadFactory.Create(node) is null
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = null;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
    }

    protected override async void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDragging
            || _dragStartPoint is null
            || DataContext is not ReportDesignerDataNodeViewModel node)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.Value.X) < DragThreshold
            && Math.Abs(position.Y - _dragStartPoint.Value.Y) < DragThreshold)
        {
            return;
        }

        var payload = ReportDesignerDragPayloadFactory.Create(node);
        if (payload is null)
        {
            _dragStartPoint = null;
            return;
        }

        _isDragging = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(
            ReportDesignerDragDataFormats.Payload,
            ReportDesignerDragDataFormats.Serialize(payload)));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        _isDragging = false;
        _dragStartPoint = null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStartPoint = null;
        _isDragging = false;
    }
}
