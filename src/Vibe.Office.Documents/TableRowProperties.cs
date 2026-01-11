using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TableRowProperties
{
    public float? Height { get; set; }
    public TableRowHeightRule? HeightRule { get; set; }
    public bool? CantSplit { get; set; }
    public bool? RepeatOnEachPage { get; set; }
    public DocColor? ShadingColor { get; set; }

    public bool HasValues => Height.HasValue
                             || HeightRule.HasValue
                             || CantSplit.HasValue
                             || RepeatOnEachPage.HasValue
                             || ShadingColor.HasValue;

    public TableRowProperties Clone()
    {
        return new TableRowProperties
        {
            Height = Height,
            HeightRule = HeightRule,
            CantSplit = CantSplit,
            RepeatOnEachPage = RepeatOnEachPage,
            ShadingColor = ShadingColor
        };
    }
}
