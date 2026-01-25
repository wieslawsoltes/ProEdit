namespace Vibe.Office.Documents;

public sealed class ShapeInline : Inline
{
    public Guid Id { get; } = Guid.NewGuid();
    public float Width { get; set; }
    public float Height { get; set; }
    public ShapeProperties Properties { get; } = new ShapeProperties();
    public ShapeTextBox? TextBox { get; set; }
    public string? Name { get; set; }

    public ShapeInline(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public ShapeInline(float width, float height, ShapeProperties properties, ShapeTextBox? textBox = null, string? name = null)
    {
        Width = width;
        Height = height;
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        TextBox = textBox;
        Name = name;
    }
}
