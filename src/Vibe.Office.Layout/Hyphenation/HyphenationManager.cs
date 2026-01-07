namespace Vibe.Office.Layout;

internal static class HyphenationManager
{
    private static readonly Lazy<Hyphenator> DefaultHyphenator = new Lazy<Hyphenator>(Hyphenator.CreateDefault);

    public static Hyphenator? ResolveHyphenator(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DefaultHyphenator.Value;
        }

        var normalized = NormalizeLanguage(language);
        if (string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultHyphenator.Value;
        }

        return null;
    }

    private static string NormalizeLanguage(string language)
    {
        var span = language.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return string.Empty;
        }

        var separatorIndex = span.IndexOfAny('-', '_');
        if (separatorIndex > 0)
        {
            span = span[..separatorIndex];
        }

        return span.ToString();
    }
}
