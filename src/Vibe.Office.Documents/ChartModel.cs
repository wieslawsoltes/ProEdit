namespace Vibe.Office.Documents;

public sealed class ChartModel
{
    public ChartType Type { get; set; } = ChartType.Unknown;
    public string? Title { get; set; }
    public List<ChartSeries> Series { get; } = new List<ChartSeries>();
}

public sealed class ChartSeries
{
    public string? Name { get; set; }
    public List<ChartPoint> Points { get; } = new List<ChartPoint>();
}

public sealed class ChartPoint
{
    public string? Category { get; set; }
    public double Value { get; set; }
}

public enum ChartType
{
    Unknown,
    Bar,
    Line,
    Pie,
    Scatter,
    Area
}
