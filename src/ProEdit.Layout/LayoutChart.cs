using ProEdit.Documents;

namespace ProEdit.Layout;

public sealed record LayoutChart(ChartInline Chart, float X, float Width, float Height, int Length);
