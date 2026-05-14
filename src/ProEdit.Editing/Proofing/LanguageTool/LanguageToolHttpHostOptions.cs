namespace ProEdit.Editing;

public sealed class LanguageToolHttpHostOptions
{
    public Uri Endpoint { get; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public LanguageToolHttpHostOptions(Uri endpoint)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    }
}
