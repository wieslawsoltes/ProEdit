namespace Vibe.Office.Editing;

public interface ILanguageDetector
{
    string? DetectLanguage(ReadOnlySpan<char> text);
}
