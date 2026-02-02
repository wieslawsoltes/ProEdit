namespace Vibe.Office.Html;

internal static class HtmlVoidElements
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area",
        "base",
        "br",
        "col",
        "embed",
        "hr",
        "img",
        "input",
        "link",
        "meta",
        "param",
        "source",
        "track",
        "wbr"
    };

    public static bool IsVoid(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && VoidElements.Contains(name);
    }
}
