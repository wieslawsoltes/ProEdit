namespace ProEdit.Editing;

public readonly record struct AutoCorrectReplacement(
    int ParagraphIndex,
    int StartOffset,
    int Length,
    string Replacement);
