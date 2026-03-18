using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Drop-enabled chart-data bucket surface.
/// </summary>
internal sealed class ReportDesignerChartDataDropZoneControl : ContentControl
{
    public static readonly DirectProperty<ReportDesignerChartDataDropZoneControl, ReportDesignerChartDropTarget> DropTargetProperty =
        AvaloniaProperty.RegisterDirect<ReportDesignerChartDataDropZoneControl, ReportDesignerChartDropTarget>(
            nameof(DropTarget),
            static owner => owner.DropTarget,
            static (owner, value) => owner.DropTarget = value);

    private ReportDesignerChartDropTarget _dropTarget;

    public ReportDesignerChartDataDropZoneControl()
    {
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    public ReportDesignerChartDropTarget DropTarget
    {
        get => _dropTarget;
        set => SetAndRaise(DropTargetProperty, ref _dropTarget, value);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DropTarget == ReportDesignerChartDropTarget.None
            || FindDesignerViewModel() is not { } designer
            || !e.DataTransfer.Contains(ReportDesignerDragDataFormats.Payload))
        {
            return;
        }

        var payload = ReportDesignerDragDataFormats.Deserialize(
            e.DataTransfer.TryGetValue(ReportDesignerDragDataFormats.Payload),
            designer);
        if (payload is null || !designer.CanAcceptChartDataDrop(payload, DropTarget))
        {
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DropTarget == ReportDesignerChartDropTarget.None
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

        if (designer.TryApplyChartDataDrop(payload, DropTarget))
        {
            e.DragEffects = DragDropEffects.Copy;
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
