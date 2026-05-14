namespace ProEdit.Documents;

public static class DocTextDirectionHelpers
{
    public static bool IsVertical(DocTextDirection? direction)
    {
        return direction.HasValue && direction.Value != DocTextDirection.LeftToRightTopToBottom;
    }

    public static bool IsRotated(DocTextDirection direction)
    {
        return direction == DocTextDirection.LeftToRightTopToBottomRotated
               || direction == DocTextDirection.TopToBottomRightToLeftRotated
               || direction == DocTextDirection.TopToBottomLeftToRightRotated;
    }

    public static bool UseUprightVerticalForms(DocTextDirection direction)
    {
        return IsVertical(direction) && !IsRotated(direction);
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
