using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Drop-enabled parameter-pane cell surface.
/// </summary>
internal sealed class ReportDesignerParameterLayoutDropZoneControl : ContentControl
{
    public static readonly DirectProperty<ReportDesignerParameterLayoutDropZoneControl, int> RowIndexProperty =
        AvaloniaProperty.RegisterDirect<ReportDesignerParameterLayoutDropZoneControl, int>(
            nameof(RowIndex),
            static owner => owner.RowIndex,
            static (owner, value) => owner.RowIndex = value);

    public static readonly DirectProperty<ReportDesignerParameterLayoutDropZoneControl, int> ColumnIndexProperty =
        AvaloniaProperty.RegisterDirect<ReportDesignerParameterLayoutDropZoneControl, int>(
            nameof(ColumnIndex),
            static owner => owner.ColumnIndex,
            static (owner, value) => owner.ColumnIndex = value);

    private int _columnIndex;
    private int _rowIndex;

    public ReportDesignerParameterLayoutDropZoneControl()
    {
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    public int RowIndex
    {
        get => _rowIndex;
        set => SetAndRaise(RowIndexProperty, ref _rowIndex, value);
    }

    public int ColumnIndex
    {
        get => _columnIndex;
        set => SetAndRaise(ColumnIndexProperty, ref _columnIndex, value);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (FindDesignerViewModel() is not { } designer
            || !designer.CanAcceptParameterLayoutDrop(RowIndex, ColumnIndex)
            || !e.DataTransfer.Contains(ReportDesignerDragDataFormats.Payload))
        {
            return;
        }

        var payload = ReportDesignerDragDataFormats.Deserialize(
            e.DataTransfer.TryGetValue(ReportDesignerDragDataFormats.Payload),
            designer);
        if (payload is not ReportDesignerParameterDragPayload)
        {
            return;
        }

        e.DragEffects = DragDropEffects.Move | DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (FindDesignerViewModel() is not { } designer)
        {
            return;
        }

        var payload = ReportDesignerDragDataFormats.Deserialize(
            e.DataTransfer.TryGetValue(ReportDesignerDragDataFormats.Payload),
            designer);
        if (payload is not ReportDesignerParameterDragPayload parameterPayload)
        {
            return;
        }

        if (designer.TryApplyParameterLayoutDrop(parameterPayload, RowIndex, ColumnIndex))
        {
            e.DragEffects = DragDropEffects.Move | DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private ReportDesignerViewModel? FindDesignerViewModel()
    {
        return this.GetVisualAncestors()
            .OfType<ReportDesignerControl>()
            .FirstOrDefault()?
            .DataContext as ReportDesignerViewModel;
    }
}
