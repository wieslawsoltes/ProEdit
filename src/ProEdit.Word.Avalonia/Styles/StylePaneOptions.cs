using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

public enum StylePaneFilterMode
{
    All,
    InUse,
    QuickStyles,
    Recommended
}

public enum StylePaneSortMode
{
    Alphabetical,
    ByPriority
}

public enum StylePaneTypeFilter
{
    All,
    Paragraph,
    Character,
    Table
}

public sealed class StylePaneOptions
{
    public StylePaneFilterMode FilterMode { get; set; } = StylePaneFilterMode.All;
    public StylePaneSortMode SortMode { get; set; } = StylePaneSortMode.Alphabetical;
    public StylePaneTypeFilter TypeFilter { get; set; } = StylePaneTypeFilter.All;
    public bool ShowPreview { get; set; } = true;
    public bool ShowHidden { get; set; }

    public StylePaneOptions Clone()
    {
        return new StylePaneOptions
        {
            FilterMode = FilterMode,
            SortMode = SortMode,
            TypeFilter = TypeFilter,
            ShowPreview = ShowPreview,
            ShowHidden = ShowHidden
        };
    }

    public bool Includes(EditorStyleType type)
    {
        return TypeFilter switch
        {
            StylePaneTypeFilter.All => true,
            StylePaneTypeFilter.Paragraph => type == EditorStyleType.Paragraph,
            StylePaneTypeFilter.Character => type == EditorStyleType.Character,
            StylePaneTypeFilter.Table => type == EditorStyleType.Table,
            _ => true
        };
    }
}
