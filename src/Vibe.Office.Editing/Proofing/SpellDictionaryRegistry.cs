namespace Vibe.Office.Editing;

public sealed class SpellDictionaryRegistry : ISpellDictionaryRegistry
{
    private readonly Dictionary<string, SpellDictionary> _dictionaries = new(StringComparer.OrdinalIgnoreCase);

    public static SpellDictionaryRegistry CreateDefault()
    {
        var registry = new SpellDictionaryRegistry();
        if (TryLoadEmbeddedDictionary("en-US", out var dictionary))
        {
            registry.Register("en-US", dictionary);
        }

        return registry;
    }

    public bool TryGetDictionary(string language, out SpellDictionary dictionary)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            dictionary = null!;
            return false;
        }

        return _dictionaries.TryGetValue(language.Trim(), out dictionary!);
    }

    public void Register(string language, SpellDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        _dictionaries[language.Trim()] = dictionary;
    }

    public bool AddUserWord(string language, string word)
    {
        if (!TryGetDictionary(language, out var dictionary))
        {
            return false;
        }

        dictionary.AddUserWord(word);
        return true;
    }

    public bool IgnoreWord(string language, string word)
    {
        if (!TryGetDictionary(language, out var dictionary))
        {
            return false;
        }

        dictionary.IgnoreWord(word);
        return true;
    }

    private static bool TryLoadEmbeddedDictionary(string language, out SpellDictionary dictionary)
    {
        dictionary = null!;
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var assembly = typeof(SpellDictionaryRegistry).Assembly;
        var root = typeof(SpellDictionaryRegistry).Namespace ?? "Vibe.Office.Editing";
        var affName = $"{root}.Proofing.Resources.{language}.aff";
        var dicName = $"{root}.Proofing.Resources.{language}.dic";

        using var affStream = assembly.GetManifestResourceStream(affName);
        using var dicStream = assembly.GetManifestResourceStream(dicName);
        if (affStream is null || dicStream is null)
        {
            return false;
        }

        dictionary = SpellDictionary.LoadHunspell(affStream, dicStream);
        return true;
    }
}
