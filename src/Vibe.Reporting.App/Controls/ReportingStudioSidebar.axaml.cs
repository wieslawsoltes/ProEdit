using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Vibe.Reporting.App.Controls;

public partial class ReportingStudioSidebar : UserControl
{
    public ReportingStudioSidebar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
