namespace ProEdit.Documents;

public sealed class DocumentStyles
{
    public Dictionary<string, ParagraphStyleDefinition> ParagraphStyles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CharacterStyleDefinition> CharacterStyles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TableStyleDefinition> TableStyles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DefaultParagraphStyleId { get; set; }
    public string? DefaultCharacterStyleId { get; set; }
    public string? DefaultTableStyleId { get; set; }
}
