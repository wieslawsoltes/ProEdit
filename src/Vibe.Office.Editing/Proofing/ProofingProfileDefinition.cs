namespace Vibe.Office.Editing;

public sealed class ProofingProfileDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SpellEngineId { get; set; } = string.Empty;
    public string? GrammarEngineId { get; set; }
    public string? StyleEngineId { get; set; }
    public string? DefaultLanguage { get; set; }

    public ProofingProfileDefinition Clone()
    {
        return new ProofingProfileDefinition
        {
            Id = Id,
            Name = Name,
            SpellEngineId = SpellEngineId,
            GrammarEngineId = GrammarEngineId,
            StyleEngineId = StyleEngineId,
            DefaultLanguage = DefaultLanguage
        };
    }
}
