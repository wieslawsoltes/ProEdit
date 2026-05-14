using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Left-side pane that mirrors the Report Builder report-data and outline experience.
/// </summary>
public sealed partial class ReportDesignerReportDataPane : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerReportDataPane" /> class.
    /// </summary>
    public ReportDesignerReportDataPane()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
