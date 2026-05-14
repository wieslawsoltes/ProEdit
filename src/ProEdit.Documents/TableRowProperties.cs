using ProEdit.Primitives;

namespace ProEdit.Documents;

public sealed class TableRowProperties
{
    public float? Height { get; set; }
    public TableRowHeightRule? HeightRule { get; set; }
    public bool? CantSplit { get; set; }
    public bool? RepeatOnEachPage { get; set; }
    public DocColor? ShadingColor { get; set; }
    public int? GridBefore { get; set; }
    public int? GridAfter { get; set; }

    public bool HasValues => Height.HasValue
                             || HeightRule.HasValue
                             || CantSplit.HasValue
                             || RepeatOnEachPage.HasValue
                             || ShadingColor.HasValue
                             || GridBefore.HasValue
                             || GridAfter.HasValue;

    public TableRowProperties Clone()
    {
        return new TableRowProperties
        {
            Height = Height,
            HeightRule = HeightRule,
            CantSplit = CantSplit,
            RepeatOnEachPage = RepeatOnEachPage,
            ShadingColor = ShadingColor,
            GridBefore = GridBefore,
            GridAfter = GridAfter
        };
    }
}
