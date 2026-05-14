using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Right-side properties pane that mirrors the Report Builder properties window.
/// </summary>
public sealed partial class ReportDesignerPropertiesPane : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerPropertiesPane" /> class.
    /// </summary>
    public ReportDesignerPropertiesPane()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
