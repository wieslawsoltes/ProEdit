using Vibe.Office.WinUICompat.Controls;

namespace Vibe.Office.WinUICompat.Uno.Tests;

internal static class UnoUiTestEnvironment
{
    private static bool? _isUiRuntimeAvailable;

    public static bool IsUiRuntimeAvailable()
    {
        if (_isUiRuntimeAvailable.HasValue)
        {
            return _isUiRuntimeAvailable.Value;
        }

        try
        {
            _ = new RichTextBlock();
            _isUiRuntimeAvailable = true;
        }
        catch (TypeInitializationException)
        {
            _isUiRuntimeAvailable = false;
        }
        catch (NotSupportedException)
        {
            _isUiRuntimeAvailable = false;
        }

        return _isUiRuntimeAvailable.Value;
    }
}
