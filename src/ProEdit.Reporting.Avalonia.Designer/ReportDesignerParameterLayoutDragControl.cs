using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Drag source wrapper for parameter-pane cells.
/// </summary>
internal sealed class ReportDesignerParameterLayoutDragControl : ContentControl
{
    private const double DragThreshold = 4d;

    private Point? _dragStartPoint;
    private bool _isDragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (DataContext is not ReportDesignerParameterLayoutCellViewModel { HasParameter: true }
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
            || DataContext is not ReportDesignerParameterLayoutCellViewModel { Parameter: { } parameter })
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.Value.X) < DragThreshold
            && Math.Abs(position.Y - _dragStartPoint.Value.Y) < DragThreshold)
        {
            return;
        }

        _isDragging = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(
            ReportDesignerDragDataFormats.Payload,
            ReportDesignerDragDataFormats.Serialize(new ReportDesignerParameterDragPayload(parameter))));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move | DragDropEffects.Copy);
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
