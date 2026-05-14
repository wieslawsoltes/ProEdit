using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Reporting.App.Controls;

public partial class ReportingStudioHeaderBar : UserControl
{
    public ReportingStudioHeaderBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
