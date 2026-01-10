namespace Vibe.Office.Ribbon;

public interface IRibbonControl
{
    string Id { get; }
    string Label { get; }
    string? KeyTip { get; }
    string? IconKey { get; }
    string ToolTip { get; }
    RibbonControlSize Size { get; }
    RibbonControlSize LayoutSize { get; }
    bool IsEnabled { get; }
    bool IsVisible { get; }
}
