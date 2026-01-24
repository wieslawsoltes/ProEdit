using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public static class EditorHomeServiceRegistry
{
    public static EditorCommandRouterAdapter Register(
        EditorServices services,
        EditorCommandDispatcher commands,
        IEditorMutableSession session,
        IFontService? fontService = null,
        IClipboardService? clipboardService = null,
        IEditorViewOptionsService? viewOptionsService = null)
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
        fontService ??= new EditorFontServiceAdapter(session);
        clipboardService ??= new EditorClipboardServiceAdapter(session);
        var formatPainter = new EditorFormatPainter(session, new EditorTextFormattingApplier(session, textNormalizer), formattingState);
        var undoRedoService = new EditorCommandHistory(session);
        var historySnapshotService = new EditorHistorySnapshotService(session, undoRedoService);
        var findReplaceService = new EditorFindReplaceService(session);
        var selectionTextService = new EditorSelectionTextServiceAdapter(session);
        var commandRouter = new EditorCommandRouterAdapter(commands, session);
        commands.History = undoRedoService;
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
        services.Register<IEditorHistorySnapshotService>(historySnapshotService);
        services.Register<IUndoRedoService>(undoRedoService);
        services.Register<IFindReplaceService>(findReplaceService);
        services.Register<ISelectionTextService>(selectionTextService);
        if (viewOptionsService is not null)
        {
            services.Register<IEditorViewOptionsService>(viewOptionsService);
        }
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

        var insertCommandMap = new EditorInsertCommandMap(commandRouter, session);
        insertCommandMap.Register();

        var tableCommandMap = new EditorTableCommandMap(commandRouter, session);
        tableCommandMap.Register();

        var layoutCommandMap = new EditorLayoutCommandMap(commandRouter, session);
        layoutCommandMap.Register();

        var referencesCommandMap = new EditorReferencesCommandMap(commandRouter, session);
        referencesCommandMap.Register();

        var reviewCommandMap = new EditorReviewCommandMap(commandRouter, session, services);
        reviewCommandMap.Register();

        var designCommandMap = new EditorDesignCommandMap(commandRouter, session);
        designCommandMap.Register();

        var mailingsCommandMap = new EditorMailingsCommandMap(commandRouter, session, services);
        mailingsCommandMap.Register();

        var drawCommandMap = new EditorDrawCommandMap(commandRouter, services);
        drawCommandMap.Register();

        var viewCommandMap = new EditorViewCommandMap(commandRouter, services);
        viewCommandMap.Register();

        return commandRouter;
    }
}
