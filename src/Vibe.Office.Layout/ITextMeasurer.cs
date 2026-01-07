using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public interface ITextMeasurer
{
    TextMetrics MeasureText(string text, TextStyle style);
}

public interface ITextMeasurerSpan : ITextMeasurer
{
    TextMetrics MeasureText(ReadOnlySpan<char> text, TextStyle style);
}

public interface ITextMeasurerAdvanced : ITextMeasurer
{
    TextShapeInfo ShapeText(string text, TextStyle style);
}

public interface ITextMeasurerAdvancedSpan : ITextMeasurerAdvanced, ITextMeasurerSpan
{
    TextShapeInfo ShapeText(ReadOnlySpan<char> text, TextStyle style);
}
