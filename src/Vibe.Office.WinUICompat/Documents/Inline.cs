namespace Vibe.Office.WinUICompat.Documents;

public abstract class Inline : TextElement
{
    public string? BaselineAlignment { get; set; }

    public string? TextDecorations { get; set; }

    public string? FlowDirection { get; set; }
}
