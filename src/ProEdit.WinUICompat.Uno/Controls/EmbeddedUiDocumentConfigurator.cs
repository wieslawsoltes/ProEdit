using Microsoft.UI.Xaml;
using ProEdit.WinUICompat.Text;

namespace ProEdit.WinUICompat.Controls;

internal static class EmbeddedUiDocumentConfigurator
{
    public static void Configure(RichEditTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.ConfigureEmbeddedUiElements(
            enabled: true,
            elementPredicate: static child => child is UIElement,
            sizeResolver: TryResolveEmbeddedUiElementSize);
    }

    private static (double Width, double Height)? TryResolveEmbeddedUiElementSize(object child, bool isInline)
    {
        if (child is not FrameworkElement frameworkElement)
        {
            return null;
        }

        var fallbackWidth = isInline ? 120d : 320d;
        var fallbackHeight = isInline ? 28d : 120d;

        var width = ResolvePreferredDimension(frameworkElement.Width, frameworkElement.MinWidth, fallbackWidth);
        var height = ResolvePreferredDimension(frameworkElement.Height, frameworkElement.MinHeight, fallbackHeight);

        if (!double.IsNaN(frameworkElement.MaxWidth)
            && !double.IsInfinity(frameworkElement.MaxWidth)
            && frameworkElement.MaxWidth > 0d)
        {
            width = Math.Min(width, frameworkElement.MaxWidth);
        }

        if (!double.IsNaN(frameworkElement.MaxHeight)
            && !double.IsInfinity(frameworkElement.MaxHeight)
            && frameworkElement.MaxHeight > 0d)
        {
            height = Math.Min(height, frameworkElement.MaxHeight);
        }

        return (Math.Max(1d, width), Math.Max(1d, height));
    }

    private static double ResolvePreferredDimension(double value, double minimum, double fallback)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0d)
        {
            return value;
        }

        if (!double.IsNaN(minimum) && !double.IsInfinity(minimum) && minimum > 0d)
        {
            return minimum;
        }

        return Math.Max(1d, fallback);
    }
}
