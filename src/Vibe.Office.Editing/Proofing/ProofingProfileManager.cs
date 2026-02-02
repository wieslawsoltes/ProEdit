using System;
using System.Linq;

namespace Vibe.Office.Editing;

public sealed class ProofingProfileManager : IProofingProfileManager
{
    private readonly ProofingEngineRegistry _engineRegistry;
    private readonly ISpellDictionaryRegistry _dictionaryRegistry;
    private readonly Dictionary<string, IProofingProfile> _profilesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IProofingProfile> _profilesByLanguage = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProofingProfileDefinition> _profileDefinitions = new();
    private ProofingOptions _options;

    public ProofingOptions Options => _options;
    public IReadOnlyList<ProofingProfileDefinition> Profiles => _profileDefinitions;
    public IReadOnlyList<ProofingEngineDescriptor> Engines => _engineRegistry.Engines;
    public IProofingProfile DefaultProfile { get; private set; }
    public bool HasGrammarOrStyle { get; private set; }

    public event EventHandler? OptionsChanged;

    public ProofingProfileManager(
        ProofingEngineRegistry engineRegistry,
        ISpellDictionaryRegistry dictionaryRegistry,
        ProofingOptions options)
    {
        _engineRegistry = engineRegistry ?? throw new ArgumentNullException(nameof(engineRegistry));
        _dictionaryRegistry = dictionaryRegistry ?? throw new ArgumentNullException(nameof(dictionaryRegistry));
        _options = options ?? ProofingOptions.CreateDefault();
        DefaultProfile = new ProofingProfile("Default", new HunspellSpellEngine(dictionaryRegistry), dictionaryRegistry, "en-US");
        RebuildProfiles();
    }

    public void UpdateOptions(ProofingOptions options)
    {
        _options = options ?? ProofingOptions.CreateDefault();
        RebuildProfiles();
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IProofingProfile ResolveProfile(string? language)
    {
        if (!_options.UseSelectedEngines)
        {
            return DefaultProfile;
        }

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

        if (!_options.UseSelectedEngines)
        {
            return false;
        }

        return _profilesByLanguage.TryGetValue(language.Trim(), out profile!);
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
    }

    private void RebuildProfiles()
    {
        _engineRegistry.UpdateEngineSettings(_options.EngineSettings);
        _engineRegistry.ReloadPlugins(_options.PluginAssemblies);

        _profilesById.Clear();
        _profilesByLanguage.Clear();
        _profileDefinitions.Clear();

        var definitions = BuildDefinitions(_options);
        foreach (var definition in definitions)
        {
            _profileDefinitions.Add(definition);
            if (string.IsNullOrWhiteSpace(definition.SpellEngineId))
            {
                continue;
            }

            if (!_engineRegistry.TryCreateSpellEngine(definition.SpellEngineId, out var spellEngine))
            {
                continue;
            }

            IGrammarEngine? grammarEngine = null;
            if (!string.IsNullOrWhiteSpace(definition.GrammarEngineId))
            {
                _engineRegistry.TryCreateGrammarEngine(definition.GrammarEngineId!, out grammarEngine);
            }

            IStyleEngine? styleEngine = null;
            if (!string.IsNullOrWhiteSpace(definition.StyleEngineId))
            {
                _engineRegistry.TryCreateStyleEngine(definition.StyleEngineId!, out styleEngine);
            }

            var profile = new ProofingProfile(
                definition.Name,
                spellEngine,
                _dictionaryRegistry,
                string.IsNullOrWhiteSpace(definition.DefaultLanguage) ? null : definition.DefaultLanguage,
                grammarEngine,
                styleEngine);
            _profilesById[definition.Id] = profile;
        }

        DefaultProfile = ResolveDefaultProfile();
        HasGrammarOrStyle = _options.UseSelectedEngines
            ? _profilesById.Values.Any(static profile => profile.GrammarEngine is not null || profile.StyleEngine is not null)
            : DefaultProfile.GrammarEngine is not null || DefaultProfile.StyleEngine is not null;

        foreach (var (language, profileId) in _options.LanguageProfiles)
        {
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(profileId))
            {
                continue;
            }

            if (_profilesById.TryGetValue(profileId, out var profile))
            {
                _profilesByLanguage[language.Trim()] = profile;
            }
        }
    }

    private IProofingProfile ResolveDefaultProfile()
    {
        if (!string.IsNullOrWhiteSpace(_options.DefaultProfileId)
            && _profilesById.TryGetValue(_options.DefaultProfileId.Trim(), out var profile))
        {
            return profile;
        }

        if (_profilesById.Count > 0)
        {
            return _profilesById.Values.First();
        }

        return DefaultProfile;
    }

    private static IEnumerable<ProofingProfileDefinition> BuildDefinitions(ProofingOptions options)
    {
        var definitions = new Dictionary<string, ProofingProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var builtIn in ProofingOptions.CreateDefault().Profiles)
        {
            if (!string.IsNullOrWhiteSpace(builtIn.Id))
            {
                definitions[builtIn.Id] = builtIn.Clone();
            }
        }

        foreach (var custom in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(custom.Id))
            {
                continue;
            }

            definitions[custom.Id.Trim()] = custom.Clone();
        }

        return definitions.Values.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase);
    }
}
