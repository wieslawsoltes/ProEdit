using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Macros;
using Vibe.Office.Vba.Runtime;

namespace Vibe.Word.Editor.Editing;

public static class EditorHomeServiceRegistry
{
    public static EditorCommandRouterAdapter Register(
        EditorServices services,
        EditorCommandDispatcher commands,
        IEditorMutableSession session,
        IFontService? fontService = null,
        IClipboardService? clipboardService = null,
        IEditorViewOptionsService? viewOptionsService = null,
        Func<Document>? documentFactory = null)
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
        var tableStyleService = new EditorTableStyleServiceAdapter(session);
        var tableSelectionProvider = new EditorTableSelectionSnapshotProvider(session);
        fontService ??= new EditorFontServiceAdapter(session);
        clipboardService ??= new EditorClipboardServiceAdapter(session);
        var formatPainter = new EditorFormatPainter(session, new EditorTextFormattingApplier(session, textNormalizer), formattingState);
        var undoRedoService = new EditorCommandHistory(session);
        var historySnapshotService = new EditorHistorySnapshotService(session, undoRedoService);
        var findReplaceService = new EditorFindReplaceService(session);
        var selectionTextService = new EditorSelectionTextServiceAdapter(session);
        var vbaHost = new WordVbaHost(session, selectionTextService, documentFactory: documentFactory);
        var vbaRuntime = new VbaRuntime(vbaHost);
        vbaHost.SetRuntime(vbaRuntime);
        var macroEngine = new MacroEngine(session.Document, vbaRuntime: vbaRuntime);
        var formatProfileService = new EditorFormatProfileService();
        var commandRouter = new EditorCommandRouterAdapter(commands, session, macroEngine, formatProfileService);
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

        var spellRegistry = SpellDictionaryRegistry.CreateDefault();
        var languageDetector = new ScriptLanguageDetector("en-US");
        var proofingOptionsStore = new ProofingOptionsStore();
        var proofingOptions = proofingOptionsStore.Load();
        var engineRegistry = new ProofingEngineRegistry(spellRegistry, new EditorServicesAdapter(services));
        engineRegistry.RegisterBuiltIn(new HunspellSpellEngineFactory());
        engineRegistry.RegisterBuiltIn(new LanguageToolEngineFactory());
        var profileManager = new ProofingProfileManager(engineRegistry, spellRegistry, proofingOptions);
        var proofingService = new EditorProofingService(session, profileManager, session as IEditorLayoutRefreshService, languageDetector);
        var autoCorrectService = AutoCorrectService.CreateDefault();

        services.Register<ISelectionState>(selectionState);
        services.Register<IFormattingState>(formattingState);
        services.Register<IParagraphService>(paragraphState);
        services.Register<IStyleService>(styleService);
        services.Register<IStyleManagerService>(styleService);
        services.Register<ITableStyleService>(tableStyleService);
        services.Register<ITableSelectionSnapshotProvider>(tableSelectionProvider);
        services.Register<IFontService>(fontService);
        services.Register<IClipboardService>(clipboardService);
        services.Register<IFormatPainterService>(formatPainter);
        services.Register<ITextContainerNormalizer>(textNormalizer);
        services.Register<IEditorHistorySnapshotService>(historySnapshotService);
        services.Register<IUndoRedoService>(undoRedoService);
        services.Register<IFindReplaceService>(findReplaceService);
        services.Register<ISelectionTextService>(selectionTextService);
        services.Register<IVbaRuntime>(vbaRuntime);
        services.Register<IMacroEngine>(macroEngine);
        services.Register<IEditorCommandObserver>(macroEngine);
        if (viewOptionsService is not null)
        {
            services.Register<IEditorViewOptionsService>(viewOptionsService);
        }
        services.Register<IProofingService>(proofingService);
        services.Register<IProofingToggleService>(proofingService);
        services.Register<IProofingProfileRegistry>(profileManager);
        services.Register<IProofingProfileManager>(profileManager);
        services.Register<IProofingEngineRegistry>(engineRegistry);
        services.Register<IProofingOptionsStore>(proofingOptionsStore);
        services.Register<ISpellDictionaryRegistry>(spellRegistry);
        services.Register<ILanguageDetector>(languageDetector);
        services.Register<IAutoCorrectService>(autoCorrectService);
        services.Register<IEditorCommandRouter>(commandRouter);
        services.Register<IRibbonContextSnapshotProvider>(ribbonSnapshotProvider);
        services.Register<IEditorFormatProfileService>(formatProfileService);

        if (session is IProofingSpanProviderHost proofingHost)
        {
            proofingHost.SetProofingSpanProvider(proofingService);
        }

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

        var insertCommandMap = new EditorInsertCommandMap(commandRouter, session, services);
        insertCommandMap.Register();

        var tableCommandMap = new EditorTableCommandMap(commandRouter, session);
        tableCommandMap.Register();

        var layoutCommandMap = new EditorLayoutCommandMap(commandRouter, session);
        layoutCommandMap.Register();

        var referencesCommandMap = new EditorReferencesCommandMap(commandRouter, session, services);
        referencesCommandMap.Register();

        var reviewCommandMap = new EditorReviewCommandMap(commandRouter, session, services);
        reviewCommandMap.Register();

        var designCommandMap = new EditorDesignCommandMap(commandRouter, session, services);
        designCommandMap.Register();

        var mailingsCommandMap = new EditorMailingsCommandMap(commandRouter, session, services);
        mailingsCommandMap.Register();

        var drawCommandMap = new EditorDrawCommandMap(commandRouter, session, services);
        drawCommandMap.Register();

        var viewCommandMap = new EditorViewCommandMap(commandRouter, services);
        viewCommandMap.Register();

        return commandRouter;
    }

    private sealed class EditorServicesAdapter : IServiceProvider
    {
        private readonly EditorServices _services;

        public EditorServicesAdapter(EditorServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public object? GetService(Type serviceType)
        {
            return _services.TryGet(serviceType, out var service) ? service : null;
        }
    }
}
