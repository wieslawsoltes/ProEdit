namespace Vibe.Office.Documents;

public sealed class DropCapSettings
{
    public DropCapKind Kind { get; set; } = DropCapKind.Drop;
    public int Lines { get; set; } = 2;
    public float? Distance { get; set; }

    public bool HasValues => Lines > 0 || Distance.HasValue;

    public DropCapSettings Clone()
    {
        return new DropCapSettings
        {
            Kind = Kind,
            Lines = Lines,
            Distance = Distance
        };
    }
}

public enum DropCapKind
{
    Drop,
    Margin
}
