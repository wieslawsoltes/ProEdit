namespace Vibe.Office.Documents;

public enum DocGridType
{
    Default,
    Lines,
    LinesAndChars,
    SnapToChars
}

public sealed class DocGridSettings
{
    public DocGridType? Type { get; set; }
    public float? LinePitch { get; set; }
    public float? CharacterSpace { get; set; }

    public bool HasValues => Type.HasValue || LinePitch.HasValue || CharacterSpace.HasValue;

    public DocGridSettings Clone()
    {
        return new DocGridSettings
        {
            Type = Type,
            LinePitch = LinePitch,
            CharacterSpace = CharacterSpace
        };
    }
}
