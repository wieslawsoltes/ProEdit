using ProEdit.Documents;
using ProEdit.WinUICompat.Documents;

namespace ProEdit.WinUICompat.Bridges;

public sealed class CompatSelectionBridge : ICompatSelectionBridge
{
    public TextPointer ToCompat(TextPosition position, LogicalDirection direction = LogicalDirection.Forward)
    {
        return new TextPointer(position.ParagraphIndex, position.Offset, direction);
    }

    public TextPosition ToEditor(TextPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        return new TextPosition(pointer.ParagraphIndex, pointer.Offset);
    }
}
