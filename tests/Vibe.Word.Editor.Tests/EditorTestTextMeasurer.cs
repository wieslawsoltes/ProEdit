using Vibe.Office.Documents;
using Vibe.Office.Layout;

namespace Vibe.Word.Editor.Tests;

internal sealed class EditorTestTextMeasurer : ITextMeasurerAdvancedSpan
{
    public TextMetrics MeasureText(string text, TextStyle style)
    {
        return new TextMetrics(text.Length, 1f, 0.8f, 0.2f);
    }

    public TextMetrics MeasureText(ReadOnlySpan<char> text, TextStyle style)
    {
        return new TextMetrics(text.Length, 1f, 0.8f, 0.2f);
    }

    public TextShapeInfo ShapeText(string text, TextStyle style)
    {
        return BuildShape(text.Length);
    }

    public TextShapeInfo ShapeText(ReadOnlySpan<char> text, TextStyle style)
    {
        return BuildShape(text.Length);
    }

    private static TextShapeInfo BuildShape(int length)
    {
        if (length <= 0)
        {
            return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
        }

        var offsets = new int[length];
        var advances = new float[length];
        for (var i = 0; i < length; i++)
        {
            offsets[i] = i;
            advances[i] = 1f;
        }

        return new TextShapeInfo(length, offsets, advances);
    }
}
