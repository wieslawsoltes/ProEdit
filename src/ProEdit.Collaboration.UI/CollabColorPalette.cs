namespace ProEdit.Collaboration.UI;

/// <summary>
/// Provides deterministic collaboration colors.
/// </summary>
public static class CollabColorPalette
{
    private static readonly string[] Palette =
    {
        "#2D7DF0",
        "#00A86B",
        "#F05A28",
        "#8E44AD",
        "#F1C40F",
        "#E91E63",
        "#16A085",
        "#D35400",
        "#2980B9",
        "#7F8C8D"
    };

    /// <summary>
    /// Resolves a stable color for the given user identifier.
    /// </summary>
    public static string ResolveColor(Guid userId)
    {
        if (Palette.Length == 0)
        {
            return "#2D7DF0";
        }

        var bytes = userId.ToByteArray();
        var hash = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            hash = (hash * 31) ^ bytes[i];
        }

        var index = Math.Abs(hash) % Palette.Length;
        return Palette[index];
    }

    /// <summary>
    /// Uses the provided color when valid; otherwise resolves a deterministic color.
    /// </summary>
    public static string ResolveColor(string? color, Guid userId)
    {
        return string.IsNullOrWhiteSpace(color) ? ResolveColor(userId) : color;
    }
}
