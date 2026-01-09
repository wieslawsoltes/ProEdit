using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public static class EditorHomeServiceRegistry
{
    public static EditorCommandRouterAdapter Register(
        EditorServices services,
        EditorCommandDispatcher commands,
        IEditorMutableSession session,
        IClipboardService? clipboardService = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(session);

        var textNormalizer = new TextContainerNormalizer();
        textNormalizer.EnsureDocumentInlines(session.Document);

        var selectionState = new EditorSelectionStateAdapter(session);
        var formattingState = new EditorFormattingStateAdapter(session);
        var paragraphState = new EditorParagraphServiceAdapter(session);
        var styleService = new EditorStyleServiceAdapter(session);
        var fontService = new EditorFontServiceAdapter(session);
        clipboardService ??= new EditorClipboardServiceAdapter(session);
        var formatPainter = new EditorFormatPainter(session, new EditorTextFormattingApplier(session, textNormalizer), formattingState);
        var undoRedoService = new EditorUndoRedoServiceAdapter();
        var findReplaceService = new EditorFindReplaceServiceAdapter();
        var commandRouter = new EditorCommandRouterAdapter(commands, session);
        var ribbonSnapshotProvider = new RibbonContextSnapshotBuilder(
            selectionState,
            formattingState,
            paragraphState,
            styleService,
            fontService,
            clipboardService,
            undoRedoService,
            findReplaceService,
            session);

        services.Register<ISelectionState>(selectionState);
        services.Register<IFormattingState>(formattingState);
        services.Register<IParagraphService>(paragraphState);
        services.Register<IStyleService>(styleService);
        services.Register<IFontService>(fontService);
        services.Register<IClipboardService>(clipboardService);
        services.Register<IFormatPainterService>(formatPainter);
        services.Register<ITextContainerNormalizer>(textNormalizer);
        services.Register<IUndoRedoService>(undoRedoService);
        services.Register<IFindReplaceService>(findReplaceService);
        services.Register<IEditorCommandRouter>(commandRouter);
        services.Register<IRibbonContextSnapshotProvider>(ribbonSnapshotProvider);

        var commandMap = new EditorHomeCommandMap(
            commandRouter,
            session,
            services,
            selectionState,
            formattingState,
            styleService,
            clipboardService,
            findReplaceService,
            formatPainter,
            textNormalizer);
        commandMap.Register();

        return commandRouter;
    }
}
