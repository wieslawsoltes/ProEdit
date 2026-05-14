using System.IO;

namespace ProEdit.Documents.Formats;

public interface IDocumentFormat
{
    string FormatId { get; }
    string DisplayName { get; }
    IReadOnlyList<string> Extensions { get; }
    DocumentFormatProfile Profile { get; }
    bool CanLoad { get; }
    bool CanSave { get; }

    Document Load(Stream stream, DocumentFormatOptions? options = null);

    void Save(Document document, Stream stream, DocumentFormatOptions? options = null);
}
