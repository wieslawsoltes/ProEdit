namespace ProEdit.Documents;

public sealed class ChartInline : Inline
{
    public Guid Id { get; } = Guid.NewGuid();
    public float Width { get; set; }
    public float Height { get; set; }
    public ChartModel? Model { get; }
    public byte[]? PartData { get; }
    public string? Name { get; set; }

    public ChartInline(float width, float height, ChartModel? model, byte[]? partData)
    {
        Width = width;
        Height = height;
        Model = model;
        PartData = partData;
    }
}
