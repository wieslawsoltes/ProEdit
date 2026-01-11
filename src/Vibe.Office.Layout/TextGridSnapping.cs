using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

internal static class TextGridSnapping
{
    public static float GetCharacterSpacing(DocGridSettings? docGrid)
    {
        if (docGrid is null || !docGrid.HasValues)
        {
            return 0f;
        }

        if (docGrid.CharacterSpace is not > 0f)
        {
            return 0f;
        }

        return docGrid.Type is DocGridType.LinesAndChars or DocGridType.SnapToChars
            ? docGrid.CharacterSpace.Value
            : 0f;
    }

    public static float MeasureText(ReadOnlySpan<char> text, TextStyle style, ITextMeasurer measurer, float gridSpacing)
    {
        if (text.IsEmpty)
        {
            return 0f;
        }

        var letterSpacing = style.LetterSpacing;
        if (gridSpacing <= 0f)
        {
            var width = MeasureTextFallback(text, style, measurer);
            return ApplyLetterSpacingFallback(width, text, letterSpacing);
        }

        TextShapeInfo shape;
        if (measurer is ITextMeasurerAdvancedSpan advancedSpan)
        {
            shape = advancedSpan.ShapeText(text, style);
        }
        else if (measurer is ITextMeasurerAdvanced advanced)
        {
            shape = advanced.ShapeText(text.ToString(), style);
        }
        else
        {
            return SnapToGridForward(MeasureTextFallback(text, style, measurer), gridSpacing);
        }

        if (shape.ClusterOffsets.Length == 0)
        {
            var width = MeasureTextFallback(text, style, measurer);
            width = ApplyLetterSpacingFallback(width, text, letterSpacing);
            return SnapToGridForward(width, gridSpacing);
        }

        return QuantizeShapeWidth(shape, gridSpacing, letterSpacing);
    }

    public static float MeasureText(string text, TextStyle style, ITextMeasurer measurer, float gridSpacing)
    {
        return MeasureText(text.AsSpan(), style, measurer, gridSpacing);
    }

    public static float QuantizeShapeWidth(TextShapeInfo shape, float gridSpacing, float letterSpacing)
    {
        if (gridSpacing <= 0f || shape.ClusterOffsets.Length == 0)
        {
            return 0f;
        }

        var total = 0f;
        var clusterCount = shape.ClusterOffsets.Length;
        for (var i = 0; i < clusterCount; i++)
        {
            var advance = i < shape.ClusterAdvances.Length ? shape.ClusterAdvances[i] : 0f;
            if (letterSpacing != 0f && i < clusterCount - 1)
            {
                advance += letterSpacing;
            }

            total = SnapToGridForward(total + advance, gridSpacing);
        }

        return total;
    }

    public static float SnapToGridForward(float value, float spacing)
    {
        if (spacing <= 0f)
        {
            return value;
        }

        return MathF.Ceiling(value / spacing) * spacing;
    }

    private static float MeasureTextFallback(ReadOnlySpan<char> text, TextStyle style, ITextMeasurer measurer)
    {
        return measurer is ITextMeasurerSpan spanMeasurer
            ? spanMeasurer.MeasureText(text, style).Width
            : measurer.MeasureText(text.ToString(), style).Width;
    }

    private static float ApplyLetterSpacingFallback(float width, ReadOnlySpan<char> text, float letterSpacing)
    {
        if (letterSpacing == 0f || text.Length <= 1)
        {
            return width;
        }

        var gapCount = Math.Max(0, text.Length - 1);
        return width + letterSpacing * gapCount;
    }
}
