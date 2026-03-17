using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Vibe.Office.Reporting.Avalonia.Viewer;

/// <summary>
/// Avalonia control that hosts the report viewer user interface.
/// </summary>
public sealed partial class ReportViewerControl : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportViewerControl" /> class.
    /// </summary>
    public ReportViewerControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
