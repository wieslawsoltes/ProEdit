namespace Vibe.Office.Editing;

public sealed class ProofingProfile : IProofingProfile
{
    public string Name { get; }
    public string? DefaultLanguage { get; }
    public ISpellEngine SpellEngine { get; }
    public ISpellDictionaryRegistry DictionaryRegistry { get; }
    public IGrammarEngine? GrammarEngine { get; }
    public IStyleEngine? StyleEngine { get; }
    public ProofingRuleSet Rules { get; }

    public ProofingProfile(
        string name,
        ISpellEngine spellEngine,
        ISpellDictionaryRegistry dictionaryRegistry,
        string? defaultLanguage = null,
        IGrammarEngine? grammarEngine = null,
        IStyleEngine? styleEngine = null,
        ProofingRuleSet? rules = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        }

        Name = name.Trim();
        SpellEngine = spellEngine ?? throw new ArgumentNullException(nameof(spellEngine));
        DictionaryRegistry = dictionaryRegistry ?? throw new ArgumentNullException(nameof(dictionaryRegistry));
        DefaultLanguage = string.IsNullOrWhiteSpace(defaultLanguage) ? null : defaultLanguage.Trim();
        GrammarEngine = grammarEngine;
        StyleEngine = styleEngine;
        Rules = rules ?? new ProofingRuleSet();
    }
}
