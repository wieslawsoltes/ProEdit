namespace ProEdit.WinUICompat.Documents;

public sealed class Run : Inline
{
    public Run()
    {
        Text = string.Empty;
    }

    public Run(string text)
    {
        Text = text ?? string.Empty;
    }

    public string Text { get; set; }
}
