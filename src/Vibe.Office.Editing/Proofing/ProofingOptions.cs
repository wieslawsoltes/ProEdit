using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibe.Office.Editing;

public sealed class ProofingOptions
{
    public int Version { get; set; } = 1;
    public bool UseSelectedEngines { get; set; } = true;
    public string? DefaultProfileId { get; set; }
    public Dictionary<string, string> LanguageProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ProofingProfileDefinition> Profiles { get; set; } = new();
    public List<string> PluginAssemblies { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> EngineSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ProofingOptions CreateDefault()
    {
        return new ProofingOptions
        {
            DefaultProfileId = "offline",
            UseSelectedEngines = true,
            LanguageProfiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en-US"] = "offline"
            },
            Profiles = new List<ProofingProfileDefinition>
            {
                new ProofingProfileDefinition
                {
                    Id = "offline",
                    Name = "Offline (Hunspell)",
                    SpellEngineId = "hunspell",
                    DefaultLanguage = "en-US"
                },
                new ProofingProfileDefinition
                {
                    Id = "languagetool",
                    Name = "LanguageTool (Remote)",
                    SpellEngineId = "hunspell",
                    GrammarEngineId = "languagetool",
                    StyleEngineId = "languagetool",
                    DefaultLanguage = "en-US"
                }
            }
        };
    }

    public ProofingOptions Clone()
    {
        var clone = new ProofingOptions
        {
            Version = Version,
            UseSelectedEngines = UseSelectedEngines,
            DefaultProfileId = DefaultProfileId,
            Profiles = Profiles.Select(static profile => profile.Clone()).ToList(),
            PluginAssemblies = PluginAssemblies.ToList()
        };

        foreach (var (language, profileId) in LanguageProfiles)
        {
            clone.LanguageProfiles[language] = profileId;
        }

        foreach (var (engineId, settings) in EngineSettings)
        {
            clone.EngineSettings[engineId] = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
        }

        return clone;
    }
}
