using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Top-of-surface parameter layout pane that mirrors Report Builder parameter grid authoring.
/// </summary>
public sealed partial class ReportDesignerParameterLayoutPane : UserControl
{
    public ReportDesignerParameterLayoutPane()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
