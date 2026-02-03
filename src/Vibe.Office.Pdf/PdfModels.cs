namespace Vibe.Office.Pdf;

public sealed class PdfDocumentAst
{
    public List<PdfPageAst> Pages { get; } = new();
    public PdfMetadata Metadata { get; } = new();
    public List<PdfEmbeddedFont> EmbeddedFonts { get; } = new();
    public byte[]? OriginalBytes { get; set; }
    public string? SourcePath { get; set; }
    public string? ParserProviderId { get; set; }
}

public sealed class PdfMetadata
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public DateTimeOffset? Created { get; set; }
    public DateTimeOffset? Modified { get; set; }
}

public sealed class PdfPageAst
{
    public int Index { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Rotation { get; set; }
    public string? ExtractedText { get; set; }
    public List<PdfTextRun> TextRuns { get; } = new();
    public List<PdfTextGlyph> Glyphs { get; } = new();
    public List<PdfImageObject> Images { get; } = new();
    public List<PdfPathObject> Paths { get; } = new();
    public List<PdfContentItem> ContentOrder { get; } = new();
}

public sealed record PdfTextRun(
    string Text,
    PdfRect Bounds,
    PdfFontInfo? Font,
    double FontSize,
    double BaselineY,
    double AverageLetterGap,
    PdfColor? Color = null,
    int Sequence = -1,
    int Index = -1);

public sealed record PdfTextGlyph(
    string Text,
    PdfRect Bounds,
    PdfFontInfo? Font,
    double FontSize,
    double BaselineX,
    double BaselineY,
    double Advance,
    PdfTextOrientation Orientation,
    PdfColor? Color = null,
    int Sequence = -1,
    int Index = -1);

public sealed record PdfImageObject(
    PdfRect Bounds,
    byte[] Data,
    string? MimeType,
    bool IsBackground = false);

public sealed class PdfPathObject
{
    public PdfRect Bounds { get; set; }
    public PdfPathStyle Style { get; set; } = new();
    public List<PdfPathSegment> Segments { get; } = new();
}

public readonly record struct PdfRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public sealed record PdfFontInfo(
    string Name,
    bool IsBold,
    bool IsItalic);

public enum PdfTextOrientation
{
    Other = 0,
    Horizontal = 1,
    Rotate180 = 2,
    Rotate90 = 3,
    Rotate270 = 4
}

public sealed record PdfEmbeddedFont(
    string FamilyName,
    byte[] Data,
    string? ContentType,
    bool IsBold,
    bool IsItalic,
    string? PostScriptName);

public readonly record struct PdfColor(byte R, byte G, byte B, byte A = 255);

public enum PdfFillRule
{
    NonZero,
    EvenOdd
}

public enum PdfLineCap
{
    Butt,
    Round,
    Square
}

public enum PdfLineJoin
{
    Miter,
    Round,
    Bevel
}

public sealed class PdfPathStyle
{
    public bool IsFilled { get; set; }
    public bool IsStroked { get; set; }
    public PdfColor? FillColor { get; set; }
    public PdfColor? StrokeColor { get; set; }
    public double LineWidth { get; set; }
    public PdfLineCap LineCap { get; set; } = PdfLineCap.Butt;
    public PdfLineJoin LineJoin { get; set; } = PdfLineJoin.Miter;
    public double? MiterLimit { get; set; }
    public IReadOnlyList<double>? DashArray { get; set; }
    public double DashPhase { get; set; }
    public PdfFillRule FillRule { get; set; } = PdfFillRule.NonZero;
}

public enum PdfContentItemKind
{
    TextRun,
    Image,
    Path
}

public sealed record PdfContentItem(
    PdfContentItemKind Kind,
    int Index);

public enum PdfPathSegmentKind
{
    MoveTo,
    LineTo,
    CubicTo,
    Close
}

public readonly record struct PdfPathSegment(
    PdfPathSegmentKind Kind,
    double X1,
    double Y1,
    double X2,
    double Y2,
    double X3,
    double Y3)
{
    public static PdfPathSegment MoveTo(double x, double y) => new(PdfPathSegmentKind.MoveTo, x, y, 0, 0, 0, 0);
    public static PdfPathSegment LineTo(double x, double y) => new(PdfPathSegmentKind.LineTo, x, y, 0, 0, 0, 0);
    public static PdfPathSegment CubicTo(double c1x, double c1y, double c2x, double c2y, double x, double y)
        => new(PdfPathSegmentKind.CubicTo, c1x, c1y, c2x, c2y, x, y);
    public static PdfPathSegment Close() => new(PdfPathSegmentKind.Close, 0, 0, 0, 0, 0, 0);
}
