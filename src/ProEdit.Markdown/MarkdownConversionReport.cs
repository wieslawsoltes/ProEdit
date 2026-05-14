namespace ProEdit.Markdown;

public enum MarkdownConversionFeature
{
    Unknown,
    UnsupportedBlock,
    UnsupportedInline,
    PageBreak,
    SectionBreak,
    ColumnBreak,
    AltChunk,
    ContentControl,
    Revision,
    Image,
    Shape,
    Chart,
    Equation,
    Field,
    Bookmark,
    Comment,
    Footnote,
    Endnote,
    PageNumber,
    TotalPages,
    TableInline,
    FloatingObject,
    EmbeddedObject,
    Html
}

public enum MarkdownConversionAction
{
    Dropped,
    Placeholder,
    Html,
    Converted,
    Preserved
}

public readonly record struct MarkdownConversionItem(
    MarkdownConversionFeature Feature,
    MarkdownConversionAction Action);

public sealed class MarkdownConversionReport
{
    private readonly Dictionary<MarkdownConversionItem, int> _counts = new();

    public IReadOnlyDictionary<MarkdownConversionItem, int> Counts => _counts;

    public int TotalEvents { get; private set; }

    public void Add(MarkdownConversionFeature feature, MarkdownConversionAction action, int count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        var key = new MarkdownConversionItem(feature, action);
        _counts.TryGetValue(key, out var existing);
        _counts[key] = existing + count;
        TotalEvents += count;
    }

    public int GetCount(MarkdownConversionFeature feature, MarkdownConversionAction action)
    {
        return _counts.TryGetValue(new MarkdownConversionItem(feature, action), out var count) ? count : 0;
    }
}
