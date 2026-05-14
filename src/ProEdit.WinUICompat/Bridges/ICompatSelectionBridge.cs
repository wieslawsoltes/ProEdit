using ProEdit.Documents;
using ProEdit.WinUICompat.Documents;

namespace ProEdit.WinUICompat.Bridges;

public interface ICompatSelectionBridge
{
    TextPointer ToCompat(TextPosition position, LogicalDirection direction = LogicalDirection.Forward);

    TextPosition ToEditor(TextPointer pointer);
}
