using ProEdit.Primitives;

namespace ProEdit.Documents;

public sealed class DocumentThemeColorMap
{
    private static readonly IReadOnlyDictionary<DocThemeColor, DocColor> DefaultColors = new Dictionary<DocThemeColor, DocColor>
    {
        { DocThemeColor.Dark1, DocColor.Black },
        { DocThemeColor.Light1, DocColor.White },
        { DocThemeColor.Dark2, new DocColor(31, 73, 125) },
        { DocThemeColor.Light2, new DocColor(238, 236, 225) },
        { DocThemeColor.Accent1, new DocColor(79, 129, 189) },
        { DocThemeColor.Accent2, new DocColor(192, 80, 77) },
        { DocThemeColor.Accent3, new DocColor(155, 187, 89) },
        { DocThemeColor.Accent4, new DocColor(128, 100, 162) },
        { DocThemeColor.Accent5, new DocColor(75, 172, 198) },
        { DocThemeColor.Accent6, new DocColor(247, 150, 70) },
        { DocThemeColor.Hyperlink, new DocColor(0, 0, 255) },
        { DocThemeColor.FollowedHyperlink, new DocColor(128, 0, 128) }
    };

    private readonly Dictionary<DocThemeColor, DocColor> _colors = new();

    public bool HasValues => _colors.Count > 0;
    public IReadOnlyDictionary<DocThemeColor, DocColor> Overrides => _colors;

    public void Set(DocThemeColor color, DocColor? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        _colors[color] = value.Value;
    }

    public void Clear()
    {
        _colors.Clear();
    }

    public bool TryGet(DocThemeColor color, out DocColor value)
    {
        return _colors.TryGetValue(color, out value);
    }

    public DocColor? Get(DocThemeColor color)
    {
        return _colors.TryGetValue(color, out var value) ? value : null;
    }

    public DocColor GetOrDefault(DocThemeColor color)
    {
        return _colors.TryGetValue(color, out var value) ? value : DefaultColors[color];
    }

    public static DocColor GetDefault(DocThemeColor color)
    {
        return DefaultColors[color];
    }
}
