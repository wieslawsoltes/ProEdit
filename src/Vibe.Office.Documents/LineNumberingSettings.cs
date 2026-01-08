namespace Vibe.Office.Documents;

public sealed class LineNumberingSettings
{
    public int? Start { get; set; }
    public int? CountBy { get; set; }
    public float? Distance { get; set; }
    public LineNumberRestart Restart { get; set; } = LineNumberRestart.Continuous;

    public bool HasValues =>
        Start.HasValue
        || CountBy.HasValue
        || Distance.HasValue
        || Restart != LineNumberRestart.Continuous;

    public LineNumberingSettings Clone()
    {
        return new LineNumberingSettings
        {
            Start = Start,
            CountBy = CountBy,
            Distance = Distance,
            Restart = Restart
        };
    }
}

public enum LineNumberRestart
{
    Continuous,
    NewPage,
    NewSection
}
