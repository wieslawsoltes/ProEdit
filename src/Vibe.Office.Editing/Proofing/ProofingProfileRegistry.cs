namespace Vibe.Office.Editing;

public sealed class ProofingProfileRegistry : IProofingProfileRegistry
{
    private readonly Dictionary<string, IProofingProfile> _profilesByLanguage = new(StringComparer.OrdinalIgnoreCase);

    public IProofingProfile DefaultProfile { get; }
    public bool HasGrammarOrStyle { get; private set; }
    public bool HasProofingSpelling { get; private set; }

    public ProofingProfileRegistry(IProofingProfile defaultProfile)
    {
        DefaultProfile = defaultProfile ?? throw new ArgumentNullException(nameof(defaultProfile));
        HasGrammarOrStyle = HasGrammarOrStyleFor(defaultProfile);
        HasProofingSpelling = HasProofingSpellingFor(defaultProfile);
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

        if (!HasProofingSpelling)
        {
            HasProofingSpelling = HasProofingSpellingFor(profile);
        }
    }

    public IProofingProfile ResolveProfile(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DefaultProfile;
        }

        return TryResolveProfile(language, out var profile)
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

        return TryResolveProfile(language, out profile);
    }

    private bool TryResolveProfile(string language, out IProofingProfile profile)
    {
        profile = DefaultProfile;
        var normalized = NormalizeLanguageTag(language);
        if (TryFindProfileByNormalizedLanguage(normalized, out profile))
        {
            return true;
        }

        var alternate = normalized.Contains('-') ? normalized.Replace('-', '_') : normalized;
        if (!string.Equals(alternate, normalized, StringComparison.OrdinalIgnoreCase)
            && TryFindProfileByNormalizedLanguage(alternate, out profile))
        {
            return true;
        }

        var baseLanguage = GetBaseLanguage(normalized);
        if (string.IsNullOrEmpty(baseLanguage))
        {
            return false;
        }

        if (TryFindProfileByNormalizedLanguage(baseLanguage, out profile))
        {
            return true;
        }

        foreach (var key in _profilesByLanguage.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedKey = NormalizeLanguageTag(key);
            if (normalizedKey.StartsWith(baseLanguage + "-", StringComparison.OrdinalIgnoreCase)
                || normalizedKey.StartsWith(baseLanguage + "_", StringComparison.OrdinalIgnoreCase))
            {
                profile = _profilesByLanguage[key];
                return true;
            }
        }

        return false;
    }

    private bool TryFindProfileByNormalizedLanguage(string normalized, out IProofingProfile profile)
    {
        foreach (var (key, value) in _profilesByLanguage)
        {
            if (string.Equals(NormalizeLanguageTag(key), normalized, StringComparison.OrdinalIgnoreCase))
            {
                profile = value;
                return true;
            }
        }

        profile = DefaultProfile;
        return false;
    }

    private static string NormalizeLanguageTag(string language)
    {
        return language.Trim().Replace('_', '-');
    }

    private static string GetBaseLanguage(string language)
    {
        var separatorIndex = language.IndexOf('-');
        return separatorIndex > 0 ? language.Substring(0, separatorIndex) : language;
    }

    private static bool HasGrammarOrStyleFor(IProofingProfile profile)
    {
        return profile.GrammarEngine is not null || profile.StyleEngine is not null;
    }

    private static bool HasProofingSpellingFor(IProofingProfile profile)
    {
        return profile.SpellEngine is IProofingEngine;
    }
}
