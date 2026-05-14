namespace ProEdit.WinUICompat.Documents;

public class Span : Inline
{
    public Span()
    {
        Inlines = new InlineCollection();
    }

    public InlineCollection Inlines { get; }
}
