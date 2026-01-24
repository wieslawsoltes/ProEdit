using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorViewCommandMap
{
    private readonly EditorCommandRouterAdapter _router;
    private readonly EditorServices _services;

    public EditorViewCommandMap(EditorCommandRouterAdapter router, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Register()
    {
        _router.RegisterAction(EditorViewCommandIds.Show.Ruler, (_, payload) => ToggleRuler(payload), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Show.Gridlines, (_, payload) => ToggleGridlines(payload), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Show.NavigationPane, (_, payload) => ToggleNavigationPane(payload), CanToggleView, isUndoable: false);

        _router.RegisterAction(EditorViewCommandIds.PageMovement.Vertical, (_, __) => SetPageMovement(PageFlowDirection.Vertical), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.PageMovement.SideToSide, (_, __) => SetPageMovement(PageFlowDirection.Horizontal), CanToggleView, isUndoable: false);
    }

    private bool CanToggleView(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IEditorViewOptionsService>(out _);
    }

    private void ToggleRuler(object? payload)
    {
        if (!TryGetViewOptions(out var options))
        {
            return;
        }

        options.ShowRuler = payload is bool value ? value : !options.ShowRuler;
    }

    private void ToggleGridlines(object? payload)
    {
        if (!TryGetViewOptions(out var options))
        {
            return;
        }

        options.ShowGridlines = payload is bool value ? value : !options.ShowGridlines;
    }

    private void ToggleNavigationPane(object? payload)
    {
        if (!TryGetViewOptions(out var options))
        {
            return;
        }

        options.ShowNavigationPane = payload is bool value ? value : !options.ShowNavigationPane;
    }

    private void SetPageMovement(PageFlowDirection movement)
    {
        if (!TryGetViewOptions(out var options))
        {
            return;
        }

        options.PageMovement = movement;
    }

    private bool TryGetViewOptions(out IEditorViewOptionsService options)
    {
        return _services.TryGet(out options);
    }
}
