using Avalonia.Controls;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

internal sealed class WordEditorProofingDialogService : IProofingDialogService
{
    private readonly Func<Window?> _ownerProvider;
    private readonly DocumentView _view;
    private SpellingGrammarWindow? _spellingWindow;
    private ThesaurusWindow? _thesaurusWindow;

    public WordEditorProofingDialogService(Func<Window?> ownerProvider, DocumentView view)
    {
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public ValueTask ShowSpellingGrammarAsync()
    {
        if (_spellingWindow is null)
        {
            _spellingWindow = new SpellingGrammarWindow(_view);
            _spellingWindow.Closed += (_, _) => _spellingWindow = null;
        }
        else
        {
            _spellingWindow.SetDocumentView(_view);
        }

        var owner = _ownerProvider();
        if (owner is not null)
        {
            _spellingWindow.Show(owner);
        }
        else
        {
            _spellingWindow.Show();
        }
        _spellingWindow.Activate();
        return ValueTask.CompletedTask;
    }

    public ValueTask ShowThesaurusAsync()
    {
        if (_thesaurusWindow is null)
        {
            _thesaurusWindow = new ThesaurusWindow(_view);
            _thesaurusWindow.Closed += (_, _) => _thesaurusWindow = null;
        }
        else
        {
            _thesaurusWindow.SetDocumentView(_view);
        }

        var owner = _ownerProvider();
        if (owner is not null)
        {
            _thesaurusWindow.Show(owner);
        }
        else
        {
            _thesaurusWindow.Show();
        }
        _thesaurusWindow.Activate();
        return ValueTask.CompletedTask;
    }
}
