namespace Vibe.Office.Editing;

public interface IProofingProfile
{
    string Name { get; }
    string? DefaultLanguage { get; }
    ISpellEngine SpellEngine { get; }
    ISpellDictionaryRegistry DictionaryRegistry { get; }
    IGrammarEngine? GrammarEngine { get; }
    IStyleEngine? StyleEngine { get; }
    ProofingRuleSet Rules { get; }
}
