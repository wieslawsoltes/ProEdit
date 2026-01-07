namespace Vibe.Office.Documents;

public sealed class DocumentSection
{
    public SectionProperties Properties { get; }
    public HeaderFooter Header { get; }
    public HeaderFooter Footer { get; }
    public HeaderFooter FirstHeader { get; }
    public HeaderFooter FirstFooter { get; }
    public HeaderFooter EvenHeader { get; }
    public HeaderFooter EvenFooter { get; }

    public DocumentSection()
        : this(new SectionProperties(), new HeaderFooter(), new HeaderFooter(), new HeaderFooter(), new HeaderFooter(), new HeaderFooter(), new HeaderFooter())
    {
    }

    public DocumentSection(
        SectionProperties properties,
        HeaderFooter header,
        HeaderFooter footer,
        HeaderFooter firstHeader,
        HeaderFooter firstFooter,
        HeaderFooter evenHeader,
        HeaderFooter evenFooter)
    {
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Footer = footer ?? throw new ArgumentNullException(nameof(footer));
        FirstHeader = firstHeader ?? throw new ArgumentNullException(nameof(firstHeader));
        FirstFooter = firstFooter ?? throw new ArgumentNullException(nameof(firstFooter));
        EvenHeader = evenHeader ?? throw new ArgumentNullException(nameof(evenHeader));
        EvenFooter = evenFooter ?? throw new ArgumentNullException(nameof(evenFooter));
    }
}
