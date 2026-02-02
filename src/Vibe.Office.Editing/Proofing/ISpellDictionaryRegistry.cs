namespace Vibe.Office.Editing;

public interface ISpellDictionaryRegistry
{
    bool TryGetDictionary(string language, out SpellDictionary dictionary);
    void Register(string language, SpellDictionary dictionary);
    bool AddUserWord(string language, string word);
    bool IgnoreWord(string language, string word);
}
