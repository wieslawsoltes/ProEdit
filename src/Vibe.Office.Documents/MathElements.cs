namespace Vibe.Office.Documents;

public abstract class MathElement
{
}

public sealed class MathRow : MathElement
{
    public List<MathElement> Elements { get; } = new List<MathElement>();
}

public sealed class MathRun : MathElement
{
    public string Text { get; set; } = string.Empty;
    public TextStyleProperties? Style { get; set; }
}

public sealed class MathFraction : MathElement
{
    public MathElement Numerator { get; set; }
    public MathElement Denominator { get; set; }
    public bool HasBar { get; set; } = true;

    public MathFraction(MathElement numerator, MathElement denominator)
    {
        Numerator = numerator ?? throw new ArgumentNullException(nameof(numerator));
        Denominator = denominator ?? throw new ArgumentNullException(nameof(denominator));
    }
}

public sealed class MathAccent : MathElement
{
    public MathElement Base { get; set; }
    public string AccentChar { get; set; } = "^";

    public MathAccent(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
    }
}

public sealed class MathDelimiter : MathElement
{
    public MathElement Body { get; set; }
    public string? BeginChar { get; set; }
    public string? EndChar { get; set; }
    public string? SeparatorChar { get; set; }

    public MathDelimiter(MathElement body)
    {
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }
}

public sealed class MathNary : MathElement
{
    public MathElement Base { get; set; }
    public MathElement? Subscript { get; set; }
    public MathElement? Superscript { get; set; }
    public string OperatorChar { get; set; } = "SUM";
    public bool HideSub { get; set; }
    public bool HideSup { get; set; }

    public MathNary(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
    }
}

public sealed class MathMatrix : MathElement
{
    public List<List<MathElement>> Rows { get; } = new List<List<MathElement>>();

    public MathMatrix()
    {
    }

    public MathMatrix(IEnumerable<IEnumerable<MathElement>> rows)
    {
        foreach (var row in rows)
        {
            Rows.Add(new List<MathElement>(row));
        }
    }
}

public sealed class MathScript : MathElement
{
    public MathElement Base { get; set; }
    public MathElement? Subscript { get; set; }
    public MathElement? Superscript { get; set; }

    public MathScript(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
    }
}

public sealed class MathRadical : MathElement
{
    public MathElement Radicand { get; set; }
    public MathElement? Degree { get; set; }

    public MathRadical(MathElement radicand)
    {
        Radicand = radicand ?? throw new ArgumentNullException(nameof(radicand));
    }
}
