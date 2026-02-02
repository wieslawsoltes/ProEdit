namespace Vibe.Office.Editing;

public interface IProofingProfileRegistry
{
    IProofingProfile DefaultProfile { get; }
    bool HasGrammarOrStyle { get; }
    bool HasProofingSpelling { get; }
    IProofingProfile ResolveProfile(string? language);
    bool TryGetProfile(string language, out IProofingProfile profile);
    void RegisterProfile(IProofingProfile profile, params string[] languages);
}
