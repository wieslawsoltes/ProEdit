namespace Vibe.Office.Documents;

public enum ShapeGuideOp
{
    MulDiv,
    AddSub,
    AddDiv,
    IfElse,
    Val,
    Abs,
    Sqrt,
    Max,
    Min,
    At2,
    Sin,
    Cos,
    Tan,
    Cat2,
    Sat2,
    Pin,
    Mod
}

public readonly struct ShapeGuideFormula
{
    public ShapeGuideOp Op { get; }
    public string? Arg1 { get; }
    public string? Arg2 { get; }
    public string? Arg3 { get; }

    private ShapeGuideFormula(ShapeGuideOp op, string? arg1, string? arg2, string? arg3)
    {
        Op = op;
        Arg1 = arg1;
        Arg2 = arg2;
        Arg3 = arg3;
    }

    public static ShapeGuideFormula Parse(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            return new ShapeGuideFormula(ShapeGuideOp.Val, "0", null, null);
        }

        var tokens = SplitTokens(formula);
        if (tokens.Length == 0)
        {
            return new ShapeGuideFormula(ShapeGuideOp.Val, "0", null, null);
        }

        var opToken = tokens[0];
        var op = opToken switch
        {
            "*/" => ShapeGuideOp.MulDiv,
            "+-" => ShapeGuideOp.AddSub,
            "+/" => ShapeGuideOp.AddDiv,
            "?:" => ShapeGuideOp.IfElse,
            _ => ParseOp(opToken)
        };

        var arg1 = tokens.Length > 1 ? tokens[1] : null;
        var arg2 = tokens.Length > 2 ? tokens[2] : null;
        var arg3 = tokens.Length > 3 ? tokens[3] : null;
        return new ShapeGuideFormula(op, arg1, arg2, arg3);
    }

    public double Evaluate(ShapeGeometryContext context)
    {
        var x = Arg1 is null ? 0d : context.GetValue(Arg1);
        var y = Arg2 is null ? 0d : context.GetValue(Arg2);
        var z = Arg3 is null ? 0d : context.GetValue(Arg3);

        return Op switch
        {
            ShapeGuideOp.Abs => Math.Abs(x),
            ShapeGuideOp.AddDiv => z == 0d ? 0d : (x + y) / z,
            ShapeGuideOp.AddSub => (x + y) - z,
            ShapeGuideOp.At2 => Math.Atan2(y, x) * (180d / Math.PI) * ShapeGeometryContext.OoxmlDegreeToDegrees,
            ShapeGuideOp.Cos => x * Math.Cos(DegreesToRadians(y / ShapeGeometryContext.OoxmlDegreeToDegrees)),
            ShapeGuideOp.Cat2 => x * Math.Cos(Math.Atan2(z, y)),
            ShapeGuideOp.IfElse => x > 0d ? y : z,
            ShapeGuideOp.Val => x,
            ShapeGuideOp.Max => Math.Max(x, y),
            ShapeGuideOp.Min => Math.Min(x, y),
            ShapeGuideOp.Mod => Math.Sqrt(x * x + y * y + z * z),
            ShapeGuideOp.MulDiv => z == 0d ? 0d : (x * y) / z,
            ShapeGuideOp.Pin => Math.Max(x, Math.Min(y, z)),
            ShapeGuideOp.Sat2 => x * Math.Sin(Math.Atan2(z, y)),
            ShapeGuideOp.Sin => x * Math.Sin(DegreesToRadians(y / ShapeGeometryContext.OoxmlDegreeToDegrees)),
            ShapeGuideOp.Sqrt => Math.Sqrt(x),
            ShapeGuideOp.Tan => x * Math.Tan(DegreesToRadians(y / ShapeGeometryContext.OoxmlDegreeToDegrees)),
            _ => 0d
        };
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }

    private static ShapeGuideOp ParseOp(string token)
    {
        return token switch
        {
            "val" => ShapeGuideOp.Val,
            "abs" => ShapeGuideOp.Abs,
            "sqrt" => ShapeGuideOp.Sqrt,
            "max" => ShapeGuideOp.Max,
            "min" => ShapeGuideOp.Min,
            "at2" => ShapeGuideOp.At2,
            "sin" => ShapeGuideOp.Sin,
            "cos" => ShapeGuideOp.Cos,
            "tan" => ShapeGuideOp.Tan,
            "cat2" => ShapeGuideOp.Cat2,
            "sat2" => ShapeGuideOp.Sat2,
            "pin" => ShapeGuideOp.Pin,
            "mod" => ShapeGuideOp.Mod,
            _ => ShapeGuideOp.Val
        };
    }

    private static string[] SplitTokens(string formula)
    {
        var count = 0;
        var span = formula.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            if (i >= span.Length)
            {
                break;
            }

            count++;
            while (i < span.Length && !char.IsWhiteSpace(span[i]))
            {
                i++;
            }
        }

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var tokens = new string[count];
        i = 0;
        var index = 0;
        while (i < span.Length && index < count)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            if (i >= span.Length)
            {
                break;
            }

            var start = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            tokens[index++] = span.Slice(start, i - start).ToString();
        }

        return tokens;
    }
}

public sealed class ShapeGuide
{
    public string Name { get; }
    public string FormulaText { get; }
    public ShapeGuideFormula Formula { get; }

    public ShapeGuide(string name, string? formula)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        FormulaText = formula ?? string.Empty;
        Formula = ShapeGuideFormula.Parse(formula);
    }

    public double Evaluate(ShapeGeometryContext context)
    {
        return Formula.Evaluate(context);
    }

    public ShapeGuide Clone() => new ShapeGuide(Name, FormulaText);
}
