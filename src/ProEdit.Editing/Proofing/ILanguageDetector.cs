namespace ProEdit.Editing;

public interface ILanguageDetector
{
    string? DetectLanguage(ReadOnlySpan<char> text);
}
