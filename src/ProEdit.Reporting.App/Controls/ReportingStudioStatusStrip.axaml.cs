using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.App.Controls;

public partial class ReportingStudioStatusStrip : UserControl
{
    public ReportingStudioStatusStrip()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
