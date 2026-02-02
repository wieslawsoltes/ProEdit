namespace Vibe.Office.Editing;

internal sealed class NullSpellEngine : ISpellEngine
{
    public bool Check(ReadOnlySpan<char> word, string language)
    {
        return true;
    }

    public IReadOnlyList<string> Suggest(ReadOnlySpan<char> word, string language, int maxSuggestions = 5)
    {
        return Array.Empty<string>();
    }
}
