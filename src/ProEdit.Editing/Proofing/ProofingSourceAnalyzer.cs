using System.Text;
using ProEdit.Documents;

namespace ProEdit.Editing;

public static class ProofingSourceAnalyzer
{
    public static async Task<IReadOnlyList<ProofingSourceDiagnostic>> AnalyzeAsync(
        IReadOnlyList<ProofingSourceSpan> spans,
        IProofingProfileRegistry profiles,
        string? language = null,
        ILanguageDetector? languageDetector = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(profiles);

        var map = BuildMap(spans, out var text);
        if (map.Count == 0 || text.Length == 0)
        {
            return Array.Empty<ProofingSourceDiagnostic>();
        }

        var resolvedLanguage = ResolveLanguage(text, language, profiles, languageDetector);
        var profile = profiles.ResolveProfile(resolvedLanguage);
        var diagnostics = new List<ProofingSourceDiagnostic>();

        CollectSpellingDiagnostics(text, resolvedLanguage, profile, map, diagnostics);

        if (profile.GrammarEngine is not null || profile.StyleEngine is not null)
        {
            var matches = await CollectGrammarStyleMatchesAsync(text, resolvedLanguage, profile, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!profile.Rules.IsEnabled(match.RuleId, match.Category))
                {
                    continue;
                }

                if (!TryMapSpan(map, match.StartOffset, match.Length, out var sourceOffset, out var sourceLength))
                {
                    continue;
                }

                diagnostics.Add(new ProofingSourceDiagnostic(
                    sourceOffset,
                    sourceLength,
                    match.Text,
                    resolvedLanguage,
                    match.Kind,
                    match.RuleId,
                    match.Category,
                    match.Message,
                    match.Suggestions));
            }
        }

        if (diagnostics.Count > 1)
        {
            diagnostics.Sort(static (left, right) => left.StartOffset.CompareTo(right.StartOffset));
        }

        return diagnostics;
    }

    private static void CollectSpellingDiagnostics(
        string text,
        string language,
        IProofingProfile profile,
        IReadOnlyList<SourceSpanMap> map,
        List<ProofingSourceDiagnostic> diagnostics)
    {
        var wordSpans = ProofingTokenizer.CollectWordSpans(text.AsSpan());
        if (wordSpans.Count == 0)
        {
            return;
        }

        for (var i = 0; i < wordSpans.Count; i++)
        {
            var wordSpan = wordSpans[i];
            var word = text.AsSpan(wordSpan.Start, wordSpan.Length);
            if (profile.SpellEngine.Check(word, language))
            {
                continue;
            }

            if (!TryMapSpan(map, wordSpan.Start, wordSpan.Length, out var sourceOffset, out var sourceLength))
            {
                continue;
            }

            diagnostics.Add(new ProofingSourceDiagnostic(
                sourceOffset,
                sourceLength,
                word.ToString(),
                language,
                ProofingIssueKind.Spelling));
        }
    }

    private static async Task<List<ProofingMatch>> CollectGrammarStyleMatchesAsync(
        string text,
        string language,
        IProofingProfile profile,
        CancellationToken cancellationToken)
    {
        var result = new List<ProofingMatch>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var grammarEngine = profile.GrammarEngine;
        var styleEngine = profile.StyleEngine;

        if (grammarEngine is not null)
        {
            var matches = await grammarEngine.CheckAsync(text, language, cancellationToken).ConfigureAwait(false);
            AddMatches(result, matches);
        }

        if (styleEngine is not null && !ReferenceEquals(styleEngine, grammarEngine))
        {
            var matches = await styleEngine.CheckAsync(text, language, cancellationToken).ConfigureAwait(false);
            AddMatches(result, matches);
        }

        return result;
    }

    private static void AddMatches(List<ProofingMatch> target, IReadOnlyList<ProofingMatch> matches)
    {
        if (matches.Count == 0)
        {
            return;
        }

        for (var i = 0; i < matches.Count; i++)
        {
            target.Add(matches[i]);
        }
    }

    private static string ResolveLanguage(
        string text,
        string? language,
        IProofingProfileRegistry profiles,
        ILanguageDetector? languageDetector)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            return language.Trim();
        }

        if (languageDetector is not null)
        {
            var detected = languageDetector.DetectLanguage(text.AsSpan());
            if (!string.IsNullOrWhiteSpace(detected))
            {
                return detected.Trim();
            }
        }

        return profiles.DefaultProfile.DefaultLanguage ?? "en-US";
    }

    private static List<SourceSpanMap> BuildMap(IReadOnlyList<ProofingSourceSpan> spans, out string text)
    {
        var builder = new StringBuilder();
        var map = new List<SourceSpanMap>(spans.Count);
        var first = true;

        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (!span.IsValid || string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            if (!first)
            {
                builder.Append('\n');
            }

            var textStart = builder.Length;
            builder.Append(span.Text);
            map.Add(new SourceSpanMap(span.Start, span.Length, textStart, span.Text.Length));
            first = false;
        }

        text = builder.ToString();
        return map;
    }

    private static bool TryMapSpan(
        IReadOnlyList<SourceSpanMap> map,
        int textOffset,
        int textLength,
        out int sourceOffset,
        out int sourceLength)
    {
        sourceOffset = 0;
        sourceLength = 0;

        if (textLength <= 0)
        {
            return false;
        }

        for (var i = 0; i < map.Count; i++)
        {
            var entry = map[i];
            if (textOffset < entry.TextStart || textOffset >= entry.TextStart + entry.TextLength)
            {
                continue;
            }

            var localOffset = textOffset - entry.TextStart;
            if (localOffset < 0 || localOffset >= entry.TextLength)
            {
                return false;
            }

            var maxLength = entry.TextLength - localOffset;
            var mappedLength = Math.Min(textLength, maxLength);
            var sourceMaxLength = entry.SourceLength - localOffset;
            if (sourceMaxLength <= 0)
            {
                return false;
            }

            sourceOffset = entry.SourceStart + localOffset;
            sourceLength = Math.Min(mappedLength, sourceMaxLength);
            return sourceLength > 0;
        }

        return false;
    }

    private readonly record struct SourceSpanMap(int SourceStart, int SourceLength, int TextStart, int TextLength);
}
