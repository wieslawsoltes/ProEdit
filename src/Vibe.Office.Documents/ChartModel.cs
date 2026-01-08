using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class ChartModel
{
    public ChartType Type { get; set; } = ChartType.Unknown;
    public ChartStacking Stacking { get; set; } = ChartStacking.None;
    public ChartBarDirection BarDirection { get; set; } = ChartBarDirection.Column;
    public ChartRadarStyle RadarStyle { get; set; } = ChartRadarStyle.Standard;
    public float DoughnutHoleSize { get; set; } = 0.5f;
    public string? Title { get; set; }
    public ChartStyle? ChartAreaStyle { get; set; }
    public ChartStyle? PlotAreaStyle { get; set; }
    public List<ChartSeries> Series { get; } = new List<ChartSeries>();
}

public sealed class ChartSeries
{
    public string? Name { get; set; }
    public ChartStyle? Style { get; set; }
    public List<ChartPoint> Points { get; } = new List<ChartPoint>();
}

public sealed class ChartPoint
{
    public string? Category { get; set; }
    public double Value { get; set; }
    public double? XValue { get; set; }
    public double? Size { get; set; }
    public ChartStyle? Style { get; set; }
}

public enum ChartType
{
    Unknown,
    Bar,
    Line,
    Pie,
    Scatter,
    Area,
    Doughnut,
    Radar,
    Bubble
}

public enum ChartStacking
{
    None,
    Stacked,
    Percent
}

public enum ChartBarDirection
{
    Column,
    Bar
}

public enum ChartRadarStyle
{
    Standard,
    Marker,
    Filled
}

public sealed class ChartStyle
{
    public ChartFillStyle? Fill { get; set; }
    public ChartLineStyle? Line { get; set; }
    public ChartEffectStyle? Effects { get; set; }
}

public sealed class ChartFillStyle
{
    public bool IsNone { get; set; }
    public DocColor? Color { get; set; }
}

public sealed class ChartLineStyle
{
    public bool IsNone { get; set; }
    public DocColor? Color { get; set; }
    public float? Width { get; set; }
    public DocBorderStyle? Style { get; set; }
}

public sealed class ChartEffectStyle
{
    public ChartShadowEffect? Shadow { get; set; }
}

public sealed class ChartShadowEffect
{
    public float BlurRadius { get; set; }
    public float Distance { get; set; }
    public float Direction { get; set; }
    public DocColor Color { get; set; } = DocColor.Black;
}
