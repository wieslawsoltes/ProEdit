namespace Vibe.Office.WinUICompat.Documents;

public sealed class RichTextDocument : DocumentObject
{
    public string? FontFamily { get; set; }

    public double? FontSize { get; set; }

    public string? FontWeight { get; set; }

    public string? FontStyle { get; set; }

    public string? FontStretch { get; set; }

    public string? Foreground { get; set; }

    public string? Background { get; set; }

    public object? TextEffects { get; set; }

    public object? Typography { get; set; }

    public double? PageWidth { get; set; }

    public double? PageHeight { get; set; }

    public Thickness? PagePadding { get; set; }

    public double? ColumnWidth { get; set; }

    public double? ColumnGap { get; set; }

    public string? TextAlignment { get; set; }

    public string? ColumnRuleBrush { get; set; }

    public double? ColumnRuleWidth { get; set; }

    public string? FlowDirection { get; set; }

    public bool? IsColumnWidthFlexible { get; set; }

    public bool? IsHyphenationEnabled { get; set; }

    public bool? IsOptimalParagraphEnabled { get; set; }

    public double? LineHeight { get; set; }

    public string? LineStackingStrategy { get; set; }

    public double? MaxPageHeight { get; set; }

    public double? MaxPageWidth { get; set; }

    public double? MinPageHeight { get; set; }

    public double? MinPageWidth { get; set; }

    public BlockCollection Blocks { get; } = new();
}
