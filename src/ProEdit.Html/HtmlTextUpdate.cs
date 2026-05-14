namespace ProEdit.Html;

public readonly record struct HtmlTextUpdate(string Text, IReadOnlyList<HtmlTextEdit> Edits);
