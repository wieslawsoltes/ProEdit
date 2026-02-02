namespace Vibe.Office.Editing;

public sealed class ProofingProfileRegistry : IProofingProfileRegistry
{
    private readonly Dictionary<string, IProofingProfile> _profilesByLanguage = new(StringComparer.OrdinalIgnoreCase);

    public IProofingProfile DefaultProfile { get; }
    public bool HasGrammarOrStyle { get; private set; }

    public ProofingProfileRegistry(IProofingProfile defaultProfile)
    {
        DefaultProfile = defaultProfile ?? throw new ArgumentNullException(nameof(defaultProfile));
        HasGrammarOrStyle = HasGrammarOrStyleFor(defaultProfile);
    }

    public void RegisterProfile(IProofingProfile profile, params string[] languages)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (languages is null || languages.Length == 0)
        {
            return;
        }

        foreach (var language in languages)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            _profilesByLanguage[language.Trim()] = profile;
        }

        if (!HasGrammarOrStyle)
        {
            HasGrammarOrStyle = HasGrammarOrStyleFor(profile);
        }
    }

    public IProofingProfile ResolveProfile(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DefaultProfile;
        }

        return _profilesByLanguage.TryGetValue(language.Trim(), out var profile)
            ? profile
            : DefaultProfile;
    }

    public bool TryGetProfile(string language, out IProofingProfile profile)
    {
        profile = DefaultProfile;
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return _profilesByLanguage.TryGetValue(language.Trim(), out profile!);
    }

    private static bool HasGrammarOrStyleFor(IProofingProfile profile)
    {
        return profile.GrammarEngine is not null || profile.StyleEngine is not null;
    }
}
