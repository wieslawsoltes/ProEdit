namespace Vibe.Office.WinUICompat.Documents;

public abstract class Block : TextElement
{
    public Thickness? Margin { get; set; }

    public string? TextAlignment { get; set; }

    public double? LineHeight { get; set; }

    public Thickness? Padding { get; set; }

    public Thickness? BorderThickness { get; set; }

    public string? BorderBrush { get; set; }

    public string? FlowDirection { get; set; }

    public string? LineStackingStrategy { get; set; }

    public bool? BreakColumnBefore { get; set; }

    public bool? BreakPageBefore { get; set; }

    public string? ClearFloaters { get; set; }

    public bool? IsHyphenationEnabled { get; set; }
}
