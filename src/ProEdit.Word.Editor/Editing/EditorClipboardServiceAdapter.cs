using ProEdit.Editing;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorClipboardServiceAdapter : IClipboardService
{
    private static readonly IReadOnlyList<string> DefaultFormats = new[] { "text/plain" };
    private readonly IEditorSession _session;
    private readonly Func<bool>? _canPasteEvaluator;
    private readonly IReadOnlyList<string> _formats;
    private string? _textBuffer;
    private ClipboardContent? _content;

    public EditorClipboardServiceAdapter(
        IEditorSession session,
        Func<bool>? canPasteEvaluator = null,
        IReadOnlyList<string>? supportedFormats = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _canPasteEvaluator = canPasteEvaluator;
        _formats = supportedFormats ?? DefaultFormats;
    }

    public bool CanCopy => !_session.Selection.IsEmpty || _session.SelectedFloatingObjectId.HasValue;
    public bool CanCut => CanCopy;
    public bool CanPaste => _canPasteEvaluator?.Invoke() ?? !string.IsNullOrEmpty(_textBuffer) || _content is not null;
    public IReadOnlyList<string> SupportedFormats => _formats;

    public bool TryGetText(out string text)
    {
        if (string.IsNullOrEmpty(_textBuffer))
        {
            text = string.Empty;
            return false;
        }

        text = _textBuffer;
        return true;
    }

    public void SetText(string text)
    {
        _textBuffer = string.IsNullOrEmpty(text) ? null : text;
        _content = null;
    }

    public bool TryGetContent(out ClipboardContent content)
    {
        if (_content is null)
        {
            content = ClipboardContent.Empty();
            return false;
        }

        content = _content;
        return true;
    }

    public void SetContent(ClipboardContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }
}
