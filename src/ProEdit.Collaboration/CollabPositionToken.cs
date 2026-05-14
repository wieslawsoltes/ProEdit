using System.Globalization;

namespace ProEdit.Collaboration;

public static class CollabPositionToken
{
    public static PositionToken FromIndex(int index)
    {
        var clamped = Math.Max(0, index);
        return new PositionToken(clamped.ToString(CultureInfo.InvariantCulture));
    }

    public static bool TryGetIndex(PositionToken token, out int index)
    {
        if (string.IsNullOrWhiteSpace(token.Value))
        {
            index = 0;
            return false;
        }

        return int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }
}
