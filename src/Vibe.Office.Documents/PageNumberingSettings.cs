namespace Vibe.Office.Documents;

public sealed class PageNumberingSettings
{
    public int? Start { get; set; }
    public PageNumberFormat? Format { get; set; }

    public bool HasValues => Start.HasValue || Format.HasValue;

    public PageNumberingSettings Clone()
    {
        return new PageNumberingSettings
        {
            Start = Start,
            Format = Format
        };
    }
}

public enum PageNumberFormat
{
    Decimal,
    UpperRoman,
    LowerRoman,
    UpperLetter,
    LowerLetter
}
