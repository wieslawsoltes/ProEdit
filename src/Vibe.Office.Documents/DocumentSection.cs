namespace Vibe.Office.Documents;

public sealed class DocumentSection
{
    public SectionProperties Properties { get; }
    public HeaderFooter Header { get; }
    public HeaderFooter Footer { get; }

    public DocumentSection()
        : this(new SectionProperties(), new HeaderFooter(), new HeaderFooter())
    {
    }

    public DocumentSection(SectionProperties properties, HeaderFooter header, HeaderFooter footer)
    {
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Footer = footer ?? throw new ArgumentNullException(nameof(footer));
    }
}
