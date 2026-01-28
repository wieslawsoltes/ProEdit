namespace Vibe.Office.Documents;

public enum NoteNumberFormat
{
    Decimal,
    UpperRoman,
    LowerRoman,
    UpperLetter,
    LowerLetter,
    Symbol
}

public enum NoteNumberRestart
{
    Continuous,
    EachSection,
    EachPage
}

public enum FootnotePosition
{
    PageBottom,
    BeneathText
}

public enum EndnotePosition
{
    EndOfDocument,
    EndOfSection
}

public sealed class FootnoteSettings
{
    public int? Start { get; set; }
    public NoteNumberFormat? Format { get; set; }
    public NoteNumberRestart? Restart { get; set; }
    public FootnotePosition? Position { get; set; }

    public bool HasValues =>
        Start.HasValue
        || Format.HasValue
        || Restart.HasValue
        || Position.HasValue;

    public FootnoteSettings Clone()
    {
        return new FootnoteSettings
        {
            Start = Start,
            Format = Format,
            Restart = Restart,
            Position = Position
        };
    }
}

public sealed class EndnoteSettings
{
    public int? Start { get; set; }
    public NoteNumberFormat? Format { get; set; }
    public NoteNumberRestart? Restart { get; set; }
    public EndnotePosition? Position { get; set; }

    public bool HasValues =>
        Start.HasValue
        || Format.HasValue
        || Restart.HasValue
        || Position.HasValue;

    public EndnoteSettings Clone()
    {
        return new EndnoteSettings
        {
            Start = Start,
            Format = Format,
            Restart = Restart,
            Position = Position
        };
    }
}
