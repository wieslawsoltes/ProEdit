namespace Vibe.Office.Documents;

public sealed class CharacterStyleDefinition
{
    public string Id { get; }
    public string? Name { get; set; }
    public string? BasedOnId { get; set; }
    public TextStyleProperties RunProperties { get; } = new TextStyleProperties();

    public CharacterStyleDefinition(string id)
    {
        Id = id;
    }
}
