namespace ProEdit.WinUICompat.Documents;

public abstract class DocumentObject
{
    public IDictionary<string, object?> Annotations { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
