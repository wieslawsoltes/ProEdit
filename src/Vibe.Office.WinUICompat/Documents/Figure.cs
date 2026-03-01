namespace Vibe.Office.WinUICompat.Documents;

public sealed class Figure : AnchoredBlock
{
    public double? Width { get; set; }

    public double? Height { get; set; }

    public string? HorizontalAnchor { get; set; }

    public string? VerticalAnchor { get; set; }

    public double? HorizontalOffset { get; set; }

    public double? VerticalOffset { get; set; }

    public bool? CanDelayPlacement { get; set; }

    public string? WrapDirection { get; set; }
}
