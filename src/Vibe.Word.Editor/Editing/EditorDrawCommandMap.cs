using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorDrawCommandMap
{
    private readonly EditorCommandRouterAdapter _router;
    private readonly EditorServices _services;

    public EditorDrawCommandMap(EditorCommandRouterAdapter router, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Register()
    {
        _router.RegisterAction(EditorDrawCommandIds.Tools.Select, (_, __) => SetTool(EditorDrawTool.Select), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.LassoSelect, (_, __) => SetTool(EditorDrawTool.LassoSelect), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Pen, (_, __) => SetTool(EditorDrawTool.Pen), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Pencil, (_, __) => SetTool(EditorDrawTool.Pencil), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Highlighter, (_, __) => SetTool(EditorDrawTool.Highlighter), CanUseDrawTools, isUndoable: false);
        _router.RegisterAction(EditorDrawCommandIds.Tools.Eraser, (_, __) => SetTool(EditorDrawTool.Eraser), CanUseDrawTools, isUndoable: false);
    }

    private bool CanUseDrawTools(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IDrawToolService>(out _);
    }

    private void SetTool(EditorDrawTool tool)
    {
        if (_services.TryGet<IDrawToolService>(out var drawTool))
        {
            drawTool.ActiveTool = tool;
        }
    }
}
