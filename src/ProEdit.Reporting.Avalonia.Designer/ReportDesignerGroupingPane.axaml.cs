using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Bottom grouping pane that mirrors the Report Builder row and column group surface.
/// </summary>
public sealed partial class ReportDesignerGroupingPane : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerGroupingPane" /> class.
    /// </summary>
    public ReportDesignerGroupingPane()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
