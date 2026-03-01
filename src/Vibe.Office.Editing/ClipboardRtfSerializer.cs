using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public static class ClipboardRtfSerializer
{
    public static string ToRtf(Document document)
    {
        return DocumentRtfSerializer.ToRtf(document);
    }

    public static bool TryParse(string rtf, out Document document)
    {
        return DocumentRtfParser.TryParse(rtf, out document);
    }
}
