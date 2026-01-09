using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public readonly record struct EditorLineSpacingRequest(
    float? Multiple = null,
    int? Twips = null,
    DocLineSpacingRule? Rule = null)
{
    public static EditorLineSpacingRequest FromMultiple(float multiple) =>
        new EditorLineSpacingRequest(multiple, null, DocLineSpacingRule.Auto);

    public static EditorLineSpacingRequest FromTwips(int twips, DocLineSpacingRule rule) =>
        new EditorLineSpacingRequest(null, twips, rule);
}
