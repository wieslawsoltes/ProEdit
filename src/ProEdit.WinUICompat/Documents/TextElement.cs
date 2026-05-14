namespace ProEdit.WinUICompat.Documents;

public abstract class TextElement : DocumentObject
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
}
