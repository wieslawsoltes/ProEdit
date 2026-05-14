using ProEdit.Primitives;

namespace ProEdit.Ribbon;

public enum RibbonColorKind
{
    Rgb,
    Theme,
    Automatic,
    None,
    Custom,
    Picker
}

public sealed class RibbonColorItem : RibbonStateNode
{
    public RibbonColorItem(
        string id,
        string label,
        RibbonColorKind kind = RibbonColorKind.Rgb,
        DocColor? color = null,
        string? themeKey = null,
        string? iconKey = null,
        object? tag = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
        : base(isEnabled, isVisible, isEnabledEvaluator, isVisibleEvaluator)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Kind = kind;
        Color = color;
        ThemeKey = themeKey;
        IconKey = iconKey;
        Tag = tag;
    }

    public string Id { get; }
    public string Label { get; }
    public RibbonColorKind Kind { get; }
    public DocColor? Color { get; }
    public string? ThemeKey { get; }
    public string? IconKey { get; }
    public object? Tag { get; }
}
