using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public enum ClipboardContentKind
{
    None,
    Blocks,
    FloatingObject
}

public sealed class ClipboardContent
{
    public ClipboardContentKind Kind { get; }
    public ClipboardDocumentFragment? Fragment { get; }
    public IReadOnlyList<FloatingObject>? FloatingObjects { get; }
    public FloatingObject? FloatingObject => FloatingObjects is { Count: > 0 } ? FloatingObjects[0] : null;

    private ClipboardContent(ClipboardContentKind kind, ClipboardDocumentFragment? fragment, IReadOnlyList<FloatingObject>? floatingObjects)
    {
        Kind = kind;
        Fragment = fragment;
        FloatingObjects = floatingObjects;
    }

    public static ClipboardContent Empty() => new ClipboardContent(ClipboardContentKind.None, null, null);

    public static ClipboardContent FromFragment(ClipboardDocumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return new ClipboardContent(ClipboardContentKind.Blocks, fragment, null);
    }

    public static ClipboardContent FromFloatingObject(FloatingObject floating)
    {
        ArgumentNullException.ThrowIfNull(floating);
        return FromFloatingObjects(new[] { floating });
    }

    public static ClipboardContent FromFloatingObjects(IReadOnlyList<FloatingObject> floatingObjects)
    {
        ArgumentNullException.ThrowIfNull(floatingObjects);
        return new ClipboardContent(ClipboardContentKind.FloatingObject, null, floatingObjects);
    }
}

public sealed class ClipboardDocumentFragment
{
    public List<Block> Blocks { get; } = new();
    public ClipboardResourceSet Resources { get; } = new();
}

public sealed class ClipboardResourceSet
{
    public DocumentStyles Styles { get; } = new();
    public DocumentFonts Fonts { get; } = new();
    public DocumentThemeColorMap ThemeColors { get; } = new();
    public Dictionary<int, ListDefinition> ListDefinitions { get; } = new();
    public Dictionary<int, FootnoteDefinition> Footnotes { get; } = new();
    public Dictionary<int, EndnoteDefinition> Endnotes { get; } = new();
    public Dictionary<int, CommentDefinition> Comments { get; } = new();
}
