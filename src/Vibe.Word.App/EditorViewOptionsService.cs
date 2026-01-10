using System;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public sealed class EditorViewOptionsService : IEditorViewOptionsService
{
    private readonly DocumentView _view;

    public EditorViewOptionsService(DocumentView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public bool ShowInvisibles
    {
        get => _view.ShowInvisibles;
        set => _view.ShowInvisibles = value;
    }
}
