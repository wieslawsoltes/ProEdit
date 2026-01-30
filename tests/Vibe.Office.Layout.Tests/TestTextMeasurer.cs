using Vibe.Office.Documents;
using Vibe.Office.Layout;

namespace Vibe.Office.Layout.Tests;

internal sealed class TestTextMeasurer : ITextMeasurerSpan
{
    public TextMetrics MeasureText(string text, TextStyle style)
    {
        return Measure(text.AsSpan(), style);
    }

    public TextMetrics MeasureText(ReadOnlySpan<char> text, TextStyle style)
    {
        return Measure(text, style);
    }

    private static TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
    {
        var width = text.Length;
        const float height = 10f;
        const float ascent = 8f;
        const float descent = 2f;
        return new TextMetrics(width, height, ascent, descent);
    }
}
