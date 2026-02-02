using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public class GrammarProofingTests
{
    [Fact]
    public async Task ProofingService_MapsGrammarAndStyleMatches()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("This are"));
        document.DefaultTextStyle.Language = "en-US";

        var grammarEngine = new StubGrammarEngine(
            new ProofingMatch(0, 4, ProofingIssueKind.Grammar, "This", "RULE1", "GRAMMAR", "Use plural.", new[] { "These" }),
            new ProofingMatch(5, 3, ProofingIssueKind.Style, "are", "RULE2", "STYLE", "Consider rewrite.", Array.Empty<string>()));

        var registry = SpellDictionaryRegistry.CreateDefault();
        var spellEngine = new HunspellSpellEngine(registry);
        var profile = new ProofingProfile("Test", spellEngine, registry, "en-US", grammarEngine: grammarEngine);
        var profiles = new ProofingProfileRegistry(profile);

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        using var proofing = new EditorProofingService(session, profiles);

        await WaitForDiagnosticsAsync(proofing, d => d.Any(item => item.Kind != ProofingIssueKind.Spelling));

        var diagnostics = proofing.GetParagraphDiagnostics(0);
        Assert.Contains(diagnostics, item => item.Kind == ProofingIssueKind.Grammar && item.RuleId == "RULE1" && item.Category == "GRAMMAR");
        Assert.Contains(diagnostics, item => item.Kind == ProofingIssueKind.Style && item.RuleId == "RULE2" && item.Category == "STYLE");
    }

    [Fact]
    public async Task ProofingService_FiltersDisabledRules()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("This are"));
        document.DefaultTextStyle.Language = "en-US";

        var grammarEngine = new StubGrammarEngine(
            new ProofingMatch(0, 4, ProofingIssueKind.Grammar, "This", "RULE_SKIP", "GRAMMAR", "Use plural.", new[] { "These" }),
            new ProofingMatch(5, 3, ProofingIssueKind.Style, "are", "RULE_KEEP", "STYLE", "Consider rewrite.", Array.Empty<string>()));

        var registry = SpellDictionaryRegistry.CreateDefault();
        var spellEngine = new HunspellSpellEngine(registry);
        var rules = new ProofingRuleSet();
        rules.DisableRule("RULE_SKIP");
        var profile = new ProofingProfile("Test", spellEngine, registry, "en-US", grammarEngine: grammarEngine, rules: rules);
        var profiles = new ProofingProfileRegistry(profile);

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        using var proofing = new EditorProofingService(session, profiles);

        await WaitForDiagnosticsAsync(proofing, d => d.Any(item => item.Kind != ProofingIssueKind.Spelling));

        var diagnostics = proofing.GetParagraphDiagnostics(0);
        Assert.DoesNotContain(diagnostics, item => item.RuleId == "RULE_SKIP");
        Assert.Contains(diagnostics, item => item.RuleId == "RULE_KEEP");
    }

    private static async Task WaitForDiagnosticsAsync(
        IProofingService proofing,
        Func<IReadOnlyList<ProofingDiagnostic>, bool> predicate)
    {
        const int maxAttempts = 40;
        for (var i = 0; i < maxAttempts; i++)
        {
            var diagnostics = proofing.GetParagraphDiagnostics(0);
            if (predicate(diagnostics))
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(predicate(proofing.GetParagraphDiagnostics(0)));
    }

    private sealed class StubGrammarEngine : IGrammarEngine
    {
        private readonly IReadOnlyList<ProofingMatch> _matches;

        public string EngineId => "stub";

        public StubGrammarEngine(params ProofingMatch[] matches)
        {
            _matches = matches;
        }

        public Task<IReadOnlyList<ProofingMatch>> CheckAsync(
            string text,
            string language,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_matches);
        }
    }
}
