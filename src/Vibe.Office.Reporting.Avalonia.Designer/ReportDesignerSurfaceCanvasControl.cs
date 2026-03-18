using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Drop-enabled host for the WYSIWYG designer page.
/// </summary>
public sealed class ReportDesignerSurfaceCanvasControl : ContentControl
{
    private Point? _insertStartPosition;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerSurfaceCanvasControl" /> class.
    /// </summary>
    public ReportDesignerSurfaceCanvasControl()
    {
        Focusable = true;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || FindDesignerViewModel() is not { } designer)
        {
            return;
        }

        if (designer.HasActiveInsertTool)
        {
            _insertStartPosition = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        designer.SelectCurrentSectionFromSurface();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_insertStartPosition is not { } startPosition || FindDesignerViewModel() is not { } designer)
        {
            e.Pointer.Capture(null);
            return;
        }

        var endPosition = e.GetPosition(this);
        designer.TryCommitInsertToolPlacement(startPosition.X, startPosition.Y, endPosition.X, endPosition.Y);
        _insertStartPosition = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _insertStartPosition = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (FindDesignerViewModel() is not { } designer)
        {
            return;
        }

        var gridStep = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 1d : 4d;
        var handled = false;
        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                handled = designer.HandleSurfaceDelete();
                break;
            case Key.D when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                handled = designer.HandleSurfaceDuplicate();
                break;
            case Key.Left when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                handled = designer.HandleSurfaceResize(-gridStep, 0d);
                break;
            case Key.Right when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                handled = designer.HandleSurfaceResize(gridStep, 0d);
                break;
            case Key.Up when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                handled = designer.HandleSurfaceResize(0d, -gridStep);
                break;
            case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                handled = designer.HandleSurfaceResize(0d, gridStep);
                break;
            case Key.Left:
                handled = designer.HandleSurfaceNudge(-gridStep, 0d);
                break;
            case Key.Right:
                handled = designer.HandleSurfaceNudge(gridStep, 0d);
                break;
            case Key.Up:
                handled = designer.HandleSurfaceNudge(0d, -gridStep);
                break;
            case Key.Down:
                handled = designer.HandleSurfaceNudge(0d, gridStep);
                break;
            case Key.Escape:
                designer.CancelInsertTool();
                handled = true;
                break;
            case Key.PageUp when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                handled = designer.HandleBringSelectedForward();
                break;
            case Key.PageDown when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                handled = designer.HandleSendSelectedBackward();
                break;
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(ReportDesignerDragDataFormats.Payload))
        {
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
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
        if (payload is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (designer.TryApplyDesignerDrop(payload, position.X, position.Y, targetCanvasItem: null))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        Focus();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (FindDesignerViewModel() is { } designer)
        {
            designer.UpdateSurfaceViewport(e.NewSize.Width, e.NewSize.Height);
        }
    }

    private ReportDesignerViewModel? FindDesignerViewModel()
    {
        return this.GetVisualAncestors()
            .OfType<ReportDesignerDesignSurface>()
            .FirstOrDefault()?
            .DataContext as ReportDesignerViewModel;
    }
}
