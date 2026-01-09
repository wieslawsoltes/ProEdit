using Avalonia.Input.Platform;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private static readonly IReadOnlyList<string> DefaultFormats = new[] { "text/plain" };
    private readonly Func<IClipboard?> _clipboardProvider;
    private readonly Func<bool>? _canCopyEvaluator;
    private readonly Func<bool>? _canCutEvaluator;
    private readonly Func<bool>? _canPasteEvaluator;
    private readonly IReadOnlyList<string> _formats;
    private string? _lastText;

    public AvaloniaClipboardService(
        Func<IClipboard?> clipboardProvider,
        Func<bool>? canCopyEvaluator = null,
        Func<bool>? canCutEvaluator = null,
        Func<bool>? canPasteEvaluator = null,
        IReadOnlyList<string>? supportedFormats = null)
    {
        _clipboardProvider = clipboardProvider ?? throw new ArgumentNullException(nameof(clipboardProvider));
        _canCopyEvaluator = canCopyEvaluator;
        _canCutEvaluator = canCutEvaluator;
        _canPasteEvaluator = canPasteEvaluator;
        _formats = supportedFormats ?? DefaultFormats;
    }

    public bool CanCopy => _canCopyEvaluator?.Invoke() ?? true;

    public bool CanCut => _canCutEvaluator?.Invoke() ?? CanCopy;

    public bool CanPaste => _canPasteEvaluator?.Invoke() ?? TryGetText(out _);

    public IReadOnlyList<string> SupportedFormats => _formats;

    public bool TryGetText(out string text)
    {
        var clipboard = _clipboardProvider();
        if (clipboard is not null)
        {
            try
            {
                var result = clipboard.TryGetTextAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(result))
                {
                    _lastText = result;
                    text = result;
                    return true;
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrEmpty(_lastText))
        {
            text = _lastText;
            return true;
        }

        text = string.Empty;
        return false;
    }

    public void SetText(string text)
    {
        var clipboard = _clipboardProvider();
        _lastText = string.IsNullOrEmpty(text) ? null : text;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            clipboard.SetTextAsync(text).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }
}
