using Vibe.Office.Documents;
using Vibe.Office.WinUICompat.Documents;

namespace Vibe.Office.WinUICompat.Bridges;

public interface ICompatSelectionBridge
{
    TextPointer ToCompat(TextPosition position, LogicalDirection direction = LogicalDirection.Forward);

    TextPosition ToEditor(TextPointer pointer);
}
