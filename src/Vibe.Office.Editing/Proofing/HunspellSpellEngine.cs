namespace Vibe.Office.Editing;

public sealed class HunspellSpellEngine : ISpellEngine
{
    private readonly ISpellDictionaryRegistry _registry;

    public HunspellSpellEngine(ISpellDictionaryRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public bool Check(ReadOnlySpan<char> word, string language)
    {
        if (word.IsEmpty)
        {
            return true;
        }

        if (!_registry.TryGetDictionary(language, out var dictionary))
        {
            return true;
        }

        return dictionary.Check(word);
    }

    public IReadOnlyList<string> Suggest(ReadOnlySpan<char> word, string language, int maxSuggestions = 5)
    {
        if (word.IsEmpty)
        {
            return Array.Empty<string>();
        }

        if (!_registry.TryGetDictionary(language, out var dictionary))
        {
            return Array.Empty<string>();
        }

        return dictionary.Suggest(word, maxSuggestions);
    }
}
