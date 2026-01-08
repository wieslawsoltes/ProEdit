namespace Vibe.Office.Documents;

public static class DocTextDirectionHelpers
{
    public static bool IsVertical(DocTextDirection? direction)
    {
        return direction.HasValue && direction.Value != DocTextDirection.LeftToRightTopToBottom;
    }

    public static float GetRotationDegrees(DocTextDirection direction)
    {
        return direction == DocTextDirection.BottomToTopLeftToRight ? -90f : 90f;
    }

    public static float GetVerticalBaselineOffset(float ascent, DocTextDirection direction)
    {
        var sign = direction == DocTextDirection.BottomToTopLeftToRight ? 1f : -1f;
        return sign * ascent;
    }
}
