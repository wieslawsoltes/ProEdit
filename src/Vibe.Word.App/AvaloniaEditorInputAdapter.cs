using Avalonia;
using Avalonia.Input;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public sealed class AvaloniaEditorInputAdapter : IEditorInputRouter
{
    private readonly IEditorInputRouter _inner;

    public AvaloniaEditorInputAdapter(IEditorInputRouter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool HandleTextInput(ReadOnlySpan<char> text, EditorModifiers modifiers)
    {
        return _inner.HandleTextInput(text, modifiers);
    }

    public bool HandleKey(EditorKey key, EditorKeyEventKind kind, EditorModifiers modifiers)
    {
        return _inner.HandleKey(key, kind, modifiers);
    }

    public bool HandlePointer(in EditorPointerEvent pointerEvent)
    {
        return _inner.HandlePointer(pointerEvent);
    }

    public bool HandleTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (string.IsNullOrEmpty(e.Text))
        {
            return false;
        }

        return HandleTextInput(e.Text.AsSpan(), EditorModifiers.None);
    }

    public bool HandleKeyDown(KeyEventArgs e)
    {
        return HandleKeyEvent(e, EditorKeyEventKind.Down);
    }

    public bool HandleKeyUp(KeyEventArgs e)
    {
        return HandleKeyEvent(e, EditorKeyEventKind.Up);
    }

    public bool HandlePointerPressed(PointerPressedEventArgs e, Vector scrollOffset, Visual relativeTo)
    {
        return HandlePointerEvent(e, scrollOffset, relativeTo, EditorPointerKind.Down, e.ClickCount);
    }

    public bool HandlePointerMoved(PointerEventArgs e, Vector scrollOffset, Visual relativeTo)
    {
        return HandlePointerEvent(e, scrollOffset, relativeTo, EditorPointerKind.Move, 0);
    }

    public bool HandlePointerReleased(PointerReleasedEventArgs e, Vector scrollOffset, Visual relativeTo)
    {
        return HandlePointerEvent(e, scrollOffset, relativeTo, EditorPointerKind.Up, 0);
    }

    private bool HandleKeyEvent(KeyEventArgs e, EditorKeyEventKind kind)
    {
        ArgumentNullException.ThrowIfNull(e);
        var key = MapKey(e.Key);
        if (key == EditorKey.Unknown)
        {
            return false;
        }

        var modifiers = MapModifiers(e.KeyModifiers);
        return HandleKey(key, kind, modifiers);
    }

    private bool HandlePointerEvent(PointerEventArgs e, Vector scrollOffset, Visual relativeTo, EditorPointerKind kind, int clickCount)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(relativeTo);
        var point = e.GetCurrentPoint(relativeTo);
        var position = point.Position;
        var docX = (float)(position.X + scrollOffset.X);
        var docY = (float)(position.Y + scrollOffset.Y);
        var button = MapButton(point.Properties);
        if (kind == EditorPointerKind.Move && button == EditorPointerButton.None)
        {
            return false;
        }

        var modifiers = MapModifiers(e.KeyModifiers);
        var editorEvent = new EditorPointerEvent(kind, docX, docY, button, modifiers, clickCount);
        return HandlePointer(editorEvent);
    }

    private static EditorPointerButton MapButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
        {
            return EditorPointerButton.Primary;
        }

        if (properties.IsRightButtonPressed)
        {
            return EditorPointerButton.Secondary;
        }

        if (properties.IsMiddleButtonPressed)
        {
            return EditorPointerButton.Middle;
        }

        if (properties.IsXButton1Pressed)
        {
            return EditorPointerButton.XButton1;
        }

        if (properties.IsXButton2Pressed)
        {
            return EditorPointerButton.XButton2;
        }

        return EditorPointerButton.None;
    }

    private static EditorModifiers MapModifiers(KeyModifiers modifiers)
    {
        var result = EditorModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= EditorModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= EditorModifiers.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= EditorModifiers.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= EditorModifiers.Meta;
        }

        return result;
    }

    private static EditorKey MapKey(Key key)
    {
        return key switch
        {
            Key.Left => EditorKey.Left,
            Key.Right => EditorKey.Right,
            Key.Up => EditorKey.Up,
            Key.Down => EditorKey.Down,
            Key.Back => EditorKey.Backspace,
            Key.Delete => EditorKey.Delete,
            Key.Enter => EditorKey.Enter,
            Key.Home => EditorKey.Home,
            Key.End => EditorKey.End,
            Key.PageUp => EditorKey.PageUp,
            Key.PageDown => EditorKey.PageDown,
            Key.Tab => EditorKey.Tab,
            Key.Z => EditorKey.Z,
            Key.Y => EditorKey.Y,
            _ => EditorKey.Unknown
        };
    }
}
