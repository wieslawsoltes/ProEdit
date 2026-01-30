namespace Vibe.Office.Documents;

public abstract class MathElement
{
}

public enum MathBarPosition
{
    Top,
    Bottom
}

public enum MathGroupCharacterPosition
{
    Top,
    Bottom
}

public enum MathLimitPosition
{
    Lower,
    Upper
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

public sealed class MathFunction : MathElement
{
    public MathElement Name { get; set; }
    public MathElement Argument { get; set; }

    public MathFunction(MathElement name, MathElement argument)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Argument = argument ?? throw new ArgumentNullException(nameof(argument));
    }
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

public sealed class MathBar : MathElement
{
    public MathElement Base { get; set; }
    public MathBarPosition Position { get; set; } = MathBarPosition.Top;

    public MathBar(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
    }
}

public sealed class MathBoxElement : MathElement
{
    public MathElement Base { get; set; }

    public MathBoxElement(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
    }
}

public sealed class MathBorderBox : MathElement
{
    public MathElement Base { get; set; }
    public bool HideTop { get; set; }
    public bool HideBottom { get; set; }
    public bool HideLeft { get; set; }
    public bool HideRight { get; set; }
    public bool StrikeHorizontal { get; set; }
    public bool StrikeVertical { get; set; }
    public bool StrikeDiagonalUp { get; set; }
    public bool StrikeDiagonalDown { get; set; }

    public MathBorderBox(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
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

public sealed class MathLimit : MathElement
{
    public MathElement Base { get; set; }
    public MathElement Limit { get; set; }
    public MathLimitPosition Position { get; set; } = MathLimitPosition.Lower;

    public MathLimit(MathElement @base, MathElement limit)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
        Limit = limit ?? throw new ArgumentNullException(nameof(limit));
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

public sealed class MathPreScript : MathElement
{
    public MathElement Base { get; set; }
    public MathElement? Subscript { get; set; }
    public MathElement? Superscript { get; set; }

    public MathPreScript(MathElement @base)
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

public sealed class MathGroupCharacter : MathElement
{
    public MathElement Base { get; set; }
    public string Character { get; set; } = "^";
    public MathGroupCharacterPosition Position { get; set; } = MathGroupCharacterPosition.Top;

    public MathGroupCharacter(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
    }
}

public sealed class MathPhantom : MathElement
{
    public MathElement Base { get; set; }
    public bool Show { get; set; }
    public bool ZeroWidth { get; set; }
    public bool ZeroAscent { get; set; }
    public bool ZeroDescent { get; set; }
    public bool Transparent { get; set; }

    public MathPhantom(MathElement @base)
    {
        Base = @base ?? throw new ArgumentNullException(nameof(@base));
    }
}
