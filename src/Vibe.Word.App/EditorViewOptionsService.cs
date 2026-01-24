using System;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.App;

public sealed class EditorViewOptionsService : IEditorViewOptionsService
{
    private readonly DocumentView _view;
    private bool _showRuler = true;
    private bool _showNavigationPane;

    public EditorViewOptionsService(DocumentView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public bool ShowInvisibles
    {
        get => _view.ShowInvisibles;
        set => _view.ShowInvisibles = value;
    }

    public bool ShowRuler
    {
        get => _showRuler;
        set => _showRuler = value;
    }

    public bool ShowGridlines
    {
        get => _view.ShowGridlines;
        set => _view.ShowGridlines = value;
    }

    public bool ShowNavigationPane
    {
        get => _showNavigationPane;
        set => _showNavigationPane = value;
    }

    public PageFlowDirection PageMovement
    {
        get => _view.PageFlow;
        set => _view.PageFlow = value;
    }
}
