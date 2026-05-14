namespace ProEdit.Documents;

public sealed class EastAsianLayoutProperties
{
    public int? Id { get; set; }
    public bool? Combine { get; set; }
    public string? CombineBrackets { get; set; }
    public bool? Vertical { get; set; }
    public bool? VerticalCompress { get; set; }

    public bool HasValues =>
        Id.HasValue
        || Combine.HasValue
        || !string.IsNullOrWhiteSpace(CombineBrackets)
        || Vertical.HasValue
        || VerticalCompress.HasValue;

    public EastAsianLayoutProperties Clone()
    {
        return new EastAsianLayoutProperties
        {
            Id = Id,
            Combine = Combine,
            CombineBrackets = CombineBrackets,
            Vertical = Vertical,
            VerticalCompress = VerticalCompress
        };
    }
}
