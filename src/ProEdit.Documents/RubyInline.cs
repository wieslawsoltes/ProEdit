namespace ProEdit.Documents;

public sealed class RubyInline : Inline
{
    public string BaseText { get; }
    public string RubyText { get; }
    public TextStyleProperties? BaseStyle { get; set; }
    public string? BaseStyleId { get; set; }
    public TextStyleProperties? RubyStyle { get; set; }
    public string? RubyStyleId { get; set; }
    public float RubyScale { get; set; } = 0.5f;

    public RubyInline(string baseText, string rubyText)
    {
        BaseText = baseText ?? string.Empty;
        RubyText = rubyText ?? string.Empty;
    }
}
