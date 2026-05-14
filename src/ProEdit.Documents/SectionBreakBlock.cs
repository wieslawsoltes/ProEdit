namespace ProEdit.Documents;

public sealed class SectionBreakBlock : Block
{
    public SectionProperties Properties { get; set; } = new SectionProperties();
    public SectionBreakType BreakType { get; set; } = SectionBreakType.NextPage;
    public int? SectionIndex { get; set; }
}
