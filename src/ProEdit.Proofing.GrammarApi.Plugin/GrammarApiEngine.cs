using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Proofing.GrammarApi;

public sealed class GrammarApiEngine : IProofingEngine
{
    private readonly IProofingEngineHost _host;
    private readonly GrammarApiEngineOptions _options;

    public string EngineId => _host.EngineId;

    public GrammarApiEngine(IProofingEngineHost host, GrammarApiEngineOptions options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<ProofingMatch>> CheckAsync(
        string text,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<ProofingMatch>();
        }

        var request = new ProofingHostRequest(
            text,
            language,
            new ProofingHostCheckOptions(
                _options.IncludeSpelling,
                _options.IncludeGrammar,
                false,
                _options.MaxSuggestions));

        var response = await _host.CheckAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Matches.Count == 0)
        {
            return Array.Empty<ProofingMatch>();
        }

        var matches = new List<ProofingMatch>(response.Matches.Count);
        foreach (var match in response.Matches)
        {
            var kind = MapIssueType(match.IssueType);
            if (!ShouldIncludeKind(kind))
            {
                continue;
            }

            if (match.Offset < 0 || match.Length <= 0 || match.Offset >= text.Length)
            {
                continue;
            }

            var length = Math.Min(match.Length, text.Length - match.Offset);
            var snippet = text.Substring(match.Offset, length);
            var suggestions = TruncateSuggestions(match.Replacements);

            matches.Add(new ProofingMatch(
                match.Offset,
                length,
                kind,
                snippet,
                match.RuleId,
                match.Category,
                match.Message,
                suggestions));
        }

        return matches;
    }

    private bool ShouldIncludeKind(ProofingIssueKind kind)
    {
        return kind switch
        {
            ProofingIssueKind.Spelling => _options.IncludeSpelling,
            ProofingIssueKind.Grammar => _options.IncludeGrammar,
            ProofingIssueKind.Style => false,
            _ => false
        };
    }

    private IReadOnlyList<string>? TruncateSuggestions(IReadOnlyList<string>? replacements)
    {
        if (replacements is null || replacements.Count == 0)
        {
            return null;
        }

        if (_options.MaxSuggestions <= 0 || replacements.Count <= _options.MaxSuggestions)
        {
            return replacements;
        }

        var result = new List<string>(Math.Min(_options.MaxSuggestions, replacements.Count));
        for (var i = 0; i < replacements.Count && result.Count < _options.MaxSuggestions; i++)
        {
            result.Add(replacements[i]);
        }

        return result;
    }

    private static ProofingIssueKind MapIssueType(string? issueType)
    {
        if (string.IsNullOrWhiteSpace(issueType))
        {
            return ProofingIssueKind.Grammar;
        }

        return issueType.Trim().ToLowerInvariant() switch
        {
            "spelling" => ProofingIssueKind.Spelling,
            "misspelling" => ProofingIssueKind.Spelling,
            "grammar" => ProofingIssueKind.Grammar,
            "style" => ProofingIssueKind.Style,
            "typographical" => ProofingIssueKind.Style,
            _ => ProofingIssueKind.Grammar
        };
    }
}
