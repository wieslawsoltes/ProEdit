using Vibe.Office.Editing;

namespace Vibe.Office.Html;

public static class HtmlSourceProofing
{
    public static Task<IReadOnlyList<ProofingSourceDiagnostic>> AnalyzeAsync(
        string html,
        IProofingProfileRegistry profiles,
        HtmlOptions? options = null,
        string? language = null,
        ILanguageDetector? languageDetector = null,
        CancellationToken cancellationToken = default)
    {
        var spans = HtmlProofingSpanExtractor.ExtractTextSpans(html, options);
        return ProofingSourceAnalyzer.AnalyzeAsync(spans, profiles, language, languageDetector, cancellationToken);
    }
}
