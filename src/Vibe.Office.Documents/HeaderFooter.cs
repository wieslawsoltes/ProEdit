namespace Vibe.Office.Documents;

public sealed class HeaderFooter
{
    public List<Block> Blocks { get; } = new List<Block>();
    public bool IsDefined { get; set; }
}
