using ProEdit.Documents;

namespace ProEdit.RichText.Avalonia;

/// <summary>
/// Represents the active selection of a <see cref="RichTextBox"/>.
/// </summary>
public sealed class FlowTextSelection : FlowTextRange
{
    internal FlowTextSelection(RichTextBox owner)
        : base(owner, bindToSelection: true, initialRange: default(TextRange))
    {
    }

    internal static FlowTextSelection CreateForSelection(RichTextBox owner)
    {
        return new FlowTextSelection(owner);
    }
}
