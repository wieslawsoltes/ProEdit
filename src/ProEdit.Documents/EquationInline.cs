namespace ProEdit.Documents;

public sealed class EquationInline : Inline
{
    public MathElement Root { get; set; }
    public TextStyleProperties? Style { get; set; }
    public string? StyleId { get; set; }

    public EquationInline(MathElement root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }
}
