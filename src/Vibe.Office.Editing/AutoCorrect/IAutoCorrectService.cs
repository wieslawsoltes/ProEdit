namespace Vibe.Office.Editing;

public interface IAutoCorrectService
{
    AutoCorrectOptions Options { get; }
    IReadOnlyList<AutoCorrectRule> Rules { get; }
    bool TryGetReplacement(IEditorMutableSession session, ReadOnlySpan<char> insertedText, out AutoCorrectReplacement replacement);
}
