namespace Vibe.Office.Documents;

public abstract class Block
{
    public Guid NodeId { get; set; } = Guid.NewGuid();
}
