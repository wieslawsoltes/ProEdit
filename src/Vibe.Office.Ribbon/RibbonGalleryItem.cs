namespace Vibe.Office.Ribbon;

public sealed class RibbonGalleryItem : RibbonStateNode
{
    public RibbonGalleryItem(
        string id,
        string label,
        string? description = null,
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
        Description = description;
        IconKey = iconKey;
        Tag = tag;
    }

    public string Id { get; }
    public string Label { get; }
    public string? Description { get; }
    public string? IconKey { get; }
    public object? Tag { get; }
}
