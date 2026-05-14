namespace ProEdit.WinUICompat.Documents;

public sealed class Paragraph : Block
{
    public Paragraph()
    {
        Inlines = new InlineCollection();
    }

    public Paragraph(string text)
        : this()
    {
        Inlines.Add(new Run(text));
    }

    public InlineCollection Inlines { get; }

    public bool? KeepTogether { get; set; }

    public bool? KeepWithNext { get; set; }

    public int? MinOrphanLines { get; set; }

    public int? MinWidowLines { get; set; }

    public double? TextIndent { get; set; }

    public string? TextDecorations { get; set; }
}
