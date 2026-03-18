using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// WYSIWYG design surface that mirrors the centered Report Builder authoring page.
/// </summary>
public sealed partial class ReportDesignerDesignSurface : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportDesignerDesignSurface" /> class.
    /// </summary>
    public ReportDesignerDesignSurface()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
