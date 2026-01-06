namespace Vibe.Office.Documents;

public sealed record HyperlinkInfo(string? Uri, string? Anchor, string? Tooltip)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Uri) && string.IsNullOrWhiteSpace(Anchor);
}
