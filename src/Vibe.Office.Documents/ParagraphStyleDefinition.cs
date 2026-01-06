namespace Vibe.Office.Documents;

public sealed class ParagraphStyleDefinition
{
    public string Id { get; }
    public string? Name { get; set; }
    public string? BasedOnId { get; set; }
    public ParagraphStyleProperties ParagraphProperties { get; } = new ParagraphStyleProperties();
    public TextStyleProperties RunProperties { get; } = new TextStyleProperties();

    public ParagraphStyleDefinition(string id)
    {
        Id = id;
    }
}
