using Avalonia;

namespace Vibe.Office.Ribbon.Avalonia;

internal static class RibbonIconResolver
{
    public static string? ResolveText(string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            return null;
        }

        if (Application.Current?.Resources.TryGetResource(iconKey, null, out var resource) == true)
        {
            return resource as string;
        }

        return null;
    }
}
