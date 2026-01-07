namespace Vibe.Office.Documents;

public sealed class ParagraphBlock : Block
{
    public string Text { get; set; }
    public ListInfo? ListInfo { get; set; }
    public string? StyleId { get; set; }
    public ParagraphProperties Properties { get; } = new ParagraphProperties();
    public List<Inline> Inlines { get; } = new List<Inline>();
    public List<FloatingObject> FloatingObjects { get; } = new List<FloatingObject>();

    public ParagraphBlock(string text = "", ListInfo? listInfo = null)
    {
        Text = text;
        ListInfo = listInfo;
    }
}
