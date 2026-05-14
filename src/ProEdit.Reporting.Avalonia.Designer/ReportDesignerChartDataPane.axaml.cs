using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Context-sensitive chart-data pane that mirrors Report Builder chart-data buckets.
/// </summary>
public sealed partial class ReportDesignerChartDataPane : UserControl
{
    public ReportDesignerChartDataPane()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
