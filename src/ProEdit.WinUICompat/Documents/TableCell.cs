namespace ProEdit.WinUICompat.Documents;

public sealed class TableCell : TextElement
{
    public BlockCollection Blocks { get; } = new();

    public int ColumnSpan { get; set; } = 1;

    public int RowSpan { get; set; } = 1;

    public Thickness? Padding { get; set; }

    public Thickness? BorderThickness { get; set; }

    public string? BorderBrush { get; set; }

    public string? FlowDirection { get; set; }

    public double? LineHeight { get; set; }

    public string? LineStackingStrategy { get; set; }

    public string? TextAlignment { get; set; }
}
