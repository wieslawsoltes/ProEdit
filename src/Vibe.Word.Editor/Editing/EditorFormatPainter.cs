using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal sealed class EditorFormatPainter : IFormatPainterService
{
    private readonly IEditorMutableSession _session;
    private readonly EditorTextFormattingApplier _textFormatting;
    private readonly IFormattingState? _formattingState;
    private TextStyleProperties? _style;
    private TextRange? _lastSelection;
    private bool _isActive;
    private bool _isApplying;

    public EditorFormatPainter(
        IEditorMutableSession session,
        EditorTextFormattingApplier textFormatting,
        IFormattingState? formattingState)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _textFormatting = textFormatting ?? throw new ArgumentNullException(nameof(textFormatting));
        _formattingState = formattingState;
        _session.Changed += OnSessionChanged;
    }

    public bool IsActive => _isActive;

    public void Toggle()
    {
        if (_isActive)
        {
            Clear();
            return;
        }

        var style = BuildStyleFromSnapshot();
        if (style is null)
        {
            return;
        }

        _style = style;
        _isActive = true;
        _lastSelection = null;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!_isActive || _style is null || _isApplying)
        {
            return;
        }

        var selection = _session.Selection.Normalize();
        if (selection.IsEmpty)
        {
            _lastSelection = selection;
            return;
        }

        if (_lastSelection.HasValue && selection.Equals(_lastSelection.Value))
        {
            return;
        }

        _isApplying = true;
        try
        {
            _textFormatting.Apply(style => ApplyStyle(style, _style));
        }
        finally
        {
            _isApplying = false;
            _lastSelection = selection;
            _isActive = false;
        }
    }

    private void Clear()
    {
        _isActive = false;
        _style = null;
        _lastSelection = null;
    }

    private TextStyleProperties? BuildStyleFromSnapshot()
    {
        if (_formattingState is null)
        {
            return null;
        }

        var snapshot = _formattingState.GetSnapshot();
        var style = new TextStyleProperties();
        var hasValue = false;

        if (TryGet(snapshot.FontFamily, out var fontFamily) && !string.IsNullOrWhiteSpace(fontFamily))
        {
            style.FontFamily = fontFamily;
            hasValue = true;
        }

        if (TryGet(snapshot.FontSize, out var fontSize))
        {
            style.FontSize = fontSize;
            hasValue = true;
        }

        if (TryGet(snapshot.Bold, out var bold))
        {
            style.FontWeight = bold ? DocFontWeight.Bold : DocFontWeight.Normal;
            hasValue = true;
        }

        if (TryGet(snapshot.Italic, out var italic))
        {
            style.FontStyle = italic ? DocFontStyle.Italic : DocFontStyle.Normal;
            hasValue = true;
        }

        if (TryGet(snapshot.UnderlineStyle, out var underlineStyle))
        {
            style.UnderlineStyle = underlineStyle;
            hasValue = true;
        }
        else if (TryGet(snapshot.Underline, out var underline))
        {
            style.Underline = underline;
            hasValue = true;
        }

        if (TryGet(snapshot.Strikethrough, out var strikethrough))
        {
            style.Strikethrough = strikethrough;
            hasValue = true;
        }

        if (TryGet(snapshot.FontColor, out var color))
        {
            style.Color = color;
            hasValue = true;
        }

        if (TryGet(snapshot.HighlightColor, out var highlight))
        {
            style.HighlightColor = highlight;
            hasValue = true;
        }

        if (TryGet(snapshot.VerticalPosition, out var verticalPosition))
        {
            style.VerticalPosition = verticalPosition;
            hasValue = true;
        }

        return hasValue ? style : null;
    }

    private static bool TryGet<T>(EditorValue<T> value, out T result)
    {
        if (!value.HasValue || value.IsMixed)
        {
            result = default!;
            return false;
        }

        result = value.Value!;
        return true;
    }

    private static void ApplyStyle(TextStyleProperties target, TextStyleProperties source)
    {
        if (!string.IsNullOrWhiteSpace(source.FontFamily))
        {
            target.FontFamily = source.FontFamily;
        }

        if (source.FontSize.HasValue)
        {
            target.FontSize = source.FontSize;
        }

        if (source.FontWeight.HasValue)
        {
            target.FontWeight = source.FontWeight;
        }

        if (source.FontStyle.HasValue)
        {
            target.FontStyle = source.FontStyle;
        }

        if (source.UnderlineStyle.HasValue)
        {
            target.UnderlineStyle = source.UnderlineStyle;
            target.Underline = source.UnderlineStyle.Value != DocUnderlineStyle.None;
        }
        else if (source.Underline.HasValue)
        {
            target.Underline = source.Underline;
        }

        if (source.Strikethrough.HasValue)
        {
            target.Strikethrough = source.Strikethrough;
        }

        if (source.Color.HasValue)
        {
            target.Color = source.Color;
        }

        if (source.HighlightColor.HasValue)
        {
            target.HighlightColor = source.HighlightColor;
        }

        if (source.VerticalPosition.HasValue)
        {
            target.VerticalPosition = source.VerticalPosition;
        }

        if (source.Effects is not null)
        {
            target.Effects = source.Effects.Clone();
        }
    }
}
