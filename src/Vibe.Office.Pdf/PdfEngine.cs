namespace Vibe.Office.Pdf;

public sealed class PdfEngine
{
    public IPdfParser Parser { get; }
    public IPdfWriter Writer { get; }

    public PdfEngine(IPdfParser parser, IPdfWriter writer)
    {
        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public PdfDocumentAst Parse(Stream stream, PdfParserOptions? options = null)
        => Parser.Parse(stream, options);

    public void Write(PdfDocumentAst document, Stream output, PdfWriteOptions? options = null)
        => Writer.Write(document, output, options);
}
