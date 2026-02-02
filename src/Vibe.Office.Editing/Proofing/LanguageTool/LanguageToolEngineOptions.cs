namespace Vibe.Office.Editing;

public sealed class LanguageToolEngineOptions
{
    public bool IncludeSpelling { get; set; }
    public bool IncludeGrammar { get; set; } = true;
    public bool IncludeStyle { get; set; } = true;
    public int MaxSuggestions { get; set; } = 5;
}
