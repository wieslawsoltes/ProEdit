using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed record LayoutChart(ChartInline Chart, float X, float Width, float Height, int Length);
