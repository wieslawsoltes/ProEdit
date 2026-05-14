using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Avalonia control that hosts the report designer user interface.
/// </summary>
public sealed partial class ReportDesignerControl : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerControl" /> class.
    /// </summary>
    public ReportDesignerControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
