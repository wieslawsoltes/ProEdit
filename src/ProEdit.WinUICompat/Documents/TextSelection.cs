namespace ProEdit.WinUICompat.Documents;

public sealed class TextSelection : TextRange
{
    public TextSelection(TextPointer start, TextPointer end)
        : base(start, end)
    {
    }
}
