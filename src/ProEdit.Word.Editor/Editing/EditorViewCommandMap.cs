using ProEdit.Editing;
using ProEdit.Layout;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorViewCommandMap
{
    private readonly EditorCommandRouterAdapter _router;
    private readonly EditorServices _services;
    private const int DefaultMultiplePages = 2;

    public EditorViewCommandMap(EditorCommandRouterAdapter router, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Register()
    {
        _router.RegisterAction(EditorViewCommandIds.Views.ReadMode, (_, __) => SetViewMode(EditorViewMode.ReadMode), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Views.PrintLayout, (_, __) => SetViewMode(EditorViewMode.PrintLayout), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Views.WebLayout, (_, __) => SetViewMode(EditorViewMode.WebLayout), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Views.Outline, (_, __) => SetViewMode(EditorViewMode.Outline), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Views.Draft, (_, __) => SetViewMode(EditorViewMode.Draft), CanToggleView, isUndoable: false);

        _router.RegisterAction(EditorViewCommandIds.Show.Ruler, (_, payload) => ToggleRuler(payload), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Show.Gridlines, (_, payload) => ToggleGridlines(payload), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Show.NavigationPane, (_, payload) => ToggleNavigationPane(payload), CanToggleView, isUndoable: false);

        _router.RegisterAction(EditorViewCommandIds.PageMovement.Vertical, (_, __) => SetPageMovement(PageFlowDirection.Vertical), CanToggleView, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.PageMovement.SideToSide, (_, __) => SetPageMovement(PageFlowDirection.Horizontal), CanToggleView, isUndoable: false);

        _router.RegisterAction(EditorViewCommandIds.Zoom.ZoomDialog, (_, __) => OpenZoomDialog(), CanZoom, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Zoom.Zoom100, (_, __) => ZoomToPercent(100f), CanZoom, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Zoom.OnePage, (_, __) => ZoomToWholePage(), CanZoom, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Zoom.MultiplePages, (_, payload) => ZoomToMultiplePages(payload), CanZoom, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Zoom.PageWidth, (_, __) => ZoomToPageWidth(), CanZoom, isUndoable: false);

        _router.RegisterAction(EditorViewCommandIds.Window.NewWindow, (_, __) => ExecuteWindowCommand(EditorWindowCommand.NewWindow), CanToggleWindow, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Window.ArrangeAll, (_, __) => ExecuteWindowCommand(EditorWindowCommand.ArrangeAll), CanToggleWindow, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Window.Split, (_, __) => ExecuteWindowCommand(EditorWindowCommand.Split), CanToggleWindow, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Window.ViewSideBySide, (_, __) => ExecuteWindowCommand(EditorWindowCommand.ViewSideBySide), CanToggleWindow, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Window.SynchronousScrolling, (_, __) => ExecuteWindowCommand(EditorWindowCommand.SynchronousScrolling), CanToggleWindow, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Window.ResetWindowPosition, (_, __) => ExecuteWindowCommand(EditorWindowCommand.ResetPosition), CanToggleWindow, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Window.SwitchWindows, (_, __) => ExecuteWindowCommand(EditorWindowCommand.SwitchWindows), CanToggleWindow, isUndoable: false);

        _router.RegisterAction(EditorViewCommandIds.Macros.Open, (_, __) => OpenMacroManager(), CanToggleMacros, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Macros.RecordMacro, (_, __) => ToggleRecordMacro(), CanToggleMacros, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Macros.VbaEditor, (_, __) => OpenVbaEditor(), CanToggleMacros, isUndoable: false);
        _router.RegisterAction(EditorViewCommandIds.Macros.Debug, (_, __) => StartMacroDebug(), CanToggleMacros, isUndoable: false);
    }

    private bool CanToggleView(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IEditorViewOptionsService>(out _);
    }

    private bool CanToggleWindow(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IEditorWindowService>(out _);
    }

    private bool CanZoom(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IEditorZoomService>(out _);
    }

    private bool CanToggleMacros(RibbonContextSnapshot? context, object? payload)
    {
        return _services.TryGet<IMacroManagerService>(out _);
    }

    private void ExecuteWindowCommand(EditorWindowCommand command)
    {
        if (_services.TryGet<IEditorWindowService>(out var service))
        {
            _ = service.ExecuteAsync(command);
        }
    }

    private void OpenZoomDialog()
    {
        if (_services.TryGet<IEditorZoomService>(out var zoom))
        {
            _ = zoom.OpenZoomDialogAsync();
        }
    }

    private void ZoomToPercent(float percent)
    {
        if (_services.TryGet<IEditorZoomService>(out var zoom))
        {
            zoom.ZoomToPercent(percent);
        }
    }

    private void ZoomToPageWidth()
    {
        if (_services.TryGet<IEditorZoomService>(out var zoom))
        {
            zoom.ZoomToPageWidth();
        }
    }

    private void ZoomToWholePage()
    {
        if (_services.TryGet<IEditorZoomService>(out var zoom))
        {
            zoom.ZoomToWholePage();
        }
    }

    private void ZoomToMultiplePages(object? payload)
    {
        if (!_services.TryGet<IEditorZoomService>(out var zoom))
        {
            return;
        }

        var pagesPerRow = payload switch
        {
            int count => Math.Max(1, count),
            float count => Math.Max(1, (int)MathF.Round(count)),
            _ => DefaultMultiplePages
        };

        zoom.ZoomToMultiplePages(pagesPerRow);
    }

    private void OpenMacroManager()
    {
        if (_services.TryGet<IMacroManagerService>(out var service))
        {
            _ = service.OpenMacroManagerAsync();
        }
    }

    private void ToggleRecordMacro()
    {
        if (_services.TryGet<IMacroManagerService>(out var service))
        {
            _ = service.ToggleRecordMacroAsync();
        }
    }

    private void OpenVbaEditor()
    {
        if (_services.TryGet<IMacroManagerService>(out var service))
        {
            _ = service.OpenVbaEditorAsync();
        }
    }

    private void StartMacroDebug()
    {
        if (_services.TryGet<IMacroManagerService>(out var service))
        {
            _ = service.StartDebugAsync();
        }
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

    private void SetViewMode(EditorViewMode mode)
    {
        if (!TryGetViewOptions(out var options))
        {
            return;
        }

        options.ViewMode = mode;
    }

    private bool TryGetViewOptions(out IEditorViewOptionsService options)
    {
        return _services.TryGet(out options);
    }
}
