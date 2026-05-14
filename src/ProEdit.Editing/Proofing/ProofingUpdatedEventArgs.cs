namespace ProEdit.Editing;

public sealed class ProofingUpdatedEventArgs : EventArgs
{
    public IReadOnlyList<int> ParagraphIndices { get; }

    public ProofingUpdatedEventArgs(IReadOnlyList<int> paragraphIndices)
    {
        ParagraphIndices = paragraphIndices ?? Array.Empty<int>();
    }
}
