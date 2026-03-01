using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Documents.Formats;
using Vibe.Office.Markdown;

namespace Vibe.Office.FlowDocument.IO.Formats;

internal sealed class MarkdownDocumentFormat : IDocumentFormat
{
    private readonly MarkdownOptions _options;

    public MarkdownDocumentFormat(MarkdownOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string FormatId => "markdown";

    public string DisplayName => "Markdown";

    public IReadOnlyList<string> Extensions => [".md", ".markdown"];

    public DocumentFormatProfile Profile => MarkdownProfiles.GitHub;

    public bool CanLoad => true;

    public bool CanSave => true;

    public Document Load(Stream stream, DocumentFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var markdown = reader.ReadToEnd();
        return MarkdownDocumentConverter.FromMarkdown(markdown.AsSpan(), _options);
    }

    public void Save(Document document, Stream stream, DocumentFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        var markdown = MarkdownDocumentConverter.ToMarkdown(document, _options);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
        writer.Write(markdown);
        writer.Flush();
    }
}
