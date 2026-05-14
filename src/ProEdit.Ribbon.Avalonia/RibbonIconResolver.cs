using Avalonia;
using Avalonia.Controls;

namespace ProEdit.Ribbon.Avalonia;

internal static class RibbonIconResolver
{
    public static string? ResolveText(string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            return null;
        }

        if (Application.Current?.TryFindResource(iconKey, out var resource) == true)
        {
            return resource as string;
        }

        return null;
    }
}
