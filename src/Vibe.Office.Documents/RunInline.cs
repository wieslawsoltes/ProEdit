namespace Vibe.Office.Documents;

public sealed class RunInline : Inline
{
    public TextBuffer Text { get; }
    public TextStyleProperties? Style { get; set; }
    public string? StyleId { get; set; }

    public RunInline(string text, TextStyleProperties? style = null)
    {
        Text = new TextBuffer(text ?? string.Empty);
        Style = style;
    }

    public RunInline(TextBuffer text, TextStyleProperties? style = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Style = style;
    }

    public int Length => Text.Length;

    public string GetText() => Text.GetText();
}
