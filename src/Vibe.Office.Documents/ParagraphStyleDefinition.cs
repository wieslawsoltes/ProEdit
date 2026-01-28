namespace Vibe.Office.Documents;

public sealed class ParagraphStyleDefinition
{
    public string Id { get; }
    public string? Name { get; set; }
    public string? BasedOnId { get; set; }
    public string? NextStyleId { get; set; }
    public string? LinkedStyleId { get; set; }
    public int? ListId { get; set; }
    public int? ListLevel { get; set; }
    public int? UiPriority { get; set; }
    public bool? QuickStyle { get; set; }
    public bool? SemiHidden { get; set; }
    public bool? UnhideWhenUsed { get; set; }
    public bool? AutoRedefine { get; set; }
    public bool? Hidden { get; set; }
    public bool? Locked { get; set; }
    public bool? PrimaryStyle { get; set; }
    public bool? CustomStyle { get; set; }
    public ParagraphStyleProperties ParagraphProperties { get; } = new ParagraphStyleProperties();
    public TextStyleProperties RunProperties { get; } = new TextStyleProperties();

    public ParagraphStyleDefinition(string id)
    {
        Id = id;
    }
}
