using Avalonia;
using Avalonia.Controls;

namespace ProEdit.Reporting.Avalonia.Viewer;

/// <summary>
/// Reports the available preview viewport size back to the viewer view model.
/// </summary>
public sealed class ReportViewerViewportHost : Border
{
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            UpdateViewerViewport();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateViewerViewport();
    }

    private void UpdateViewerViewport()
    {
        if (DataContext is ReportViewerViewModel viewModel)
        {
            viewModel.UpdateViewportSize(Bounds.Width, Bounds.Height);
        }
    }
}
