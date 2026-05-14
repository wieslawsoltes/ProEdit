using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Editing;

public enum ContentControlActivationSource
{
    Pointer,
    Keyboard
}

public readonly record struct ContentControlHit(ContentControlProperties Properties, TextPosition Position);

public interface IContentControlInteractionService
{
    bool TryPickListItem(
        ContentControlProperties properties,
        IReadOnlyList<ContentControlListItem> items,
        string? currentValue,
        bool allowCustom,
        out string? selectedValue);

    bool TryPickDate(
        ContentControlProperties properties,
        DateTimeOffset? currentValue,
        out DateTimeOffset selectedDate);
}

public interface IContentControlInteractionSession
{
    bool TryGetContentControlAtPoint(float x, float y, out ContentControlHit hit);
    bool TryGetContentControlAtCaret(out ContentControlHit hit);
    bool TryActivateContentControl(
        in ContentControlHit hit,
        ContentControlActivationSource source,
        EditorModifiers modifiers,
        IContentControlInteractionService? interactionService);
}
