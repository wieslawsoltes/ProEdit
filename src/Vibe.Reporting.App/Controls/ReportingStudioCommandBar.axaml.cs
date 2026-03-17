using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Vibe.Reporting.App.Controls;

public partial class ReportingStudioCommandBar : UserControl
{
    public ReportingStudioCommandBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
