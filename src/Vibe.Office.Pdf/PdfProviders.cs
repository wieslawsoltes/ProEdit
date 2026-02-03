namespace Vibe.Office.Pdf;

public static class PdfProviderIds
{
    public const string PdfPig = "pdfpig";
    public const string PdfSharp = "pdfsharp";
}

public interface IPdfParser
{
    string ProviderId { get; }

    PdfDocumentAst Parse(Stream stream, PdfParserOptions? options = null);
}

public interface IPdfWriter
{
    string ProviderId { get; }
    bool SupportsIncrementalUpdate { get; }

    void Write(PdfDocumentAst document, Stream output, PdfWriteOptions? options = null);
}

public sealed class PdfProviderRegistry
{
    private readonly Dictionary<string, IPdfParser> _parsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPdfWriter> _writers = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterParser(IPdfParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parsers[parser.ProviderId] = parser;
    }

    public void RegisterWriter(IPdfWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writers[writer.ProviderId] = writer;
    }

    public bool TryGetParser(string providerId, out IPdfParser parser)
        => _parsers.TryGetValue(providerId, out parser!);

    public bool TryGetWriter(string providerId, out IPdfWriter writer)
        => _writers.TryGetValue(providerId, out writer!);
}
