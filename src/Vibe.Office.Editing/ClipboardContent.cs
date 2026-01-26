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
    public FloatingObject? FloatingObject { get; }

    private ClipboardContent(ClipboardContentKind kind, ClipboardDocumentFragment? fragment, FloatingObject? floatingObject)
    {
        Kind = kind;
        Fragment = fragment;
        FloatingObject = floatingObject;
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
        return new ClipboardContent(ClipboardContentKind.FloatingObject, null, floating);
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
