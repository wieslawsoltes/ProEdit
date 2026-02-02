using System;
using System.Collections.Generic;

namespace Vibe.Office.Editing;

public sealed class ProofingEngineContext
{
    public ISpellDictionaryRegistry DictionaryRegistry { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }
    public IServiceProvider? Services { get; }

    public ProofingEngineContext(
        ISpellDictionaryRegistry dictionaryRegistry,
        IReadOnlyDictionary<string, string>? settings = null,
        IServiceProvider? services = null)
    {
        DictionaryRegistry = dictionaryRegistry ?? throw new ArgumentNullException(nameof(dictionaryRegistry));
        Settings = settings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Services = services;
    }
}
