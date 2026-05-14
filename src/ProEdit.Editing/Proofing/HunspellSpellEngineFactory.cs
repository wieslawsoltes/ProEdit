namespace ProEdit.Editing;

public sealed class HunspellSpellEngineFactory : IProofingEngineFactory
{
    public string EngineId => "hunspell";
    public string DisplayName => "Hunspell (Offline)";
    public ProofingEngineKind Kind => ProofingEngineKind.Spell;

    public object? Create(ProofingEngineContext context)
    {
        return new HunspellSpellEngine(context.DictionaryRegistry);
    }
}
