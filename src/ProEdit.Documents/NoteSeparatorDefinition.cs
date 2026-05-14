namespace ProEdit.Documents;

public sealed class NoteSeparatorDefinition
{
    public List<Block> SeparatorBlocks { get; } = new List<Block>();
    public List<Block> ContinuationSeparatorBlocks { get; } = new List<Block>();

    public bool HasSeparator => SeparatorBlocks.Count > 0;
    public bool HasContinuationSeparator => ContinuationSeparatorBlocks.Count > 0;
}
