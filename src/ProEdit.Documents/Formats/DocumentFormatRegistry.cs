namespace ProEdit.Documents.Formats;

public sealed class DocumentFormatRegistry
{
    private readonly Dictionary<string, IDocumentFormat> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IDocumentFormat> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IDocumentFormat> Formats => _byId.Values;

    public void Register(IDocumentFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (string.IsNullOrWhiteSpace(format.FormatId))
        {
            throw new ArgumentException("FormatId is required.", nameof(format));
        }

        _byId[format.FormatId] = format;
        foreach (var extension in format.Extensions)
        {
            var normalized = NormalizeExtension(extension);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            _byExtension[normalized] = format;
        }
    }

    public bool TryGetById(string formatId, out IDocumentFormat format)
    {
        if (string.IsNullOrWhiteSpace(formatId))
        {
            format = null!;
            return false;
        }

        return _byId.TryGetValue(formatId, out format!);
    }

    public bool TryGetByExtension(string extension, out IDocumentFormat format)
    {
        var normalized = NormalizeExtension(extension);
        if (string.IsNullOrEmpty(normalized))
        {
            format = null!;
            return false;
        }

        return _byExtension.TryGetValue(normalized, out format!);
    }

    public static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim();
        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (!trimmed.StartsWith('.'))
        {
            trimmed = "." + trimmed;
        }

        return trimmed;
    }
}
