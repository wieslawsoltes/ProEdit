namespace ProEdit.WinUICompat.Documents;

public abstract class AnchoredBlock : Inline
{
    public BlockCollection Blocks { get; } = new();

    public Thickness? Margin { get; set; }

    public Thickness? Padding { get; set; }

    public Thickness? BorderThickness { get; set; }

    public string? BorderBrush { get; set; }

    public string? TextAlignment { get; set; }

    public double? LineHeight { get; set; }

    public string? LineStackingStrategy { get; set; }
}
