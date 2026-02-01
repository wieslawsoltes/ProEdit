namespace Vibe.Office.Documents.Formats;

public sealed class DocumentFormatProfile
{
    public string Id { get; }
    public string DisplayName { get; }
    public DocumentFormatCapability Capabilities { get; }

    public DocumentFormatProfile(string id, string displayName, DocumentFormatCapability capabilities)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Profile id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Profile display name is required.", nameof(displayName));
        }

        Id = id;
        DisplayName = displayName;
        Capabilities = capabilities;
    }

    public bool Supports(DocumentFormatCapability capability)
    {
        return (Capabilities & capability) == capability;
    }
}
