namespace ProEdit.Documents;

public static class DocumentPlainTextParser
{
    public static Document FromPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FromPlainText(text.AsSpan());
    }

    public static Document FromPlainText(ReadOnlySpan<char> text)
    {
        var document = DocumentTextFactory.CreateEmptyDocument();
        if (text.Length == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
            return document;
        }

        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\r' && ch != '\n')
            {
                continue;
            }

            var line = text.Slice(start, i - start);
            document.Blocks.Add(new ParagraphBlock(line.ToString()));

            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start <= text.Length)
        {
            var line = text.Slice(start);
            document.Blocks.Add(new ParagraphBlock(line.ToString()));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        return document;
    }
}
