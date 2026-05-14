using ProEdit.Editing;

namespace ProEdit.Markdown;

public static class MarkdownSourceProofing
{
    public static Task<IReadOnlyList<ProofingSourceDiagnostic>> AnalyzeAsync(
        string markdown,
        IProofingProfileRegistry profiles,
        MarkdownOptions? options = null,
        string? language = null,
        ILanguageDetector? languageDetector = null,
        CancellationToken cancellationToken = default)
    {
        var spans = MarkdownProofingSpanExtractor.ExtractTextSpans(markdown, options);
        return ProofingSourceAnalyzer.AnalyzeAsync(spans, profiles, language, languageDetector, cancellationToken);
    }
}
