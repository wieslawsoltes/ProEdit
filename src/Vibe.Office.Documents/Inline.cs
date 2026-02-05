namespace Vibe.Office.Documents;

public abstract class Inline
{
    public Guid NodeId { get; set; } = Guid.NewGuid();
    public HyperlinkInfo? Hyperlink { get; set; }
}
