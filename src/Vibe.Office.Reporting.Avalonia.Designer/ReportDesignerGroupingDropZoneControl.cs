using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Drop-enabled grouping surface for row-group authoring from the Report Data pane.
/// </summary>
public sealed class ReportDesignerGroupingDropZoneControl : ContentControl
{
    public static readonly DirectProperty<ReportDesignerGroupingDropZoneControl, ReportDesignerGroupingDropTarget> DropTargetProperty =
        AvaloniaProperty.RegisterDirect<ReportDesignerGroupingDropZoneControl, ReportDesignerGroupingDropTarget>(
            nameof(DropTarget),
            static owner => owner.DropTarget,
            static (owner, value) => owner.DropTarget = value);

    private ReportDesignerGroupingDropTarget _dropTarget;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerGroupingDropZoneControl" /> class.
    /// </summary>
    public ReportDesignerGroupingDropZoneControl()
    {
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    /// <summary>
    /// Gets or sets the grouping drop target.
    /// </summary>
    public ReportDesignerGroupingDropTarget DropTarget
    {
        get => _dropTarget;
        set => SetAndRaise(DropTargetProperty, ref _dropTarget, value);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DropTarget == ReportDesignerGroupingDropTarget.None
            || FindDesignerViewModel() is not { } designer
            || !designer.CanAcceptGroupingDrop(DropTarget))
        {
            return;
        }

        if (!e.DataTransfer.Contains(ReportDesignerDragDataFormats.Payload))
        {
            return;
        }

        var payload = ReportDesignerDragDataFormats.Deserialize(
            e.DataTransfer.TryGetValue(ReportDesignerDragDataFormats.Payload),
            designer);
        if (payload is not ReportDesignerDataFieldDragPayload)
        {
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DropTarget == ReportDesignerGroupingDropTarget.None
            || FindDesignerViewModel() is not { } designer)
        {
            return;
        }

        var payload = ReportDesignerDragDataFormats.Deserialize(
            e.DataTransfer.TryGetValue(ReportDesignerDragDataFormats.Payload),
            designer);
        if (payload is not ReportDesignerDataFieldDragPayload fieldPayload)
        {
            return;
        }

        if (designer.TryApplyGroupingDrop(fieldPayload, DropTarget))
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
