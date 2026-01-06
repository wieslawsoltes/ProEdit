using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public interface ITextMeasurer
{
    TextMetrics MeasureText(string text, TextStyle style);
}

public interface ITextMeasurerAdvanced : ITextMeasurer
{
    TextShapeInfo ShapeText(string text, TextStyle style);
}
