namespace Vibe.Office.Editing;

public readonly record struct ProofingSourceSpan
{
    public ProofingSourceSpan(int start, int length, string text)
    {
        Start = start;
        Length = length;
        Text = text ?? string.Empty;
    }

    public int Start { get; }
    public int Length { get; }
    public string Text { get; }

    public int End => Start + Length;

    public bool IsValid => Start >= 0 && Length > 0;
}
