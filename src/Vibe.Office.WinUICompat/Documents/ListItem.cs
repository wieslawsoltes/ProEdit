namespace Vibe.Office.WinUICompat.Documents;

public sealed class ListItem : TextElement
{
    public BlockCollection Blocks { get; } = new();

    public Thickness? Margin { get; set; }

    public Thickness? Padding { get; set; }

    public Thickness? BorderThickness { get; set; }

    public string? BorderBrush { get; set; }

    public string? FlowDirection { get; set; }

    public double? LineHeight { get; set; }

    public string? LineStackingStrategy { get; set; }

    public string? TextAlignment { get; set; }
}
