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
    public ChartLegend? Legend { get; set; }
    public ChartDataLabelSettings? DataLabels { get; set; }
    public List<ChartAxis> Axes { get; } = new List<ChartAxis>();
    public List<ChartSeries> Series { get; } = new List<ChartSeries>();
}

public sealed class ChartSeries
{
    public string? Name { get; set; }
    public ChartStyle? Style { get; set; }
    public ChartDataLabelSettings? DataLabels { get; set; }
    public List<ChartPoint> Points { get; } = new List<ChartPoint>();
}

public sealed class ChartPoint
{
    public string? Category { get; set; }
    public double Value { get; set; }
    public double? XValue { get; set; }
    public double? Size { get; set; }
    public ChartStyle? Style { get; set; }
    public ChartDataLabelSettings? DataLabel { get; set; }
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

public sealed class ChartAxis
{
    public uint? AxisId { get; set; }
    public uint? CrossAxisId { get; set; }
    public ChartAxisKind Kind { get; set; } = ChartAxisKind.Category;
    public ChartAxisPosition Position { get; set; } = ChartAxisPosition.Bottom;
    public bool IsVisible { get; set; } = true;
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public double? MajorUnit { get; set; }
    public double? MinorUnit { get; set; }
    public ChartTickMark MajorTickMark { get; set; } = ChartTickMark.Outside;
    public ChartTickMark MinorTickMark { get; set; } = ChartTickMark.None;
    public ChartTickLabelPosition TickLabelPosition { get; set; } = ChartTickLabelPosition.NextToAxis;
    public string? NumberFormat { get; set; }
    public string? Title { get; set; }
    public ChartLineStyle? LineStyle { get; set; }
    public ChartLineStyle? MajorGridlineStyle { get; set; }
    public ChartLineStyle? MinorGridlineStyle { get; set; }
    public ChartTextStyle? LabelTextStyle { get; set; }
    public ChartTextStyle? TitleTextStyle { get; set; }
}

public enum ChartAxisKind
{
    Category,
    Value
}

public enum ChartAxisPosition
{
    Bottom,
    Left,
    Top,
    Right
}

public enum ChartTickMark
{
    None,
    Inside,
    Outside,
    Cross
}

public enum ChartTickLabelPosition
{
    None,
    NextToAxis,
    High,
    Low
}

public sealed class ChartLegend
{
    public bool IsVisible { get; set; } = true;
    public ChartLegendPosition Position { get; set; } = ChartLegendPosition.Right;
    public bool Overlay { get; set; }
    public ChartTextStyle? TextStyle { get; set; }
}

public enum ChartLegendPosition
{
    Right,
    Left,
    Top,
    Bottom,
    Corner
}

public sealed class ChartDataLabelSettings
{
    public bool? IsHidden { get; set; }
    public bool? ShowValue { get; set; }
    public bool? ShowCategoryName { get; set; }
    public bool? ShowSeriesName { get; set; }
    public bool? ShowPercent { get; set; }
    public bool? ShowBubbleSize { get; set; }
    public bool? ShowLegendKey { get; set; }
    public bool? ShowLeaderLines { get; set; }
    public ChartDataLabelPosition Position { get; set; } = ChartDataLabelPosition.Center;
    public string? NumberFormat { get; set; }
    public ChartTextStyle? TextStyle { get; set; }
    public ChartStyle? ShapeStyle { get; set; }
}

public enum ChartDataLabelPosition
{
    BestFit,
    Center,
    InsideEnd,
    InsideBase,
    OutsideEnd,
    Left,
    Right,
    Top,
    Bottom
}

public sealed class ChartTextStyle
{
    public string? FontFamily { get; set; }
    public float? FontSize { get; set; }
    public DocColor? Color { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
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
