namespace Vibe.Office.Editing;

[Flags]
public enum EditorModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8
}

public enum EditorKey
{
    Unknown = 0,
    Left,
    Right,
    Up,
    Down,
    Backspace,
    Delete,
    Enter,
    Home,
    End,
    PageUp,
    PageDown,
    Tab
}

public enum EditorKeyEventKind
{
    Down,
    Up
}

public enum EditorPointerKind
{
    Down,
    Move,
    Up
}

public enum EditorPointerButton
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Middle = 3,
    XButton1 = 4,
    XButton2 = 5
}

public readonly struct EditorPointerEvent
{
    public EditorPointerKind Kind { get; }
    public float X { get; }
    public float Y { get; }
    public EditorPointerButton Button { get; }
    public EditorModifiers Modifiers { get; }
    public int ClickCount { get; }

    public EditorPointerEvent(
        EditorPointerKind kind,
        float x,
        float y,
        EditorPointerButton button,
        EditorModifiers modifiers,
        int clickCount)
    {
        Kind = kind;
        X = x;
        Y = y;
        Button = button;
        Modifiers = modifiers;
        ClickCount = clickCount;
    }
}

public interface IEditorInputRouter
{
    bool HandleTextInput(ReadOnlySpan<char> text, EditorModifiers modifiers);
    bool HandleKey(EditorKey key, EditorKeyEventKind kind, EditorModifiers modifiers);
    bool HandlePointer(in EditorPointerEvent pointerEvent);
}
