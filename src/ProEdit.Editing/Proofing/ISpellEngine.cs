namespace ProEdit.Editing;

public interface ISpellEngine
{
    bool Check(ReadOnlySpan<char> word, string language);
    IReadOnlyList<string> Suggest(ReadOnlySpan<char> word, string language, int maxSuggestions = 5);
}
