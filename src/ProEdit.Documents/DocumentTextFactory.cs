namespace ProEdit.Documents;

internal static class DocumentTextFactory
{
    public static Document CreateEmptyDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(
            document.SectionProperties,
            document.Header,
            document.Footer,
            document.FirstHeader,
            document.FirstFooter,
            document.EvenHeader,
            document.EvenFooter));
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);
        return document;
    }
}
