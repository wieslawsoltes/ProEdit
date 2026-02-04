using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SkiaSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Html;
using Vibe.Office.Layout;
using Vibe.Office.Markdown;
using Vibe.Office.Macros;
using Vibe.Office.Pdf;
using Vibe.Office.Pdf.Documents;
using Vibe.Office.Pdf.PdfPig;
using Vibe.Office.Pdf.PdfSharp;
using Vibe.Office.Printing;
using Vibe.Office.Printing.Avalonia;
using Vibe.Office.Printing.Documents;
using Vibe.Office.Printing.Skia;
using Vibe.Office.Printing.System;
using Vibe.Office.Vba.Runtime;
using Vibe.Office.OpenXml;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;
using Vibe.Office.Ribbon;
using Vibe.Office.Ribbon.Avalonia;

namespace Vibe.Word.Avalonia;

public partial class WordEditorControl : UserControl
{
    private const string WindowTitleBase = "Vibe Word";
    private const float PdfIncrementalOverlayDpi = 144f;
    private const int PdfIncrementalOverlayJpegQuality = 85;
    private readonly DocumentView? _editorView;
    private readonly HorizontalRuler? _horizontalRuler;
    private readonly VerticalRuler? _verticalRuler;
    private readonly RulerCornerControl? _rulerCorner;
    private readonly Border? _navigationPane;
    private readonly ListBox? _navigationPaneList;
    private readonly ListBox? _pagePaneList;
    private readonly Grid? _layoutGrid;
    private readonly Grid? _rightPane;
    private readonly Border? _equationPanel;
    private readonly Border? _reviewPane;
    private readonly ListBox? _reviewCommentsList;
    private readonly ListBox? _reviewChangesList;
    private readonly ListBox? _reviewProofingList;
    private readonly TextBox? _reviewCommentEditor;
    private readonly Button? _reviewCommentApplyButton;
    private readonly Button? _reviewCommentDeleteButton;
    private readonly Button? _reviewCommentReplyButton;
    private readonly Button? _reviewCommentResolveButton;
    private readonly Button? _reviewChangeAcceptButton;
    private readonly Button? _reviewChangeRejectButton;
    private readonly Button? _reviewChangePreviousButton;
    private readonly Button? _reviewChangeNextButton;
    private readonly Button? _reviewProofingPreviousButton;
    private readonly Button? _reviewProofingNextButton;
    private readonly Button? _reviewProofingApplyButton;
    private readonly Button? _reviewProofingIgnoreButton;
    private readonly Button? _reviewProofingAddButton;
    private readonly Button? _reviewPaneCloseButton;
    private readonly Border? _loadingOverlay;
    private readonly TextBlock? _loadingText;
    private readonly RibbonControl? _ribbon;
    private readonly TextBlock? _statusPageText;
    private readonly Border? _pdfFixedLayoutBadge;
    private readonly TextBlock? _pdfFixedLayoutText;
    private readonly Button? _pdfReimportButton;
    private readonly Slider? _zoomSlider;
    private readonly Button? _zoomInButton;
    private readonly Button? _zoomOutButton;
    private readonly Button? _zoomResetButton;
    private WindowNotificationManager? _notificationManager;
    private FindReplaceDialog? _findReplaceDialog;
    private VbaToolingWindow? _vbaToolingWindow;
    private NotesPaneWindow? _notesPaneWindow;
    private HtmlSourceWindow? _htmlSourceWindow;
    private readonly RibbonQuickAccessStore _quickAccessStore = new();
    private readonly PdfImportPreferencesStore _pdfImportPreferencesStore = new();
    private readonly PdfEngine _pdfEngine;
    private readonly ObservableCollection<RibbonGalleryItem> _styleGalleryItems = new();
    private readonly Dictionary<string, RibbonGalleryItem> _styleGalleryItemMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<NavigationPaneItem> _navigationItems = new();
    private readonly ObservableCollection<PageNavigationItem> _pageItems = new();
    private readonly ObservableCollection<ReviewCommentItem> _reviewCommentItems = new();
    private readonly ObservableCollection<ReviewRevisionItem> _reviewRevisionItems = new();
    private readonly ObservableCollection<ReviewProofingItem> _reviewProofingItems = new();
    private IProofingService? _proofingService;
    private bool _proofingRefreshPending;
    private string? _currentPath;
    private DocumentLayout? _navigationLayout;
    private Document? _navigationDocument;
    private IStyleManagerService? _styleManagerService;
    private bool _pendingStyleGalleryRefresh;
    private bool _isLoading;
    private bool _suppressQuickAccessSave;
    private bool _suppressZoomUpdate;
    private bool _suppressPageSelection;
    private bool _suppressReviewSelection;
    private bool _fixedLayoutWarningShown;
    private bool _pdfDiagnosticsShown;
    private bool _pdfImportNotificationShown;
    private static readonly FilePickerFileType DocxFileType = new("Word Documents")
    {
        Patterns = new[] { "*.docx", "*.docm" }
    };
    private static readonly FilePickerFileType PdfFileType = new("PDF")
    {
        Patterns = new[] { "*.pdf" }
    };
    private static readonly FilePickerFileType SupportedFileType = new("Supported Files")
    {
        Patterns = new[] { "*.docx", "*.docm", "*.md", "*.markdown", "*.html", "*.htm", "*.pdf" }
    };
    private static readonly FilePickerFileType MarkdownFileType = new("Markdown")
    {
        Patterns = new[] { "*.md", "*.markdown" }
    };
    private static readonly FilePickerFileType HtmlFileType = new("HTML")
    {
        Patterns = new[] { "*.html", "*.htm" }
    };
    private static readonly FilePickerFileType ImageFileType = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.svg" }
    };
    private static readonly FilePickerFileType ModelFileType = new("3D Models")
    {
        Patterns = new[] { "*.glb", "*.gltf", "*.obj", "*.fbx" }
    };
    private static readonly FilePickerFileType ObjectFileType = new("All Files")
    {
        Patterns = new[] { "*.*" }
    };
    private const float PageThumbnailWidth = 120f;
    private const float PageThumbnailHeight = 160f;
    private const double ReviewCommentReplyIndent = 16d;

    private Window? _ownerWindow;
    private string? _pendingOpenPath;
    private bool _isAttachedToVisualTree;

    public Window? OwnerWindow
    {
        get => _ownerWindow;
        set => _ownerWindow = value;
    }

    public Func<Document?, Window>? WindowFactory { get; set; }

    public WordEditorControl()
        : this(null, null)
    {
    }

    public WordEditorControl(Document? document, string? path)
    {
        InitializeComponent();

        _pdfEngine = CreatePdfEngine();

        _ribbon = this.FindControl<RibbonControl>("Ribbon");
        _editorView = this.FindControl<DocumentView>("EditorView");
        _horizontalRuler = this.FindControl<HorizontalRuler>("HorizontalRuler");
        _verticalRuler = this.FindControl<VerticalRuler>("VerticalRuler");
        _rulerCorner = this.FindControl<RulerCornerControl>("RulerCorner");
        _navigationPane = this.FindControl<Border>("NavigationPane");
        _navigationPaneList = this.FindControl<ListBox>("NavigationPaneList");
        _pagePaneList = this.FindControl<ListBox>("PagePaneList");
        _layoutGrid = this.FindControl<Grid>("LayoutGrid");
        _rightPane = this.FindControl<Grid>("RightPane");
        _reviewPane = this.FindControl<Border>("ReviewPane");
        _reviewCommentsList = this.FindControl<ListBox>("ReviewCommentsList");
        _reviewChangesList = this.FindControl<ListBox>("ReviewChangesList");
        _reviewProofingList = this.FindControl<ListBox>("ReviewProofingList");
        _reviewCommentEditor = this.FindControl<TextBox>("ReviewCommentEditor");
        _reviewCommentApplyButton = this.FindControl<Button>("ReviewCommentApplyButton");
        _reviewCommentDeleteButton = this.FindControl<Button>("ReviewCommentDeleteButton");
        _reviewCommentReplyButton = this.FindControl<Button>("ReviewCommentReplyButton");
        _reviewCommentResolveButton = this.FindControl<Button>("ReviewCommentResolveButton");
        _reviewChangeAcceptButton = this.FindControl<Button>("ReviewChangeAcceptButton");
        _reviewChangeRejectButton = this.FindControl<Button>("ReviewChangeRejectButton");
        _reviewChangePreviousButton = this.FindControl<Button>("ReviewChangePreviousButton");
        _reviewChangeNextButton = this.FindControl<Button>("ReviewChangeNextButton");
        _reviewProofingPreviousButton = this.FindControl<Button>("ReviewProofingPreviousButton");
        _reviewProofingNextButton = this.FindControl<Button>("ReviewProofingNextButton");
        _reviewProofingApplyButton = this.FindControl<Button>("ReviewProofingApplyButton");
        _reviewProofingIgnoreButton = this.FindControl<Button>("ReviewProofingIgnoreButton");
        _reviewProofingAddButton = this.FindControl<Button>("ReviewProofingAddButton");
        _reviewPaneCloseButton = this.FindControl<Button>("ReviewPaneCloseButton");
        var equationEditor = this.FindControl<EquationEditor>("EquationEditor");
        _equationPanel = this.FindControl<Border>("EquationEditorPanel");
        _loadingOverlay = this.FindControl<Border>("LoadingOverlay");
        _loadingText = this.FindControl<TextBlock>("LoadingText");
        _statusPageText = this.FindControl<TextBlock>("StatusPageText");
        _pdfFixedLayoutBadge = this.FindControl<Border>("PdfFixedLayoutBadge");
        _pdfFixedLayoutText = this.FindControl<TextBlock>("PdfFixedLayoutText");
        _pdfReimportButton = this.FindControl<Button>("PdfReimportButton");
        _zoomSlider = this.FindControl<Slider>("ZoomSlider");
        _zoomInButton = this.FindControl<Button>("ZoomInButton");
        _zoomOutButton = this.FindControl<Button>("ZoomOutButton");
        _zoomResetButton = this.FindControl<Button>("ZoomResetButton");

        if (_navigationPane is not null)
        {
            _navigationPane.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty && _navigationPane.IsVisible)
                {
                    RefreshNavigationPaneItems(force: true);
                }
            };
        }

        if (_zoomSlider is not null)
        {
            _zoomSlider.ValueChanged += (_, e) =>
            {
                if (_suppressZoomUpdate || _editorView is null)
                {
                    return;
                }

                _editorView.ZoomToPercent((float)e.NewValue);
                UpdateZoomUi();
            };
        }

        if (_zoomInButton is not null)
        {
            _zoomInButton.Click += (_, _) =>
            {
                _editorView?.ZoomIn();
                UpdateZoomUi();
            };
        }

        if (_pdfReimportButton is not null)
        {
            _pdfReimportButton.Click += async (_, _) => await ReimportPdfAsync();
        }

        if (_zoomOutButton is not null)
        {
            _zoomOutButton.Click += (_, _) =>
            {
                _editorView?.ZoomOut();
                UpdateZoomUi();
            };
        }

        if (_zoomResetButton is not null)
        {
            _zoomResetButton.Click += (_, _) =>
            {
                _editorView?.ZoomToDefault();
                UpdateZoomUi();
            };
        }

        if (_editorView is not null)
        {
            if (_horizontalRuler is not null)
            {
                _horizontalRuler.EditorView = _editorView;
            }

            if (_verticalRuler is not null)
            {
                _verticalRuler.EditorView = _editorView;
            }

            if (_rulerCorner is not null && _horizontalRuler is not null)
            {
                _horizontalRuler.DefaultTabAlignment = _rulerCorner.SelectedAlignment;
                _rulerCorner.SelectedAlignmentChanged += (_, alignment) => _horizontalRuler.DefaultTabAlignment = alignment;
            }

            if (_navigationPaneList is not null)
            {
                _navigationPaneList.ItemsSource = _navigationItems;
                _navigationPaneList.SelectionChanged += OnNavigationSelectionChanged;
            }

        if (_pagePaneList is not null)
        {
            _pagePaneList.ItemsSource = _pageItems;
            _pagePaneList.SelectionChanged += OnPageSelectionChanged;
        }

        if (_reviewCommentsList is not null)
        {
            _reviewCommentsList.ItemsSource = _reviewCommentItems;
            _reviewCommentsList.SelectionChanged += OnReviewCommentSelectionChanged;
        }

        if (_reviewChangesList is not null)
        {
            _reviewChangesList.ItemsSource = _reviewRevisionItems;
            _reviewChangesList.SelectionChanged += OnReviewChangeSelectionChanged;
        }

        if (_reviewProofingList is not null)
        {
            _reviewProofingList.ItemsSource = _reviewProofingItems;
            _reviewProofingList.SelectionChanged += OnReviewProofingSelectionChanged;
        }

        if (_reviewCommentApplyButton is not null)
        {
            _reviewCommentApplyButton.Click += OnReviewCommentApplyClicked;
        }

        if (_reviewCommentReplyButton is not null)
        {
            _reviewCommentReplyButton.Click += OnReviewCommentReplyClicked;
        }

        if (_reviewCommentResolveButton is not null)
        {
            _reviewCommentResolveButton.Click += OnReviewCommentResolveClicked;
        }

        if (_reviewCommentDeleteButton is not null)
        {
            _reviewCommentDeleteButton.Click += OnReviewCommentDeleteClicked;
        }

        if (_reviewChangeAcceptButton is not null)
        {
            _reviewChangeAcceptButton.Click += OnReviewChangeAcceptClicked;
        }

        if (_reviewChangeRejectButton is not null)
        {
            _reviewChangeRejectButton.Click += OnReviewChangeRejectClicked;
        }

        if (_reviewChangePreviousButton is not null)
        {
            _reviewChangePreviousButton.Click += OnReviewChangePreviousClicked;
        }

        if (_reviewChangeNextButton is not null)
        {
            _reviewChangeNextButton.Click += OnReviewChangeNextClicked;
        }

        if (_reviewProofingPreviousButton is not null)
        {
            _reviewProofingPreviousButton.Click += OnReviewProofingPreviousClicked;
        }

        if (_reviewProofingNextButton is not null)
        {
            _reviewProofingNextButton.Click += OnReviewProofingNextClicked;
        }

        if (_reviewProofingApplyButton is not null)
        {
            _reviewProofingApplyButton.Click += OnReviewProofingApplyClicked;
        }

        if (_reviewProofingIgnoreButton is not null)
        {
            _reviewProofingIgnoreButton.Click += OnReviewProofingIgnoreClicked;
        }

        if (_reviewProofingAddButton is not null)
        {
            _reviewProofingAddButton.Click += OnReviewProofingAddClicked;
        }

        if (_reviewPaneCloseButton is not null)
        {
            _reviewPaneCloseButton.Click += (_, _) => SetReviewPaneVisible(false);
        }

        if (_reviewCommentEditor is not null)
        {
            _reviewCommentEditor.IsEnabled = false;
        }

        var navigationColumn = _layoutGrid?.ColumnDefinitions.Count > 0
            ? _layoutGrid.ColumnDefinitions[0]
            : null;

        _editorView.RegisterService<IEditorViewOptionsService>(new WordEditorViewOptionsService(
            _editorView,
            _horizontalRuler,
            _verticalRuler,
            _rulerCorner,
            _navigationPane,
            navigationColumn));

        var dialogService = new WordEditorDialogService(ResolveOwnerWindow);
        _editorView.RegisterService<IEditorDialogService>(dialogService);
        _editorView.RegisterService<IContentControlInteractionService>(new WordEditorContentControlInteractionService(dialogService));
        _editorView.RegisterService<IProofingDialogService>(new WordEditorProofingDialogService(ResolveOwnerWindow, _editorView));
        _editorView.RegisterService<IEditorWindowService>(
            new WordEditorWindowService(
                ResolveOwnerWindow,
                () => _editorView.Document,
                dialogService,
                document => WindowFactory?.Invoke(document)));
        _editorView.RegisterService<IEditorZoomService>(new WordEditorZoomService(_editorView, OpenZoomDialogAsync));
        _editorView.RegisterService<IMacroManagerService>(
            new WordEditorMacroManagerService(
                OpenMacroManagerAsync,
                ToggleMacroRecordingFromServiceAsync,
                OpenVbaToolingWindowFromServiceAsync,
                StartMacroDebugFromServiceAsync));

        _editorView.RegisterService<IDrawToolService>(new DrawToolService());
        _editorView.RegisterService<IInkReplayService>(new InkReplayService(_editorView));
        _editorView.RegisterService<IMailMergeSourceManager>(new MailMergeSourceManager(ResolveOwnerWindow));
        _editorView.RegisterService<ICitationSourceManager>(new CitationSourceManager(ResolveOwnerWindow));

        _editorView.RegisterService<IStylePaneService>(new StylesPaneService(
            ResolveOwnerWindow,
            () => _editorView.TryGetService<IStyleManagerService>(out var service) ? service : null,
            () => _editorView.TryGetService<IFontService>(out var fontService) ? fontService : null));

        _editorView.RegisterService<IReviewPaneService>(new WordEditorReviewPaneService(
            _editorView,
            _reviewPane,
            RefreshReviewPaneItems,
            SetReviewPaneVisible));

        AttachStyleManagerEvents();
    }

        if (_ribbon is not null)
        {
            var model = BuildRibbonModel();
            _ribbon.Model = model;
            _ribbon.CustomizeQuickAccessRequested += OnCustomizeQuickAccessRequested;
            model.QuickAccess.CollectionChanged += OnQuickAccessCollectionChanged;
            _ = RestoreQuickAccessAsync(model);
        }

        if (_editorView is not null && equationEditor is not null)
        {
            void UpdateEquationEditor(EquationInline? equation)
            {
                equationEditor.SetEquation(equation);
                var visible = equation is not null;
                equationEditor.IsVisible = visible;
                if (_equationPanel is not null)
                {
                    _equationPanel.IsVisible = visible;
                }

                _ribbon?.RefreshState();
                UpdateRightPaneVisibility();
            }

            _editorView.SelectedEquationChanged += (_, equation) => UpdateEquationEditor(equation);
            equationEditor.EquationEdited += (_, _) => _editorView.RefreshLayout();
            UpdateEquationEditor(_editorView.SelectedEquation);
        }

        if (_editorView is not null)
        {
            _editorView.EditorStateChanged += (_, _) =>
            {
                AttachProofingService();
                _ribbon?.RefreshState();
                UpdateStatusBar();
                RefreshNavigationPaneItems();
                RefreshReviewPaneItems();
            };
            _editorView.ZoomChanged += (_, _) => UpdateZoomUi();
            UpdateZoomUi();
            UpdateStatusBar();
            RefreshNavigationPaneItems(force: true);
        }

        ApplyInitialDocument(document, path);
    }

    public void SetInitialDocument(Document? document, string? path)
    {
        ApplyInitialDocument(document, path);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;

        if (!string.IsNullOrWhiteSpace(_pendingOpenPath))
        {
            var pending = _pendingOpenPath;
            _pendingOpenPath = null;
            _ = LoadDocumentAsync(pending);
        }
    }

    private void ApplyInitialDocument(Document? document, string? path)
    {
        if (_editorView is null)
        {
            return;
        }

        _pendingOpenPath = null;
        if (document is not null)
        {
            _editorView.LoadDocument(document);
            _currentPath = path;
            UpdateWindowTitle();
            RefreshStyleGalleryItems();
            AttachStyleManagerEvents();
            _ribbon?.RefreshState();
            RefreshNavigationPaneItems(force: true);
            UpdateOpenAuxiliaryWindows();
            return;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (_isAttachedToVisualTree)
            {
                _ = LoadDocumentAsync(path);
            }
            else
            {
                _pendingOpenPath = path;
            }
            return;
        }

        UpdateWindowTitle();
    }

    private TopLevel? ResolveTopLevel()
    {
        return _ownerWindow ?? TopLevel.GetTopLevel(this);
    }

    private Window? ResolveOwnerWindow()
    {
        return ResolveTopLevel() as Window;
    }

    private WindowNotificationManager? ResolveNotificationManager()
    {
        if (_notificationManager is not null)
        {
            return _notificationManager;
        }

        var topLevel = ResolveTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        _notificationManager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3,
            Margin = new Thickness(0, 56, 16, 16)
        };

        return _notificationManager;
    }

    private void ShowNotification(string title, string message, NotificationType type = NotificationType.Information, TimeSpan? expiration = null)
    {
        var manager = ResolveNotificationManager();
        if (manager is null)
        {
            return;
        }

        var duration = expiration ?? TimeSpan.FromSeconds(6);
        manager.Show(new Notification(title, message, type, duration));
    }

    private void UpdateWindowTitle()
    {
        var owner = ResolveOwnerWindow();
        if (owner is null)
        {
            return;
        }

        var fileName = ResolveRibbonDocumentTitle();
        owner.Title = $"{fileName} - {WindowTitleBase}";
        UpdateRibbonTopBarTitle(fileName);
    }

    private string ResolveRibbonDocumentTitle()
    {
        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            return Path.GetFileName(_currentPath);
        }

        return "Untitled";
    }

    private void UpdateRibbonTopBarTitle(string? title = null)
    {
        if (_ribbon?.Model is not { } model)
        {
            return;
        }

        model.TopBarTitle = string.IsNullOrWhiteSpace(title) ? ResolveRibbonDocumentTitle() : title;
    }

    private string ResolveSuggestedFileName(string? suggestedName)
    {
        if (!string.IsNullOrWhiteSpace(suggestedName))
        {
            return suggestedName;
        }

        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            return Path.GetFileNameWithoutExtension(_currentPath);
        }

        return "Untitled";
    }

    private IStorageProvider? ResolveStorageProvider()
    {
        return ResolveTopLevel()?.StorageProvider;
    }

    private async Task ShowDialogAsync(Window dialog)
    {
        var owner = ResolveOwnerWindow();
        if (owner is null)
        {
            dialog.Show();
            return;
        }

        await dialog.ShowDialog(owner);
    }

    private async Task<T?> ShowDialogAsync<T>(Window dialog)
    {
        var owner = ResolveOwnerWindow();
        if (owner is null)
        {
            dialog.Show();
            return default;
        }

        return await dialog.ShowDialog<T>(owner);
    }

    private void ShowOwnedWindow(Window window)
    {
        var owner = ResolveOwnerWindow();
        if (owner is not null)
        {
            window.Show(owner);
            return;
        }

        window.Show();
    }

    private async Task ShowPrintDialogAsync()
    {
        if (_editorView is null || _isLoading)
        {
            return;
        }

        _editorView.UpdateFieldsForPrint();
        var selection = _editorView.Selection;
        var documentInfo = new DocumentPrintContext(_editorView.Document, _editorView.LayoutSettingsSnapshot)
        {
            Selection = selection.IsEmpty ? null : selection,
            CurrentPageIndex = _editorView.CurrentPageIndex
        };

        var systemPrintService = new SystemPrintService();
        var printService = new SkiaPrintService(systemPrintService, systemPrintService);
        var viewModel = new PrintDialogViewModel(printService, documentInfo);
        var dialog = new PrintDialog(viewModel);

        void CloseDialog(bool result) => dialog.Close(result);
        viewModel.RequestClose += CloseDialog;

        var browseHandler = viewModel.BrowseOutputPath.RegisterHandler(async interaction =>
        {
            var path = await BrowsePdfOutputPathAsync();
            interaction.SetOutput(path);
        });

        dialog.Closed += (_, _) =>
        {
            viewModel.RequestClose -= CloseDialog;
            browseHandler.Dispose();
        };

        await viewModel.InitializeAsync();
        await ShowDialogAsync(dialog);

        async Task<string?> BrowsePdfOutputPathAsync()
        {
            var storageProvider = ResolveStorageProvider();
            if (storageProvider is null)
            {
                return null;
            }

            var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF")
                    {
                        Patterns = new[] { "*.pdf" }
                    }
                },
                SuggestedFileName = ResolveSuggestedFileName("Document")
            });

            return result?.TryGetLocalPath();
        }
    }

    private async Task<PdfImportOptions?> ShowPdfImportDialogAsync()
    {
        var preferencesResult = await _pdfImportPreferencesStore.LoadAsync();
        PdfImportMode importMode;
        PdfPreservationMode preservationMode;

        if (preferencesResult.HasValue && preferencesResult.Preferences is { } preferences)
        {
            importMode = preferences.ImportMode;
            preservationMode = preferences.PreservationMode;
        }
        else
        {
            importMode = PdfImportMode.Reflow;
            preservationMode = PdfPreservationMode.None;
            var defaults = new PdfImportPreferences
            {
                ImportMode = importMode,
                PreservationMode = preservationMode,
                SkipDialog = true
            };
            await _pdfImportPreferencesStore.SaveAsync(defaults);
        }

        var options = CreatePdfImportOptions(importMode, preservationMode);
        if (!_pdfImportNotificationShown)
        {
            _pdfImportNotificationShown = true;
            ShowNotification(
                "PDF Import",
                $"Imported using {importMode} layout with {DescribePdfPreservation(preservationMode)}.",
                NotificationType.Information);
        }

        return options;
    }

    private static string DescribePdfPreservation(PdfPreservationMode preservationMode)
    {
        return preservationMode switch
        {
            PdfPreservationMode.StoreOriginal => "original PDF preservation",
            PdfPreservationMode.Incremental => "incremental preservation",
            _ => "no preservation"
        };
    }

    private static PdfImportOptions CreatePdfImportOptions(PdfImportMode mode, PdfPreservationMode preservationMode)
    {
        var options = new PdfImportOptions
        {
            Mode = mode,
            PreservationMode = preservationMode
        };

        if (options.PreservationMode != PdfPreservationMode.None)
        {
            options.ParserOptions.PreserveSourceBytes = true;
        }

        if (mode == PdfImportMode.FixedLayout)
        {
            options.ParserOptions.ExtractPaths = true;
            options.ParserOptions.NormalizeFontNames = true;
        }

        return options;
    }

    private async Task<PdfDocumentAst?> LoadPdfAsync(string path, PdfImportOptions options)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var document = await Task.Run(() => _pdfEngine.Parse(stream, options.ParserOptions));
            document.SourcePath = path;
            return document;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load PDF: {ex.Message}");
            return null;
        }
    }

    private static PdfEngine CreatePdfEngine()
    {
        var registry = new PdfProviderRegistry();
        registry.RegisterParser(new PdfPigParser());
        registry.RegisterWriter(new PdfSharpWriter());

        if (!registry.TryGetParser(PdfProviderIds.PdfPig, out var parser))
        {
            throw new InvalidOperationException("PDF parser provider is not registered.");
        }

        if (!registry.TryGetWriter(PdfProviderIds.PdfSharp, out var writer))
        {
            throw new InvalidOperationException("PDF writer provider is not registered.");
        }

        return new PdfEngine(parser, writer);
    }

    private async Task OpenDocumentAsync()
    {
        if (_isLoading)
        {
            return;
        }

        var storageProvider = ResolveStorageProvider();
        if (storageProvider is null)
        {
            return;
        }

        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { SupportedFileType, DocxFileType, PdfFileType, MarkdownFileType, HtmlFileType }
        });

        if (result.Count == 0)
        {
            return;
        }

        var path = result[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await LoadDocumentAsync(path);
    }

    private async Task NewDocumentAsync()
    {
        if (_editorView is null || _isLoading)
        {
            return;
        }

        _currentPath = null;
        UpdateWindowTitle();
        var document = DocumentTemplates.CreateDefaultDocument();
        await _editorView.LoadDocumentAsync(document);
        ApplyFormatProfile(null);
        RefreshStyleGalleryItems();
        AttachStyleManagerEvents();
        _ribbon?.RefreshState();
        ResetPdfIndicators();
    }

    private async Task SaveDocumentAsync(string? suggestedName = null)
    {
        if (_editorView is null || _isLoading)
        {
            return;
        }

        var path = _currentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var storageProvider = ResolveStorageProvider();
            if (storageProvider is null)
            {
                return;
            }

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                DefaultExtension = "docx",
                FileTypeChoices = new[] { DocxFileType, PdfFileType, MarkdownFileType, HtmlFileType },
                SuggestedFileName = ResolveSuggestedFileName(suggestedName)
            });

            path = file?.TryGetLocalPath();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (IsMarkdownPath(path))
        {
            var markdown = MarkdownDocumentConverter.ToMarkdown(_editorView.Document, CreateMarkdownOptions());
            await File.WriteAllTextAsync(path, markdown);
        }
        else if (IsHtmlPath(path))
        {
            var html = HtmlDocumentConverter.ToHtml(_editorView.Document, CreateHtmlOptions());
            await File.WriteAllTextAsync(path, html);
        }
        else if (IsPdfPath(path))
        {
            var saved = await ExportPdfAsync(path);
            if (!saved)
            {
                return;
            }
        }
        else
        {
            new DocxExporter().Save(_editorView.Document, path);
        }
        _currentPath = path;
        UpdateWindowTitle();
    }

    private async Task<bool> ExportPdfAsync(string path)
    {
        if (_editorView is null)
        {
            return false;
        }

        var document = _editorView.Document;
        PdfPreservedData? preservedData = null;
        var hasPreservedData = PdfPreservationStore.TryRead(document, out preservedData) && preservedData is not null;
        var preservationMode = preservedData?.Manifest.PreservationMode ?? PdfPreservationMode.None;
        var hasChanges = hasPreservedData && HasPdfContentChanges(document, preservedData!);
        PdfIncrementalUpdatePlan? incrementalPlan = null;
        if (hasPreservedData && preservationMode == PdfPreservationMode.Incremental && preservedData is not null)
        {
            incrementalPlan = PdfIncrementalUpdatePlanner.Build(document, preservedData);
        }

        PdfExportOptions exportOptions;
        if (hasPreservedData)
        {
            var dialogResult = await ShowPdfExportDialogAsync(
                hasPreservedData,
                hasChanges,
                preservationMode,
                incrementalPlan?.CanApply ?? false,
                incrementalPlan?.Issues);
            if (dialogResult is null)
            {
                return false;
            }

            exportOptions = dialogResult;
        }
        else
        {
            exportOptions = new PdfExportOptions { ExportMode = PdfExportMode.Regenerate };
        }

        if (exportOptions.ExportMode == PdfExportMode.Preserve
            && preservedData is not null
            && (!hasChanges || exportOptions.AllowPreserveWithChanges))
        {
            if (preservationMode == PdfPreservationMode.Incremental && hasChanges)
            {
                if (incrementalPlan is not null && incrementalPlan.Issues.Count > 0)
                {
                    var message = string.Join(Environment.NewLine, incrementalPlan.Issues.Select(issue => $"- {issue}"));
                    message = $"Incremental preservation cannot be applied:{Environment.NewLine}{message}";
                    var dialog = new MessageDialog("PDF Preserve Not Available", message);
                    await ShowDialogAsync(dialog);
                }

                var overlays = incrementalPlan?.Overlays ?? (IReadOnlyList<PdfIncrementalOverlay>)Array.Empty<PdfIncrementalOverlay>();
                var overlayBuildIssues = new List<string>();
                if (incrementalPlan is not null && overlays.Count > 0)
                {
                    var overlayResult = await BuildIncrementalImageOverlaysAsync(
                        document,
                        _editorView.LayoutSettingsSnapshot,
                        overlays);
                    overlays = overlayResult.Overlays;
                    overlayBuildIssues.AddRange(overlayResult.Issues);
                }

                string? overlayError = null;
                if (overlays.Count == 0 && overlayBuildIssues.Count > 0)
                {
                    var details = string.Join(Environment.NewLine, overlayBuildIssues.Select(issue => $"- {issue}"));
                    var dialog = new MessageDialog("PDF Incremental Overlay Failed", details);
                    await ShowDialogAsync(dialog);
                }
                else if (PdfIncrementalWriter.TryAppendOverlayIncrementalUpdate(
                             preservedData.Bytes,
                             overlays,
                             out var updatedBytes,
                             out overlayError,
                             out var overlayIssues))
                {
                    if (overlayBuildIssues.Count > 0)
                    {
                        overlayIssues.AddRange(overlayBuildIssues);
                    }

                    if (overlayIssues.Count > 0)
                    {
                        var details = string.Join(Environment.NewLine, overlayIssues.Select(issue => $"- {issue}"));
                        var dialog = new MessageDialog("PDF Incremental Overlay Issues", details);
                        await ShowDialogAsync(dialog);
                    }

                    await File.WriteAllBytesAsync(path, updatedBytes);
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(overlayError))
                {
                    var dialog = new MessageDialog("PDF Incremental Update Failed", overlayError);
                    await ShowDialogAsync(dialog);
                }
            }
            else if (preservationMode == PdfPreservationMode.Incremental && !hasChanges)
            {
                if (PdfIncrementalWriter.TryAppendPlaceholderIncrementalUpdate(
                        preservedData.Bytes,
                        out var updatedBytes,
                        out var error))
                {
                    await File.WriteAllBytesAsync(path, updatedBytes);
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    var dialog = new MessageDialog("PDF Incremental Update Failed", error);
                    await ShowDialogAsync(dialog);
                }
            }
            else
            {
                await File.WriteAllBytesAsync(path, preservedData.Bytes);
                return true;
            }
        }

        _editorView.UpdateFieldsForPrint();
        var documentInfo = new DocumentPrintContext(document, _editorView.LayoutSettingsSnapshot)
        {
            CurrentPageIndex = _editorView.CurrentPageIndex
        };

        var systemPrintService = new SystemPrintService();
        var printService = new SkiaPrintService(systemPrintService, systemPrintService);
        var settings = new PrintSettings
        {
            OutputKind = PrintOutputKind.Pdf,
            OutputPath = path,
            RangeKind = PrintRangeKind.All,
            Copies = 1,
            Collate = true
        };

        var result = await printService.PrintAsync(documentInfo, settings);
        if (!result.Succeeded)
        {
            Console.WriteLine($"PDF export failed: {result.Message}");
            return false;
        }

        return true;
    }

    private async Task<(List<PdfIncrementalOverlay> Overlays, List<string> Issues)> BuildIncrementalImageOverlaysAsync(
        Document document,
        LayoutSettings layoutSettings,
        IReadOnlyList<PdfIncrementalOverlay> planOverlays)
    {
        var issues = new List<string>();
        var overlays = new List<PdfIncrementalOverlay>();

        if (planOverlays.Count == 0)
        {
            return (overlays, issues);
        }

        var requestedPages = planOverlays
            .Select(overlay => overlay.PageIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        var documentInfo = new DocumentPrintContext(document, layoutSettings)
        {
            CurrentPageIndex = _editorView?.CurrentPageIndex
        };

        var settings = new PrintSettings
        {
            OutputKind = PrintOutputKind.Pdf,
            RangeKind = PrintRangeKind.All,
            Copies = 1,
            Collate = true
        };

        var systemPrintService = new SystemPrintService();
        var printService = new SkiaPrintService(systemPrintService, systemPrintService);
        var request = new PrintPreviewRequest(documentInfo, settings)
        {
            PageIndices = requestedPages,
            Dpi = PdfIncrementalOverlayDpi
        };

        PrintPreviewResult previewResult;
        try
        {
            previewResult = await printService.BuildPreviewAsync(request);
        }
        catch (Exception ex)
        {
            issues.Add($"Failed to render preview overlays: {ex.Message}");
            return (overlays, issues);
        }

        var pageWidthPoints = PdfUnits.DipToPoints(layoutSettings.PageWidth);
        var pageHeightPoints = PdfUnits.DipToPoints(layoutSettings.PageHeight);

        foreach (var preview in previewResult.Pages)
        {
            if (!TryEncodePreviewAsJpeg(preview, out var jpegBytes, out var width, out var height))
            {
                issues.Add($"Failed to encode preview image for page {preview.PageNumber}.");
                continue;
            }

            overlays.Add(new PdfIncrementalOverlay
            {
                PageIndex = preview.PageNumber - 1,
                Kind = PdfIncrementalOverlayKind.Image,
                ImageBytes = jpegBytes,
                ImageWidth = width,
                ImageHeight = height,
                ImageEncoding = PdfImageEncoding.Jpeg,
                Bounds = new PdfRect(0, 0, pageWidthPoints, pageHeightPoints),
                Description = "Incremental overlay preview"
            });
        }

        var overlayPageSet = overlays.Select(overlay => overlay.PageIndex).ToHashSet();
        foreach (var pageIndex in requestedPages)
        {
            if (!overlayPageSet.Contains(pageIndex))
            {
                issues.Add($"Missing preview overlay for page {pageIndex + 1}.");
            }
        }

        return (overlays, issues);
    }

    private static bool TryEncodePreviewAsJpeg(
        PrintPreviewPage preview,
        out byte[] jpegBytes,
        out int width,
        out int height)
    {
        jpegBytes = Array.Empty<byte>();
        width = 0;
        height = 0;

        if (preview.ImageBytes is null || preview.ImageBytes.Length == 0)
        {
            return false;
        }

        using var image = SKImage.FromEncodedData(preview.ImageBytes);
        if (image is null)
        {
            return false;
        }

        width = image.Width;
        height = image.Height;

        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, PdfIncrementalOverlayJpegQuality);
        if (encoded is null)
        {
            return false;
        }

        jpegBytes = encoded.ToArray();
        return jpegBytes.Length > 0;
    }

    private async Task<PdfExportOptions?> ShowPdfExportDialogAsync(
        bool hasPreservedData,
        bool hasChanges,
        PdfPreservationMode preservationMode,
        bool supportsIncremental,
        IReadOnlyList<string>? incrementalIssues)
    {
        var details = incrementalIssues is { Count: > 0 }
            ? string.Join(Environment.NewLine, incrementalIssues.Select(issue => $"- {issue}"))
            : null;
        var viewModel = new PdfExportDialogViewModel(
            hasPreservedData,
            hasChanges,
            preservationMode,
            supportsIncremental,
            details);
        var dialog = new PdfExportDialog(viewModel);

        void CloseDialog(PdfExportOptions? result) => dialog.Close(result);
        viewModel.RequestClose += CloseDialog;

        dialog.Closed += (_, _) =>
        {
            viewModel.RequestClose -= CloseDialog;
        };

        return await ShowDialogAsync<PdfExportOptions?>(dialog);
    }

    private static bool HasPdfContentChanges(Document document, PdfPreservedData preservedData)
    {
        if (string.IsNullOrWhiteSpace(preservedData.Manifest.ContentHash))
        {
            return true;
        }

        var currentHash = PdfDocumentHash.Compute(document);
        return !string.Equals(currentHash, preservedData.Manifest.ContentHash, StringComparison.Ordinal);
    }

    private async Task SaveDocumentAsAsync()
    {
        var previousPath = _currentPath;
        _currentPath = null;
        var suggestedName = string.IsNullOrWhiteSpace(previousPath)
            ? "Untitled"
            : Path.GetFileNameWithoutExtension(previousPath);
        await SaveDocumentAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            _currentPath = previousPath;
            UpdateWindowTitle();
        }
    }

    private static bool IsMarkdownPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPdfPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static MarkdownOptions CreateMarkdownOptions()
    {
        return new MarkdownOptions
        {
            Flavor = MarkdownFlavor.GitHub,
            UseGfmTables = true,
            UseTaskLists = true,
            UseStrikethrough = true
        };
    }

    private static HtmlOptions CreateHtmlOptions()
    {
        return new HtmlOptions
        {
            Flavor = HtmlFlavor.Html5,
            PrettyPrint = true
        };
    }

    private void ApplyFormatProfile(string? path)
    {
        if (_editorView is null)
        {
            return;
        }

        if (_editorView.TryGetService<IEditorFormatProfileService>(out var profileService))
        {
            if (IsMarkdownPath(path))
            {
                profileService.CurrentProfile = MarkdownProfiles.GitHub;
                profileService.CommandPolicy = MarkdownCommandPolicy.Create(MarkdownProfiles.GitHub);
            }
            else if (IsHtmlPath(path))
            {
                profileService.CurrentProfile = HtmlProfiles.Html5;
                profileService.CommandPolicy = HtmlCommandPolicy.Create(HtmlProfiles.Html5);
            }
            else
            {
                profileService.CurrentProfile = null;
                profileService.CommandPolicy = null;
            }
        }
    }

    private RibbonModel BuildRibbonModel()
    {
        var canInteract = () => !_isLoading && _editorView is not null;

        IEditorCommandRouter? GetCommandRouter()
        {
            if (!canInteract() || _editorView is null)
            {
                return null;
            }

            return _editorView.TryGetService<IEditorCommandRouter>(out var router) ? router : null;
        }

        IRibbonContextSnapshotProvider? GetSnapshotProvider()
        {
            if (!canInteract() || _editorView is null)
            {
                return null;
            }

            return _editorView.TryGetService<IRibbonContextSnapshotProvider>(out var provider) ? provider : null;
        }

        IProofingToggleService? GetProofingToggle()
        {
            if (!canInteract() || _editorView is null)
            {
                return null;
            }

            return _editorView.TryGetService<IProofingToggleService>(out var toggle) ? toggle : null;
        }

        IProofingProfileManager? GetProofingManager()
        {
            if (!canInteract() || _editorView is null)
            {
                return null;
            }

            return _editorView.TryGetService<IProofingProfileManager>(out var manager) ? manager : null;
        }

        IProofingOptionsStore? GetProofingOptionsStore()
        {
            if (!canInteract() || _editorView is null)
            {
                return null;
            }

            return _editorView.TryGetService<IProofingOptionsStore>(out var store) ? store : null;
        }

        bool IsProofingSpellingEnabled()
        {
            var toggle = GetProofingToggle();
            return toggle is not null && toggle.IsEnabled && toggle.IsSpellingEnabled;
        }

        bool IsProofingGrammarEnabled()
        {
            var toggle = GetProofingToggle();
            return toggle is not null && toggle.IsEnabled && toggle.IsGrammarEnabled;
        }

        bool IsProofingStyleEnabled()
        {
            var toggle = GetProofingToggle();
            return toggle is not null && toggle.IsEnabled && toggle.IsStyleEnabled;
        }

        ValueTask ToggleProofingSpellingAsync(bool value)
        {
            var toggle = GetProofingToggle();
            if (toggle is not null)
            {
                toggle.SetSpellingEnabled(value);
            }

            return ValueTask.CompletedTask;
        }

        ValueTask ToggleProofingGrammarAsync(bool value)
        {
            var toggle = GetProofingToggle();
            if (toggle is not null)
            {
                toggle.SetGrammarEnabled(value);
            }

            return ValueTask.CompletedTask;
        }

        ValueTask ToggleProofingStyleAsync(bool value)
        {
            var toggle = GetProofingToggle();
            if (toggle is not null)
            {
                toggle.SetStyleEnabled(value);
            }

            return ValueTask.CompletedTask;
        }

        bool IsUsingSelectedEngine()
        {
            var manager = GetProofingManager();
            return manager?.Options.UseSelectedEngines ?? true;
        }

        void RefreshProofingIfNeeded()
        {
            if (_editorView is null)
            {
                return;
            }

            if (!_editorView.TryGetService<IProofingService>(out var proofing))
            {
                return;
            }

            if (_editorView.TryGetService<IProofingToggleService>(out var toggle) && toggle.IsEnabled)
            {
                proofing.RefreshAll();
            }
        }

        void ApplyProofingOptions(ProofingOptions options)
        {
            var manager = GetProofingManager();
            if (manager is null)
            {
                return;
            }

            manager.UpdateOptions(options);
            var store = GetProofingOptionsStore();
            store?.Save(options);
            RefreshProofingIfNeeded();
            _ribbon?.RefreshState();
        }

        ValueTask ToggleUseSelectedEngineAsync(bool value)
        {
            var manager = GetProofingManager();
            if (manager is null)
            {
                return ValueTask.CompletedTask;
            }

            var options = manager.Options.Clone();
            options.UseSelectedEngines = value;
            ApplyProofingOptions(options);
            return ValueTask.CompletedTask;
        }

        async ValueTask OpenProofingOptionsAsync()
        {
            var manager = GetProofingManager();
            if (manager is null)
            {
                return;
            }

            var dialog = new ProofingOptionsWindow(manager.Options, manager.Profiles, manager.Engines);
            var result = await ShowDialogAsync<ProofingOptions?>(dialog);
            if (result is not null)
            {
                ApplyProofingOptions(result);
            }
        }

        bool TryGetSnapshot(out RibbonContextSnapshot snapshot)
        {
            var provider = GetSnapshotProvider();
            if (provider is null)
            {
                snapshot = default;
                return false;
            }

            snapshot = provider.GetSnapshot();
            return true;
        }

        bool CanExecuteEditorCommand(string commandId, object? payload = null)
        {
            if (!canInteract())
            {
                return false;
            }

            var router = GetCommandRouter();
            if (router is null)
            {
                return false;
            }

            if (TryGetSnapshot(out var snapshot))
            {
                return router.CanExecute(commandId, payload, snapshot);
            }

            return router.CanExecute(commandId, payload);
        }

        bool ShouldRecordHistory(string commandId)
        {
            if (_editorView is null)
            {
                return true;
            }

            if (!string.Equals(commandId, EditorHomeCommandIds.Styles.Apply, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_editorView.TryGetService<IEditorFormatProfileService>(out var profileService))
            {
                var profileId = profileService.CurrentProfile?.Id;
                if (!string.IsNullOrWhiteSpace(profileId)
                    && profileId.StartsWith("markdown", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        async ValueTask ExecuteEditorCommandAsync(string commandId, object? payload = null)
        {
            var router = GetCommandRouter();
            if (router is null)
            {
                return;
            }

            var recordHistory = ShouldRecordHistory(commandId);
            if (TryGetSnapshot(out var snapshot))
            {
                await router.ExecuteAsync(commandId, payload, snapshot, recordHistory);
            }
            else
            {
                await router.ExecuteAsync(commandId, payload, recordHistory: recordHistory);
            }

            _ribbon?.RefreshState();
        }

        RibbonCommand CreateEditorCommand(string commandId, object? payload = null)
        {
            return new RibbonCommand(
                () => ExecuteEditorCommandAsync(commandId, payload),
                () => CanExecuteEditorCommand(commandId, payload));
        }

        RibbonCommand CreateEditorCommandWithPayload(string commandId, Func<object?> payloadFactory)
        {
            return new RibbonCommand(
                () => ExecuteEditorCommandAsync(commandId, payloadFactory()),
                () => CanExecuteEditorCommand(commandId, payloadFactory()));
        }

        RibbonCommand CreateViewCommand(Action action)
        {
            return new RibbonCommand(
                () =>
                {
                    action();
                    return ValueTask.CompletedTask;
                },
                canInteract);
        }

        RibbonCommand CreateViewCommandWithCanExecute(Action action, Func<bool> canExecute)
        {
            return new RibbonCommand(
                () =>
                {
                    action();
                    return ValueTask.CompletedTask;
                },
                canExecute);
        }

        bool CanUseFindReplace()
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return false;
            }

            return snapshot.IsFindAvailable;
        }

        Task ShowFindReplaceDialogAsync(bool replaceMode)
        {
            if (!canInteract())
            {
                return Task.CompletedTask;
            }

            if (_findReplaceDialog is null)
            {
                _findReplaceDialog = new FindReplaceDialog();
                _findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;
                _findReplaceDialog.FindNextRequested += async (_, query) =>
                    await ExecuteEditorCommandAsync(EditorHomeCommandIds.Editing.Find, query);
                _findReplaceDialog.ReplaceRequested += async (_, query) =>
                    await ExecuteEditorCommandAsync(EditorHomeCommandIds.Editing.Replace, query);
                _findReplaceDialog.ReplaceAllRequested += async (_, query) =>
                    await ExecuteEditorCommandAsync(EditorHomeCommandIds.Editing.ReplaceAll, query);
            }

            _findReplaceDialog.SetMode(replaceMode ? FindReplaceDialogMode.Replace : FindReplaceDialogMode.Find);
            ShowOwnedWindow(_findReplaceDialog);
            _findReplaceDialog.Activate();
            return Task.CompletedTask;
        }

        async Task OpenLineSpacingOptionsAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var state = ResolveLineSpacingDialogState();
            var dialog = new LineSpacingOptionsDialog(state);
            var result = await ShowDialogAsync<EditorParagraphSpacingOptions?>(dialog);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorHomeCommandIds.Paragraph.LineSpacingOptions, result.Value);
            }
        }

        async Task OpenParagraphDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var state = ResolveParagraphDialogState();
            var dialog = new ParagraphDialog(state);
            var result = await ShowDialogAsync<EditorParagraphDialogOptions?>(dialog);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorHomeCommandIds.Paragraph.DialogApply, result.Value);
            }
        }

        async Task OpenFontDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var fonts = ResolveFontDialogFamilies();
            var state = ResolveFontDialogState();
            var dialog = new FontDialog(fonts, state);
            var result = await ShowDialogAsync<EditorFontDialogOptions?>(dialog);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.DialogApply, result.Value);
            }
        }

        async Task OpenTableInsertDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var dialog = new TableInsertDialog();
            var result = await ShowDialogAsync<EditorTableInsertRequest?>(dialog);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Tables.InsertTable, result.Value);
            }
        }

        async ValueTask OpenTablePropertiesDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            if (!TryResolveTablePropertiesDialogState(out var state))
            {
                return;
            }

            var dialog = new TablePropertiesDialog(state);
            var result = await ShowDialogAsync<EditorTablePropertiesDialogOptions?>(dialog);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorTableCommandIds.Layout.PropertiesApply, result.Value);
            }
        }

        async Task InsertQuickTableAsync(QuickTableTemplate template)
        {
            if (!canInteract())
            {
                return;
            }

            var request = new EditorTableTemplateInsertRequest(
                template.Rows,
                template.Columns,
                template.StyleId,
                template.CellText);
            await ExecuteEditorCommandAsync(EditorInsertCommandIds.Tables.InsertTable, request);
        }

        async Task OpenHyperlinkDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            string? displayText = null;
            string? address = null;
            if (_editorView is not null && _editorView.TryGetService<ISelectionTextService>(out var selectionTextService))
            {
                if (selectionTextService.TryGetSelectionText(out var selectionText, 200))
                {
                    var normalized = NormalizeSelectionText(selectionText);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        displayText = normalized;
                        if (IsLikelyHyperlinkAddress(normalized))
                        {
                            address = normalized;
                        }
                    }
                }
            }

            var dialog = new HyperlinkDialog(displayText, address);
            var result = await ShowDialogAsync<EditorHyperlinkInsertRequest?>(dialog);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Links.Hyperlink, result.Value);
            }
        }

        void BeginHeaderFooterEdit(bool editHeader)
        {
            if (!canInteract() || _editorView is null)
            {
                return;
            }

            var mode = editHeader ? HeaderFooterEditMode.Header : HeaderFooterEditMode.Footer;
            _editorView.BeginHeaderFooterEdit(mode);
        }

        async Task OpenCrossReferenceDialogAsync()
        {
            if (!canInteract() || _editorView is null)
            {
                return;
            }

            var items = BuildBookmarkPickerItems(_editorView.Document);
            if (items.Count == 0)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Links.CrossReference);
                return;
            }

            var dialog = new PickerDialog("Insert Cross-reference", items);
            var result = await ShowDialogAsync<PickerItem?>(dialog);
            if (result is not null)
            {
                var request = new EditorCrossReferenceInsertRequest(result.Id);
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Links.CrossReference, request);
            }
        }

        async Task OpenLanguageDialogAsync()
        {
            if (!canInteract() || _editorView is null)
            {
                return;
            }

            var current = _editorView.Document.DefaultTextStyle.Language ?? "en-US";
            var dialog = new TextInputDialog("Set Language", "Language tag (e.g., en-US):", current);
            var result = await ShowDialogAsync<string?>(dialog);
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            await ExecuteEditorCommandAsync(EditorReviewCommandIds.Language.SetLanguage, result.Trim());
        }

        Task OpenNotesPaneAsync()
        {
            if (!canInteract() || _editorView is null)
            {
                return Task.CompletedTask;
            }

            if (_notesPaneWindow is null)
            {
                _notesPaneWindow = new NotesPaneWindow(_editorView);
                _notesPaneWindow.Closed += (_, _) => _notesPaneWindow = null;
            }
            else
            {
                _notesPaneWindow.SetDocumentView(_editorView);
            }

            ShowOwnedWindow(_notesPaneWindow);
            _notesPaneWindow.Activate();
            return Task.CompletedTask;
        }

        async ValueTask ToggleHtmlSourceAsync(bool isChecked)
        {
            if (!canInteract() || _editorView is null)
            {
                return;
            }

            if (!isChecked)
            {
                if (_htmlSourceWindow is not null)
                {
                    _htmlSourceWindow.Close();
                    _htmlSourceWindow = null;
                }

                return;
            }

            if (_htmlSourceWindow is null)
            {
                _htmlSourceWindow = new HtmlSourceWindow(_editorView);
                _htmlSourceWindow.Closed += (_, _) =>
                {
                    _htmlSourceWindow = null;
                    _ribbon?.RefreshState();
                };
            }
            else
            {
                _htmlSourceWindow.SetDocumentView(_editorView);
            }

            ShowOwnedWindow(_htmlSourceWindow);
            _htmlSourceWindow.Activate();
            await Task.CompletedTask;
        }

        VbaDebugSession? GetActiveDebugSession()
        {
            if (_editorView is null)
            {
                return null;
            }

            if (!_editorView.TryGetService<IMacroEngine>(out var macroEngine))
            {
                return null;
            }

            return macroEngine is IMacroDebugEngine debugEngine ? debugEngine.ActiveDebugSession : null;
        }

        void RunDebugAction(Action<VbaDebugSession> action)
        {
            var session = GetActiveDebugSession();
            if (session is null)
            {
                return;
            }

            action(session);
        }

        async Task InsertPictureAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var storageProvider = ResolveStorageProvider();
            if (storageProvider is null)
            {
                return;
            }

            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[] { ImageFileType }
            });

            if (result.Count == 0)
            {
                return;
            }

            var path = result[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            byte[] data;
            try
            {
                data = await File.ReadAllBytesAsync(path);
            }
            catch (Exception)
            {
                return;
            }

            var contentType = ResolveImageContentType(path);
            var request = new EditorImageInsertRequest(data, 240f, 160f, contentType);
            await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.Pictures, request);
        }

        async Task InsertScreenshotAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var storageProvider = ResolveStorageProvider();
            if (storageProvider is null)
            {
                return;
            }

            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[] { ImageFileType }
            });

            if (result.Count == 0)
            {
                return;
            }

            var path = result[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            byte[] data;
            try
            {
                data = await File.ReadAllBytesAsync(path);
            }
            catch (Exception)
            {
                return;
            }

            var contentType = ResolveImageContentType(path);
            var request = new EditorImageInsertRequest(data, 240f, 160f, contentType);
            await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.Screenshot, request);
        }

        async Task InsertModel3DAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var storageProvider = ResolveStorageProvider();
            if (storageProvider is null)
            {
                return;
            }

            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[] { ModelFileType }
            });

            if (result.Count == 0)
            {
                return;
            }

            var path = result[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            byte[] data;
            try
            {
                data = await File.ReadAllBytesAsync(path);
            }
            catch (Exception)
            {
                return;
            }

            var contentType = ResolveModelContentType(path);
            var request = new EditorEmbeddedObjectInsertRequest(data, contentType, "3D Model", path);
            await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.Models3D, request);
        }

        async Task InsertObjectFromFileAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var storageProvider = ResolveStorageProvider();
            if (storageProvider is null)
            {
                return;
            }

            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[] { ObjectFileType }
            });

            if (result.Count == 0)
            {
                return;
            }

            var path = result[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            byte[] data;
            try
            {
                data = await File.ReadAllBytesAsync(path);
            }
            catch (Exception)
            {
                return;
            }

            var request = new EditorEmbeddedObjectInsertRequest(data, "application/octet-stream", "Embedded Object", path);
            await ExecuteEditorCommandAsync(EditorInsertCommandIds.Text.Object, request);
        }

        static string ResolveImageContentType(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
        }

        static string ResolveModelContentType(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".glb" => "model/gltf-binary",
                ".gltf" => "model/gltf+json",
                ".obj" => "model/obj",
                ".fbx" => "model/fbx",
                _ => "model/3d"
            };
        }

        async Task OpenShapePickerDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var items = BuildShapePickerItems();
            var dialog = new PickerDialog("Insert Shape", items);
            var result = await ShowDialogAsync<PickerItem?>(dialog);
            if (result is not null)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.Shapes, result.Id);
            }
        }

        async Task OpenIconPickerDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var items = BuildIconPickerItems();
            var dialog = new PickerDialog("Insert Icon", items);
            var result = await ShowDialogAsync<PickerItem?>(dialog);
            if (result is not null)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.Icons, result.Id);
            }
        }

        async Task OpenSmartArtPickerDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var items = BuildSmartArtPickerItems();
            var dialog = new PickerDialog("Insert SmartArt", items);
            var result = await ShowDialogAsync<PickerItem?>(dialog);
            if (result is not null)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.SmartArt, result.Id);
            }
        }

        async Task OpenChartGalleryDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var items = BuildChartPickerItems(out var payloads);
            var dialog = new PickerDialog("Insert Chart", items);
            var result = await ShowDialogAsync<PickerItem?>(dialog);
            if (result is not null && payloads.TryGetValue(result.Id, out var request))
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.Chart, request);
            }
        }

        static IReadOnlyList<PickerItem> BuildBookmarkPickerItems(Document document)
        {
            var items = new List<PickerItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddBookmark(string? name)
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                {
                    return;
                }

                items.Add(new PickerItem(name, name, IconKey: "RibbonIcon.Bookmark"));
            }

            void ScanParagraph(ParagraphBlock paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is BookmarkStartInline bookmarkStart)
                    {
                        AddBookmark(bookmarkStart.Name);
                    }
                }
            }

            void ScanBlocks(IEnumerable<Block> blocks)
            {
                foreach (var block in blocks)
                {
                    switch (block)
                    {
                        case ParagraphBlock paragraph:
                            ScanParagraph(paragraph);
                            break;
                        case TableBlock table:
                            foreach (var row in table.Rows)
                            {
                                foreach (var cell in row.Cells)
                                {
                                    foreach (var paragraph in cell.Paragraphs)
                                    {
                                        ScanParagraph(paragraph);
                                    }
                                }
                            }

                            break;
                    }
                }
            }

            ScanBlocks(document.Blocks);
            ScanBlocks(document.Header.Blocks);
            ScanBlocks(document.Footer.Blocks);
            ScanBlocks(document.FirstHeader.Blocks);
            ScanBlocks(document.FirstFooter.Blocks);
            ScanBlocks(document.EvenHeader.Blocks);
            ScanBlocks(document.EvenFooter.Blocks);

            return items;
        }

        static IReadOnlyList<PickerItem> BuildShapePickerItems()
        {
            return new[]
            {
                new PickerItem("rect", "Rectangle", GeometryData: "M0,0 L100,0 100,100 0,100 Z"),
                new PickerItem("roundrect", "Rounded Rectangle", GeometryData: "M15,0 L85,0 Q100,0 100,15 L100,85 Q100,100 85,100 L15,100 Q0,100 0,85 L0,15 Q0,0 15,0 Z"),
                new PickerItem("ellipse", "Oval", GeometryData: "M50,0 A50,50 0 1 1 49.9,0 Z"),
                new PickerItem("line", "Line", GeometryData: "M0,100 L100,0"),
                new PickerItem("triangle", "Triangle", GeometryData: "M50,0 L100,100 0,100 Z"),
                new PickerItem("rightTriangle", "Right Triangle", GeometryData: "M0,0 L100,0 0,100 Z"),
                new PickerItem("diamond", "Diamond", GeometryData: "M50,0 L100,50 50,100 0,50 Z"),
                new PickerItem("parallelogram", "Parallelogram", GeometryData: "M20,0 L100,0 80,100 0,100 Z"),
                new PickerItem("trapezoid", "Trapezoid", GeometryData: "M20,0 L80,0 100,100 0,100 Z"),
                new PickerItem("pentagon", "Pentagon", GeometryData: "M50,0 L100,40 80,100 20,100 0,40 Z"),
                new PickerItem("hexagon", "Hexagon", GeometryData: "M25,0 L75,0 100,50 75,100 25,100 0,50 Z"),
                new PickerItem("octagon", "Octagon", GeometryData: "M30,0 L70,0 100,30 100,70 70,100 30,100 0,70 0,30 Z"),
                new PickerItem("star5", "5-Point Star", GeometryData: "M50,0 L61,35 98,35 68,57 79,91 50,70 21,91 32,57 2,35 39,35 Z"),
                new PickerItem("star8", "8-Point Star", GeometryData: "M50,0 L62,32 98,32 68,50 98,68 62,68 50,100 38,68 2,68 32,50 2,32 38,32 Z"),
                new PickerItem("rightArrow", "Right Arrow", GeometryData: "M0,40 L60,40 60,20 100,50 60,80 60,60 0,60 Z"),
                new PickerItem("leftArrow", "Left Arrow", GeometryData: "M100,40 L40,40 40,20 0,50 40,80 40,60 100,60 Z"),
                new PickerItem("upArrow", "Up Arrow", GeometryData: "M40,100 L40,40 20,40 50,0 80,40 60,40 60,100 Z"),
                new PickerItem("downArrow", "Down Arrow", GeometryData: "M40,0 L40,60 20,60 50,100 80,60 60,60 60,0 Z"),
                new PickerItem("chevron", "Chevron", GeometryData: "M0,20 L50,80 100,20 80,0 50,40 20,0 Z"),
                new PickerItem("plus", "Plus", GeometryData: "M40,0 L60,0 60,40 100,40 100,60 60,60 60,100 40,100 40,60 0,60 0,40 40,40 Z"),
                new PickerItem("cross", "Cross", GeometryData: "M10,0 L50,40 90,0 100,10 60,50 100,90 90,100 50,60 10,100 0,90 40,50 0,10 Z")
            };
        }

        static IReadOnlyList<PickerItem> BuildIconPickerItems()
        {
            return new[]
            {
                new PickerItem("Info", "Info", IconKey: "RibbonIcon.Info"),
                new PickerItem("Alert", "Alert", IconKey: "RibbonIcon.Alert"),
                new PickerItem("Check", "Check", IconKey: "RibbonIcon.Check"),
                new PickerItem("Star", "Star", IconKey: "RibbonIcon.Star"),
                new PickerItem("Calendar", "Calendar", IconKey: "RibbonIcon.Calendar"),
                new PickerItem("Search", "Search", IconKey: "RibbonIcon.Search"),
                new PickerItem("Lock", "Lock", IconKey: "RibbonIcon.Lock"),
                new PickerItem("Globe", "Globe", IconKey: "RibbonIcon.Globe"),
                new PickerItem("User", "User", IconKey: "RibbonIcon.User"),
                new PickerItem("Settings", "Settings", IconKey: "RibbonIcon.Settings")
            };
        }

        static IReadOnlyList<PickerItem> BuildSmartArtPickerItems()
        {
            return new[]
            {
                new PickerItem("List", "List", IconKey: "RibbonIcon.SmartArt"),
                new PickerItem("Process", "Process", IconKey: "RibbonIcon.SmartArt"),
                new PickerItem("Cycle", "Cycle", IconKey: "RibbonIcon.SmartArt"),
                new PickerItem("Hierarchy", "Hierarchy", IconKey: "RibbonIcon.SmartArt"),
                new PickerItem("Relationship", "Relationship", IconKey: "RibbonIcon.SmartArt"),
                new PickerItem("Matrix", "Matrix", IconKey: "RibbonIcon.SmartArt"),
                new PickerItem("Pyramid", "Pyramid", IconKey: "RibbonIcon.SmartArt")
            };
        }

        static IReadOnlyList<PickerItem> BuildChartPickerItems(out Dictionary<string, EditorChartInsertRequest> payloads)
        {
            var payloadMap = new Dictionary<string, EditorChartInsertRequest>(StringComparer.OrdinalIgnoreCase);
            var items = new List<PickerItem>();

            void Add(string id, string label, string description, EditorChartInsertRequest request)
            {
                items.Add(new PickerItem(id, label, description, IconKey: "RibbonIcon.Chart"));
                payloadMap[id] = request;
            }

            Add(
                "chart-column",
                "Column",
                "Clustered",
                new EditorChartInsertRequest(ChartType.Bar, "Column Chart", ChartBarDirection.Column, ChartStacking.None, 3, 5));
            Add(
                "chart-column-stacked",
                "Column",
                "Stacked",
                new EditorChartInsertRequest(ChartType.Bar, "Stacked Column Chart", ChartBarDirection.Column, ChartStacking.Stacked, 3, 5));
            Add(
                "chart-column-percent",
                "Column",
                "100% Stacked",
                new EditorChartInsertRequest(ChartType.Bar, "100% Stacked Column Chart", ChartBarDirection.Column, ChartStacking.Percent, 3, 5));
            Add(
                "chart-line",
                "Line",
                "Standard",
                new EditorChartInsertRequest(ChartType.Line, "Line Chart", null, ChartStacking.None, 2, 6));
            Add(
                "chart-area",
                "Area",
                "Stacked",
                new EditorChartInsertRequest(ChartType.Area, "Stacked Area Chart", null, ChartStacking.Stacked, 3, 6));
            Add(
                "chart-pie",
                "Pie",
                "Standard",
                new EditorChartInsertRequest(ChartType.Pie, "Pie Chart", null, null, 1, 5));
            Add(
                "chart-doughnut",
                "Doughnut",
                "Standard",
                new EditorChartInsertRequest(ChartType.Doughnut, "Doughnut Chart", null, null, 1, 5));
            Add(
                "chart-scatter",
                "Scatter",
                "Markers",
                new EditorChartInsertRequest(ChartType.Scatter, "Scatter Chart", null, null, 2, 6));
            Add(
                "chart-bubble",
                "Bubble",
                "Bubbles",
                new EditorChartInsertRequest(ChartType.Bubble, "Bubble Chart", null, null, 2, 6));

            payloads = payloadMap;
            return items;
        }

        LineSpacingDialogState ResolveLineSpacingDialogState()
        {
            var kind = LineSpacingOptionKind.Single;
            var atValue = 1f;
            var beforePoints = 0f;
            var afterPoints = 0f;

            if (TryGetSnapshot(out var snapshot))
            {
                beforePoints = ResolveSpacingPoints(snapshot.Paragraph.SpacingBefore);
                afterPoints = ResolveSpacingPoints(snapshot.Paragraph.SpacingAfter);
                ResolveLineSpacingKind(snapshot.Paragraph, ref kind, ref atValue);
            }

            return new LineSpacingDialogState(kind, atValue, beforePoints, afterPoints);
        }

        ParagraphDialogState ResolveParagraphDialogState()
        {
            ParagraphAlignment? alignment = null;
            DocTextDirection? textDirection = null;
            float? indentLeftPoints = null;
            float? indentRightPoints = null;
            ParagraphSpecialIndentKind? specialIndent = null;
            float? specialIndentBy = null;
            float? spacingBeforePoints = null;
            float? spacingAfterPoints = null;
            LineSpacingOptionKind? lineSpacingKind = null;
            float? lineSpacingAt = null;
            bool? contextualSpacing = null;
            bool? rightToLeft = null;
            bool? widowControl = null;
            bool? keepWithNext = null;
            bool? keepLinesTogether = null;
            bool? pageBreakBefore = null;
            bool? suppressLineNumbers = null;

            if (TryGetSnapshot(out var snapshot))
            {
                if (TryGetValue(snapshot.Paragraph.Alignment, out var resolvedAlignment))
                {
                    alignment = resolvedAlignment;
                }

                if (TryGetValue(snapshot.Paragraph.TextDirection, out var resolvedDirection))
                {
                    textDirection = resolvedDirection;
                }

                indentLeftPoints = ResolveSpacingPoints(snapshot.Paragraph.IndentLeft);
                indentRightPoints = ResolveSpacingPoints(snapshot.Paragraph.IndentRight);
                spacingBeforePoints = ResolveSpacingPoints(snapshot.Paragraph.SpacingBefore);
                spacingAfterPoints = ResolveSpacingPoints(snapshot.Paragraph.SpacingAfter);

                if (TryGetValue(snapshot.Paragraph.FirstLineIndent, out var firstIndent))
                {
                    var points = DipToPoints(firstIndent);
                    if (MathF.Abs(points) > 0.01f)
                    {
                        specialIndentBy = MathF.Abs(points);
                        specialIndent = points > 0
                            ? ParagraphSpecialIndentKind.FirstLine
                            : ParagraphSpecialIndentKind.Hanging;
                    }
                    else
                    {
                        specialIndentBy = 0f;
                        specialIndent = ParagraphSpecialIndentKind.None;
                    }
                }

                ResolveLineSpacingKindOptional(snapshot.Paragraph, ref lineSpacingKind, ref lineSpacingAt);

                if (TryGetValue(snapshot.Paragraph.ContextualSpacing, out var resolvedContextual))
                {
                    contextualSpacing = resolvedContextual;
                }

                if (TryGetValue(snapshot.Paragraph.Bidi, out var resolvedBidi))
                {
                    rightToLeft = resolvedBidi;
                }

                if (TryGetValue(snapshot.Paragraph.WidowControl, out var resolvedWidow))
                {
                    widowControl = resolvedWidow;
                }

                if (TryGetValue(snapshot.Paragraph.KeepWithNext, out var resolvedKeepWithNext))
                {
                    keepWithNext = resolvedKeepWithNext;
                }

                if (TryGetValue(snapshot.Paragraph.KeepLinesTogether, out var resolvedKeepLines))
                {
                    keepLinesTogether = resolvedKeepLines;
                }

                if (TryGetValue(snapshot.Paragraph.PageBreakBefore, out var resolvedPageBreak))
                {
                    pageBreakBefore = resolvedPageBreak;
                }

                if (TryGetValue(snapshot.Paragraph.SuppressLineNumbers, out var resolvedSuppress))
                {
                    suppressLineNumbers = resolvedSuppress;
                }
            }

            return new ParagraphDialogState(
                alignment,
                textDirection,
                indentLeftPoints,
                indentRightPoints,
                specialIndent,
                specialIndentBy,
                spacingBeforePoints,
                spacingAfterPoints,
                lineSpacingKind,
                lineSpacingAt,
                contextualSpacing,
                rightToLeft,
                widowControl,
                keepWithNext,
                keepLinesTogether,
                pageBreakBefore,
                suppressLineNumbers);
        }

        bool TryResolveTablePropertiesDialogState(out TablePropertiesDialogState state)
        {
            state = default;
            if (_editorView is null)
            {
                return false;
            }

            if (!_editorView.TryGetService<ISelectionState>(out var selectionState))
            {
                return false;
            }

            var selectionSnapshot = selectionState.GetSnapshot();
            if (!selectionSnapshot.IsInTable)
            {
                return false;
            }

            var selection = selectionSnapshot.Selection.Normalize();
            var document = _editorView.Document;
            var startLocation = document.GetParagraphLocation(selection.Start.ParagraphIndex);
            var endLocation = document.GetParagraphLocation(selection.End.ParagraphIndex);
            if (!startLocation.IsInTable || !endLocation.IsInTable || !ReferenceEquals(startLocation.Table, endLocation.Table))
            {
                return false;
            }

            var table = startLocation.Table!;
            var rowIndex = Math.Clamp(startLocation.RowIndex, 0, Math.Max(0, table.Rows.Count - 1));
            var row = table.Rows[rowIndex];
            var cell = startLocation.Cell ?? (row.Cells.Count > 0 ? row.Cells[0] : null);
            if (cell is null)
            {
                return false;
            }

            float? columnWidthPoints = null;
            float? rowHeightPoints = null;
            if (TryGetTableLayout(_editorView.Layout, document, table, out var tableLayout))
            {
                if (startLocation.ColumnIndex >= 0 && startLocation.ColumnIndex < tableLayout.ColumnWidths.Count)
                {
                    columnWidthPoints = (float)DipsToPoints(tableLayout.ColumnWidths[startLocation.ColumnIndex]);
                }

                if (rowIndex >= 0 && rowIndex < tableLayout.RowHeights.Count)
                {
                    rowHeightPoints = (float)DipsToPoints(tableLayout.RowHeights[rowIndex]);
                }
            }

            var tableProperties = table.Properties;
            var preferredWidthUnit = tableProperties.WidthUnit;
            float? preferredWidth = preferredWidthUnit switch
            {
                TableWidthUnit.Pct => tableProperties.Width,
                TableWidthUnit.Auto => null,
                _ => tableProperties.Width.HasValue ? (float?)DipsToPoints(tableProperties.Width.Value) : null
            };

            var indentUnit = tableProperties.IndentUnit;
            float? indentValue = indentUnit == TableWidthUnit.Pct
                ? tableProperties.Indent
                : tableProperties.Indent.HasValue
                    ? (float?)DipsToPoints(tableProperties.Indent.Value)
                    : null;

            var spacingUnit = tableProperties.CellSpacingUnit;
            float? spacingValue = spacingUnit == TableWidthUnit.Pct
                ? tableProperties.CellSpacing
                : tableProperties.CellSpacing.HasValue
                    ? (float?)DipsToPoints(tableProperties.CellSpacing.Value)
                    : null;

            DocThickness? paddingPoints = null;
            if (tableProperties.CellPadding.HasValue)
            {
                var padding = tableProperties.CellPadding.Value;
                paddingPoints = new DocThickness(
                    (float)DipsToPoints(padding.Left),
                    (float)DipsToPoints(padding.Top),
                    (float)DipsToPoints(padding.Right),
                    (float)DipsToPoints(padding.Bottom));
            }

            var allowRowBreak = row.Properties.CantSplit.HasValue
                ? !row.Properties.CantSplit.Value
                : (bool?)null;

            state = new TablePropertiesDialogState(
                tableProperties.Alignment,
                preferredWidth,
                preferredWidthUnit,
                indentValue,
                indentUnit,
                tableProperties.LayoutMode,
                spacingValue,
                spacingUnit,
                paddingPoints,
                rowHeightPoints,
                row.Properties.HeightRule,
                allowRowBreak,
                row.Properties.RepeatOnEachPage,
                columnWidthPoints,
                cell.Properties.VerticalAlignment);

            return true;
        }

        static bool TryGetTableLayout(
            DocumentLayout layout,
            Document document,
            TableBlock table,
            out TableLayout tableLayout)
        {
            tableLayout = null!;
            var tableIndex = 0;
            foreach (var block in document.Blocks)
            {
                if (block is not TableBlock candidate)
                {
                    continue;
                }

                if (ReferenceEquals(candidate, table))
                {
                    if (layout.Tables.Count > tableIndex)
                    {
                        tableLayout = layout.Tables[tableIndex];
                        return true;
                    }

                    return false;
                }

                tableIndex++;
            }

            return false;
        }

        FontDialogState ResolveFontDialogState()
        {
            string? fontFamily = null;
            float? fontSize = null;
            DocFontWeight? fontWeight = null;
            DocFontStyle? fontStyle = null;
            DocUnderlineStyle? underlineStyle = null;
            DocColor? underlineColor = null;
            DocColor? fontColor = null;
            bool? strikethrough = null;
            bool? smallCaps = null;
            bool? caps = null;
            DocVerticalPosition? verticalPosition = null;
            float? characterScalePercent = null;
            float? characterSpacingPoints = null;
            float? characterPositionPoints = null;
            bool? textOutline = null;
            bool? textShadow = null;
            bool? textEmboss = null;
            bool? textImprint = null;
            DocLigatureOptions? ligatures = null;
            bool? contextualAlternates = null;
            DocNumberForm? numberForm = null;
            DocNumberSpacing? numberSpacing = null;
            uint? stylisticSets = null;

            if (TryGetSnapshot(out var snapshot))
            {
                if (TryGetValue(snapshot.Formatting.FontFamily, out var resolvedFamily))
                {
                    fontFamily = resolvedFamily;
                }

                if (TryGetValue(snapshot.Formatting.FontSize, out var resolvedSize))
                {
                    fontSize = resolvedSize;
                }

                if (TryGetValue(snapshot.Formatting.Bold, out var bold))
                {
                    fontWeight = bold ? DocFontWeight.Bold : DocFontWeight.Normal;
                }

                if (TryGetValue(snapshot.Formatting.Italic, out var italic))
                {
                    fontStyle = italic ? DocFontStyle.Italic : DocFontStyle.Normal;
                }

                if (TryGetValue(snapshot.Formatting.UnderlineStyle, out var resolvedUnderlineStyle))
                {
                    underlineStyle = resolvedUnderlineStyle;
                }
                else if (TryGetValue(snapshot.Formatting.Underline, out var underline))
                {
                    underlineStyle = underline ? DocUnderlineStyle.Single : DocUnderlineStyle.None;
                }

                if (TryGetValue(snapshot.Formatting.UnderlineColor, out var resolvedUnderlineColor))
                {
                    underlineColor = resolvedUnderlineColor;
                }

                if (TryGetValue(snapshot.Formatting.FontColor, out var resolvedColor))
                {
                    fontColor = resolvedColor;
                }

                if (TryGetValue(snapshot.Formatting.Strikethrough, out var resolvedStrike))
                {
                    strikethrough = resolvedStrike;
                }

                if (TryGetValue(snapshot.Formatting.SmallCaps, out var resolvedSmallCaps))
                {
                    smallCaps = resolvedSmallCaps;
                }

                if (TryGetValue(snapshot.Formatting.Caps, out var resolvedCaps))
                {
                    caps = resolvedCaps;
                }

                if (TryGetValue(snapshot.Formatting.VerticalPosition, out var resolvedPosition))
                {
                    verticalPosition = resolvedPosition;
                }

                if (TryGetValue(snapshot.Formatting.HorizontalScale, out var resolvedScale))
                {
                    characterScalePercent = resolvedScale * 100f;
                }

                if (TryGetValue(snapshot.Formatting.LetterSpacing, out var resolvedSpacing))
                {
                    characterSpacingPoints = DipToPoints(resolvedSpacing);
                }

                if (TryGetValue(snapshot.Formatting.BaselineOffset, out var resolvedOffset))
                {
                    characterPositionPoints = DipToPoints(resolvedOffset);
                }

                if (TryGetValue(snapshot.Formatting.TextOutline, out var resolvedOutline))
                {
                    textOutline = resolvedOutline;
                }

                if (TryGetValue(snapshot.Formatting.TextShadow, out var resolvedShadow))
                {
                    textShadow = resolvedShadow;
                }

                if (TryGetValue(snapshot.Formatting.TextEmboss, out var resolvedEmboss))
                {
                    textEmboss = resolvedEmboss;
                }

                if (TryGetValue(snapshot.Formatting.TextImprint, out var resolvedImprint))
                {
                    textImprint = resolvedImprint;
                }

                if (TryGetValue(snapshot.Formatting.Ligatures, out var resolvedLigatures))
                {
                    ligatures = resolvedLigatures;
                }

                if (TryGetValue(snapshot.Formatting.ContextualAlternates, out var resolvedContextualAlternates))
                {
                    contextualAlternates = resolvedContextualAlternates;
                }

                if (TryGetValue(snapshot.Formatting.NumberForm, out var resolvedNumberForm))
                {
                    numberForm = resolvedNumberForm;
                }

                if (TryGetValue(snapshot.Formatting.NumberSpacing, out var resolvedNumberSpacing))
                {
                    numberSpacing = resolvedNumberSpacing;
                }

                if (TryGetValue(snapshot.Formatting.StylisticSets, out var resolvedStylisticSets))
                {
                    stylisticSets = resolvedStylisticSets;
                }
            }

            return new FontDialogState(
                fontFamily,
                fontSize,
                fontWeight,
                fontStyle,
                underlineStyle,
                underlineColor,
                fontColor,
                strikethrough,
                smallCaps,
                caps,
                verticalPosition,
                textOutline,
                textShadow,
                textEmboss,
                textImprint,
                characterScalePercent,
                characterSpacingPoints,
                characterPositionPoints,
                ligatures,
                contextualAlternates,
                numberForm,
                numberSpacing,
                stylisticSets);
        }

        static float ResolveSpacingPoints(EditorValue<float> value)
        {
            if (!value.HasValue || value.IsMixed)
            {
                return 0f;
            }

            return DipToPoints(value.Value);
        }

        static void ResolveLineSpacingKind(
            EditorParagraphSnapshot paragraph,
            ref LineSpacingOptionKind kind,
            ref float atValue)
        {
            if (!paragraph.LineSpacing.HasValue || paragraph.LineSpacing.IsMixed)
            {
                return;
            }

            var lineSpacing = paragraph.LineSpacing.Value;
            if (lineSpacing <= 0)
            {
                return;
            }

            var rule = paragraph.LineSpacingRule.HasValue && !paragraph.LineSpacingRule.IsMixed
                ? paragraph.LineSpacingRule.Value
                : DocLineSpacingRule.Auto;

            if (rule == DocLineSpacingRule.Auto)
            {
                var multiple = lineSpacing / 240f;
                if (IsClose(multiple, 1f))
                {
                    kind = LineSpacingOptionKind.Single;
                    atValue = 1f;
                }
                else if (IsClose(multiple, 1.15f))
                {
                    kind = LineSpacingOptionKind.One15;
                    atValue = 1.15f;
                }
                else if (IsClose(multiple, 1.5f))
                {
                    kind = LineSpacingOptionKind.One5;
                    atValue = 1.5f;
                }
                else if (IsClose(multiple, 2f))
                {
                    kind = LineSpacingOptionKind.Double;
                    atValue = 2f;
                }
                else
                {
                    kind = LineSpacingOptionKind.Multiple;
                    atValue = multiple;
                }

                return;
            }

            kind = rule == DocLineSpacingRule.AtLeast
                ? LineSpacingOptionKind.AtLeast
                : LineSpacingOptionKind.Exactly;
            atValue = lineSpacing / 20f;
        }

        static void ResolveLineSpacingKindOptional(
            EditorParagraphSnapshot paragraph,
            ref LineSpacingOptionKind? kind,
            ref float? atValue)
        {
            if (!paragraph.LineSpacing.HasValue || paragraph.LineSpacing.IsMixed)
            {
                return;
            }

            var lineSpacing = paragraph.LineSpacing.Value;
            if (lineSpacing <= 0)
            {
                return;
            }

            var rule = paragraph.LineSpacingRule.HasValue && !paragraph.LineSpacingRule.IsMixed
                ? paragraph.LineSpacingRule.Value
                : DocLineSpacingRule.Auto;

            if (rule == DocLineSpacingRule.Auto)
            {
                var multiple = lineSpacing / 240f;
                if (IsClose(multiple, 1f))
                {
                    kind = LineSpacingOptionKind.Single;
                    atValue = 1f;
                }
                else if (IsClose(multiple, 1.15f))
                {
                    kind = LineSpacingOptionKind.One15;
                    atValue = 1.15f;
                }
                else if (IsClose(multiple, 1.5f))
                {
                    kind = LineSpacingOptionKind.One5;
                    atValue = 1.5f;
                }
                else if (IsClose(multiple, 2f))
                {
                    kind = LineSpacingOptionKind.Double;
                    atValue = 2f;
                }
                else
                {
                    kind = LineSpacingOptionKind.Multiple;
                    atValue = multiple;
                }

                return;
            }

            kind = rule == DocLineSpacingRule.AtLeast
                ? LineSpacingOptionKind.AtLeast
                : LineSpacingOptionKind.Exactly;
            atValue = lineSpacing / 20f;
        }

        static bool IsClose(float value, float target)
        {
            return MathF.Abs(value - target) < 0.05f;
        }

        static float DipToPoints(float dip)
        {
            return dip / (96f / 72f);
        }

        IReadOnlyList<string> ResolveFontDialogFamilies()
        {
            if (_editorView is null)
            {
                return Array.Empty<string>();
            }

            if (_editorView.TryGetService<IFontService>(out var fontService))
            {
                var list = new List<string>();
                foreach (var font in fontService.GetFontFamilies())
                {
                    if (!string.IsNullOrWhiteSpace(font.Name))
                    {
                        list.Add(font.Name);
                    }
                }

                return list;
            }

            return Array.Empty<string>();
        }

        static bool TryGetValue<T>(EditorValue<T> value, out T result)
        {
            if (!value.HasValue || value.IsMixed)
            {
                result = default!;
                return false;
            }

            result = value.Value!;
            return true;
        }

        bool IsFormatPainterActive()
        {
            if (_editorView is null)
            {
                return false;
            }

            return _editorView.TryGetService<IFormatPainterService>(out var service) && service.IsActive;
        }

        bool IsShowInvisiblesActive()
        {
            if (!canInteract() || _editorView is null)
            {
                return false;
            }

            return _editorView.TryGetService<IEditorViewOptionsService>(out var service) && service.ShowInvisibles;
        }

        bool TryGetViewOptionsService(out IEditorViewOptionsService service)
        {
            service = null!;
            return _editorView is not null && _editorView.TryGetService(out service);
        }

        bool IsRulerActive()
        {
            return canInteract() && TryGetViewOptionsService(out var service) && service.ShowRuler;
        }

        bool IsGridlinesActive()
        {
            return canInteract() && TryGetViewOptionsService(out var service) && service.ShowGridlines;
        }

        bool IsNavigationPaneActive()
        {
            return canInteract() && TryGetViewOptionsService(out var service) && service.ShowNavigationPane;
        }

        bool IsHeaderFooterEditing()
        {
            return canInteract() && _editorView?.IsHeaderFooterEditing == true;
        }

        bool IsViewMode(EditorViewMode mode)
        {
            return canInteract() && TryGetViewOptionsService(out var service) && service.ViewMode == mode;
        }

        bool IsPageMovementVertical()
        {
            return canInteract() && TryGetViewOptionsService(out var service)
                   && service.PageMovement == PageFlowDirection.Vertical;
        }

        bool IsPageMovementSideToSide()
        {
            return canInteract() && TryGetViewOptionsService(out var service)
                   && service.PageMovement == PageFlowDirection.Horizontal;
        }

        static bool MatchesValue<T>(EditorValue<T> value, T expected) where T : struct
        {
            return value.HasValue && !value.IsMixed
                   && EqualityComparer<T>.Default.Equals(value.Value, expected);
        }

        static bool IsActive(EditorValue<bool> value)
        {
            if (value.IsMixed)
            {
                return true;
            }

            return value.HasValue && value.Value;
        }

        bool IsFormattingValue<T>(Func<EditorFormattingSnapshot, EditorValue<T>> selector, T expected) where T : struct
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return false;
            }

            return MatchesValue(selector(snapshot.Formatting), expected);
        }

        bool IsEffectActive(Func<EditorFormattingSnapshot, EditorValue<bool>> selector)
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return false;
            }

            return IsActive(selector(snapshot.Formatting));
        }

        bool CanClearTextEffects()
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return false;
            }

            var formatting = snapshot.Formatting;
            return IsActive(formatting.TextOutline)
                   || IsActive(formatting.TextShadow)
                   || IsActive(formatting.TextEmboss)
                   || IsActive(formatting.TextImprint);
        }

        bool IsParagraphValue<T>(Func<EditorParagraphSnapshot, EditorValue<T>> selector, T expected) where T : struct
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return false;
            }

            return MatchesValue(selector(snapshot.Paragraph), expected);
        }

        string? ResolveFontFamilyText()
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return null;
            }

            var value = snapshot.Formatting.FontFamily;
            if (!value.HasValue || value.IsMixed || string.IsNullOrWhiteSpace(value.Value))
            {
                return null;
            }

            return value.Value;
        }

        string? ResolveFontSizeText()
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return null;
            }

            var value = snapshot.Formatting.FontSize;
            if (!value.HasValue || value.IsMixed)
            {
                return null;
            }

            var size = value.Value;
            var points = DipsToPoints(size);
            return points.ToString("0.#", CultureInfo.InvariantCulture);
        }

        static bool TryParseFontSize(string? text, out float value)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                value = default;
                return false;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        async ValueTask ApplyFontFamilyAsync(string? family)
        {
            if (string.IsNullOrWhiteSpace(family))
            {
                return;
            }

            await ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.FamilySet, family);
        }

        async ValueTask ApplyFontSizeAsync(string? sizeText)
        {
            if (!TryParseFontSize(sizeText, out var size))
            {
                return;
            }

            await ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.SizeSet, PointsToDips(size));
        }

        async ValueTask ApplyFontSizeItemAsync(RibbonComboBoxItem? item)
        {
            if (item is null)
            {
                return;
            }

            await ApplyFontSizeAsync(item.Value ?? item.Label ?? item.Id);
        }

        async ValueTask ApplyFontFamilyItemAsync(RibbonComboBoxItem? item)
        {
            if (item is null)
            {
                return;
            }

            await ApplyFontFamilyAsync(item.Value ?? item.Label ?? item.Id);
        }

        List<RibbonComboBoxItem> BuildFontFamilyItems()
        {
            var list = new List<RibbonComboBoxItem>();
            if (TryGetSnapshot(out var snapshot))
            {
                foreach (var font in snapshot.FontFamilies)
                {
                    list.Add(new RibbonComboBoxItem(font.Name, font.Name, font.Name));
                }

                if (list.Count == 0)
                {
                    var current = snapshot.Formatting.FontFamily;
                    if (current.HasValue && !current.IsMixed && !string.IsNullOrWhiteSpace(current.Value))
                    {
                        var name = current.Value!;
                        list.Add(new RibbonComboBoxItem(name, name, name));
                    }
                }
            }

            return list;
        }

        List<RibbonComboBoxItem> BuildFontSizeItems()
        {
            var sizes = new[]
            {
                "8", "9", "10", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "36", "48", "72"
            };
            var list = new List<RibbonComboBoxItem>(sizes.Length);
            foreach (var size in sizes)
            {
                list.Add(new RibbonComboBoxItem($"size-{size}", size, size));
            }

            return list;
        }

        static RibbonColorItem? FindColorItem(
            IReadOnlyList<RibbonColorItem> palette,
            DocColor? color,
            RibbonColorKind? fallbackKind = null,
            string? customId = null)
        {
            if (color.HasValue)
            {
                foreach (var item in palette)
                {
                    if (item.Color.HasValue && item.Color.Value == color.Value)
                    {
                        return item;
                    }
                }

                return CreateCustomColorItem(customId ?? "custom-color", color.Value);
            }

            if (fallbackKind.HasValue)
            {
                foreach (var item in palette)
                {
                    if (item.Kind == fallbackKind.Value)
                    {
                        return item;
                    }
                }
            }

            return palette.Count > 0 ? palette[0] : null;
        }

        static object? ResolveColorPayload(RibbonColorItem? item)
        {
            if (item is null || item.Kind is RibbonColorKind.None or RibbonColorKind.Picker)
            {
                return null;
            }

            return item.Color;
        }

        RibbonColorItem? ResolveFontColorSelection(IReadOnlyList<RibbonColorItem> palette)
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return FindColorItem(palette, null, RibbonColorKind.Automatic);
            }

            var value = snapshot.Formatting.FontColor;
            if (value.HasValue && !value.IsMixed)
            {
                return FindColorItem(palette, value.Value, RibbonColorKind.Automatic, "font-color-custom");
            }

            return FindColorItem(palette, null, RibbonColorKind.Automatic);
        }

        RibbonColorItem? ResolveHighlightSelection(IReadOnlyList<RibbonColorItem> palette)
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return FindColorItem(palette, null, RibbonColorKind.None);
            }

            var value = snapshot.Formatting.HighlightColor;
            if (value.HasValue && !value.IsMixed)
            {
                return FindColorItem(palette, value.Value, RibbonColorKind.None, "highlight-custom");
            }

            return FindColorItem(palette, null, RibbonColorKind.None);
        }

        RibbonColorItem? ResolveShadingSelection(IReadOnlyList<RibbonColorItem> palette)
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return FindColorItem(palette, null, RibbonColorKind.None);
            }

            var value = snapshot.Paragraph.ShadingColor;
            if (value.HasValue && !value.IsMixed)
            {
                return FindColorItem(palette, value.Value, RibbonColorKind.None, "shading-custom");
            }

            return FindColorItem(palette, null, RibbonColorKind.None);
        }

        List<RibbonColorItem> BuildFontColorPalette()
        {
            return new List<RibbonColorItem>
            {
                new RibbonColorItem("font-color-auto", "Automatic", RibbonColorKind.Automatic, DocColor.Black),
                new RibbonColorItem("font-color-black", "Black", RibbonColorKind.Rgb, new DocColor(0, 0, 0)),
                new RibbonColorItem("font-color-dark-gray", "Dark Gray", RibbonColorKind.Rgb, new DocColor(64, 64, 64)),
                new RibbonColorItem("font-color-gray", "Gray", RibbonColorKind.Rgb, new DocColor(102, 102, 102)),
                new RibbonColorItem("font-color-light-gray", "Light Gray", RibbonColorKind.Rgb, new DocColor(217, 217, 217)),
                new RibbonColorItem("font-color-white", "White", RibbonColorKind.Rgb, new DocColor(255, 255, 255)),
                new RibbonColorItem("font-color-dark-red", "Dark Red", RibbonColorKind.Rgb, new DocColor(128, 0, 0)),
                new RibbonColorItem("font-color-red", "Red", RibbonColorKind.Rgb, new DocColor(192, 0, 0)),
                new RibbonColorItem("font-color-orange", "Orange", RibbonColorKind.Rgb, new DocColor(230, 145, 56)),
                new RibbonColorItem("font-color-gold", "Gold", RibbonColorKind.Rgb, new DocColor(191, 144, 0)),
                new RibbonColorItem("font-color-yellow", "Yellow", RibbonColorKind.Rgb, new DocColor(241, 194, 50)),
                new RibbonColorItem("font-color-green", "Green", RibbonColorKind.Rgb, new DocColor(106, 168, 79)),
                new RibbonColorItem("font-color-dark-green", "Dark Green", RibbonColorKind.Rgb, new DocColor(0, 100, 0)),
                new RibbonColorItem("font-color-teal", "Teal", RibbonColorKind.Rgb, new DocColor(69, 129, 142)),
                new RibbonColorItem("font-color-blue", "Blue", RibbonColorKind.Rgb, new DocColor(61, 133, 198)),
                new RibbonColorItem("font-color-dark-blue", "Dark Blue", RibbonColorKind.Rgb, new DocColor(0, 51, 102)),
                new RibbonColorItem("font-color-purple", "Purple", RibbonColorKind.Rgb, new DocColor(142, 124, 195)),
                new RibbonColorItem("font-color-dark-purple", "Dark Purple", RibbonColorKind.Rgb, new DocColor(76, 0, 130)),
                CreateMoreColorsItem("font-color-more")
            };
        }

        List<RibbonColorItem> BuildHighlightPalette()
        {
            return new List<RibbonColorItem>
            {
                new RibbonColorItem("highlight-none", "No Color", RibbonColorKind.None),
                new RibbonColorItem("highlight-yellow", "Yellow", RibbonColorKind.Rgb, new DocColor(255, 255, 0)),
                new RibbonColorItem("highlight-lime", "Bright Green", RibbonColorKind.Rgb, new DocColor(0, 255, 0)),
                new RibbonColorItem("highlight-cyan", "Turquoise", RibbonColorKind.Rgb, new DocColor(0, 255, 255)),
                new RibbonColorItem("highlight-magenta", "Pink", RibbonColorKind.Rgb, new DocColor(255, 0, 255)),
                new RibbonColorItem("highlight-blue", "Blue", RibbonColorKind.Rgb, new DocColor(0, 0, 255)),
                new RibbonColorItem("highlight-red", "Red", RibbonColorKind.Rgb, new DocColor(255, 0, 0)),
                new RibbonColorItem("highlight-dark-blue", "Dark Blue", RibbonColorKind.Rgb, new DocColor(0, 0, 128)),
                new RibbonColorItem("highlight-teal", "Teal", RibbonColorKind.Rgb, new DocColor(0, 128, 128)),
                new RibbonColorItem("highlight-green", "Green", RibbonColorKind.Rgb, new DocColor(0, 128, 0)),
                new RibbonColorItem("highlight-violet", "Violet", RibbonColorKind.Rgb, new DocColor(128, 0, 128)),
                new RibbonColorItem("highlight-dark-red", "Dark Red", RibbonColorKind.Rgb, new DocColor(128, 0, 0)),
                new RibbonColorItem("highlight-dark-yellow", "Dark Yellow", RibbonColorKind.Rgb, new DocColor(128, 128, 0)),
                new RibbonColorItem("highlight-gray", "Gray", RibbonColorKind.Rgb, new DocColor(128, 128, 128)),
                new RibbonColorItem("highlight-black", "Black", RibbonColorKind.Rgb, new DocColor(0, 0, 0)),
                new RibbonColorItem("highlight-white", "White", RibbonColorKind.Rgb, new DocColor(255, 255, 255)),
                CreateMoreColorsItem("highlight-more")
            };
        }

        List<RibbonColorItem> BuildShadingPalette()
        {
            return new List<RibbonColorItem>
            {
                new RibbonColorItem("shading-none", "No Color", RibbonColorKind.None),
                new RibbonColorItem("shading-light-gray", "Light Gray", RibbonColorKind.Rgb, new DocColor(242, 242, 242)),
                new RibbonColorItem("shading-gray", "Gray", RibbonColorKind.Rgb, new DocColor(217, 217, 217)),
                new RibbonColorItem("shading-dark-gray", "Dark Gray", RibbonColorKind.Rgb, new DocColor(191, 191, 191)),
                new RibbonColorItem("shading-light-yellow", "Light Yellow", RibbonColorKind.Rgb, new DocColor(255, 249, 196)),
                new RibbonColorItem("shading-light-orange", "Light Orange", RibbonColorKind.Rgb, new DocColor(255, 229, 204)),
                new RibbonColorItem("shading-light-red", "Light Red", RibbonColorKind.Rgb, new DocColor(244, 204, 204)),
                new RibbonColorItem("shading-light-pink", "Light Pink", RibbonColorKind.Rgb, new DocColor(252, 228, 214)),
                new RibbonColorItem("shading-light-blue", "Light Blue", RibbonColorKind.Rgb, new DocColor(217, 231, 252)),
                new RibbonColorItem("shading-light-green", "Light Green", RibbonColorKind.Rgb, new DocColor(219, 241, 222)),
                new RibbonColorItem("shading-light-teal", "Light Teal", RibbonColorKind.Rgb, new DocColor(217, 234, 211)),
                new RibbonColorItem("shading-light-purple", "Light Purple", RibbonColorKind.Rgb, new DocColor(232, 223, 245)),
                CreateMoreColorsItem("shading-more")
            };
        }

        static RibbonColorItem CreateCustomColorItem(string id, DocColor color)
        {
            return new RibbonColorItem(id, "Custom", RibbonColorKind.Custom, color);
        }

        static RibbonColorItem CreateMoreColorsItem(string id)
        {
            return new RibbonColorItem(id, "More Colors...", RibbonColorKind.Picker, iconKey: "RibbonIcon.MoreColors");
        }

        const float DipsPerInch = 96f;
        const float PointsToDipScale = 96f / 72f;
        static float InchesToDips(float inches) => inches * DipsPerInch;
        static float PointsToDips(double points) => (float)(points * PointsToDipScale);
        static double DipsToPoints(float dips) => dips / PointsToDipScale;

        var newCommand = CreateAsyncCommand(NewDocumentAsync, canInteract);
        var openCommand = CreateAsyncCommand(OpenDocumentAsync, canInteract);
        var saveCommand = CreateAsyncCommand(() => SaveDocumentAsync(), canInteract);
        var saveAsCommand = CreateAsyncCommand(SaveDocumentAsAsync, canInteract);
        var printCommand = CreateAsyncCommand(ShowPrintDialogAsync, canInteract);

        var newButton = new RibbonButton(
            "new",
            "New",
            newCommand,
            keyTip: "N",
            iconKey: "RibbonIcon.New",
            canExecute: canInteract);

        var openButton = new RibbonButton(
            "open",
            "Open",
            openCommand,
            keyTip: "O",
            iconKey: "RibbonIcon.Open",
            canExecute: canInteract);

        var saveMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "save-item",
                "Save",
                saveCommand,
                iconKey: "RibbonIcon.Save",
                canExecute: canInteract),
            new RibbonMenuItem(
                "save-as",
                "Save As",
                saveAsCommand,
                iconKey: "RibbonIcon.SaveAs",
                canExecute: canInteract)
        });

        var saveSplit = new RibbonSplitButton(
            "save",
            "Save",
            saveCommand,
            saveMenu,
            keyTip: "S",
            iconKey: "RibbonIcon.Save",
            canExecute: canInteract);

        var printButton = new RibbonButton(
            "print",
            "Print",
            printCommand,
            keyTip: "P",
            iconKey: "RibbonIcon.Print",
            canExecute: canInteract,
            toolTipDescription: "Print the current document.");

        var undoButton = new RibbonButton(
            "undo",
            "Undo",
            CreateEditorCommand(EditorHomeCommandIds.Editing.Undo),
            keyTip: "Z",
            iconKey: "RibbonIcon.Undo",
            size: RibbonControlSize.Small);

        var redoButton = new RibbonButton(
            "redo",
            "Redo",
            CreateEditorCommand(EditorHomeCommandIds.Editing.Redo),
            keyTip: "Y",
            iconKey: "RibbonIcon.Redo",
            size: RibbonControlSize.Small);

        var undoGroup = new RibbonGroup(
            "undo",
            "Undo",
            new IRibbonControl[]
            {
                undoButton,
                redoButton
            },
            keyTip: "UD");

        var fileGroup = new RibbonGroup(
            "file",
            "File",
            new IRibbonControl[]
            {
                newButton,
                openButton,
                saveSplit,
                printButton
            },
            keyTip: "FI");

        var fontFamilyItems = BuildFontFamilyItems();
        var fontSizeItems = BuildFontSizeItems();
        var fontColorPalette = BuildFontColorPalette();
        var highlightPalette = BuildHighlightPalette();
        var shadingPalette = BuildShadingPalette();
        RefreshStyleGalleryItems();

        RibbonGalleryItem? ResolveStyleSelection()
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return null;
            }

            var value = snapshot.CurrentParagraphStyleId;
            if (!value.HasValue || value.IsMixed || string.IsNullOrWhiteSpace(value.Value))
            {
                return null;
            }

            return _styleGalleryItemMap.TryGetValue(value.Value, out var item) ? item : null;
        }

        RibbonGroup BuildClipboardGroup()
        {
            var pasteMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "paste-keep-source",
                    "Keep Source Formatting",
                    CreateEditorCommand(EditorHomeCommandIds.Clipboard.PasteKeepSource)),
                new RibbonMenuItem(
                    "paste-match-destination",
                    "Match Destination Formatting",
                    CreateEditorCommand(EditorHomeCommandIds.Clipboard.PasteMatchDestination)),
                new RibbonMenuItem(
                    "paste-text-only",
                    "Keep Text Only",
                    CreateEditorCommand(EditorHomeCommandIds.Clipboard.PasteTextOnly)),
                new RibbonMenuItem(
                    "paste-markdown",
                    "Paste Markdown",
                    CreateEditorCommand(EditorHomeCommandIds.Clipboard.PasteMarkdown))
            });

            var pasteSplit = new RibbonSplitButton(
                "paste",
                "Paste",
                CreateEditorCommand(EditorHomeCommandIds.Clipboard.Paste),
                pasteMenu,
                keyTip: "V",
                iconKey: "RibbonIcon.Paste",
                size: RibbonControlSize.Large);

            var cutButton = new RibbonButton(
                "cut",
                "Cut",
                CreateEditorCommand(EditorHomeCommandIds.Clipboard.Cut),
                keyTip: "X",
                iconKey: "RibbonIcon.Cut",
                size: RibbonControlSize.Small);

            var copyButton = new RibbonButton(
                "copy",
                "Copy",
                CreateEditorCommand(EditorHomeCommandIds.Clipboard.Copy),
                keyTip: "C",
                iconKey: "RibbonIcon.Copy",
                size: RibbonControlSize.Small);

            var copyMarkdownButton = new RibbonButton(
                "copy-markdown",
                "Copy as Markdown",
                CreateEditorCommand(EditorHomeCommandIds.Clipboard.CopyAsMarkdown),
                iconKey: "RibbonIcon.Copy",
                size: RibbonControlSize.Small);

            var formatPainter = new RibbonToggleButton(
                "format-painter",
                "Format Painter",
                IsFormatPainterActive,
                command: CreateEditorCommand(EditorHomeCommandIds.Clipboard.FormatPainterToggle),
                keyTip: "FP",
                iconKey: "RibbonIcon.FormatPainter",
                size: RibbonControlSize.Small);

            return new RibbonGroup(
                "clipboard",
                "Clipboard",
                new IRibbonControl[]
                {
                    pasteSplit,
                    cutButton,
                    copyButton,
                    copyMarkdownButton,
                    formatPainter
                },
                keyTip: "CL");
        }

        RibbonGroup BuildFontGroup()
        {
            var fontFamilyCombo = new RibbonComboBox(
                "font-family",
                "Font",
                fontFamilyItems,
                isEditable: true,
                textEvaluator: ResolveFontFamilyText,
                textChangedHandler: ApplyFontFamilyAsync,
                selectionHandler: ApplyFontFamilyItemAsync,
                keyTip: "FF",
                iconKey: "RibbonIcon.FontFamily",
                size: RibbonControlSize.Medium);

            var fontSizeCombo = new RibbonComboBox(
                "font-size",
                "Size",
                fontSizeItems,
                isEditable: true,
                textEvaluator: ResolveFontSizeText,
                textChangedHandler: ApplyFontSizeAsync,
                selectionHandler: ApplyFontSizeItemAsync,
                keyTip: "FS",
                iconKey: "RibbonIcon.FontSize",
                size: RibbonControlSize.Medium);

            var growFont = new RibbonButton(
                "font-grow",
                "Grow Font",
                CreateEditorCommand(EditorHomeCommandIds.Font.SizeIncrease),
                keyTip: "FG",
                iconKey: "RibbonIcon.GrowFont",
                size: RibbonControlSize.Small);

            var shrinkFont = new RibbonButton(
                "font-shrink",
                "Shrink Font",
                CreateEditorCommand(EditorHomeCommandIds.Font.SizeDecrease),
                keyTip: "FK",
                iconKey: "RibbonIcon.ShrinkFont",
                size: RibbonControlSize.Small);

            var changeCaseMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "change-case-sentence",
                    "Sentence case",
                    CreateEditorCommand(EditorHomeCommandIds.Font.ChangeCaseSentence)),
                new RibbonMenuItem(
                    "change-case-lower",
                    "lowercase",
                    CreateEditorCommand(EditorHomeCommandIds.Font.ChangeCaseLower)),
                new RibbonMenuItem(
                    "change-case-upper",
                    "UPPERCASE",
                    CreateEditorCommand(EditorHomeCommandIds.Font.ChangeCaseUpper)),
                new RibbonMenuItem(
                    "change-case-capitalize",
                    "Capitalize Each Word",
                    CreateEditorCommand(EditorHomeCommandIds.Font.ChangeCaseCapitalize)),
                new RibbonMenuItem(
                    "change-case-toggle",
                    "tOGGLE cASE",
                    CreateEditorCommand(EditorHomeCommandIds.Font.ChangeCaseToggle))
            });

            var changeCase = new RibbonDropdownButton(
                "font-case",
                "Change Case",
                changeCaseMenu,
                keyTip: "CC",
                iconKey: "RibbonIcon.ChangeCase",
                size: RibbonControlSize.Small,
                compactLabel: "Case",
                labelMode: RibbonLabelMode.ForceVisible);

            var clearFormatting = new RibbonButton(
                "font-clear",
                "Clear Formatting",
                CreateEditorCommand(EditorHomeCommandIds.Font.ClearFormatting),
                keyTip: "CF",
                iconKey: "RibbonIcon.ClearFormatting",
                size: RibbonControlSize.Small);

            var boldToggle = new RibbonToggleButton(
                "font-bold",
                "Bold",
                () => IsFormattingValue(snapshot => snapshot.Bold, true),
                command: CreateEditorCommand(EditorHomeCommandIds.Font.BoldToggle),
                keyTip: "B",
                iconKey: "RibbonIcon.Bold",
                size: RibbonControlSize.Small);

            var italicToggle = new RibbonToggleButton(
                "font-italic",
                "Italic",
                () => IsFormattingValue(snapshot => snapshot.Italic, true),
                command: CreateEditorCommand(EditorHomeCommandIds.Font.ItalicToggle),
                keyTip: "I",
                iconKey: "RibbonIcon.Italic",
                size: RibbonControlSize.Small);

            var underlineMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "underline-single",
                    "Single",
                    CreateEditorCommand(EditorHomeCommandIds.Font.UnderlineStyleSet, DocUnderlineStyle.Single)),
                new RibbonMenuItem(
                    "underline-double",
                    "Double",
                    CreateEditorCommand(EditorHomeCommandIds.Font.UnderlineStyleSet, DocUnderlineStyle.Double)),
                new RibbonMenuItem(
                    "underline-wavy",
                    "Wavy",
                    CreateEditorCommand(EditorHomeCommandIds.Font.UnderlineStyleSet, DocUnderlineStyle.Wave)),
                new RibbonMenuItem(
                    "underline-none",
                    "None",
                    CreateEditorCommand(EditorHomeCommandIds.Font.UnderlineStyleSet, DocUnderlineStyle.None))
            });

            var underlineSplit = new RibbonSplitToggleButton(
                "font-underline",
                "Underline",
                underlineMenu,
                isCheckedEvaluator: () => IsFormattingValue(snapshot => snapshot.Underline, true),
                command: CreateEditorCommand(EditorHomeCommandIds.Font.UnderlineToggle),
                keyTip: "U",
                iconKey: "RibbonIcon.Underline",
                size: RibbonControlSize.Small);

            var strikethroughToggle = new RibbonToggleButton(
                "font-strikethrough",
                "Strikethrough",
                () => IsFormattingValue(snapshot => snapshot.Strikethrough, true),
                command: CreateEditorCommand(EditorHomeCommandIds.Font.StrikethroughToggle),
                keyTip: "S",
                iconKey: "RibbonIcon.Strikethrough",
                size: RibbonControlSize.Small);

            var superscriptToggle = new RibbonToggleButton(
                "font-superscript",
                "Superscript",
                () => IsFormattingValue(snapshot => snapshot.VerticalPosition, DocVerticalPosition.Superscript),
                command: CreateEditorCommand(EditorHomeCommandIds.Font.SuperscriptToggle),
                keyTip: "SU",
                iconKey: "RibbonIcon.Superscript",
                size: RibbonControlSize.Small);

            var subscriptToggle = new RibbonToggleButton(
                "font-subscript",
                "Subscript",
                () => IsFormattingValue(snapshot => snapshot.VerticalPosition, DocVerticalPosition.Subscript),
                command: CreateEditorCommand(EditorHomeCommandIds.Font.SubscriptToggle),
                keyTip: "SB",
                iconKey: "RibbonIcon.Subscript",
                size: RibbonControlSize.Small);

            var textEffectsMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuToggleItem(
                    "text-effects-outline",
                    "Outline",
                    () => IsEffectActive(snapshot => snapshot.TextOutline),
                    CreateEditorCommand(EditorHomeCommandIds.Font.TextEffectOutline)),
                new RibbonMenuToggleItem(
                    "text-effects-shadow",
                    "Shadow",
                    () => IsEffectActive(snapshot => snapshot.TextShadow),
                    CreateEditorCommand(EditorHomeCommandIds.Font.TextEffectShadow)),
                new RibbonMenuToggleItem(
                    "text-effects-emboss",
                    "Emboss",
                    () => IsEffectActive(snapshot => snapshot.TextEmboss),
                    CreateEditorCommand(EditorHomeCommandIds.Font.TextEffectEmboss)),
                new RibbonMenuToggleItem(
                    "text-effects-imprint",
                    "Imprint",
                    () => IsEffectActive(snapshot => snapshot.TextImprint),
                    CreateEditorCommand(EditorHomeCommandIds.Font.TextEffectImprint)),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "text-effects-clear",
                    "Clear Text Effects",
                    CreateEditorCommand(EditorHomeCommandIds.Font.TextEffectClear),
                    canExecute: () => CanClearTextEffects())
            });

            var textEffects = new RibbonDropdownButton(
                "font-effects",
                "Text Effects",
                textEffectsMenu,
                keyTip: "TX",
                iconKey: "RibbonIcon.TextEffects",
                size: RibbonControlSize.Small);

            var highlightButton = new RibbonColorSplitButton(
                "font-highlight",
                "Highlight",
                CreateEditorCommandWithPayload(
                    EditorHomeCommandIds.Font.HighlightSet,
                    () => ResolveColorPayload(ResolveHighlightSelection(highlightPalette))),
                highlightPalette,
                selectedColorEvaluator: () => ResolveHighlightSelection(highlightPalette),
                selectionHandler: color =>
                    ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.HighlightSet, ResolveColorPayload(color)),
                keyTip: "H",
                iconKey: "RibbonIcon.Highlight",
                size: RibbonControlSize.Small);

            var fontColorButton = new RibbonColorSplitButton(
                "font-color",
                "Font Color",
                CreateEditorCommandWithPayload(
                    EditorHomeCommandIds.Font.ColorSet,
                    () => ResolveColorPayload(ResolveFontColorSelection(fontColorPalette))),
                fontColorPalette,
                selectedColorEvaluator: () => ResolveFontColorSelection(fontColorPalette),
                selectionHandler: color =>
                    ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.ColorSet, ResolveColorPayload(color)),
                keyTip: "FC",
                iconKey: "RibbonIcon.FontColor",
                size: RibbonControlSize.Small);

            var fontLauncher = new RibbonGroupLauncher(
                "font-launcher",
                "Font Dialog",
                CreateAsyncCommand(OpenFontDialogAsync, () => CanExecuteEditorCommand(EditorHomeCommandIds.Font.DialogApply)),
                keyTip: "FO",
                iconKey: "RibbonIcon.Launcher");

            return new RibbonGroup(
                "font",
                "Font",
                new IRibbonControl[]
                {
                    fontFamilyCombo,
                    fontSizeCombo,
                    growFont,
                    shrinkFont,
                    changeCase,
                    clearFormatting,
                    boldToggle,
                    italicToggle,
                    underlineSplit,
                    strikethroughToggle,
                    subscriptToggle,
                    superscriptToggle,
                    textEffects,
                    highlightButton,
                    fontColorButton
                },
                keyTip: "FN",
                launcher: fontLauncher);
        }

        RibbonGroup BuildParagraphGroup()
        {
            var bulletsToggle = new RibbonToggleButton(
                "para-bullets",
                "Bullets",
                () => IsParagraphValue(snapshot => snapshot.ListKind, ListKind.Bullet),
                command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.ListBullets),
                keyTip: "BU",
                iconKey: "RibbonIcon.Bullets",
                size: RibbonControlSize.Small);

            var numberingToggle = new RibbonToggleButton(
                "para-numbering",
                "Numbering",
                () => IsParagraphValue(snapshot => snapshot.ListKind, ListKind.Numbered),
                command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.ListNumbering),
                keyTip: "NU",
                iconKey: "RibbonIcon.Numbering",
                size: RibbonControlSize.Small);

            var multilevelToggle = new RibbonToggleButton(
                "para-multilevel",
                "Multilevel List",
                () => IsParagraphValue(snapshot => snapshot.ListKind, ListKind.Numbered),
                command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.ListMultilevel),
                keyTip: "ML",
                iconKey: "RibbonIcon.Multilevel",
                size: RibbonControlSize.Small);

            var listToggleGroup = new RibbonToggleGroup(
                "para-lists",
                "Lists",
                new[] { bulletsToggle, numberingToggle, multilevelToggle },
                columns: 3,
                size: RibbonControlSize.Small);

            var indentDecrease = new RibbonButton(
                "para-indent-decrease",
                "Decrease Indent",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.IndentDecrease),
                keyTip: "ID",
                iconKey: "RibbonIcon.IndentDecrease",
                size: RibbonControlSize.Small);

            var indentIncrease = new RibbonButton(
                "para-indent-increase",
                "Increase Indent",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.IndentIncrease),
                keyTip: "IN",
                iconKey: "RibbonIcon.IndentIncrease",
                size: RibbonControlSize.Small);

            var sortParagraph = new RibbonButton(
                "para-sort",
                "Sort",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.Sort),
                keyTip: "SO",
                iconKey: "RibbonIcon.Sort",
                size: RibbonControlSize.Small);

            var showParagraphMarks = new RibbonToggleButton(
                "para-show-marks",
                "Show/Hide ¶",
                IsShowInvisiblesActive,
                value => ToggleShowInvisibles(value),
                keyTip: "SH",
                iconKey: "RibbonIcon.Invisibles",
                canExecute: canInteract,
                size: RibbonControlSize.Small);

            var alignLeft = new RibbonToggleButton(
                "para-align-left",
                "Align Left",
                () => IsParagraphValue(snapshot => snapshot.Alignment, ParagraphAlignment.Left),
                command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.AlignLeft),
                keyTip: "AL",
                iconKey: "RibbonIcon.AlignLeft",
                size: RibbonControlSize.Small,
                compactLabel: "Left");

            var alignCenter = new RibbonToggleButton(
                "para-align-center",
                "Center",
                () => IsParagraphValue(snapshot => snapshot.Alignment, ParagraphAlignment.Center),
                command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.AlignCenter),
                keyTip: "AC",
                iconKey: "RibbonIcon.AlignCenter",
                size: RibbonControlSize.Small,
                compactLabel: "Center");

            var alignRight = new RibbonToggleButton(
                "para-align-right",
                "Align Right",
                () => IsParagraphValue(snapshot => snapshot.Alignment, ParagraphAlignment.Right),
                command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.AlignRight),
                keyTip: "AR",
                iconKey: "RibbonIcon.AlignRight",
                size: RibbonControlSize.Small,
                compactLabel: "Right");

            var alignJustify = new RibbonToggleButton(
                "para-align-justify",
                "Justify",
                () => IsParagraphValue(snapshot => snapshot.Alignment, ParagraphAlignment.Justify),
                command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.AlignJustify),
                keyTip: "AJ",
                iconKey: "RibbonIcon.AlignJustify",
                size: RibbonControlSize.Small,
                compactLabel: "Just");

            var alignToggleGroup = new RibbonToggleGroup(
                "para-align",
                "Alignment",
                new[] { alignLeft, alignCenter, alignRight, alignJustify },
                columns: 2,
                size: RibbonControlSize.Medium);

            var lineSpacingMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "line-spacing-1",
                    "1.0",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.LineSpacingSet,
                        EditorLineSpacingRequest.FromMultiple(1f))),
                new RibbonMenuItem(
                    "line-spacing-1-15",
                    "1.15",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.LineSpacingSet,
                        EditorLineSpacingRequest.FromMultiple(1.15f))),
                new RibbonMenuItem(
                    "line-spacing-1-5",
                    "1.5",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.LineSpacingSet,
                        EditorLineSpacingRequest.FromMultiple(1.5f))),
                new RibbonMenuItem(
                    "line-spacing-2",
                    "2.0",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.LineSpacingSet,
                        EditorLineSpacingRequest.FromMultiple(2f))),
                new RibbonMenuItem(
                    "line-spacing-options",
                    "Line Spacing Options...",
                    CreateAsyncCommand(
                        OpenLineSpacingOptionsAsync,
                        () => CanExecuteEditorCommand(EditorHomeCommandIds.Paragraph.LineSpacingOptions)))
            });

            var lineSpacing = new RibbonDropdownButton(
                "para-line-spacing",
                "Line Spacing",
                lineSpacingMenu,
                keyTip: "LS",
                iconKey: "RibbonIcon.LineSpacing",
                size: RibbonControlSize.Small);

            var shading = new RibbonColorSplitButton(
                "para-shading",
                "Shading",
                CreateEditorCommandWithPayload(
                    EditorHomeCommandIds.Paragraph.ShadingSet,
                    () => ResolveColorPayload(ResolveShadingSelection(shadingPalette))),
                shadingPalette,
                selectedColorEvaluator: () => ResolveShadingSelection(shadingPalette),
                selectionHandler: color =>
                    ExecuteEditorCommandAsync(EditorHomeCommandIds.Paragraph.ShadingSet, ResolveColorPayload(color)),
                keyTip: "SD",
                iconKey: "RibbonIcon.Shading",
                size: RibbonControlSize.Small);

            var borderMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "para-border-none",
                    "No Border",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.BorderSet,
                        new EditorParagraphBorderRequest(EditorParagraphBorderKind.None))),
                new RibbonMenuItem(
                    "para-border-all",
                    "All Borders",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.BorderSet,
                        new EditorParagraphBorderRequest(EditorParagraphBorderKind.All))),
                new RibbonMenuItem(
                    "para-border-outside",
                    "Outside Borders",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.BorderSet,
                        new EditorParagraphBorderRequest(EditorParagraphBorderKind.Outside))),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "para-border-top",
                    "Top Border",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.BorderSet,
                        new EditorParagraphBorderRequest(EditorParagraphBorderKind.Top))),
                new RibbonMenuItem(
                    "para-border-bottom",
                    "Bottom Border",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.BorderSet,
                        new EditorParagraphBorderRequest(EditorParagraphBorderKind.Bottom))),
                new RibbonMenuItem(
                    "para-border-left",
                    "Left Border",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.BorderSet,
                        new EditorParagraphBorderRequest(EditorParagraphBorderKind.Left))),
                new RibbonMenuItem(
                    "para-border-right",
                    "Right Border",
                    CreateEditorCommand(
                        EditorHomeCommandIds.Paragraph.BorderSet,
                        new EditorParagraphBorderRequest(EditorParagraphBorderKind.Right)))
            });

            var borders = new RibbonDropdownButton(
                "para-borders",
                "Borders",
                borderMenu,
                keyTip: "BR",
                iconKey: "RibbonIcon.Borders",
                size: RibbonControlSize.Small);

            var paragraphLauncher = new RibbonGroupLauncher(
                "paragraph-launcher",
                "Paragraph Dialog",
                CreateAsyncCommand(
                    OpenParagraphDialogAsync,
                    () => CanExecuteEditorCommand(EditorHomeCommandIds.Paragraph.DialogApply)),
                keyTip: "PP",
                iconKey: "RibbonIcon.Launcher");

            return new RibbonGroup(
                "paragraph",
                "Paragraph",
                new IRibbonControl[]
                {
                    listToggleGroup,
                    indentDecrease,
                    indentIncrease,
                    sortParagraph,
                    showParagraphMarks,
                    alignToggleGroup,
                    lineSpacing,
                    shading,
                    borders
                },
                keyTip: "PG",
                launcher: paragraphLauncher);
        }

        bool CanCreateStyle()
        {
            return canInteract()
                && _editorView is not null
                && _editorView.TryGetService<IStyleManagerService>(out _);
        }

        async Task OpenCreateStyleDialogAsync()
        {
            if (!canInteract() || _editorView is null)
            {
                return;
            }

            if (!_editorView.TryGetService<IStyleManagerService>(out var styleService))
            {
                return;
            }

            _editorView.TryGetService<IFontService>(out var fontService);
            var dialog = new StyleEditorDialog(
                new StyleEditorState(EditorStyleType.Paragraph, string.Empty, null, null, null, false, false, null, null, null, null, null),
                styleService,
                fontService);
            var result = await ShowDialogAsync<StyleEditorResult?>(dialog);
            if (result is not StyleEditorResult created)
            {
                return;
            }

            var options = new EditorStyleCreateOptions(
                created.Type,
                created.Name,
                created.BasedOnId,
                created.NextStyleId,
                created.LinkedStyleId,
                created.QuickStyle,
                created.AutoRedefine,
                created.RunProperties,
                created.ParagraphProperties,
                created.TableProperties,
                created.TableCellProperties,
                created.StyleId);

            if (styleService.CreateStyle(options))
            {
                RefreshStyleGalleryItems();
                _ribbon?.RefreshState();
            }
        }

        RibbonMenu BuildStylesPopupMenu()
        {
            return new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "styles-popup-create",
                    "Create a Style",
                    CreateAsyncCommand(OpenCreateStyleDialogAsync, CanCreateStyle),
                    iconKey: "RibbonIcon.New"),
                new RibbonMenuItem(
                    "styles-popup-clear",
                    "Clear Formatting",
                    CreateEditorCommand(EditorHomeCommandIds.Font.ClearFormatting),
                    iconKey: "RibbonIcon.ClearFormatting"),
                new RibbonMenuItem(
                    "styles-popup-apply",
                    "Apply Styles...",
                    CreateEditorCommand(EditorHomeCommandIds.Styles.OpenPane),
                    iconKey: "RibbonIcon.Styles")
            });
        }

        RibbonGroup BuildStylesGroup()
        {
            var stylesGallery = new RibbonGallery(
                "styles-gallery",
                "Styles",
                _styleGalleryItems,
                selectedItemEvaluator: ResolveStyleSelection,
                selectionHandler: item => ExecuteEditorCommandAsync(EditorHomeCommandIds.Styles.Apply, item?.Id),
                showDropDown: true,
                keyTip: "SG",
                iconKey: "RibbonIcon.Styles",
                size: RibbonControlSize.Large,
                popupColumns: 5,
                popupMinWidth: 560,
                popupMaxHeight: 320,
                popupItemMinWidth: 110,
                popupMenu: BuildStylesPopupMenu());

            var stylesLauncher = new RibbonGroupLauncher(
                "styles-launcher",
                "Styles Pane",
                CreateEditorCommand(EditorHomeCommandIds.Styles.OpenPane),
                iconKey: "RibbonIcon.Launcher");

            return new RibbonGroup(
                "styles",
                "Styles",
                new IRibbonControl[]
                {
                    stylesGallery
                },
                keyTip: "ST",
                launcher: stylesLauncher);
        }

        RibbonGroup BuildEditingGroup()
        {
            var findButton = new RibbonButton(
                "edit-find",
                "Find",
                CreateAsyncCommand(() => ShowFindReplaceDialogAsync(false), CanUseFindReplace),
                keyTip: "FD",
                iconKey: "RibbonIcon.Find",
                size: RibbonControlSize.Small,
                compactLabel: "Find");

            var replaceButton = new RibbonButton(
                "edit-replace",
                "Replace",
                CreateAsyncCommand(() => ShowFindReplaceDialogAsync(true), CanUseFindReplace),
                keyTip: "RP",
                iconKey: "RibbonIcon.Replace",
                size: RibbonControlSize.Small,
                compactLabel: "Repl");

            var selectMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "edit-select-all",
                    "Select All",
                    CreateEditorCommand(EditorHomeCommandIds.Editing.SelectAll)),
                new RibbonMenuItem(
                    "edit-select-objects",
                    "Select Objects",
                    CreateEditorCommand(EditorHomeCommandIds.Editing.SelectObjects)),
                new RibbonMenuItem(
                    "edit-select-similar",
                    "Select Text with Similar Formatting",
                    CreateEditorCommand(EditorHomeCommandIds.Editing.SelectSimilarFormatting))
            });

            var selectSplit = new RibbonDropdownButton(
                "edit-select",
                "Select",
                selectMenu,
                keyTip: "SL",
                iconKey: "RibbonIcon.Select",
                size: RibbonControlSize.Small,
                compactLabel: "Select",
                labelMode: RibbonLabelMode.ForceVisible);

            return new RibbonGroup(
                "editing",
                "Editing",
                new IRibbonControl[]
                {
                    findButton,
                    replaceButton,
                    selectSplit
                },
                keyTip: "ED");
        }

        var clipboardGroup = BuildClipboardGroup();
        var fontGroup = BuildFontGroup();
        var paragraphGroup = BuildParagraphGroup();
        var stylesGroup = BuildStylesGroup();
        var editingGroup = BuildEditingGroup();

        var coverPageMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "cover-page-blank",
                "Blank",
                CreateEditorCommand(EditorInsertCommandIds.Pages.CoverPage, "Blank")),
            new RibbonMenuItem(
                "cover-page-facet",
                "Facet",
                CreateEditorCommand(EditorInsertCommandIds.Pages.CoverPage, "Facet")),
            new RibbonMenuItem(
                "cover-page-ion",
                "Ion",
                CreateEditorCommand(EditorInsertCommandIds.Pages.CoverPage, "Ion"))
        });

        var coverPageButton = new RibbonDropdownButton(
            "insert-cover-page",
            "Cover Page",
            coverPageMenu,
            keyTip: "CP",
            iconKey: "RibbonIcon.CoverPage",
            size: RibbonControlSize.Large);

        var blankPageButton = new RibbonButton(
            "insert-blank-page",
            "Blank Page",
            CreateEditorCommand(EditorInsertCommandIds.Pages.BlankPage),
            keyTip: "BP",
            iconKey: "RibbonIcon.BlankPage",
            size: RibbonControlSize.Large);

        var pageBreakButton = new RibbonButton(
            "insert-page-break",
            "Page Break",
            CreateEditorCommand(EditorInsertCommandIds.Pages.PageBreak),
            keyTip: "PB",
            iconKey: "RibbonIcon.PageBreak",
            size: RibbonControlSize.Small);

        var pagesGroup = new RibbonGroup(
            "insert-pages",
            "Pages",
            new IRibbonControl[]
            {
                coverPageButton,
                blankPageButton,
                pageBreakButton
            },
            keyTip: "PG");

        var tableMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "table-2x2",
                "2 x 2 Table",
                CreateEditorCommand(EditorInsertCommandIds.Tables.InsertTable, new EditorTableInsertRequest(2, 2))),
            new RibbonMenuItem(
                "table-3x3",
                "3 x 3 Table",
                CreateEditorCommand(EditorInsertCommandIds.Tables.InsertTable, new EditorTableInsertRequest(3, 3))),
            new RibbonMenuItem(
                "table-4x4",
                "4 x 4 Table",
                CreateEditorCommand(EditorInsertCommandIds.Tables.InsertTable, new EditorTableInsertRequest(4, 4))),
            new RibbonMenuItem(
                "table-5x5",
                "5 x 5 Table",
                CreateEditorCommand(EditorInsertCommandIds.Tables.InsertTable, new EditorTableInsertRequest(5, 5))),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "table-draw",
                "Draw Table",
                CreateEditorCommand(EditorInsertCommandIds.Tables.InsertTable, new EditorTableInsertRequest(1, 1))),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "table-insert",
                "Insert Table...",
                CreateAsyncCommand(OpenTableInsertDialogAsync, canInteract))
        });

        var quickTableMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "quick-table-list",
                "Simple List",
                CreateAsyncCommand(() => InsertQuickTableAsync(QuickTableTemplate.SimpleList), canInteract),
                iconKey: "RibbonIcon.Table"),
            new RibbonMenuItem(
                "quick-table-matrix",
                "Matrix",
                CreateAsyncCommand(() => InsertQuickTableAsync(QuickTableTemplate.Matrix), canInteract),
                iconKey: "RibbonIcon.Table"),
            new RibbonMenuItem(
                "quick-table-calendar",
                "Calendar (Week)",
                CreateAsyncCommand(() => InsertQuickTableAsync(QuickTableTemplate.WeekCalendar), canInteract),
                iconKey: "RibbonIcon.Table")
        });

        var tableButton = new RibbonSplitButton(
            "insert-table",
            "Table",
            CreateAsyncCommand(OpenTableInsertDialogAsync, canInteract),
            tableMenu,
            keyTip: "TB",
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Large);

        var quickTablesButton = new RibbonDropdownButton(
            "insert-quick-tables",
            "Quick Tables",
            quickTableMenu,
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Small);

        var tablesGroup = new RibbonGroup(
            "insert-tables",
            "Tables",
            new IRibbonControl[]
            {
                tableButton,
                quickTablesButton
            },
            keyTip: "TS");

        var picturesMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "pictures-device",
                "This Device...",
                CreateAsyncCommand(InsertPictureAsync, canInteract),
                iconKey: "RibbonIcon.Pictures"),
            new RibbonMenuItem(
                "pictures-stock",
                "Stock Images",
                CreateEditorCommand(EditorInsertCommandIds.Illustrations.Pictures, "Stock Images"),
                iconKey: "RibbonIcon.Pictures"),
            new RibbonMenuItem(
                "pictures-online",
                "Online Pictures",
                CreateEditorCommand(EditorInsertCommandIds.Illustrations.Pictures, "Online Pictures"),
                iconKey: "RibbonIcon.Pictures")
        });

        var picturesButton = new RibbonSplitButton(
            "insert-pictures",
            "Pictures",
            CreateAsyncCommand(InsertPictureAsync, canInteract),
            picturesMenu,
            keyTip: "PI",
            iconKey: "RibbonIcon.Pictures",
            size: RibbonControlSize.Large);

        var shapesButton = new RibbonButton(
            "insert-shapes",
            "Shapes",
            CreateAsyncCommand(OpenShapePickerDialogAsync, canInteract),
            keyTip: "SH",
            iconKey: "RibbonIcon.Shapes",
            size: RibbonControlSize.Medium);

        var iconsButton = new RibbonButton(
            "insert-icons",
            "Icons",
            CreateAsyncCommand(OpenIconPickerDialogAsync, canInteract),
            keyTip: "IC",
            iconKey: "RibbonIcon.Icons",
            size: RibbonControlSize.Medium);

        var modelsButton = new RibbonButton(
            "insert-models-3d",
            "3D Models",
            CreateAsyncCommand(InsertModel3DAsync, canInteract),
            keyTip: "3D",
            iconKey: "RibbonIcon.Models3D",
            size: RibbonControlSize.Medium);

        var smartArtButton = new RibbonButton(
            "insert-smartart",
            "SmartArt",
            CreateAsyncCommand(OpenSmartArtPickerDialogAsync, canInteract),
            keyTip: "SA",
            iconKey: "RibbonIcon.SmartArt",
            size: RibbonControlSize.Medium);

        var chartMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "chart-column",
                "Column Chart",
                CreateEditorCommand(
                    EditorInsertCommandIds.Illustrations.Chart,
                    new EditorChartInsertRequest(ChartType.Bar, "Column Chart", ChartBarDirection.Column, ChartStacking.None, 3, 5)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuItem(
                "chart-column-stacked",
                "Stacked Column",
                CreateEditorCommand(
                    EditorInsertCommandIds.Illustrations.Chart,
                    new EditorChartInsertRequest(ChartType.Bar, "Stacked Column Chart", ChartBarDirection.Column, ChartStacking.Stacked, 3, 5)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuItem(
                "chart-column-percent",
                "100% Stacked Column",
                CreateEditorCommand(
                    EditorInsertCommandIds.Illustrations.Chart,
                    new EditorChartInsertRequest(ChartType.Bar, "100% Stacked Column Chart", ChartBarDirection.Column, ChartStacking.Percent, 3, 5)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "chart-line",
                "Line Chart",
                CreateEditorCommand(
                    EditorInsertCommandIds.Illustrations.Chart,
                    new EditorChartInsertRequest(ChartType.Line, "Line Chart", null, ChartStacking.None, 2, 6)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuItem(
                "chart-area",
                "Area Chart",
                CreateEditorCommand(
                    EditorInsertCommandIds.Illustrations.Chart,
                    new EditorChartInsertRequest(ChartType.Area, "Area Chart", null, ChartStacking.Stacked, 3, 6)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuItem(
                "chart-bar",
                "Bar Chart",
                CreateEditorCommand(
                    EditorInsertCommandIds.Illustrations.Chart,
                    new EditorChartInsertRequest(ChartType.Bar, "Bar Chart", ChartBarDirection.Bar, ChartStacking.None, 3, 5)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "chart-pie",
                "Pie Chart",
                CreateEditorCommand(EditorInsertCommandIds.Illustrations.Chart, new EditorChartInsertRequest(ChartType.Pie, "Pie Chart", null, null, 1, 5)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuItem(
                "chart-scatter",
                "Scatter Chart",
                CreateEditorCommand(EditorInsertCommandIds.Illustrations.Chart, new EditorChartInsertRequest(ChartType.Scatter, "Scatter Chart", null, null, 2, 6)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuItem(
                "chart-doughnut",
                "Doughnut Chart",
                CreateEditorCommand(EditorInsertCommandIds.Illustrations.Chart, new EditorChartInsertRequest(ChartType.Doughnut, "Doughnut Chart", null, null, 1, 5)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuItem(
                "chart-bubble",
                "Bubble Chart",
                CreateEditorCommand(EditorInsertCommandIds.Illustrations.Chart, new EditorChartInsertRequest(ChartType.Bubble, "Bubble Chart", null, null, 2, 6)),
                iconKey: "RibbonIcon.Chart"),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "chart-more",
                "More Charts...",
                CreateAsyncCommand(OpenChartGalleryDialogAsync, canInteract),
                iconKey: "RibbonIcon.Chart")
        });

        var chartButton = new RibbonSplitButton(
            "insert-chart",
            "Chart",
            CreateAsyncCommand(OpenChartGalleryDialogAsync, canInteract),
            chartMenu,
            keyTip: "CH",
            iconKey: "RibbonIcon.Chart",
            size: RibbonControlSize.Medium);

        var screenshotButton = new RibbonButton(
            "insert-screenshot",
            "Screenshot",
            CreateAsyncCommand(InsertScreenshotAsync, canInteract),
            keyTip: "SS",
            iconKey: "RibbonIcon.Screenshot",
            size: RibbonControlSize.Small);

        var illustrationsGroup = new RibbonGroup(
            "insert-illustrations",
            "Illustrations",
            new IRibbonControl[]
            {
                picturesButton,
                shapesButton,
                iconsButton,
                modelsButton,
                smartArtButton,
                chartButton,
                screenshotButton
            },
            keyTip: "IL");

        var addInsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "insert-addins-get",
                "Get Add-ins",
                CreateEditorCommand(EditorInsertCommandIds.AddIns.GetAddIns),
                iconKey: "RibbonIcon.Settings"),
            new RibbonMenuItem(
                "insert-addins-my",
                "My Add-ins",
                CreateEditorCommand(EditorInsertCommandIds.AddIns.MyAddIns),
                iconKey: "RibbonIcon.User")
        });

        var addInsButton = new RibbonDropdownButton(
            "insert-addins",
            "Add-ins",
            addInsMenu,
            keyTip: "AI",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Medium);

        var addInsGroup = new RibbonGroup(
            "insert-addins-group",
            "Add-ins",
            new IRibbonControl[]
            {
                addInsButton
            },
            keyTip: "AD");

        var onlineVideoButton = new RibbonButton(
            "insert-online-video",
            "Online Video",
            CreateEditorCommand(EditorInsertCommandIds.Media.OnlineVideo),
            keyTip: "OV",
            iconKey: "RibbonIcon.Globe",
            size: RibbonControlSize.Medium);

        var mediaGroup = new RibbonGroup(
            "insert-media",
            "Media",
            new IRibbonControl[]
            {
                onlineVideoButton
            },
            keyTip: "ME");

        var hyperlinkButton = new RibbonButton(
            "insert-hyperlink",
            "Link",
            CreateAsyncCommand(OpenHyperlinkDialogAsync, canInteract),
            keyTip: "LN",
            iconKey: "RibbonIcon.Link",
            size: RibbonControlSize.Medium);

        var bookmarkButton = new RibbonButton(
            "insert-bookmark",
            "Bookmark",
            CreateEditorCommand(EditorInsertCommandIds.Links.Bookmark),
            keyTip: "BM",
            iconKey: "RibbonIcon.Bookmark",
            size: RibbonControlSize.Medium);

        var crossReferenceButton = new RibbonButton(
            "insert-cross-reference",
            "Cross-reference",
            CreateAsyncCommand(OpenCrossReferenceDialogAsync, canInteract),
            keyTip: "CR",
            iconKey: "RibbonIcon.CrossReference",
            size: RibbonControlSize.Medium);

        var linksGroup = new RibbonGroup(
            "insert-links",
            "Links",
            new IRibbonControl[]
            {
                hyperlinkButton,
                bookmarkButton,
                crossReferenceButton
            },
            keyTip: "LK");

        var headerMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "header-blank",
                "Blank",
                CreateEditorCommand(EditorInsertCommandIds.HeaderFooter.Header),
                iconKey: "RibbonIcon.Header"),
            new RibbonMenuItem(
                "header-edit",
                "Edit Header",
                CreateViewCommand(() => BeginHeaderFooterEdit(true)),
                iconKey: "RibbonIcon.Header")
        });

        var headerButton = new RibbonDropdownButton(
            "insert-header",
            "Header",
            headerMenu,
            keyTip: "HD",
            iconKey: "RibbonIcon.Header",
            size: RibbonControlSize.Medium);

        var footerMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "footer-blank",
                "Blank",
                CreateEditorCommand(EditorInsertCommandIds.HeaderFooter.Footer),
                iconKey: "RibbonIcon.Footer"),
            new RibbonMenuItem(
                "footer-edit",
                "Edit Footer",
                CreateViewCommand(() => BeginHeaderFooterEdit(false)),
                iconKey: "RibbonIcon.Footer")
        });

        var footerButton = new RibbonDropdownButton(
            "insert-footer",
            "Footer",
            footerMenu,
            keyTip: "FT",
            iconKey: "RibbonIcon.Footer",
            size: RibbonControlSize.Medium);

        var pageNumberMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "page-number-top",
                "Top of Page",
                CreateEditorCommand(EditorInsertCommandIds.HeaderFooter.PageNumber, new EditorPageNumberInsertRequest(false, false)),
                iconKey: "RibbonIcon.PageNumber"),
            new RibbonMenuItem(
                "page-number-bottom",
                "Bottom of Page",
                CreateEditorCommand(EditorInsertCommandIds.HeaderFooter.PageNumber, new EditorPageNumberInsertRequest(true, false)),
                iconKey: "RibbonIcon.PageNumber"),
            new RibbonMenuItem(
                "page-number-total",
                "Bottom of Page X of Y",
                CreateEditorCommand(EditorInsertCommandIds.HeaderFooter.PageNumber, new EditorPageNumberInsertRequest(true, true)),
                iconKey: "RibbonIcon.PageNumber")
        });

        var pageNumberButton = new RibbonDropdownButton(
            "insert-page-number",
            "Page Number",
            pageNumberMenu,
            keyTip: "PN",
            iconKey: "RibbonIcon.PageNumber",
            size: RibbonControlSize.Medium);

        var closeHeaderFooterButton = new RibbonButton(
            "insert-close-header-footer",
            "Close Header and Footer",
            CreateViewCommandWithCanExecute(() => _editorView?.EndHeaderFooterEdit(), IsHeaderFooterEditing),
            iconKey: "RibbonIcon.Check",
            size: RibbonControlSize.Small);

        var headerFooterGroup = new RibbonGroup(
            "insert-header-footer",
            "Header & Footer",
            new IRibbonControl[]
            {
                headerButton,
                footerButton,
                pageNumberButton,
                closeHeaderFooterButton
            },
            keyTip: "HF");

        var textBoxMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "textbox-simple",
                "Simple Text Box",
                CreateEditorCommand(EditorInsertCommandIds.Text.TextBox, "Simple"),
                iconKey: "RibbonIcon.TextBox"),
            new RibbonMenuItem(
                "textbox-quote",
                "Quote Text Box",
                CreateEditorCommand(EditorInsertCommandIds.Text.TextBox, "Quote"),
                iconKey: "RibbonIcon.TextBox")
        });

        var textBoxButton = new RibbonDropdownButton(
            "insert-textbox",
            "Text Box",
            textBoxMenu,
            keyTip: "TX",
            iconKey: "RibbonIcon.TextBox",
            size: RibbonControlSize.Medium);

        var quickPartsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "quick-parts-autotext",
                "AutoText",
                CreateEditorCommand(EditorInsertCommandIds.Text.QuickParts, "AutoText"),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "quick-parts-field",
                "Field",
                CreateEditorCommand(EditorInsertCommandIds.Text.QuickParts, "Field"),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "quick-parts-doc-prop",
                "Document Property",
                CreateEditorCommand(EditorInsertCommandIds.Text.QuickParts, "DocProperty"),
                iconKey: "RibbonIcon.QuickParts")
        });

        var quickPartsButton = new RibbonDropdownButton(
            "insert-quick-parts",
            "Quick Parts",
            quickPartsMenu,
            keyTip: "QP",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Small);

        var wordArtMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "wordart-simple",
                "Simple Text",
                CreateEditorCommand(EditorInsertCommandIds.Text.WordArt, "Simple"),
                iconKey: "RibbonIcon.WordArt"),
            new RibbonMenuItem(
                "wordart-outline",
                "Outline",
                CreateEditorCommand(EditorInsertCommandIds.Text.WordArt, "Outline"),
                iconKey: "RibbonIcon.WordArt")
        });

        var wordArtButton = new RibbonDropdownButton(
            "insert-wordart",
            "WordArt",
            wordArtMenu,
            keyTip: "WA",
            iconKey: "RibbonIcon.WordArt",
            size: RibbonControlSize.Small);

        var dropCapMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "dropcap-drop",
                "Dropped",
                CreateEditorCommand(EditorInsertCommandIds.Text.DropCap, "drop"),
                iconKey: "RibbonIcon.DropCap"),
            new RibbonMenuItem(
                "dropcap-margin",
                "In Margin",
                CreateEditorCommand(EditorInsertCommandIds.Text.DropCap, "margin"),
                iconKey: "RibbonIcon.DropCap")
        });

        var dropCapButton = new RibbonDropdownButton(
            "insert-dropcap",
            "Drop Cap",
            dropCapMenu,
            keyTip: "DC",
            iconKey: "RibbonIcon.DropCap",
            size: RibbonControlSize.Small);

        var signatureButton = new RibbonButton(
            "insert-signature",
            "Signature Line",
            CreateEditorCommand(EditorInsertCommandIds.Text.SignatureLine),
            keyTip: "SG",
            iconKey: "RibbonIcon.Signature",
            size: RibbonControlSize.Small);

        var dateTimeMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "date-time-short",
                "Short Date",
                CreateEditorCommand(EditorInsertCommandIds.Text.DateTime, "short"),
                iconKey: "RibbonIcon.DateTime"),
            new RibbonMenuItem(
                "date-time-long",
                "Long Date",
                CreateEditorCommand(EditorInsertCommandIds.Text.DateTime, "long"),
                iconKey: "RibbonIcon.DateTime")
        });

        var dateTimeButton = new RibbonDropdownButton(
            "insert-date-time",
            "Date & Time",
            dateTimeMenu,
            keyTip: "DT",
            iconKey: "RibbonIcon.DateTime",
            size: RibbonControlSize.Small);

        var objectMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "object-file",
                "Object from File",
                CreateAsyncCommand(InsertObjectFromFileAsync, canInteract),
                iconKey: "RibbonIcon.Object"),
            new RibbonMenuItem(
                "object-new",
                "New Object",
                CreateEditorCommand(
                    EditorInsertCommandIds.Text.Object,
                    new EditorEmbeddedObjectInsertRequest(Array.Empty<byte>(), "application/octet-stream", "New Object")),
                iconKey: "RibbonIcon.Object")
        });

        var objectButton = new RibbonDropdownButton(
            "insert-object",
            "Object",
            objectMenu,
            keyTip: "OB",
            iconKey: "RibbonIcon.Object",
            size: RibbonControlSize.Small);

        var textInsertGroup = new RibbonGroup(
            "insert-text",
            "Text",
            new IRibbonControl[]
            {
                textBoxButton,
                quickPartsButton,
                wordArtButton,
                dropCapButton,
                signatureButton,
                dateTimeButton,
                objectButton
            },
            keyTip: "TX");

        var equationButton = new RibbonButton(
            "insert-equation",
            "Equation",
            CreateEditorCommand(EditorInsertCommandIds.Symbols.Equation),
            keyTip: "EQ",
            iconKey: "RibbonIcon.Equation",
            size: RibbonControlSize.Medium);

        var symbolMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "symbol-omega",
                "Omega",
                CreateEditorCommand(EditorInsertCommandIds.Symbols.Symbol, "\u03A9"),
                iconKey: "RibbonIcon.Symbol"),
            new RibbonMenuItem(
                "symbol-pi",
                "Pi",
                CreateEditorCommand(EditorInsertCommandIds.Symbols.Symbol, "\u03C0"),
                iconKey: "RibbonIcon.Symbol"),
            new RibbonMenuItem(
                "symbol-sigma",
                "Sigma",
                CreateEditorCommand(EditorInsertCommandIds.Symbols.Symbol, "\u03A3"),
                iconKey: "RibbonIcon.Symbol"),
            new RibbonMenuItem(
                "symbol-degree",
                "Degree",
                CreateEditorCommand(EditorInsertCommandIds.Symbols.Symbol, "\u00B0"),
                iconKey: "RibbonIcon.Symbol")
        });

        var symbolButton = new RibbonDropdownButton(
            "insert-symbol",
            "Symbol",
            symbolMenu,
            keyTip: "SY",
            iconKey: "RibbonIcon.Symbol",
            size: RibbonControlSize.Medium);

        var symbolsGroup = new RibbonGroup(
            "insert-symbols",
            "Symbols",
            new IRibbonControl[]
            {
                equationButton,
                symbolButton
            },
            keyTip: "SB");

        var tocMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "references-toc-auto-1",
                "Automatic Table 1",
                CreateEditorCommand(EditorReferencesCommandIds.TableOfContents.Insert, new EditorTocInsertRequest(3, true, true)),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "references-toc-auto-2",
                "Automatic Table 2",
                CreateEditorCommand(EditorReferencesCommandIds.TableOfContents.Insert, new EditorTocInsertRequest(3, true, true)),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "references-toc-add-text-1",
                "Add Text Level 1",
                CreateEditorCommand(EditorReferencesCommandIds.TableOfContents.AddText, 1),
                iconKey: "RibbonIcon.Styles"),
            new RibbonMenuItem(
                "references-toc-add-text-2",
                "Add Text Level 2",
                CreateEditorCommand(EditorReferencesCommandIds.TableOfContents.AddText, 2),
                iconKey: "RibbonIcon.Styles"),
            new RibbonMenuItem(
                "references-toc-add-text-3",
                "Add Text Level 3",
                CreateEditorCommand(EditorReferencesCommandIds.TableOfContents.AddText, 3),
                iconKey: "RibbonIcon.Styles"),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "references-toc-update",
                "Update Table",
                CreateEditorCommand(EditorReferencesCommandIds.TableOfContents.Update),
                iconKey: "RibbonIcon.QuickParts")
        });

        var tocButton = new RibbonDropdownButton(
            "references-toc",
            "Table of Contents",
            tocMenu,
            keyTip: "TO",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Large);

        var tocGroup = new RibbonGroup(
            "references-toc-group",
            "Table of Contents",
            new IRibbonControl[]
            {
                tocButton
            },
            keyTip: "TC");

        var footnoteButton = new RibbonButton(
            "references-footnote",
            "Insert Footnote",
            CreateEditorCommand(EditorReferencesCommandIds.Notes.InsertFootnote),
            keyTip: "FN",
            iconKey: "RibbonIcon.PageNumber",
            size: RibbonControlSize.Medium);

        var endnoteButton = new RibbonButton(
            "references-endnote",
            "Insert Endnote",
            CreateEditorCommand(EditorReferencesCommandIds.Notes.InsertEndnote),
            keyTip: "EN",
            iconKey: "RibbonIcon.PageNumber",
            size: RibbonControlSize.Medium);

        var nextFootnoteButton = new RibbonButton(
            "references-next-footnote",
            "Next Footnote",
            CreateEditorCommand(EditorReferencesCommandIds.Notes.NextFootnote),
            keyTip: "NF",
            iconKey: "RibbonIcon.Search",
            size: RibbonControlSize.Medium);

        var showNotesButton = new RibbonButton(
            "references-show-notes",
            "Show Notes",
            CreateAsyncCommand(OpenNotesPaneAsync, canInteract),
            keyTip: "SN",
            iconKey: "RibbonIcon.Info",
            size: RibbonControlSize.Medium);

        var footnotesGroup = new RibbonGroup(
            "references-footnotes",
            "Footnotes",
            new IRibbonControl[]
            {
                footnoteButton,
                endnoteButton,
                nextFootnoteButton,
                showNotesButton
            },
            keyTip: "FN");

        var citationStyleMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "references-citation-style-apa",
                "APA",
                CreateEditorCommand(EditorReferencesCommandIds.Citations.Style, "APA"),
                iconKey: "RibbonIcon.Styles"),
            new RibbonMenuItem(
                "references-citation-style-mla",
                "MLA",
                CreateEditorCommand(EditorReferencesCommandIds.Citations.Style, "MLA"),
                iconKey: "RibbonIcon.Styles"),
            new RibbonMenuItem(
                "references-citation-style-chicago",
                "Chicago",
                CreateEditorCommand(EditorReferencesCommandIds.Citations.Style, "Chicago"),
                iconKey: "RibbonIcon.Styles")
        });

        var insertCitationButton = new RibbonButton(
            "references-insert-citation",
            "Insert Citation",
            CreateEditorCommand(EditorReferencesCommandIds.Citations.InsertCitation),
            keyTip: "IC",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var bibliographyButton = new RibbonButton(
            "references-bibliography",
            "Bibliography",
            CreateEditorCommand(EditorReferencesCommandIds.Citations.Bibliography),
            keyTip: "BI",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var manageSourcesButton = new RibbonButton(
            "references-manage-sources",
            "Manage Sources",
            CreateEditorCommand(EditorReferencesCommandIds.Citations.ManageSources),
            keyTip: "MS",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Medium);

        var citationStyleButton = new RibbonDropdownButton(
            "references-citation-style",
            "Style",
            citationStyleMenu,
            keyTip: "ST",
            iconKey: "RibbonIcon.Styles",
            size: RibbonControlSize.Medium);

        var citationsGroup = new RibbonGroup(
            "references-citations",
            "Citations & Bibliography",
            new IRibbonControl[]
            {
                insertCitationButton,
                bibliographyButton,
                citationStyleButton,
                manageSourcesButton
            },
            keyTip: "CB");

        var captionMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "references-caption-figure",
                "Figure",
                CreateEditorCommand(EditorReferencesCommandIds.Captions.InsertCaption, new EditorCaptionInsertRequest("Figure", null)),
                iconKey: "RibbonIcon.Pictures"),
            new RibbonMenuItem(
                "references-caption-table",
                "Table",
                CreateEditorCommand(EditorReferencesCommandIds.Captions.InsertCaption, new EditorCaptionInsertRequest("Table", null)),
                iconKey: "RibbonIcon.Table"),
            new RibbonMenuItem(
                "references-caption-equation",
                "Equation",
                CreateEditorCommand(EditorReferencesCommandIds.Captions.InsertCaption, new EditorCaptionInsertRequest("Equation", null)),
                iconKey: "RibbonIcon.Equation")
        });

        var captionButton = new RibbonDropdownButton(
            "references-caption",
            "Insert Caption",
            captionMenu,
            keyTip: "CA",
            iconKey: "RibbonIcon.Pictures",
            size: RibbonControlSize.Medium);

        var tableOfFiguresButton = new RibbonButton(
            "references-table-of-figures",
            "Insert Table of Figures",
            CreateEditorCommand(EditorReferencesCommandIds.TableOfFigures.Insert),
            keyTip: "TF",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var updateTableOfFiguresButton = new RibbonButton(
            "references-update-table-of-figures",
            "Update Table",
            CreateEditorCommand(EditorReferencesCommandIds.TableOfFigures.Update),
            keyTip: "UT",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var captionsGroup = new RibbonGroup(
            "references-captions",
            "Captions",
            new IRibbonControl[]
            {
                captionButton,
                tableOfFiguresButton,
                updateTableOfFiguresButton
            },
            keyTip: "CP");

        var crossReferenceMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "references-cross-dialog",
                "Insert Cross-reference...",
                CreateAsyncCommand(OpenCrossReferenceDialogAsync, canInteract),
                iconKey: "RibbonIcon.CrossReference"),
            new RibbonMenuItem(
                "references-cross-page",
                "Cross-reference with Page Number",
                CreateEditorCommand(EditorInsertCommandIds.Links.CrossReference, new EditorCrossReferenceInsertRequest(null, true, true)),
                iconKey: "RibbonIcon.CrossReference"),
            new RibbonMenuItem(
                "references-cross-no-link",
                "Cross-reference without Hyperlink",
                CreateEditorCommand(EditorInsertCommandIds.Links.CrossReference, new EditorCrossReferenceInsertRequest(null, false, false)),
                iconKey: "RibbonIcon.CrossReference")
        });

        var referencesCrossReferenceButton = new RibbonDropdownButton(
            "references-cross-reference",
            "Cross-reference",
            crossReferenceMenu,
            keyTip: "CR",
            iconKey: "RibbonIcon.CrossReference",
            size: RibbonControlSize.Medium);

        var referencesLinksGroup = new RibbonGroup(
            "references-links",
            "Cross-reference",
            new IRibbonControl[]
            {
                referencesCrossReferenceButton
            },
            keyTip: "CR");

        var updateFieldsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "references-fields-update-current",
                "Update Field",
                CreateEditorCommand(EditorReferencesCommandIds.Fields.UpdateCurrent),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "references-fields-update-all",
                "Update All",
                CreateEditorCommand(EditorReferencesCommandIds.Fields.UpdateAll),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "references-fields-update-page",
                "Update Page Numbers",
                CreateEditorCommand(EditorReferencesCommandIds.Fields.UpdatePageNumbers),
                iconKey: "RibbonIcon.PageNumber")
        });

        var updateFieldsButton = new RibbonDropdownButton(
            "references-fields-update",
            "Update Fields",
            updateFieldsMenu,
            keyTip: "UF",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var lockFieldButton = new RibbonButton(
            "references-fields-lock",
            "Lock Field",
            CreateEditorCommand(EditorReferencesCommandIds.Fields.Lock),
            keyTip: "LK",
            iconKey: "RibbonIcon.Lock",
            size: RibbonControlSize.Medium);

        var unlockFieldButton = new RibbonButton(
            "references-fields-unlock",
            "Unlock Field",
            CreateEditorCommand(EditorReferencesCommandIds.Fields.Unlock),
            keyTip: "UL",
            iconKey: "RibbonIcon.Lock",
            size: RibbonControlSize.Medium);

        var fieldsGroup = new RibbonGroup(
            "references-fields",
            "Fields",
            new IRibbonControl[]
            {
                updateFieldsButton,
                lockFieldButton,
                unlockFieldButton
            },
            keyTip: "FD");

        var markIndexEntryButton = new RibbonButton(
            "references-mark-index-entry",
            "Mark Entry",
            CreateEditorCommand(EditorReferencesCommandIds.Index.MarkEntry),
            keyTip: "ME",
            iconKey: "RibbonIcon.Bookmark",
            size: RibbonControlSize.Medium);

        var insertIndexButton = new RibbonButton(
            "references-insert-index",
            "Insert Index",
            CreateEditorCommand(EditorReferencesCommandIds.Index.InsertIndex),
            keyTip: "II",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var indexGroup = new RibbonGroup(
            "references-index",
            "Index",
            new IRibbonControl[]
            {
                markIndexEntryButton,
                insertIndexButton
            },
            keyTip: "IX");

        var markCitationButton = new RibbonButton(
            "references-mark-citation",
            "Mark Citation",
            CreateEditorCommand(EditorReferencesCommandIds.TableOfAuthorities.MarkCitation),
            keyTip: "MC",
            iconKey: "RibbonIcon.Bookmark",
            size: RibbonControlSize.Medium);

        var insertTableAuthoritiesButton = new RibbonButton(
            "references-insert-table-authorities",
            "Insert Table of Authorities",
            CreateEditorCommand(EditorReferencesCommandIds.TableOfAuthorities.InsertTable),
            keyTip: "TA",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var tableAuthoritiesGroup = new RibbonGroup(
            "references-table-authorities",
            "Table of Authorities",
            new IRibbonControl[]
            {
                markCitationButton,
                insertTableAuthoritiesButton
            },
            keyTip: "TA");

        RibbonGroup BuildPageSetupGroup()
        {
            var marginsMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "layout-margins-normal",
                    "Normal",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Margins,
                        new EditorPageMarginsRequest(
                            InchesToDips(1f),
                            InchesToDips(1f),
                            InchesToDips(1f),
                            InchesToDips(1f))),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-margins-narrow",
                    "Narrow",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Margins,
                        new EditorPageMarginsRequest(
                            InchesToDips(0.5f),
                            InchesToDips(0.5f),
                            InchesToDips(0.5f),
                            InchesToDips(0.5f))),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-margins-moderate",
                    "Moderate",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Margins,
                        new EditorPageMarginsRequest(
                            InchesToDips(0.75f),
                            InchesToDips(1f),
                            InchesToDips(0.75f),
                            InchesToDips(1f))),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-margins-wide",
                    "Wide",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Margins,
                        new EditorPageMarginsRequest(
                            InchesToDips(2f),
                            InchesToDips(1f),
                            InchesToDips(2f),
                            InchesToDips(1f))),
                    iconKey: "RibbonIcon.Layout")
            });

            var marginsButton = new RibbonDropdownButton(
                "layout-margins",
                "Margins",
                marginsMenu,
                keyTip: "MA",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            var orientationMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "layout-orientation-portrait",
                    "Portrait",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Orientation,
                        new EditorPageOrientationRequest(PageOrientation.Portrait)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-orientation-landscape",
                    "Landscape",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Orientation,
                        new EditorPageOrientationRequest(PageOrientation.Landscape)),
                    iconKey: "RibbonIcon.Layout")
            });

            var orientationButton = new RibbonDropdownButton(
                "layout-orientation",
                "Orientation",
                orientationMenu,
                keyTip: "OR",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            var sizeMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "layout-size-letter",
                    "Letter",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Size,
                        new EditorPageSizeRequest(
                            InchesToDips(8.5f),
                            InchesToDips(11f),
                            PageOrientation.Portrait)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-size-a4",
                    "A4",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Size,
                        new EditorPageSizeRequest(
                            InchesToDips(8.27f),
                            InchesToDips(11.69f),
                            PageOrientation.Portrait)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-size-a5",
                    "A5",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Size,
                        new EditorPageSizeRequest(
                            InchesToDips(5.83f),
                            InchesToDips(8.27f),
                            PageOrientation.Portrait)),
                    iconKey: "RibbonIcon.Layout")
            });

            var sizeButton = new RibbonDropdownButton(
                "layout-size",
                "Size",
                sizeMenu,
                keyTip: "SZ",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            var columnsMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "layout-columns-one",
                    "One",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Columns,
                        new EditorColumnLayoutRequest(1)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-columns-two",
                    "Two",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Columns,
                        new EditorColumnLayoutRequest(2)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-columns-three",
                    "Three",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Columns,
                        new EditorColumnLayoutRequest(3)),
                    iconKey: "RibbonIcon.Layout")
            });

            var columnsButton = new RibbonDropdownButton(
                "layout-columns",
                "Columns",
                columnsMenu,
                keyTip: "CL",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            var breaksMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "layout-break-page",
                    "Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Breaks,
                        new EditorBreakRequest(EditorBreakKind.Page)),
                    iconKey: "RibbonIcon.PageBreak"),
                new RibbonMenuItem(
                    "layout-break-column",
                    "Column",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Breaks,
                        new EditorBreakRequest(EditorBreakKind.Column)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "layout-break-section-next",
                    "Next Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Breaks,
                        new EditorBreakRequest(EditorBreakKind.SectionNextPage)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-break-section-continuous",
                    "Continuous",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Breaks,
                        new EditorBreakRequest(EditorBreakKind.SectionContinuous)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-break-section-even",
                    "Even Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Breaks,
                        new EditorBreakRequest(EditorBreakKind.SectionEvenPage)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-break-section-odd",
                    "Odd Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Breaks,
                        new EditorBreakRequest(EditorBreakKind.SectionOddPage)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "layout-break-section-next-column",
                    "Next Column",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.PageSetup.Breaks,
                        new EditorBreakRequest(EditorBreakKind.SectionNextColumn)),
                    iconKey: "RibbonIcon.Layout")
            });

            var breaksButton = new RibbonDropdownButton(
                "layout-breaks",
                "Breaks",
                breaksMenu,
                keyTip: "BR",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            return new RibbonGroup(
                "layout-page-setup",
                "Page Setup",
                new IRibbonControl[]
                {
                    marginsButton,
                    orientationButton,
                    sizeButton,
                    columnsButton,
                    breaksButton
                },
                keyTip: "PS");
        }

        RibbonGroup BuildLayoutParagraphGroup()
        {
            double? ResolveParagraphPoints(Func<EditorParagraphSnapshot, EditorValue<float>> selector)
            {
                if (!TryGetSnapshot(out var snapshot))
                {
                    return null;
                }

                var value = selector(snapshot.Paragraph);
                if (!value.HasValue || value.IsMixed)
                {
                    return null;
                }

                return DipsToPoints(value.Value);
            }

            ValueTask ApplyParagraphOptionsAsync(
                double? indentLeftPoints = null,
                double? indentRightPoints = null,
                double? spacingBeforePoints = null,
                double? spacingAfterPoints = null)
            {
                if (!indentLeftPoints.HasValue
                    && !indentRightPoints.HasValue
                    && !spacingBeforePoints.HasValue
                    && !spacingAfterPoints.HasValue)
                {
                    return ValueTask.CompletedTask;
                }

                var options = new EditorParagraphDialogOptions(
                    Alignment: null,
                    IndentLeft: indentLeftPoints.HasValue ? PointsToDips(indentLeftPoints.Value) : null,
                    IndentRight: indentRightPoints.HasValue ? PointsToDips(indentRightPoints.Value) : null,
                    FirstLineIndent: null,
                    SpacingBefore: spacingBeforePoints.HasValue
                        ? PointsToDips(Math.Max(0d, spacingBeforePoints.Value))
                        : null,
                    SpacingAfter: spacingAfterPoints.HasValue
                        ? PointsToDips(Math.Max(0d, spacingAfterPoints.Value))
                        : null,
                    LineSpacing: null,
                    LineSpacingRule: null,
                    ContextualSpacing: null,
                    KeepWithNext: null,
                    KeepLinesTogether: null,
                    WidowControl: null,
                    PageBreakBefore: null,
                    SuppressLineNumbers: null,
                    Bidi: null,
                    TextDirection: null);

                return ExecuteEditorCommandAsync(EditorHomeCommandIds.Paragraph.DialogApply, options);
            }

            var indentLeftSpinner = new RibbonSpinner(
                "layout-indent-left",
                "Indent Left",
                step: 1d,
                valueEvaluator: () => ResolveParagraphPoints(snapshot => snapshot.IndentLeft),
                valueChangedHandler: value => ApplyParagraphOptionsAsync(indentLeftPoints: value),
                keyTip: "IL",
                iconKey: "RibbonIcon.IndentDecrease",
                size: RibbonControlSize.Medium);

            var indentRightSpinner = new RibbonSpinner(
                "layout-indent-right",
                "Indent Right",
                step: 1d,
                valueEvaluator: () => ResolveParagraphPoints(snapshot => snapshot.IndentRight),
                valueChangedHandler: value => ApplyParagraphOptionsAsync(indentRightPoints: value),
                keyTip: "IR",
                iconKey: "RibbonIcon.IndentIncrease",
                size: RibbonControlSize.Medium);

            var spacingBeforeSpinner = new RibbonSpinner(
                "layout-spacing-before",
                "Spacing Before",
                step: 1d,
                minimum: 0d,
                valueEvaluator: () => ResolveParagraphPoints(snapshot => snapshot.SpacingBefore),
                valueChangedHandler: value => ApplyParagraphOptionsAsync(spacingBeforePoints: value),
                keyTip: "SB",
                iconKey: "RibbonIcon.LineSpacing",
                size: RibbonControlSize.Medium);

            var spacingAfterSpinner = new RibbonSpinner(
                "layout-spacing-after",
                "Spacing After",
                step: 1d,
                minimum: 0d,
                valueEvaluator: () => ResolveParagraphPoints(snapshot => snapshot.SpacingAfter),
                valueChangedHandler: value => ApplyParagraphOptionsAsync(spacingAfterPoints: value),
                keyTip: "SA",
                iconKey: "RibbonIcon.LineSpacing",
                size: RibbonControlSize.Medium);

            var layoutParagraphLauncher = new RibbonGroupLauncher(
                "layout-paragraph-launcher",
                "Paragraph",
                CreateAsyncCommand(
                    OpenParagraphDialogAsync,
                    () => CanExecuteEditorCommand(EditorHomeCommandIds.Paragraph.DialogApply)),
                iconKey: "RibbonIcon.Launcher");

            return new RibbonGroup(
                "layout-paragraph",
                "Paragraph",
                new IRibbonControl[]
                {
                    indentLeftSpinner,
                    indentRightSpinner,
                    spacingBeforeSpinner,
                    spacingAfterSpinner
                },
                keyTip: "PG",
                launcher: layoutParagraphLauncher);
        }

        RibbonGroup BuildArrangeGroup()
        {
            var positionMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "arrange-position-top-left",
                    "Top Left",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.TopLeft)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-position-top-center",
                    "Top Center",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.TopCenter)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-position-top-right",
                    "Top Right",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.TopRight)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-position-middle-left",
                    "Middle Left",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.MiddleLeft)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-position-middle-center",
                    "Center",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.MiddleCenter)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-position-middle-right",
                    "Middle Right",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.MiddleRight)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-position-bottom-left",
                    "Bottom Left",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.BottomLeft)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-position-bottom-center",
                    "Bottom Center",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.BottomCenter)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-position-bottom-right",
                    "Bottom Right",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Position,
                        new EditorFloatingPositionRequest(EditorFloatingPositionKind.BottomRight)),
                    iconKey: "RibbonIcon.Layout")
            });

            var positionButton = new RibbonDropdownButton(
                "arrange-position",
                "Position",
                positionMenu,
                keyTip: "PO",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            var wrapMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "arrange-wrap-square",
                    "Square",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapText,
                        new EditorFloatingWrapRequest(EditorFloatingWrapKind.Square)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-wrap-tight",
                    "Tight",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapText,
                        new EditorFloatingWrapRequest(EditorFloatingWrapKind.Tight)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-wrap-through",
                    "Through",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapText,
                        new EditorFloatingWrapRequest(EditorFloatingWrapKind.Through)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-wrap-top-bottom",
                    "Top and Bottom",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapText,
                        new EditorFloatingWrapRequest(EditorFloatingWrapKind.TopBottom)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-wrap-both-sides",
                    "Both Sides",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapSide,
                        new EditorFloatingWrapSideRequest(FloatingWrapSide.Both)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-wrap-left-only",
                    "Left Only",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapSide,
                        new EditorFloatingWrapSideRequest(FloatingWrapSide.Left)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-wrap-right-only",
                    "Right Only",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapSide,
                        new EditorFloatingWrapSideRequest(FloatingWrapSide.Right)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-wrap-largest",
                    "Largest",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapSide,
                        new EditorFloatingWrapSideRequest(FloatingWrapSide.Largest)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-wrap-behind",
                    "Behind Text",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapText,
                        new EditorFloatingWrapRequest(EditorFloatingWrapKind.BehindText)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-wrap-front",
                    "In Front of Text",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.WrapText,
                        new EditorFloatingWrapRequest(EditorFloatingWrapKind.InFrontOfText)),
                    iconKey: "RibbonIcon.Layout")
            });

            var wrapButton = new RibbonDropdownButton(
                "arrange-wrap",
                "Wrap Text",
                wrapMenu,
                keyTip: "WR",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            var bringForwardMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "arrange-bring-forward",
                    "Bring Forward",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Order,
                        new EditorFloatingOrderRequest(EditorFloatingOrderKind.BringForward)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-bring-front",
                    "Bring to Front",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Order,
                        new EditorFloatingOrderRequest(EditorFloatingOrderKind.BringToFront)),
                    iconKey: "RibbonIcon.Layout")
            });

            var bringForwardButton = new RibbonSplitButton(
                "arrange-bring-forward-split",
                "Bring Forward",
                CreateEditorCommand(
                    EditorLayoutCommandIds.Arrange.Order,
                    new EditorFloatingOrderRequest(EditorFloatingOrderKind.BringForward)),
                bringForwardMenu,
                keyTip: "BF",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Small);

            var sendBackwardMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "arrange-send-backward",
                    "Send Backward",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Order,
                        new EditorFloatingOrderRequest(EditorFloatingOrderKind.SendBackward)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-send-back",
                    "Send to Back",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Order,
                        new EditorFloatingOrderRequest(EditorFloatingOrderKind.SendToBack)),
                    iconKey: "RibbonIcon.Layout")
            });

            var sendBackwardButton = new RibbonSplitButton(
                "arrange-send-backward-split",
                "Send Backward",
                CreateEditorCommand(
                    EditorLayoutCommandIds.Arrange.Order,
                    new EditorFloatingOrderRequest(EditorFloatingOrderKind.SendBackward)),
                sendBackwardMenu,
                keyTip: "SBK",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Small);

            var alignMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "arrange-align-left",
                    "Align Left",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(EditorFloatingAlignKind.Left)),
                    iconKey: "RibbonIcon.AlignLeft"),
                new RibbonMenuItem(
                    "arrange-align-center",
                    "Align Center",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(EditorFloatingAlignKind.Center)),
                    iconKey: "RibbonIcon.AlignCenter"),
                new RibbonMenuItem(
                    "arrange-align-right",
                    "Align Right",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(EditorFloatingAlignKind.Right)),
                    iconKey: "RibbonIcon.AlignRight"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-align-top",
                    "Align Top",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(EditorFloatingAlignKind.Top)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-align-middle",
                    "Align Middle",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(EditorFloatingAlignKind.Middle)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-align-bottom",
                    "Align Bottom",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(EditorFloatingAlignKind.Bottom)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-align-left-page",
                    "Align Left to Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Left,
                            EditorFloatingAlignTarget.Page)),
                    iconKey: "RibbonIcon.AlignLeft"),
                new RibbonMenuItem(
                    "arrange-align-center-page",
                    "Align Center to Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Center,
                            EditorFloatingAlignTarget.Page)),
                    iconKey: "RibbonIcon.AlignCenter"),
                new RibbonMenuItem(
                    "arrange-align-right-page",
                    "Align Right to Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Right,
                            EditorFloatingAlignTarget.Page)),
                    iconKey: "RibbonIcon.AlignRight"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-align-top-page",
                    "Align Top to Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Top,
                            EditorFloatingAlignTarget.Page)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-align-middle-page",
                    "Align Middle to Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Middle,
                            EditorFloatingAlignTarget.Page)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-align-bottom-page",
                    "Align Bottom to Page",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Bottom,
                            EditorFloatingAlignTarget.Page)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-align-left-selection",
                    "Align Left to Selected Objects",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Left,
                            EditorFloatingAlignTarget.SelectedObjects)),
                    iconKey: "RibbonIcon.AlignLeft"),
                new RibbonMenuItem(
                    "arrange-align-center-selection",
                    "Align Center to Selected Objects",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Center,
                            EditorFloatingAlignTarget.SelectedObjects)),
                    iconKey: "RibbonIcon.AlignCenter"),
                new RibbonMenuItem(
                    "arrange-align-right-selection",
                    "Align Right to Selected Objects",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Right,
                            EditorFloatingAlignTarget.SelectedObjects)),
                    iconKey: "RibbonIcon.AlignRight"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-align-top-selection",
                    "Align Top to Selected Objects",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Top,
                            EditorFloatingAlignTarget.SelectedObjects)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-align-middle-selection",
                    "Align Middle to Selected Objects",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Middle,
                            EditorFloatingAlignTarget.SelectedObjects)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-align-bottom-selection",
                    "Align Bottom to Selected Objects",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Align,
                        new EditorFloatingAlignRequest(
                            EditorFloatingAlignKind.Bottom,
                            EditorFloatingAlignTarget.SelectedObjects)),
                    iconKey: "RibbonIcon.Layout")
            });

            var alignButton = new RibbonDropdownButton(
                "arrange-align",
                "Align",
                alignMenu,
                keyTip: "AL",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            var rotateMenu = new RibbonMenu(new IRibbonMenuEntry[]
            {
                new RibbonMenuItem(
                    "arrange-rotate-right",
                    "Rotate Right 90°",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Rotate,
                        new EditorFloatingRotateRequest(EditorFloatingRotateKind.RotateRight90)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-rotate-left",
                    "Rotate Left 90°",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Rotate,
                        new EditorFloatingRotateRequest(EditorFloatingRotateKind.RotateLeft90)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuSeparator(),
                new RibbonMenuItem(
                    "arrange-flip-horizontal",
                    "Flip Horizontal",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Rotate,
                        new EditorFloatingRotateRequest(EditorFloatingRotateKind.FlipHorizontal)),
                    iconKey: "RibbonIcon.Layout"),
                new RibbonMenuItem(
                    "arrange-flip-vertical",
                    "Flip Vertical",
                    CreateEditorCommand(
                        EditorLayoutCommandIds.Arrange.Rotate,
                        new EditorFloatingRotateRequest(EditorFloatingRotateKind.FlipVertical)),
                    iconKey: "RibbonIcon.Layout")
            });

            var rotateButton = new RibbonDropdownButton(
                "arrange-rotate",
                "Rotate",
                rotateMenu,
                keyTip: "RT",
                iconKey: "RibbonIcon.Layout",
                size: RibbonControlSize.Medium);

            return new RibbonGroup(
                "layout-arrange",
                "Arrange",
                new IRibbonControl[]
                {
                    positionButton,
                    wrapButton,
                    bringForwardButton,
                    sendBackwardButton,
                    alignButton,
                    rotateButton
                },
                keyTip: "AR");
        }

        var pageSetupGroup = BuildPageSetupGroup();
        var layoutParagraphGroup = BuildLayoutParagraphGroup();
        var arrangeGroup = BuildArrangeGroup();

        var themesMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-theme-office",
                "Office",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Theme, "Office"),
                iconKey: "RibbonIcon.Theme"),
            new RibbonMenuItem(
                "design-theme-facet",
                "Facet",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Theme, "Facet"),
                iconKey: "RibbonIcon.Theme"),
            new RibbonMenuItem(
                "design-theme-ion",
                "Ion",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Theme, "Ion"),
                iconKey: "RibbonIcon.Theme")
        });

        var themesButton = new RibbonDropdownButton(
            "design-themes",
            "Themes",
            themesMenu,
            keyTip: "TH",
            iconKey: "RibbonIcon.Theme",
            size: RibbonControlSize.Large);

        var themeColorsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-theme-colors-office",
                "Office",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Colors, "Office"),
                iconKey: "RibbonIcon.FontColor"),
            new RibbonMenuItem(
                "design-theme-colors-blue",
                "Blue",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Colors, "Blue"),
                iconKey: "RibbonIcon.FontColor"),
            new RibbonMenuItem(
                "design-theme-colors-green",
                "Green",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Colors, "Green"),
                iconKey: "RibbonIcon.FontColor")
        });

        var themeColorsButton = new RibbonDropdownButton(
            "design-theme-colors",
            "Colors",
            themeColorsMenu,
            keyTip: "TC",
            iconKey: "RibbonIcon.FontColor",
            size: RibbonControlSize.Medium);

        var themeFontsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-theme-fonts-aptos",
                "Aptos",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Fonts, "Aptos"),
                iconKey: "RibbonIcon.FontFamily"),
            new RibbonMenuItem(
                "design-theme-fonts-calibri",
                "Calibri",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Fonts, "Calibri"),
                iconKey: "RibbonIcon.FontFamily"),
            new RibbonMenuItem(
                "design-theme-fonts-cambria",
                "Cambria",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Fonts, "Cambria"),
                iconKey: "RibbonIcon.FontFamily")
        });

        var themeFontsButton = new RibbonDropdownButton(
            "design-theme-fonts",
            "Fonts",
            themeFontsMenu,
            keyTip: "TF",
            iconKey: "RibbonIcon.FontFamily",
            size: RibbonControlSize.Medium);

        var themeEffectsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-theme-effects-subtle",
                "Subtle",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Effects, "Subtle"),
                iconKey: "RibbonIcon.TextEffects"),
            new RibbonMenuItem(
                "design-theme-effects-moderate",
                "Moderate",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Effects, "Moderate"),
                iconKey: "RibbonIcon.TextEffects"),
            new RibbonMenuItem(
                "design-theme-effects-intense",
                "Intense",
                CreateEditorCommand(EditorDesignCommandIds.Themes.Effects, "Intense"),
                iconKey: "RibbonIcon.TextEffects")
        });

        var themeEffectsButton = new RibbonDropdownButton(
            "design-theme-effects",
            "Effects",
            themeEffectsMenu,
            keyTip: "TE",
            iconKey: "RibbonIcon.TextEffects",
            size: RibbonControlSize.Medium);

        var themesGroup = new RibbonGroup(
            "design-themes-group",
            "Themes",
            new IRibbonControl[]
            {
                themesButton,
                themeColorsButton,
                themeFontsButton,
                themeEffectsButton
            },
            keyTip: "TH");

        var styleSetMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-style-set-default",
                "Default",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.StyleSet, "Default"),
                iconKey: "RibbonIcon.Styles"),
            new RibbonMenuItem(
                "design-style-set-modern",
                "Modern",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.StyleSet, "Modern"),
                iconKey: "RibbonIcon.Styles"),
            new RibbonMenuItem(
                "design-style-set-elegant",
                "Elegant",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.StyleSet, "Elegant"),
                iconKey: "RibbonIcon.Styles")
        });

        var styleSetButton = new RibbonDropdownButton(
            "design-style-set",
            "Style Set",
            styleSetMenu,
            keyTip: "SS",
            iconKey: "RibbonIcon.Styles",
            size: RibbonControlSize.Medium);

        var designColorsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-format-colors-office",
                "Office",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Colors, "Office"),
                iconKey: "RibbonIcon.FontColor"),
            new RibbonMenuItem(
                "design-format-colors-colorful",
                "Colorful",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Colors, "Colorful"),
                iconKey: "RibbonIcon.FontColor"),
            new RibbonMenuItem(
                "design-format-colors-monochrome",
                "Monochrome",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Colors, "Monochrome"),
                iconKey: "RibbonIcon.FontColor")
        });

        var designColorsButton = new RibbonDropdownButton(
            "design-format-colors",
            "Colors",
            designColorsMenu,
            keyTip: "DC",
            iconKey: "RibbonIcon.FontColor",
            size: RibbonControlSize.Medium);

        var designFontsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-format-fonts-aptos",
                "Aptos",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Fonts, "Aptos"),
                iconKey: "RibbonIcon.FontFamily"),
            new RibbonMenuItem(
                "design-format-fonts-calibri",
                "Calibri",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Fonts, "Calibri"),
                iconKey: "RibbonIcon.FontFamily"),
            new RibbonMenuItem(
                "design-format-fonts-cambria",
                "Cambria",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Fonts, "Cambria"),
                iconKey: "RibbonIcon.FontFamily")
        });

        var designFontsButton = new RibbonDropdownButton(
            "design-format-fonts",
            "Fonts",
            designFontsMenu,
            keyTip: "DF",
            iconKey: "RibbonIcon.FontFamily",
            size: RibbonControlSize.Medium);

        var paragraphSpacingMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-paragraph-spacing-default",
                "Default",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.ParagraphSpacing, "Default"),
                iconKey: "RibbonIcon.LineSpacing"),
            new RibbonMenuItem(
                "design-paragraph-spacing-no",
                "No Spacing",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.ParagraphSpacing, "No Spacing"),
                iconKey: "RibbonIcon.LineSpacing"),
            new RibbonMenuItem(
                "design-paragraph-spacing-compact",
                "Compact",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.ParagraphSpacing, "Compact"),
                iconKey: "RibbonIcon.LineSpacing"),
            new RibbonMenuItem(
                "design-paragraph-spacing-open",
                "Open",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.ParagraphSpacing, "Open"),
                iconKey: "RibbonIcon.LineSpacing")
        });

        var paragraphSpacingButton = new RibbonDropdownButton(
            "design-paragraph-spacing",
            "Paragraph Spacing",
            paragraphSpacingMenu,
            keyTip: "PS",
            iconKey: "RibbonIcon.LineSpacing",
            size: RibbonControlSize.Medium);

        var designEffectsMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "design-format-effects-subtle",
                "Subtle",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Effects, "Subtle"),
                iconKey: "RibbonIcon.TextEffects"),
            new RibbonMenuItem(
                "design-format-effects-moderate",
                "Moderate",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Effects, "Moderate"),
                iconKey: "RibbonIcon.TextEffects"),
            new RibbonMenuItem(
                "design-format-effects-intense",
                "Intense",
                CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.Effects, "Intense"),
                iconKey: "RibbonIcon.TextEffects")
        });

        var designEffectsButton = new RibbonDropdownButton(
            "design-format-effects",
            "Effects",
            designEffectsMenu,
            keyTip: "EF",
            iconKey: "RibbonIcon.TextEffects",
            size: RibbonControlSize.Medium);

        var setDefaultButton = new RibbonButton(
            "design-set-default",
            "Set as Default",
            CreateEditorCommand(EditorDesignCommandIds.DocumentFormatting.SetAsDefault),
            keyTip: "SD",
            iconKey: "RibbonIcon.Star",
            size: RibbonControlSize.Small);

        var documentFormattingGroup = new RibbonGroup(
            "design-document-formatting",
            "Document Formatting",
            new IRibbonControl[]
            {
                styleSetButton,
                designColorsButton,
                designFontsButton,
                paragraphSpacingButton,
                designEffectsButton,
                setDefaultButton
            },
            keyTip: "DF");

        var watermarkButton = new RibbonButton(
            "design-watermark",
            "Watermark",
            CreateEditorCommand(EditorDesignCommandIds.PageBackground.Watermark),
            keyTip: "WM",
            iconKey: "RibbonIcon.Watermark",
            size: RibbonControlSize.Medium);

        var pageColorButton = new RibbonButton(
            "design-page-color",
            "Page Color",
            CreateEditorCommand(EditorDesignCommandIds.PageBackground.PageColor),
            keyTip: "PC",
            iconKey: "RibbonIcon.PageColor",
            size: RibbonControlSize.Medium);

        var pageBordersButton = new RibbonButton(
            "design-page-borders",
            "Page Borders",
            CreateEditorCommand(EditorDesignCommandIds.PageBackground.PageBorders),
            keyTip: "PB",
            iconKey: "RibbonIcon.PageBorders",
            size: RibbonControlSize.Medium);

        var pageBackgroundGroup = new RibbonGroup(
            "design-page-background",
            "Page Background",
            new IRibbonControl[]
            {
                watermarkButton,
                pageColorButton,
                pageBordersButton
            },
            keyTip: "PB");

        var drawSelectButton = new RibbonButton(
            "draw-select",
            "Select",
            CreateEditorCommand(EditorDrawCommandIds.Tools.Select),
            keyTip: "DS",
            iconKey: "RibbonIcon.Select",
            size: RibbonControlSize.Small);

        var drawLassoButton = new RibbonButton(
            "draw-lasso",
            "Lasso Select",
            CreateEditorCommand(EditorDrawCommandIds.Tools.LassoSelect),
            keyTip: "LS",
            iconKey: "RibbonIcon.Lasso",
            size: RibbonControlSize.Small);

        var drawPenButton = new RibbonButton(
            "draw-pen",
            "Pen",
            CreateEditorCommand(EditorDrawCommandIds.Tools.Pen),
            keyTip: "PN",
            iconKey: "RibbonIcon.Pen",
            size: RibbonControlSize.Small);

        var drawPencilButton = new RibbonButton(
            "draw-pencil",
            "Pencil",
            CreateEditorCommand(EditorDrawCommandIds.Tools.Pencil),
            keyTip: "PC",
            iconKey: "RibbonIcon.Pencil",
            size: RibbonControlSize.Small);

        var drawHighlighterButton = new RibbonButton(
            "draw-highlighter",
            "Highlighter",
            CreateEditorCommand(EditorDrawCommandIds.Tools.Highlighter),
            keyTip: "HL",
            iconKey: "RibbonIcon.Highlight",
            size: RibbonControlSize.Small);

        var drawEraserButton = new RibbonButton(
            "draw-eraser",
            "Eraser",
            CreateEditorCommand(EditorDrawCommandIds.Tools.Eraser),
            keyTip: "ER",
            iconKey: "RibbonIcon.Eraser",
            size: RibbonControlSize.Small);

        var drawToolsGroup = new RibbonGroup(
            "draw-tools",
            "Tools",
            new IRibbonControl[]
            {
                drawSelectButton,
                drawLassoButton,
                drawPenButton,
                drawPencilButton,
                drawHighlighterButton,
                drawEraserButton
            },
            keyTip: "TL");

        var inkToShapeButton = new RibbonButton(
            "draw-ink-to-shape",
            "Ink to Shape",
            CreateEditorCommand(EditorDrawCommandIds.Convert.InkToShape),
            keyTip: "IS",
            iconKey: "RibbonIcon.Shapes",
            size: RibbonControlSize.Medium);

        var inkToMathButton = new RibbonButton(
            "draw-ink-to-math",
            "Ink to Math",
            CreateEditorCommand(EditorDrawCommandIds.Convert.InkToMath),
            keyTip: "IM",
            iconKey: "RibbonIcon.Equation",
            size: RibbonControlSize.Medium);

        var inkReplayButton = new RibbonButton(
            "draw-ink-replay",
            "Ink Replay",
            CreateEditorCommand(EditorDrawCommandIds.Convert.InkReplay),
            keyTip: "IR",
            iconKey: "RibbonIcon.Redo",
            size: RibbonControlSize.Medium);

        var drawConvertGroup = new RibbonGroup(
            "draw-convert",
            "Convert",
            new IRibbonControl[]
            {
                inkToShapeButton,
                inkToMathButton,
                inkReplayButton
            },
            keyTip: "CV");

        var drawAddPenButton = new RibbonButton(
            "draw-add-pen",
            "Add Pen",
            CreateEditorCommand(EditorDrawCommandIds.AddPen.Add),
            keyTip: "AP",
            iconKey: "RibbonIcon.Pen",
            size: RibbonControlSize.Medium);

        var drawAddPenGroup = new RibbonGroup(
            "draw-add-pen-group",
            "Add Pen",
            new IRibbonControl[]
            {
                drawAddPenButton
            },
            keyTip: "AP");

        var envelopesButton = new RibbonButton(
            "mailings-envelopes",
            "Envelopes",
            CreateEditorCommand(EditorMailingsCommandIds.Create.Envelopes),
            keyTip: "EV",
            iconKey: "RibbonIcon.Envelope",
            size: RibbonControlSize.Medium);

        var labelsButton = new RibbonButton(
            "mailings-labels",
            "Labels",
            CreateEditorCommand(EditorMailingsCommandIds.Create.Labels),
            keyTip: "LB",
            iconKey: "RibbonIcon.Label",
            size: RibbonControlSize.Medium);

        var mailingsCreateGroup = new RibbonGroup(
            "mailings-create",
            "Create",
            new IRibbonControl[]
            {
                envelopesButton,
                labelsButton
            },
            keyTip: "CR");

        var startMailMergeMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "mailings-start-letters",
                "Letters",
                CreateEditorCommand(EditorMailingsCommandIds.StartMailMerge.Start, "Letters"),
                iconKey: "RibbonIcon.MailMerge"),
            new RibbonMenuItem(
                "mailings-start-email",
                "E-mail Messages",
                CreateEditorCommand(EditorMailingsCommandIds.StartMailMerge.Start, "Email"),
                iconKey: "RibbonIcon.MailMerge"),
            new RibbonMenuItem(
                "mailings-start-envelopes",
                "Envelopes",
                CreateEditorCommand(EditorMailingsCommandIds.StartMailMerge.Start, "Envelopes"),
                iconKey: "RibbonIcon.Envelope"),
            new RibbonMenuItem(
                "mailings-start-labels",
                "Labels",
                CreateEditorCommand(EditorMailingsCommandIds.StartMailMerge.Start, "Labels"),
                iconKey: "RibbonIcon.Label"),
            new RibbonMenuItem(
                "mailings-start-directory",
                "Directory",
                CreateEditorCommand(EditorMailingsCommandIds.StartMailMerge.Start, "Directory"),
                iconKey: "RibbonIcon.MailMerge")
        });

        var startMailMergeButton = new RibbonDropdownButton(
            "mailings-start-mail-merge",
            "Start Mail Merge",
            startMailMergeMenu,
            keyTip: "SM",
            iconKey: "RibbonIcon.MailMerge",
            size: RibbonControlSize.Large);

        var selectRecipientsButton = new RibbonButton(
            "mailings-select-recipients",
            "Select Recipients",
            CreateEditorCommand(EditorMailingsCommandIds.StartMailMerge.SelectRecipients),
            keyTip: "SR",
            iconKey: "RibbonIcon.User",
            size: RibbonControlSize.Medium);

        var editRecipientsButton = new RibbonButton(
            "mailings-edit-recipients",
            "Edit Recipient List",
            CreateEditorCommand(EditorMailingsCommandIds.StartMailMerge.EditRecipients),
            keyTip: "ER",
            iconKey: "RibbonIcon.User",
            size: RibbonControlSize.Medium);

        var mailingsStartGroup = new RibbonGroup(
            "mailings-start",
            "Start Mail Merge",
            new IRibbonControl[]
            {
                startMailMergeButton,
                selectRecipientsButton,
                editRecipientsButton
            },
            keyTip: "SM");

        var highlightMergeFieldsToggle = new RibbonToggleButton(
            "mailings-highlight-merge-fields",
            "Highlight Merge Fields",
            command: CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.HighlightMergeFields),
            keyTip: "HM",
            iconKey: "RibbonIcon.Highlight",
            size: RibbonControlSize.Small);

        var addressBlockButton = new RibbonButton(
            "mailings-address-block",
            "Address Block",
            CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.AddressBlock),
            keyTip: "AB",
            iconKey: "RibbonIcon.User",
            size: RibbonControlSize.Medium);

        var greetingLineButton = new RibbonButton(
            "mailings-greeting-line",
            "Greeting Line",
            CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.GreetingLine),
            keyTip: "GL",
            iconKey: "RibbonIcon.Comment",
            size: RibbonControlSize.Medium);

        var insertMergeFieldMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "mailings-merge-field-first-name",
                "First Name",
                CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.InsertMergeField, "FirstName"),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "mailings-merge-field-last-name",
                "Last Name",
                CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.InsertMergeField, "LastName"),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "mailings-merge-field-company",
                "Company",
                CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.InsertMergeField, "Company"),
                iconKey: "RibbonIcon.QuickParts"),
            new RibbonMenuItem(
                "mailings-merge-field-address",
                "Address",
                CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.InsertMergeField, "Address"),
                iconKey: "RibbonIcon.QuickParts")
        });

        var insertMergeFieldButton = new RibbonDropdownButton(
            "mailings-insert-merge-field",
            "Insert Merge Field",
            insertMergeFieldMenu,
            keyTip: "IM",
            iconKey: "RibbonIcon.QuickParts",
            size: RibbonControlSize.Medium);

        var rulesMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "mailings-rules-if",
                "If...Then...Else",
                CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.Rules, "IfThenElse"),
                iconKey: "RibbonIcon.Settings"),
            new RibbonMenuItem(
                "mailings-rules-next",
                "Next Record",
                CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.Rules, "NextRecord"),
                iconKey: "RibbonIcon.Settings"),
            new RibbonMenuItem(
                "mailings-rules-skip",
                "Skip Record",
                CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.Rules, "SkipRecord"),
                iconKey: "RibbonIcon.Settings")
        });

        var rulesButton = new RibbonDropdownButton(
            "mailings-rules",
            "Rules",
            rulesMenu,
            keyTip: "RL",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Medium);

        var matchFieldsButton = new RibbonButton(
            "mailings-match-fields",
            "Match Fields",
            CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.MatchFields),
            keyTip: "MF",
            iconKey: "RibbonIcon.Link",
            size: RibbonControlSize.Medium);

        var updateLabelsButton = new RibbonButton(
            "mailings-update-labels",
            "Update Labels",
            CreateEditorCommand(EditorMailingsCommandIds.WriteInsert.UpdateLabels),
            keyTip: "UL",
            iconKey: "RibbonIcon.Label",
            size: RibbonControlSize.Medium);

        var mailingsWriteGroup = new RibbonGroup(
            "mailings-write",
            "Write & Insert Fields",
            new IRibbonControl[]
            {
                highlightMergeFieldsToggle,
                addressBlockButton,
                greetingLineButton,
                insertMergeFieldButton,
                rulesButton,
                matchFieldsButton,
                updateLabelsButton
            },
            keyTip: "WI");

        var previewResultsToggle = new RibbonToggleButton(
            "mailings-preview-results",
            "Preview Results",
            command: CreateEditorCommand(EditorMailingsCommandIds.PreviewResults.Toggle),
            keyTip: "PR",
            iconKey: "RibbonIcon.Invisibles",
            size: RibbonControlSize.Small);

        var firstRecordButton = new RibbonButton(
            "mailings-first-record",
            "First Record",
            CreateEditorCommand(EditorMailingsCommandIds.PreviewResults.FirstRecord),
            keyTip: "FR",
            iconKey: "RibbonIcon.PageNumber",
            size: RibbonControlSize.Small);

        var previousRecordButton = new RibbonButton(
            "mailings-prev-record",
            "Previous Record",
            CreateEditorCommand(EditorMailingsCommandIds.PreviewResults.PreviousRecord),
            keyTip: "PR",
            iconKey: "RibbonIcon.Undo",
            size: RibbonControlSize.Small);

        var nextRecordButton = new RibbonButton(
            "mailings-next-record",
            "Next Record",
            CreateEditorCommand(EditorMailingsCommandIds.PreviewResults.NextRecord),
            keyTip: "NR",
            iconKey: "RibbonIcon.Redo",
            size: RibbonControlSize.Small);

        var lastRecordButton = new RibbonButton(
            "mailings-last-record",
            "Last Record",
            CreateEditorCommand(EditorMailingsCommandIds.PreviewResults.LastRecord),
            keyTip: "LR",
            iconKey: "RibbonIcon.PageNumber",
            size: RibbonControlSize.Small);

        var findRecipientButton = new RibbonButton(
            "mailings-find-recipient",
            "Find Recipient",
            CreateEditorCommand(EditorMailingsCommandIds.PreviewResults.FindRecipient),
            keyTip: "FR",
            iconKey: "RibbonIcon.Search",
            size: RibbonControlSize.Small);

        var checkErrorsButton = new RibbonButton(
            "mailings-check-errors",
            "Check Errors",
            CreateEditorCommand(EditorMailingsCommandIds.PreviewResults.CheckErrors),
            keyTip: "CE",
            iconKey: "RibbonIcon.Alert",
            size: RibbonControlSize.Small);

        var mailingsPreviewGroup = new RibbonGroup(
            "mailings-preview",
            "Preview Results",
            new IRibbonControl[]
            {
                previewResultsToggle,
                firstRecordButton,
                previousRecordButton,
                nextRecordButton,
                lastRecordButton,
                findRecipientButton,
                checkErrorsButton
            },
            keyTip: "PR");

        var finishMergeMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "mailings-finish-edit",
                "Edit Individual Documents",
                CreateEditorCommand(EditorMailingsCommandIds.Finish.FinishAndMerge, "EditDocuments"),
                iconKey: "RibbonIcon.MailMerge"),
            new RibbonMenuItem(
                "mailings-finish-print",
                "Print Documents",
                CreateEditorCommand(EditorMailingsCommandIds.Finish.FinishAndMerge, "PrintDocuments"),
                iconKey: "RibbonIcon.Print"),
            new RibbonMenuItem(
                "mailings-finish-email",
                "Send Email Messages",
                CreateEditorCommand(EditorMailingsCommandIds.Finish.FinishAndMerge, "SendEmail"),
                iconKey: "RibbonIcon.MailMerge")
        });

        var finishMergeButton = new RibbonDropdownButton(
            "mailings-finish",
            "Finish & Merge",
            finishMergeMenu,
            keyTip: "FM",
            iconKey: "RibbonIcon.MailMerge",
            size: RibbonControlSize.Large);

        var mailingsFinishGroup = new RibbonGroup(
            "mailings-finish",
            "Finish",
            new IRibbonControl[]
            {
                finishMergeButton
            },
            keyTip: "FN");

        var spellingButton = new RibbonButton(
            "review-spelling",
            "Spelling & Grammar",
            CreateEditorCommand(EditorReviewCommandIds.Proofing.SpellingGrammar),
            keyTip: "SG",
            iconKey: "RibbonIcon.Check",
            size: RibbonControlSize.Medium);

        var thesaurusButton = new RibbonButton(
            "review-thesaurus",
            "Thesaurus",
            CreateEditorCommand(EditorReviewCommandIds.Proofing.Thesaurus),
            keyTip: "TH",
            iconKey: "RibbonIcon.Thesaurus",
            size: RibbonControlSize.Medium);

        var wordCountButton = new RibbonButton(
            "review-word-count",
            "Word Count",
            CreateEditorCommand(EditorReviewCommandIds.Proofing.WordCount),
            keyTip: "WC",
            iconKey: "RibbonIcon.WordCount",
            size: RibbonControlSize.Medium);

        var spellingToggle = new RibbonToggleButton(
            "review-proofing-spelling-toggle",
            "Check Spelling as You Type",
            IsProofingSpellingEnabled,
            ToggleProofingSpellingAsync,
            keyTip: "CS",
            iconKey: "RibbonIcon.Check",
            size: RibbonControlSize.Small);

        var grammarToggle = new RibbonToggleButton(
            "review-proofing-grammar-toggle",
            "Mark Grammar Errors as You Type",
            IsProofingGrammarEnabled,
            ToggleProofingGrammarAsync,
            keyTip: "MG",
            iconKey: "RibbonIcon.Alert",
            size: RibbonControlSize.Small);

        var styleToggle = new RibbonToggleButton(
            "review-proofing-style-toggle",
            "Mark Style Issues as You Type",
            IsProofingStyleEnabled,
            ToggleProofingStyleAsync,
            keyTip: "MS",
            iconKey: "RibbonIcon.Star",
            size: RibbonControlSize.Small);

        var useEngineToggle = new RibbonToggleButton(
            "review-proofing-engine-toggle",
            "Use This Engine",
            IsUsingSelectedEngine,
            ToggleUseSelectedEngineAsync,
            keyTip: "UE",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Small);

        var proofingOptionsButton = new RibbonButton(
            "review-proofing-options",
            "Proofing Options...",
            new RibbonCommand(OpenProofingOptionsAsync, canInteract),
            keyTip: "PO",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Small);

        var proofingGroup = new RibbonGroup(
            "review-proofing",
            "Proofing",
            new IRibbonControl[]
            {
                spellingButton,
                thesaurusButton,
                wordCountButton,
                spellingToggle,
                grammarToggle,
                styleToggle,
                useEngineToggle,
                proofingOptionsButton
            },
            keyTip: "PF");

        var readAloudButton = new RibbonButton(
            "review-read-aloud",
            "Read Aloud",
            CreateEditorCommand(EditorReviewCommandIds.Speech.ReadAloud),
            keyTip: "RA",
            iconKey: "RibbonIcon.ReadMode",
            size: RibbonControlSize.Medium);

        var speechGroup = new RibbonGroup(
            "review-speech",
            "Speech",
            new IRibbonControl[]
            {
                readAloudButton
            },
            keyTip: "SP");

        var checkAccessibilityButton = new RibbonButton(
            "review-accessibility",
            "Check Accessibility",
            CreateEditorCommand(EditorReviewCommandIds.Accessibility.CheckAccessibility),
            keyTip: "AC",
            iconKey: "RibbonIcon.Alert",
            size: RibbonControlSize.Medium);

        var accessibilityGroup = new RibbonGroup(
            "review-accessibility",
            "Accessibility",
            new IRibbonControl[]
            {
                checkAccessibilityButton
            },
            keyTip: "AC");

        var translateButton = new RibbonButton(
            "review-translate",
            "Translate",
            CreateEditorCommand(EditorReviewCommandIds.Language.Translate),
            keyTip: "TR",
            iconKey: "RibbonIcon.Globe",
            size: RibbonControlSize.Medium);

        var languageButton = new RibbonButton(
            "review-language",
            "Language",
            CreateAsyncCommand(OpenLanguageDialogAsync, canInteract),
            keyTip: "LG",
            iconKey: "RibbonIcon.Text",
            size: RibbonControlSize.Medium);

        var languageGroup = new RibbonGroup(
            "review-language",
            "Language",
            new IRibbonControl[]
            {
                translateButton,
                languageButton
            },
            keyTip: "LG");

        var newCommentButton = new RibbonButton(
            "review-new-comment",
            "New Comment",
            CreateEditorCommand(EditorReviewCommandIds.Comments.NewComment),
            keyTip: "NC",
            iconKey: "RibbonIcon.Comment",
            size: RibbonControlSize.Medium);

        var deleteCommentButton = new RibbonButton(
            "review-delete-comment",
            "Delete",
            CreateEditorCommand(EditorReviewCommandIds.Comments.DeleteComment),
            keyTip: "DC",
            iconKey: "RibbonIcon.Reject",
            size: RibbonControlSize.Medium);

        var previousCommentButton = new RibbonButton(
            "review-previous-comment",
            "Previous",
            CreateEditorCommand(EditorReviewCommandIds.Comments.PreviousComment),
            keyTip: "PC",
            iconKey: "RibbonIcon.Undo",
            size: RibbonControlSize.Small);

        var nextCommentButton = new RibbonButton(
            "review-next-comment",
            "Next",
            CreateEditorCommand(EditorReviewCommandIds.Comments.NextComment),
            keyTip: "NC",
            iconKey: "RibbonIcon.Redo",
            size: RibbonControlSize.Small);

        var commentsGroup = new RibbonGroup(
            "review-comments",
            "Comments",
            new IRibbonControl[]
            {
                newCommentButton,
                deleteCommentButton,
                previousCommentButton,
                nextCommentButton
            },
            keyTip: "CM");

        var trackChangesToggle = new RibbonToggleButton(
            "review-track-changes",
            "Track Changes",
            () => _editorView?.Document.TrackChangesEnabled ?? false,
            value => ExecuteEditorCommandAsync(EditorReviewCommandIds.Tracking.TrackChangesToggle, value),
            keyTip: "TC",
            iconKey: "RibbonIcon.TrackChanges",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        var showMarkupMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "review-show-markup-all",
                "All Markup",
                CreateEditorCommand(EditorReviewCommandIds.Tracking.ShowMarkup, "All"),
                iconKey: "RibbonIcon.TrackChanges"),
            new RibbonMenuItem(
                "review-show-markup-simple",
                "Simple Markup",
                CreateEditorCommand(EditorReviewCommandIds.Tracking.ShowMarkup, "Simple"),
                iconKey: "RibbonIcon.TrackChanges"),
            new RibbonMenuItem(
                "review-show-markup-none",
                "No Markup",
                CreateEditorCommand(EditorReviewCommandIds.Tracking.ShowMarkup, "None"),
                iconKey: "RibbonIcon.TrackChanges"),
            new RibbonMenuItem(
                "review-show-markup-balloons",
                "Show Revisions in Balloons",
                CreateEditorCommand(EditorReviewCommandIds.Tracking.ShowMarkup, "Balloons"),
                iconKey: "RibbonIcon.TrackChanges")
        });

        var showMarkupButton = new RibbonDropdownButton(
            "review-show-markup",
            "Show Markup",
            showMarkupMenu,
            keyTip: "SM",
            iconKey: "RibbonIcon.TrackChanges",
            size: RibbonControlSize.Medium);

        var reviewingPaneButton = new RibbonButton(
            "review-reviewing-pane",
            "Reviewing Pane",
            CreateEditorCommand(EditorReviewCommandIds.Tracking.ReviewingPane),
            keyTip: "RP",
            iconKey: "RibbonIcon.TrackChanges",
            size: RibbonControlSize.Medium);

        var trackingGroup = new RibbonGroup(
            "review-tracking",
            "Tracking",
            new IRibbonControl[]
            {
                trackChangesToggle,
                showMarkupButton,
                reviewingPaneButton
            },
            keyTip: "TR");

        var acceptChangeMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "review-accept",
                "Accept",
                CreateEditorCommand(EditorReviewCommandIds.Changes.Accept),
                iconKey: "RibbonIcon.Check"),
            new RibbonMenuItem(
                "review-accept-all",
                "Accept All Changes",
                CreateEditorCommand(EditorReviewCommandIds.Changes.AcceptAll),
                iconKey: "RibbonIcon.Check")
        });

        var acceptChangeButton = new RibbonDropdownButton(
            "review-accept",
            "Accept",
            acceptChangeMenu,
            keyTip: "AC",
            iconKey: "RibbonIcon.Check",
            size: RibbonControlSize.Medium);

        var rejectChangeMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "review-reject",
                "Reject",
                CreateEditorCommand(EditorReviewCommandIds.Changes.Reject),
                iconKey: "RibbonIcon.Reject"),
            new RibbonMenuItem(
                "review-reject-all",
                "Reject All Changes",
                CreateEditorCommand(EditorReviewCommandIds.Changes.RejectAll),
                iconKey: "RibbonIcon.Reject")
        });

        var rejectChangeButton = new RibbonDropdownButton(
            "review-reject",
            "Reject",
            rejectChangeMenu,
            keyTip: "RJ",
            iconKey: "RibbonIcon.Reject",
            size: RibbonControlSize.Medium);

        var previousChangeButton = new RibbonButton(
            "review-previous-change",
            "Previous",
            CreateEditorCommand(EditorReviewCommandIds.Changes.PreviousChange),
            keyTip: "PC",
            iconKey: "RibbonIcon.Undo",
            size: RibbonControlSize.Small);

        var nextChangeButton = new RibbonButton(
            "review-next-change",
            "Next",
            CreateEditorCommand(EditorReviewCommandIds.Changes.NextChange),
            keyTip: "NC",
            iconKey: "RibbonIcon.Redo",
            size: RibbonControlSize.Small);

        var changesGroup = new RibbonGroup(
            "review-changes",
            "Changes",
            new IRibbonControl[]
            {
                acceptChangeButton,
                rejectChangeButton,
                previousChangeButton,
                nextChangeButton
            },
            keyTip: "CH");

        var compareButton = new RibbonButton(
            "review-compare",
            "Compare",
            CreateEditorCommand(EditorReviewCommandIds.Compare.CompareDocuments),
            keyTip: "CP",
            iconKey: "RibbonIcon.Sort",
            size: RibbonControlSize.Medium);

        var combineButton = new RibbonButton(
            "review-combine",
            "Combine",
            CreateEditorCommand(EditorReviewCommandIds.Compare.Combine),
            keyTip: "CB",
            iconKey: "RibbonIcon.Sort",
            size: RibbonControlSize.Medium);

        var compareGroup = new RibbonGroup(
            "review-compare",
            "Compare",
            new IRibbonControl[]
            {
                compareButton,
                combineButton
            },
            keyTip: "CP");

        var restrictEditingButton = new RibbonButton(
            "review-restrict-editing",
            "Restrict Editing",
            CreateEditorCommand(EditorReviewCommandIds.Protect.RestrictEditing),
            keyTip: "RE",
            iconKey: "RibbonIcon.Lock",
            size: RibbonControlSize.Medium);

        var protectGroup = new RibbonGroup(
            "review-protect",
            "Protect",
            new IRibbonControl[]
            {
                restrictEditingButton
            },
            keyTip: "PR");

        var readModeButton = new RibbonButton(
            "view-read-mode",
            "Read Mode",
            CreateEditorCommand(EditorViewCommandIds.Views.ReadMode),
            keyTip: "RM",
            iconKey: "RibbonIcon.ReadMode",
            size: RibbonControlSize.Medium);

        var printLayoutToggle = new RibbonToggleButton(
            "view-print-layout",
            "Print Layout",
            () => IsViewMode(EditorViewMode.PrintLayout),
            value => value ? ExecuteEditorCommandAsync(EditorViewCommandIds.Views.PrintLayout) : ValueTask.CompletedTask,
            iconKey: "RibbonIcon.Layout",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        var webLayoutToggle = new RibbonToggleButton(
            "view-web-layout",
            "Web Layout",
            () => IsViewMode(EditorViewMode.WebLayout),
            value => value ? ExecuteEditorCommandAsync(EditorViewCommandIds.Views.WebLayout) : ValueTask.CompletedTask,
            keyTip: "WL",
            iconKey: "RibbonIcon.Globe",
            size: RibbonControlSize.Medium);

        var outlineToggle = new RibbonToggleButton(
            "view-outline",
            "Outline",
            () => IsViewMode(EditorViewMode.Outline),
            value => value ? ExecuteEditorCommandAsync(EditorViewCommandIds.Views.Outline) : ValueTask.CompletedTask,
            keyTip: "OL",
            iconKey: "RibbonIcon.Outline",
            size: RibbonControlSize.Medium);

        var draftViewToggle = new RibbonToggleButton(
            "view-draft",
            "Draft",
            () => IsViewMode(EditorViewMode.Draft),
            value => value ? ExecuteEditorCommandAsync(EditorViewCommandIds.Views.Draft) : ValueTask.CompletedTask,
            iconKey: "RibbonIcon.Draft",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        bool IsHtmlSourceOpen()
        {
            return _htmlSourceWindow is not null && _htmlSourceWindow.IsVisible;
        }

        var htmlSourceToggle = new RibbonToggleButton(
            "view-html-source",
            "HTML Source",
            IsHtmlSourceOpen,
            ToggleHtmlSourceAsync,
            iconKey: "RibbonIcon.Text",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        var viewsGroup = new RibbonGroup(
            "view-views",
            "Views",
            new IRibbonControl[]
            {
                readModeButton,
                printLayoutToggle,
                webLayoutToggle,
                outlineToggle,
                draftViewToggle
            },
            keyTip: "VW");

        var sourceGroup = new RibbonGroup(
            "view-source",
            "Source",
            new IRibbonControl[]
            {
                htmlSourceToggle
            },
            keyTip: "SR");

        var rulerToggle = new RibbonToggleButton(
            "view-ruler",
            "Ruler",
            IsRulerActive,
            value => ExecuteEditorCommandAsync(EditorViewCommandIds.Show.Ruler, value),
            keyTip: "RU",
            iconKey: "RibbonIcon.Ruler",
            canExecute: canInteract,
            size: RibbonControlSize.Small);

        var gridlinesToggle = new RibbonToggleButton(
            "view-gridlines",
            "Gridlines",
            IsGridlinesActive,
            value => ExecuteEditorCommandAsync(EditorViewCommandIds.Show.Gridlines, value),
            keyTip: "GL",
            iconKey: "RibbonIcon.Gridlines",
            canExecute: canInteract,
            size: RibbonControlSize.Small);

        var navigationPaneToggle = new RibbonToggleButton(
            "view-navigation-pane",
            "Navigation Pane",
            IsNavigationPaneActive,
            value => ExecuteEditorCommandAsync(EditorViewCommandIds.Show.NavigationPane, value),
            keyTip: "NP",
            iconKey: "RibbonIcon.NavigationPane",
            canExecute: canInteract,
            size: RibbonControlSize.Small);

        var showInvisibles = new RibbonToggleButton(
            "show-invisibles",
            "Show Invisibles",
            IsShowInvisiblesActive,
            value => ToggleShowInvisibles(value),
            iconKey: "RibbonIcon.Invisibles",
            canExecute: canInteract,
            size: RibbonControlSize.Small);

        var showGroup = new RibbonGroup(
            "view-show",
            "Show",
            new IRibbonControl[]
            {
                rulerToggle,
                gridlinesToggle,
                navigationPaneToggle,
                showInvisibles
            },
            keyTip: "SH");

        var pageMovementVerticalToggle = new RibbonToggleButton(
            "view-page-vertical",
            "Vertical",
            IsPageMovementVertical,
            value => value ? ExecuteEditorCommandAsync(EditorViewCommandIds.PageMovement.Vertical) : ValueTask.CompletedTask,
            keyTip: "PV",
            iconKey: "RibbonIcon.Layout",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        var pageMovementSideToggle = new RibbonToggleButton(
            "view-page-side",
            "Side to Side",
            IsPageMovementSideToSide,
            value => value ? ExecuteEditorCommandAsync(EditorViewCommandIds.PageMovement.SideToSide) : ValueTask.CompletedTask,
            keyTip: "PS",
            iconKey: "RibbonIcon.PageWidth",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        var pageMovementGroup = new RibbonGroup(
            "view-page-movement",
            "Page Movement",
            new IRibbonControl[]
            {
                pageMovementVerticalToggle,
                pageMovementSideToggle
            },
            keyTip: "PM");

        var zoomDialogButton = new RibbonButton(
            "zoom-dialog",
            "Zoom",
            CreateAsyncCommand(OpenZoomDialogAsync, canInteract),
            keyTip: "ZD",
            iconKey: "RibbonIcon.ZoomIn",
            size: RibbonControlSize.Medium);

        var zoomInButton = new RibbonButton(
            "zoom-in",
            "Zoom In",
            CreateViewCommand(() => _editorView?.ZoomIn()),
            keyTip: "ZI",
            iconKey: "RibbonIcon.ZoomIn",
            size: RibbonControlSize.Small);

        var zoomOutButton = new RibbonButton(
            "zoom-out",
            "Zoom Out",
            CreateViewCommand(() => _editorView?.ZoomOut()),
            keyTip: "ZO",
            iconKey: "RibbonIcon.ZoomOut",
            size: RibbonControlSize.Small);

        var zoom100Button = new RibbonButton(
            "zoom-100",
            "100%",
            CreateViewCommand(() => _editorView?.ZoomToDefault()),
            keyTip: "1",
            iconKey: "RibbonIcon.Zoom100",
            size: RibbonControlSize.Medium);

        var zoomPageWidthButton = new RibbonButton(
            "zoom-page-width",
            "Page Width",
            CreateViewCommand(() => _editorView?.ZoomToPageWidth()),
            keyTip: "PW",
            iconKey: "RibbonIcon.PageWidth",
            size: RibbonControlSize.Medium);

        var zoomWholePageButton = new RibbonButton(
            "zoom-whole-page",
            "One Page",
            CreateViewCommand(() => _editorView?.ZoomToWholePage()),
            keyTip: "OP",
            iconKey: "RibbonIcon.OnePage",
            size: RibbonControlSize.Medium);

        var zoomMultiplePagesButton = new RibbonButton(
            "zoom-multiple-pages",
            "Multiple Pages",
            CreateViewCommand(() => _editorView?.ZoomToMultiplePages()),
            keyTip: "MP",
            iconKey: "RibbonIcon.MultiplePages",
            size: RibbonControlSize.Medium);

        var zoomGroup = new RibbonGroup(
            "zoom",
            "Zoom",
            new IRibbonControl[]
            {
                zoomDialogButton,
                zoom100Button,
                zoomWholePageButton,
                zoomPageWidthButton,
                zoomMultiplePagesButton,
                zoomInButton,
                zoomOutButton
            },
            keyTip: "ZM");

        var newWindowButton = new RibbonButton(
            "view-new-window",
            "New Window",
            CreateEditorCommand(EditorViewCommandIds.Window.NewWindow),
            keyTip: "NW",
            iconKey: "RibbonIcon.Window",
            size: RibbonControlSize.Medium);

        var arrangeAllButton = new RibbonButton(
            "view-arrange-all",
            "Arrange All",
            CreateEditorCommand(EditorViewCommandIds.Window.ArrangeAll),
            keyTip: "AA",
            iconKey: "RibbonIcon.Sort",
            size: RibbonControlSize.Medium);

        var splitWindowButton = new RibbonButton(
            "view-split",
            "Split",
            CreateEditorCommand(EditorViewCommandIds.Window.Split),
            keyTip: "SP",
            iconKey: "RibbonIcon.Cut",
            size: RibbonControlSize.Medium);

        var viewSideBySideButton = new RibbonButton(
            "view-side-by-side",
            "View Side by Side",
            CreateEditorCommand(EditorViewCommandIds.Window.ViewSideBySide),
            keyTip: "VS",
            iconKey: "RibbonIcon.PageWidth",
            size: RibbonControlSize.Medium);

        var syncScrollingButton = new RibbonButton(
            "view-sync-scroll",
            "Synchronous Scrolling",
            CreateEditorCommand(EditorViewCommandIds.Window.SynchronousScrolling),
            keyTip: "SS",
            iconKey: "RibbonIcon.Link",
            size: RibbonControlSize.Medium);

        var resetWindowButton = new RibbonButton(
            "view-reset-window",
            "Reset Window Position",
            CreateEditorCommand(EditorViewCommandIds.Window.ResetWindowPosition),
            keyTip: "RW",
            iconKey: "RibbonIcon.Layout",
            size: RibbonControlSize.Medium);

        var switchWindowsButton = new RibbonButton(
            "view-switch-windows",
            "Switch Windows",
            CreateEditorCommand(EditorViewCommandIds.Window.SwitchWindows),
            keyTip: "SW",
            iconKey: "RibbonIcon.Sort",
            size: RibbonControlSize.Medium);

        var windowGroup = new RibbonGroup(
            "view-window",
            "Window",
            new IRibbonControl[]
            {
                newWindowButton,
                arrangeAllButton,
                splitWindowButton,
                viewSideBySideButton,
                syncScrollingButton,
                resetWindowButton,
                switchWindowsButton
            },
            keyTip: "WN");

        var macrosButton = new RibbonButton(
            "view-macros",
            "Macros",
            CreateEditorCommand(EditorViewCommandIds.Macros.Open),
            keyTip: "MC",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Medium);

        var recordMacroButton = new RibbonButton(
            "view-record-macro",
            "Record Macro",
            CreateEditorCommand(EditorViewCommandIds.Macros.RecordMacro),
            keyTip: "RM",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Medium);

        var macrosGroup = new RibbonGroup(
            "view-macros",
            "Macros",
            new IRibbonControl[]
            {
                macrosButton,
                recordMacroButton
            },
            keyTip: "MC");

        var vbaEditorButton = new RibbonButton(
            "vba-editor",
            "VBA Editor",
            CreateEditorCommand(EditorViewCommandIds.Macros.VbaEditor),
            keyTip: "VE",
            iconKey: "RibbonIcon.Settings",
            size: RibbonControlSize.Medium);

        var debugMacroButton = new RibbonButton(
            "vba-debug-start",
            "Debug Macro",
            CreateEditorCommand(EditorViewCommandIds.Macros.Debug),
            keyTip: "VM",
            iconKey: "RibbonIcon.Check",
            size: RibbonControlSize.Medium);

        var vbaToolsGroup = new RibbonGroup(
            "view-vba-tools",
            "VBA",
            new IRibbonControl[]
            {
                vbaEditorButton,
                debugMacroButton
            },
            keyTip: "VB");

        var debugContinueButton = new RibbonButton(
            "vba-debug-continue",
            "Continue",
            CreateViewCommandWithCanExecute(
                () => RunDebugAction(session => session.Continue()),
                () => GetActiveDebugSession() is not null),
            keyTip: "DC",
            iconKey: "RibbonIcon.Check",
            size: RibbonControlSize.Small);

        var debugBreakButton = new RibbonButton(
            "vba-debug-break",
            "Break",
            CreateViewCommandWithCanExecute(
                () => RunDebugAction(session => session.Break()),
                () => GetActiveDebugSession() is not null),
            keyTip: "DB",
            iconKey: "RibbonIcon.Alert",
            size: RibbonControlSize.Small);

        var debugStepInButton = new RibbonButton(
            "vba-debug-step-in",
            "Step In",
            CreateViewCommandWithCanExecute(
                () => RunDebugAction(session => session.StepIn()),
                () => GetActiveDebugSession() is not null),
            keyTip: "DI",
            iconKey: "RibbonIcon.Undo",
            size: RibbonControlSize.Small);

        var debugStepOverButton = new RibbonButton(
            "vba-debug-step-over",
            "Step Over",
            CreateViewCommandWithCanExecute(
                () => RunDebugAction(session => session.StepOver()),
                () => GetActiveDebugSession() is not null),
            keyTip: "DO",
            iconKey: "RibbonIcon.Redo",
            size: RibbonControlSize.Small);

        var debugStepOutButton = new RibbonButton(
            "vba-debug-step-out",
            "Step Out",
            CreateViewCommandWithCanExecute(
                () => RunDebugAction(session => session.StepOut()),
                () => GetActiveDebugSession() is not null),
            keyTip: "DU",
            iconKey: "RibbonIcon.Sort",
            size: RibbonControlSize.Small);

        var debugStopButton = new RibbonButton(
            "vba-debug-stop",
            "Stop",
            CreateViewCommandWithCanExecute(
                () => RunDebugAction(session => session.Stop()),
                () => GetActiveDebugSession() is not null),
            keyTip: "DS",
            iconKey: "RibbonIcon.Reject",
            size: RibbonControlSize.Small);

        var debugGroup = new RibbonGroup(
            "view-debug",
            "Debug",
            new IRibbonControl[]
            {
                debugContinueButton,
                debugBreakButton,
                debugStepInButton,
                debugStepOverButton,
                debugStepOutButton,
                debugStopButton
            },
            keyTip: "DG");

        var useHarfBuzz = new RibbonToggleButton(
            "use-harfbuzz",
            "Use HarfBuzz",
            () => _editorView?.UseHarfBuzz ?? true,
            value => ToggleUseHarfBuzz(value),
            iconKey: "RibbonIcon.Text",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        var useCache = new RibbonToggleButton(
            "use-cache",
            "Use Page Cache",
            () => _editorView?.UsePictureCache ?? true,
            value => ToggleUsePictureCache(value),
            iconKey: "RibbonIcon.Cache",
            canExecute: canInteract,
            size: RibbonControlSize.Medium);

        var textGroup = new RibbonGroup(
            "text",
            "Text",
            new IRibbonControl[]
            {
                useHarfBuzz,
                useCache
            },
            keyTip: "TX");

        bool IsTextContextActive()
        {
            if (!TryGetSnapshot(out var snapshot))
            {
                return false;
            }

            var selection = snapshot.Selection;
            if (selection.IsInTable)
            {
                return false;
            }

            return selection.Kind == EditorSelectionKind.Range && !selection.IsCollapsed;
        }

        async ValueTask SubmitTopBarSearchAsync(string? query)
        {
            if (!CanUseFindReplace())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                await ShowFindReplaceDialogAsync(false);
                return;
            }

            await ExecuteEditorCommandAsync(EditorHomeCommandIds.Editing.Find, query);
        }

        var topBarSearch = new RibbonTextBox(
            "topbar-search",
            "Search tools",
            placeholder: "Search tools, help, and more",
            submitHandler: SubmitTopBarSearchAsync,
            keyTip: "Q",
            iconKey: "RibbonIcon.Search",
            canExecute: CanUseFindReplace,
            size: RibbonControlSize.Medium,
            toolTipDescription: "Search commands or find text in the document.");

        var builder = new RibbonModelBuilder();
        builder.SetTopBarSearch(topBarSearch);
        builder.SetTopBarAppBadge("W");
        builder.SetTopBarAppName("Vibe Word");
        builder.SetTopBarTitle(ResolveRibbonDocumentTitle());
        builder.SetTopBarStatus("Saved", "RibbonIcon.Check");
        builder.SetTopBarProfileInitials("WS");
        builder.AddTab("file", "File", keyTip: "F")
            .AddGroup(fileGroup);
        builder.AddTab("home", "Home", keyTip: "H")
            .AddGroups(new[] { undoGroup, clipboardGroup, fontGroup, paragraphGroup, stylesGroup, editingGroup });
        builder.AddTab("insert", "Insert", keyTip: "N")
            .AddGroups(new[]
            {
                pagesGroup,
                tablesGroup,
                illustrationsGroup,
                addInsGroup,
                mediaGroup,
                linksGroup,
                headerFooterGroup,
                textInsertGroup,
                symbolsGroup
            });
        builder.AddTab("draw", "Draw", keyTip: "D")
            .AddGroups(new[]
            {
                drawToolsGroup,
                drawConvertGroup,
                drawAddPenGroup
            });
        builder.AddTab("design", "Design", keyTip: "G")
            .AddGroups(new[]
            {
                themesGroup,
                documentFormattingGroup,
                pageBackgroundGroup
            });
        builder.AddTab("references", "References", keyTip: "R")
            .AddGroups(new[]
            {
                tocGroup,
                footnotesGroup,
                citationsGroup,
                captionsGroup,
                referencesLinksGroup,
                fieldsGroup,
                indexGroup,
                tableAuthoritiesGroup
            });
        builder.AddTab("layout", "Layout", keyTip: "L")
            .AddGroups(new[] { pageSetupGroup, layoutParagraphGroup, arrangeGroup });
        builder.AddTab("mailings", "Mailings", keyTip: "M")
            .AddGroups(new[]
            {
                mailingsCreateGroup,
                mailingsStartGroup,
                mailingsWriteGroup,
                mailingsPreviewGroup,
                mailingsFinishGroup
            });
        builder.AddTab("review", "Review", keyTip: "E")
            .AddGroups(new[]
            {
                proofingGroup,
                speechGroup,
                accessibilityGroup,
                languageGroup,
                commentsGroup,
                trackingGroup,
                changesGroup,
                compareGroup,
                protectGroup
            });
        builder.AddTab("view", "View", keyTip: "V")
            .AddGroups(new[]
            {
                viewsGroup,
                sourceGroup,
                showGroup,
                pageMovementGroup,
                zoomGroup,
                windowGroup,
                macrosGroup,
                vbaToolsGroup,
                debugGroup,
                textGroup
            });

        var textContextualSet = new RibbonContextualTabSet(
            "text-tools",
            "Text Tools",
            IsTextContextActive,
            accentKey: "Text");
        builder.AddContextualSet(textContextualSet);

        builder.AddTab("text-tools-text", "Text", keyTip: "TX", contextualSet: textContextualSet)
            .AddGroups(new[]
            {
                BuildClipboardGroup(),
                BuildFontGroup(),
                BuildStylesGroup(),
                BuildEditingGroup()
            });
        builder.AddTab("text-tools-paragraph", "Paragraph", keyTip: "PG", contextualSet: textContextualSet)
            .AddGroup(BuildParagraphGroup());
        builder.AddTab("text-tools-layout", "Layout", keyTip: "LY", contextualSet: textContextualSet)
            .AddGroups(new[]
            {
                BuildPageSetupGroup(),
                BuildLayoutParagraphGroup(),
                BuildArrangeGroup()
            });

        builder.AddQuickAccess(undoButton);
        builder.AddQuickAccess(redoButton);
        builder.AddQuickAccess(newButton);
        builder.AddQuickAccess(openButton);
        builder.AddQuickAccess(saveSplit);

        object? ResolveService(Type serviceType)
        {
            if (_editorView is null)
            {
                return null;
            }

            return _editorView.TryGetService(serviceType, out var service) ? service : null;
        }

        ValueTask RefreshEquationLayoutAsync()
        {
            _editorView?.RefreshLayout();
            return ValueTask.CompletedTask;
        }

        var extensions = new IRibbonExtension[]
        {
            new EquationRibbonExtension(
                () => _editorView?.SelectedEquation is not null,
                RefreshEquationLayoutAsync,
                canInteract),
            new HeaderFooterRibbonExtension(canInteract),
            new TableRibbonExtension(OpenTablePropertiesDialogAsync)
        };

        var extensionContext = new RibbonExtensionContext(ResolveService);
        builder.ApplyExtensions(extensions, extensionContext);

        return builder.Build();
    }

    private bool TryGetRibbonSnapshot(out RibbonContextSnapshot snapshot)
    {
        if (_isLoading || _editorView is null)
        {
            snapshot = default;
            return false;
        }

        if (_editorView.TryGetService<IRibbonContextSnapshotProvider>(out var provider))
        {
            snapshot = provider.GetSnapshot();
            return true;
        }

        snapshot = default;
        return false;
    }

    private List<RibbonGalleryItem> BuildStyleItems()
    {
        var list = new List<RibbonGalleryItem>();
        IStyleService? styleService = null;
        IStyleManagerService? styleManager = null;
        if (_editorView is not null && _editorView.TryGetService<IStyleService>(out var resolvedService))
        {
            styleService = resolvedService;
        }
        if (_editorView is not null && _editorView.TryGetService<IStyleManagerService>(out var resolvedManager))
        {
            styleManager = resolvedManager;
        }

        static string? ResolveFontFamily(TextStyleProperties properties)
        {
            return FirstNonEmpty(
                properties.FontFamily,
                properties.FontFamilyAscii,
                properties.FontFamilyHighAnsi,
                properties.FontFamilyEastAsia,
                properties.FontFamilyComplexScript);
        }

        static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        RibbonTextPreview BuildPreview(string styleId, string name)
        {
            var previewText = string.IsNullOrWhiteSpace(name) ? styleId : name;
            if (styleService is null)
            {
                return new RibbonTextPreview(previewText);
            }

            var definition = styleService.GetParagraphStyle(styleId);
            if (definition is null)
            {
                return new RibbonTextPreview(previewText);
            }

            var run = definition.RunProperties;
            var fontFamily = ResolveFontFamily(run);
            var fontSize = run.FontSize ?? run.FontSizeComplexScript;
            var bold = run.FontWeight.HasValue ? run.FontWeight.Value == DocFontWeight.Bold : (bool?)null;
            var italic = run.FontStyle.HasValue ? run.FontStyle.Value == DocFontStyle.Italic : (bool?)null;
            var underline = run.Underline;
            if (!underline.HasValue && run.UnderlineStyle.HasValue)
            {
                underline = run.UnderlineStyle.Value != DocUnderlineStyle.None;
            }

            var paragraph = definition.ParagraphProperties;
            var lineSpacing = paragraph.LineSpacing.HasValue
                ? paragraph.LineSpacing.Value / 240f
                : (float?)null;
            RibbonParagraphSpacingPreview? spacing = null;
            if (paragraph.SpacingBefore.HasValue || paragraph.SpacingAfter.HasValue || lineSpacing.HasValue)
            {
                spacing = new RibbonParagraphSpacingPreview(paragraph.SpacingBefore, paragraph.SpacingAfter, lineSpacing);
            }

            return new RibbonTextPreview(
                previewText,
                fontFamily,
                fontSize,
                bold,
                italic,
                underline,
                run.Color,
                run.HighlightColor,
                paragraph.ShadingColor,
                spacing);
        }

        if (styleManager is not null)
        {
            var paragraphStyles = styleManager.GetStyles(EditorStyleType.Paragraph);
            var quickStyles = new List<EditorStyleInfo>(paragraphStyles.Count);
            foreach (var style in paragraphStyles)
            {
                if (style.IsQuickStyle)
                {
                    quickStyles.Add(style);
                }
            }

            if (quickStyles.Count == 0)
            {
                quickStyles.AddRange(paragraphStyles);
            }

            quickStyles.Sort(CompareQuickStyleInfo);
            foreach (var style in quickStyles)
            {
                list.Add(new RibbonGalleryItem(style.Id, style.Name, BuildPreview(style.Id, style.Name)));
            }
        }
        else if (TryGetRibbonSnapshot(out var snapshot))
        {
            foreach (var style in snapshot.ParagraphStyles)
            {
                list.Add(new RibbonGalleryItem(style.Id, style.Name, BuildPreview(style.Id, style.Name)));
            }
        }

        if (list.Count == 0)
        {
            list.Add(new RibbonGalleryItem("style-normal", "Normal", new RibbonTextPreview("Normal"), isEnabled: false));
            list.Add(new RibbonGalleryItem("style-no-spacing", "No Spacing", new RibbonTextPreview("No Spacing"), isEnabled: false));
            list.Add(new RibbonGalleryItem("style-heading-1", "Heading 1", new RibbonTextPreview("Heading 1"), isEnabled: false));
            list.Add(new RibbonGalleryItem("style-heading-2", "Heading 2", new RibbonTextPreview("Heading 2"), isEnabled: false));
            list.Add(new RibbonGalleryItem("style-title", "Title", new RibbonTextPreview("Title"), isEnabled: false));
        }

        return list;
    }

    private static int CompareQuickStyleInfo(EditorStyleInfo left, EditorStyleInfo right)
    {
        var leftPriority = left.UiPriority ?? int.MaxValue;
        var rightPriority = right.UiPriority ?? int.MaxValue;
        if (leftPriority != rightPriority)
        {
            return leftPriority.CompareTo(rightPriority);
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshStyleGalleryItems()
    {
        using var _ = _ribbon?.BeginStateUpdateScope();
        var items = BuildStyleItems();
        _styleGalleryItems.Clear();
        _styleGalleryItemMap.Clear();
        foreach (var item in items)
        {
            _styleGalleryItems.Add(item);
            _styleGalleryItemMap[item.Id] = item;
        }
    }

    private void AttachStyleManagerEvents()
    {
        if (_editorView is null)
        {
            DetachStyleManagerEvents();
            return;
        }

        if (!_editorView.TryGetService<IStyleManagerService>(out var styleManager))
        {
            DetachStyleManagerEvents();
            return;
        }

        if (ReferenceEquals(_styleManagerService, styleManager))
        {
            return;
        }

        if (_styleManagerService is not null)
        {
            _styleManagerService.StylesChanged -= OnStylesChanged;
        }

        _styleManagerService = styleManager;
        _styleManagerService.StylesChanged += OnStylesChanged;
    }

    private void AttachProofingService()
    {
        if (_editorView is null)
        {
            DetachProofingService();
            return;
        }

        if (!_editorView.TryGetService<IProofingService>(out var proofing))
        {
            DetachProofingService();
            return;
        }

        if (ReferenceEquals(_proofingService, proofing))
        {
            return;
        }

        if (_proofingService is not null)
        {
            _proofingService.Updated -= OnProofingUpdated;
        }

        _proofingService = proofing;
        _proofingService.Updated += OnProofingUpdated;
    }

    private void DetachProofingService()
    {
        if (_proofingService is null)
        {
            return;
        }

        _proofingService.Updated -= OnProofingUpdated;
        _proofingService = null;
    }

    private void DetachStyleManagerEvents()
    {
        if (_styleManagerService is null)
        {
            return;
        }

        _styleManagerService.StylesChanged -= OnStylesChanged;
        _styleManagerService = null;
    }

    private void OnProofingUpdated(object? sender, ProofingUpdatedEventArgs e)
    {
        if (_proofingRefreshPending)
        {
            return;
        }

        _proofingRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _proofingRefreshPending = false;
            RefreshReviewPaneItems();
        });
    }

    private void OnStylesChanged(object? sender, EventArgs e)
    {
        if (_pendingStyleGalleryRefresh)
        {
            return;
        }

        _pendingStyleGalleryRefresh = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pendingStyleGalleryRefresh = false;
            RefreshStyleGalleryItems();
            _ribbon?.RefreshState();
        });
    }

    private async void OnQuickAccessCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressQuickAccessSave || _ribbon?.Model is null)
        {
            return;
        }

        await SaveQuickAccessAsync(_ribbon.Model);
    }

    private async void OnCustomizeQuickAccessRequested(object? sender, EventArgs e)
    {
        if (_ribbon?.Model is not { } model)
        {
            return;
        }

        var candidates = BuildQuickAccessCandidates(model);
        var dialog = new RibbonQuickAccessDialog(candidates);
        var selection = await ShowDialogAsync<IReadOnlyList<string>?>(dialog);
        if (selection is null)
        {
            return;
        }

        ApplyQuickAccessLayout(model, selection);
        await SaveQuickAccessAsync(model);
    }

    private async Task RestoreQuickAccessAsync(RibbonModel model)
    {
        var layout = await _quickAccessStore.LoadAsync();
        if (!layout.HasValue)
        {
            return;
        }

        ApplyQuickAccessLayout(model, layout.ControlIds);
    }

    private async Task SaveQuickAccessAsync(RibbonModel model)
    {
        try
        {
            var controlIds = model.QuickAccess.Select(item => item.Control.Id);
            await _quickAccessStore.SaveAsync(controlIds);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save Quick Access Toolbar layout: {ex.Message}");
        }
    }

    private void ApplyQuickAccessLayout(RibbonModel model, IReadOnlyList<string> controlIds)
    {
        var controlMap = BuildControlMap(model);
        _suppressQuickAccessSave = true;
        try
        {
            model.QuickAccess.Clear();
            foreach (var controlId in controlIds)
            {
                if (controlMap.TryGetValue(controlId, out var control))
                {
                    model.AddQuickAccess(control);
                }
            }

            model.RefreshState();
        }
        finally
        {
            _suppressQuickAccessSave = false;
        }
    }

    private static Dictionary<string, IRibbonControl> BuildControlMap(RibbonModel model)
    {
        var map = new Dictionary<string, IRibbonControl>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in EnumerateRibbonControls(model))
        {
            map.TryAdd(entry.Control.Id, entry.Control);
        }

        return map;
    }

    private static IReadOnlyList<RibbonQuickAccessCandidate> BuildQuickAccessCandidates(RibbonModel model)
    {
        var selected = new HashSet<string>(
            model.QuickAccess.Select(item => item.Control.Id),
            StringComparer.OrdinalIgnoreCase);

        var list = new List<RibbonQuickAccessCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in EnumerateRibbonControls(model))
        {
            if (!seen.Add(entry.Control.Id))
            {
                continue;
            }

            list.Add(new RibbonQuickAccessCandidate(
                entry.Control,
                entry.Tab.Header,
                entry.Group.Header,
                selected.Contains(entry.Control.Id)));
        }

        return list;
    }

    private static IEnumerable<(RibbonTab Tab, RibbonGroup Group, IRibbonControl Control)> EnumerateRibbonControls(RibbonModel model)
    {
        foreach (var tab in model.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                {
                    if (control is RibbonToggleGroup toggleGroup)
                    {
                        foreach (var item in toggleGroup.Items)
                        {
                            yield return (tab, group, item);
                        }

                        continue;
                    }

                    yield return (tab, group, control);
                }
            }
        }
    }

    private static RibbonCommand CreateAsyncCommand(Func<Task> action, Func<bool>? canExecute = null)
    {
        return new RibbonCommand(() => new ValueTask(action()), canExecute);
    }

    private static string NormalizeSelectionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var span = text.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(span.Length);
        var previousWasSpace = false;
        foreach (var ch in span)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    private static bool IsLikelyHyperlinkAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return false;
        }

        if (span.IndexOfAny(" \t\r\n".AsSpan()) >= 0)
        {
            return false;
        }

        var candidate = span.ToString();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "mailto" or "file" or "ftp";
        }

        if (Uri.TryCreate(candidate, UriKind.Relative, out _))
        {
            return true;
        }

        return span.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || span.Contains('@');
    }

    private async ValueTask ToggleShowInvisibles(bool value)
    {
        if (_editorView is null)
        {
            return;
        }

        if (_editorView.TryGetService<IEditorCommandRouter>(out var router))
        {
            if (_editorView.TryGetService<IRibbonContextSnapshotProvider>(out var snapshotProvider))
            {
                var snapshot = snapshotProvider.GetSnapshot();
                await router.ExecuteAsync(EditorHomeCommandIds.Paragraph.ShowInvisiblesToggle, value, snapshot);
                return;
            }

            await router.ExecuteAsync(EditorHomeCommandIds.Paragraph.ShowInvisiblesToggle, value);
            return;
        }

        if (_editorView.TryGetService<IEditorViewOptionsService>(out var service))
        {
            service.ShowInvisibles = value;
        }
    }

    private ValueTask ToggleUseHarfBuzz(bool value)
    {
        if (_editorView is null)
        {
            return ValueTask.CompletedTask;
        }

        _editorView.UseHarfBuzz = value;
        return ValueTask.CompletedTask;
    }

    private ValueTask ToggleUsePictureCache(bool value)
    {
        if (_editorView is null)
        {
            return ValueTask.CompletedTask;
        }

        _editorView.UsePictureCache = value;
        return ValueTask.CompletedTask;
    }

    private bool CanInteract()
    {
        return !_isLoading && _editorView is not null;
    }

    private async Task OpenMacroManagerAsync()
    {
        if (!CanInteract() || _editorView is null)
        {
            return;
        }

        if (!_editorView.TryGetService<IMacroEngine>(out var macroEngine))
        {
            return;
        }

        if (!_editorView.TryGetService<IEditorCommandRouter>(out var router))
        {
            return;
        }

        var dialog = new MacroManagerDialog(
            _editorView.Document,
            macroEngine,
            router,
            () => TryGetRibbonSnapshot(out var snapshotValue) ? snapshotValue : null);
        await ShowDialogAsync(dialog);
    }

    private async Task ToggleMacroRecordingFromServiceAsync()
    {
        if (!CanInteract() || _editorView is null)
        {
            return;
        }

        if (!_editorView.TryGetService<IMacroEngine>(out var macroEngine))
        {
            return;
        }

        if (macroEngine.IsRecording)
        {
            macroEngine.StopRecording(save: true);
            _ribbon?.RefreshState();
            return;
        }

        var dialog = new TextInputDialog("Record Macro", "Macro name:", "Macro");
        var name = await ShowDialogAsync<string?>(dialog);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (macroEngine.StartRecording(name))
        {
            _ribbon?.RefreshState();
        }
    }

    private async Task OpenVbaToolingWindowFromServiceAsync()
    {
        if (!CanInteract() || _editorView is null)
        {
            return;
        }

        if (!_editorView.TryGetService<IMacroEngine>(out var macroEngine))
        {
            return;
        }

        if (!_editorView.TryGetService<IEditorCommandRouter>(out var router))
        {
            return;
        }

        if (_vbaToolingWindow is null)
        {
            _vbaToolingWindow = new VbaToolingWindow(
                _editorView.Document,
                macroEngine,
                router,
                () => TryGetRibbonSnapshot(out var snapshotValue) ? snapshotValue : null);
            _vbaToolingWindow.Closed += (_, _) => _vbaToolingWindow = null;
        }
        else
        {
            _vbaToolingWindow.UpdateContext(
                _editorView.Document,
                macroEngine,
                router,
                () => TryGetRibbonSnapshot(out var snapshotValue) ? snapshotValue : null);
        }

        ShowOwnedWindow(_vbaToolingWindow);
        _vbaToolingWindow.Activate();
    }

    private async Task StartMacroDebugFromServiceAsync()
    {
        await OpenVbaToolingWindowFromServiceAsync();
        if (_vbaToolingWindow is not null)
        {
            await _vbaToolingWindow.StartDebugAsync();
        }
    }

    private async Task OpenZoomDialogAsync()
    {
        if (_editorView is null)
        {
            return;
        }

        var items = new[]
        {
            new PickerItem("200", "200%"),
            new PickerItem("150", "150%"),
            new PickerItem("125", "125%"),
            new PickerItem("100", "100%"),
            new PickerItem("75", "75%"),
            new PickerItem("50", "50%"),
            new PickerItem("pageWidth", "Page Width"),
            new PickerItem("wholePage", "One Page"),
            new PickerItem("multiplePages", "Multiple Pages")
        };

        var dialog = new PickerDialog("Zoom", items);
        var result = await ShowDialogAsync<PickerItem?>(dialog);
        if (result is null)
        {
            return;
        }

        if (int.TryParse(result.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            _editorView.ZoomToPercent(percent);
            return;
        }

        switch (result.Id)
        {
            case "pageWidth":
                _editorView.ZoomToPageWidth();
                break;
            case "wholePage":
                _editorView.ZoomToWholePage();
                break;
            case "multiplePages":
                _editorView.ZoomToMultiplePages();
                break;
        }
    }

    private async Task LoadDocumentAsync(string path)
    {
        if (_editorView is null)
        {
            return;
        }

        _fixedLayoutWarningShown = false;
        _pdfDiagnosticsShown = false;
        PdfImportOptions? pdfImportOptions = null;
        if (IsPdfPath(path))
        {
            pdfImportOptions = await ShowPdfImportDialogAsync();
            if (pdfImportOptions is null)
            {
                return;
            }
        }

        SetLoadingState(true, $"Loading {Path.GetFileName(path)}...");
        var loaded = false;
        Document? loadedDocument = null;
        try
        {
            Document document;
            if (IsMarkdownPath(path))
            {
                var markdown = await File.ReadAllTextAsync(path);
                document = MarkdownDocumentConverter.FromMarkdown(markdown.AsSpan(), CreateMarkdownOptions());
            }
            else if (IsHtmlPath(path))
            {
                var html = await File.ReadAllTextAsync(path);
                document = HtmlDocumentConverter.FromHtml(html.AsSpan(), CreateHtmlOptions());
            }
            else if (IsPdfPath(path))
            {
                var options = pdfImportOptions ?? new PdfImportOptions();
                var pdfDocument = await LoadPdfAsync(path, options);
                if (pdfDocument is null)
                {
                    return;
                }

                document = PdfDocumentConverter.FromPdf(pdfDocument, options);
            }
            else
            {
                document = await Task.Run(() => new DocxImporter().Load(path));
            }
            await _editorView.LoadDocumentAsync(document);
            _currentPath = path;
            UpdateWindowTitle();
            loadedDocument = document;
            loaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load document: {ex.Message}");
        }
        finally
        {
            SetLoadingState(false);
        }

        if (loaded)
        {
            ApplyFormatProfile(path);
            RefreshStyleGalleryItems();
            AttachStyleManagerEvents();
            _ribbon?.RefreshState();
            UpdateOpenAuxiliaryWindows();
            if (loadedDocument is not null)
            {
                await UpdatePdfImportIndicatorsAsync(loadedDocument);
            }
        }
    }

    private async Task ReimportPdfAsync()
    {
        if (_editorView is null)
        {
            return;
        }

        var currentDocument = _editorView.Document;
        if (currentDocument is null)
        {
            return;
        }

        var options = await ShowPdfImportDialogAsync();
        if (options is null)
        {
            return;
        }

        byte[]? sourceBytes = null;
        string? sourcePath = null;
        if (PdfPreservationStore.TryRead(currentDocument, out var preserved) && preserved is not null)
        {
            sourceBytes = preserved.Bytes;
        }

        if (sourceBytes is null && !string.IsNullOrWhiteSpace(_currentPath) && IsPdfPath(_currentPath))
        {
            sourcePath = _currentPath;
        }

        if (sourceBytes is null && sourcePath is null)
        {
            var dialog = new MessageDialog("PDF Reimport Unavailable", "No original PDF source is available to re-import.");
            await ShowDialogAsync(dialog);
            return;
        }

        SetLoadingState(true, "Re-importing PDF...");
        var loaded = false;
        Document? loadedDocument = null;
        try
        {
            PdfDocumentAst pdfDocument;
            if (sourceBytes is not null)
            {
                using var stream = new MemoryStream(sourceBytes);
                pdfDocument = _pdfEngine.Parse(stream, options.ParserOptions);
            }
            else
            {
                pdfDocument = await LoadPdfAsync(sourcePath!, options) ?? throw new InvalidOperationException("Failed to reload PDF.");
            }

            var document = PdfDocumentConverter.FromPdf(pdfDocument, options);
            await _editorView.LoadDocumentAsync(document);
            loadedDocument = document;
            loaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to re-import PDF: {ex.Message}");
        }
        finally
        {
            SetLoadingState(false);
        }

        if (loaded)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                _currentPath = sourcePath;
            }

            UpdateWindowTitle();
            ApplyFormatProfile(_currentPath ?? string.Empty);
            RefreshStyleGalleryItems();
            AttachStyleManagerEvents();
            _ribbon?.RefreshState();
            UpdateOpenAuxiliaryWindows();
            if (loadedDocument is not null)
            {
                _fixedLayoutWarningShown = false;
                _pdfDiagnosticsShown = false;
                await UpdatePdfImportIndicatorsAsync(loadedDocument);
            }
        }
    }

    private void UpdateOpenAuxiliaryWindows()
    {
        if (_editorView is null)
        {
            return;
        }

        if (_notesPaneWindow is not null)
        {
            _notesPaneWindow.SetDocumentView(_editorView);
        }

        if (_htmlSourceWindow is not null)
        {
            _htmlSourceWindow.SetDocumentView(_editorView);
        }

        if (_vbaToolingWindow is null)
        {
            return;
        }

        if (_editorView.TryGetService<IMacroEngine>(out var macroEngine)
            && _editorView.TryGetService<IEditorCommandRouter>(out var router))
        {
            _vbaToolingWindow.UpdateContext(
                _editorView.Document,
                macroEngine,
                router,
                () => TryGetRibbonSnapshot(out var snapshotValue) ? snapshotValue : null);
        }
        else
        {
            _vbaToolingWindow.LoadDocument(_editorView.Document);
        }
    }

    private void SetLoadingState(bool isLoading, string? message = null)
    {
        _isLoading = isLoading;
        _editorView?.SetLoading(isLoading);
        if (_loadingOverlay is not null)
        {
            _loadingOverlay.IsVisible = isLoading;
            _loadingOverlay.IsHitTestVisible = isLoading;
        }

        if (_loadingText is not null && !string.IsNullOrWhiteSpace(message))
        {
            _loadingText.Text = message;
        }

        _ribbon?.RefreshState();
    }

    private void UpdateZoomUi()
    {
        if (_editorView is null)
        {
            return;
        }

        var zoomPercent = (int)MathF.Round(_editorView.ZoomFactor * 100f);
        _suppressZoomUpdate = true;
        if (_zoomSlider is not null)
        {
            _zoomSlider.Value = Math.Clamp(zoomPercent, (int)_zoomSlider.Minimum, (int)_zoomSlider.Maximum);
        }
        _suppressZoomUpdate = false;

        if (_zoomResetButton is not null)
        {
            _zoomResetButton.Content = $"{zoomPercent}%";
        }
    }

    private void UpdateStatusBar()
    {
        if (_editorView is null || _statusPageText is null)
        {
            return;
        }

        var layout = _editorView.Layout;
        var totalPages = Math.Max(1, layout.Pages.Count);
        var currentPage = ResolveCurrentPage(layout, _editorView.Caret);
        _statusPageText.Text = $"Page {currentPage} of {totalPages}";
    }

    private async Task UpdatePdfImportIndicatorsAsync(Document document)
    {
        if (TryResolvePdfImportMode(document, out var importMode))
        {
            if (_pdfFixedLayoutBadge is not null)
            {
                _pdfFixedLayoutBadge.IsVisible = true;
            }

            if (_pdfFixedLayoutText is not null)
            {
                _pdfFixedLayoutText.Text = importMode == PdfImportMode.FixedLayout ? "PDF Fixed Layout" : "PDF Reflow";
            }

            if (_pdfReimportButton is not null)
            {
                _pdfReimportButton.IsVisible = true;
            }

            if (importMode == PdfImportMode.FixedLayout && !_fixedLayoutWarningShown)
            {
                _fixedLayoutWarningShown = true;
                ShowNotification(
                    "Fixed Layout PDF",
                    "This PDF was imported in fixed layout mode. Editing is constrained to preserve the original page geometry.",
                    NotificationType.Information);
            }

            await ShowPdfDiagnosticsAsync(document);
            return;
        }

        ResetPdfIndicators();
    }

    private void ResetPdfIndicators()
    {
        if (_pdfFixedLayoutBadge is not null)
        {
            _pdfFixedLayoutBadge.IsVisible = false;
        }

        if (_pdfReimportButton is not null)
        {
            _pdfReimportButton.IsVisible = false;
        }
    }

    private Task ShowPdfDiagnosticsAsync(Document document)
    {
        if (_pdfDiagnosticsShown)
        {
            return Task.CompletedTask;
        }

        if (!PdfImportDiagnosticsStore.TryRead(document, out var diagnostics)
            || diagnostics is null
            || diagnostics.Issues.Count == 0)
        {
            _pdfDiagnosticsShown = true;
            return Task.CompletedTask;
        }

        _pdfDiagnosticsShown = true;
        var message = string.Join(Environment.NewLine, diagnostics.Issues.Select(issue => $"• {issue}"));
        ShowNotification("PDF Import Diagnostics", message, NotificationType.Warning, TimeSpan.FromSeconds(10));
        return Task.CompletedTask;
    }

    private static bool TryResolvePdfImportMode(Document document, out PdfImportMode importMode)
    {
        if (PdfPreservationStore.TryRead(document, out var preservedData) && preservedData?.Manifest is not null)
        {
            importMode = preservedData.Manifest.ImportMode;
            return true;
        }

        if (PdfImportMetadataStore.TryRead(document, out var metadata) && metadata is not null)
        {
            importMode = metadata.ImportMode;
            return true;
        }

        importMode = PdfImportMode.Reflow;
        return false;
    }

    private void RefreshNavigationPaneItems(bool force = false)
    {
        if (_editorView is null)
        {
            return;
        }

        if (!force && _navigationPane?.IsVisible != true)
        {
            return;
        }

        var layout = _editorView.Layout;
        var document = _editorView.Document;
        _navigationLayout = layout;
        _navigationDocument = document;

        var items = BuildNavigationItems(document);
        _navigationItems.Clear();
        foreach (var item in items)
        {
            _navigationItems.Add(item);
        }

        var pageCount = Math.Max(1, layout.Pages.Count);
        if (force || _pageItems.Count != pageCount)
        {
            RefreshPagePaneItems(layout);
        }
        else
        {
            UpdatePagePaneSelection(layout);
            if (_pagePaneList?.IsVisible == true)
            {
                UpdateCurrentPageThumbnail(layout);
            }
        }
    }

    private void RefreshPagePaneItems(DocumentLayout layout)
    {
        DisposePageThumbnails();
        var pageCount = Math.Max(1, layout.Pages.Count);
        _pageItems.Clear();
        for (var i = 0; i < pageCount; i++)
        {
            _pageItems.Add(new PageNavigationItem(i, CreatePageThumbnail(i)));
        }

        UpdatePagePaneSelection(layout);
    }

    private void UpdatePagePaneSelection(DocumentLayout layout)
    {
        if (_editorView is null || _pagePaneList is null || _pageItems.Count == 0)
        {
            return;
        }

        var currentPage = ResolveCurrentPage(layout, _editorView.Caret) - 1;
        if (currentPage < 0 || currentPage >= _pageItems.Count)
        {
            return;
        }

        _suppressPageSelection = true;
        _pagePaneList.SelectedItem = _pageItems[currentPage];
        _suppressPageSelection = false;
    }

    private void UpdateCurrentPageThumbnail(DocumentLayout layout)
    {
        if (_editorView is null || _pageItems.Count == 0)
        {
            return;
        }

        var currentPage = ResolveCurrentPage(layout, _editorView.Caret) - 1;
        if (currentPage < 0 || currentPage >= _pageItems.Count)
        {
            return;
        }

        var thumbnail = CreatePageThumbnail(currentPage);
        if (thumbnail is null)
        {
            return;
        }

        _pageItems[currentPage].SetThumbnail(thumbnail);
    }
    private Bitmap? CreatePageThumbnail(int pageIndex)
    {
        if (_editorView is null)
        {
            return null;
        }

        return _editorView.TryCreatePageThumbnail(pageIndex, PageThumbnailWidth, PageThumbnailHeight, out var bitmap)
            ? bitmap
            : null;
    }

    private void DisposePageThumbnails()
    {
        foreach (var item in _pageItems)
        {
            item.Dispose();
        }
    }

    private void OnNavigationSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_navigationPaneList?.SelectedItem is NavigationPaneItem item)
        {
            _editorView?.GoToParagraph(item.ParagraphIndex);
            _navigationPaneList.SelectedItem = null;
        }
    }

    private void OnPageSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressPageSelection)
        {
            return;
        }

        if (_pagePaneList?.SelectedItem is PageNavigationItem item)
        {
            _editorView?.ScrollToPage(item.PageIndex);
        }
    }

    private void SetReviewPaneVisible(bool visible)
    {
        if (_reviewPane is null)
        {
            return;
        }

        if (_reviewPane.IsVisible == visible)
        {
            return;
        }

        _reviewPane.IsVisible = visible;
        if (visible)
        {
            RefreshReviewPaneItems();
        }

        UpdateRightPaneVisibility();
    }

    private void UpdateRightPaneVisibility()
    {
        if (_layoutGrid is null || _layoutGrid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        var showPane = (_reviewPane?.IsVisible ?? false) || (_equationPanel?.IsVisible ?? false);
        if (_rightPane is not null)
        {
            _rightPane.IsVisible = showPane;
        }

        _layoutGrid.ColumnDefinitions[2].Width = showPane ? GridLength.Auto : new GridLength(0);
    }

    private void RefreshReviewPaneItems()
    {
        if (_editorView is null || _reviewPane?.IsVisible != true)
        {
            return;
        }

        var document = _editorView.Document;
        var caret = _editorView.Caret;
        var layout = _editorView.Layout;
        var caretCommentId = ResolveCommentSelectionId(document, layout, caret);
        var selectedCommentId = caretCommentId ?? (_reviewCommentsList?.SelectedItem as ReviewCommentItem)?.Id;
        var selectedRevision = _reviewChangesList?.SelectedItem as ReviewRevisionItem;

        var commentAnchors = ReviewingHelpers.BuildCommentAnchors(document);
        var commentItems = BuildReviewCommentItems(document, commentAnchors);

        _suppressReviewSelection = true;
        _reviewCommentItems.Clear();
        foreach (var item in commentItems)
        {
            _reviewCommentItems.Add(item);
        }

        if (_reviewCommentsList is not null)
        {
            _reviewCommentsList.SelectedItem = FindReviewCommentItem(selectedCommentId);
        }

        var revisionAnchors = ReviewingHelpers.BuildRevisionAnchors(document);
        revisionAnchors.Sort(CompareRevisionAnchors);
        var caretRevisionAnchor = ResolveRevisionAnchorAtCaret(revisionAnchors, caret);
        _reviewRevisionItems.Clear();
        foreach (var anchor in revisionAnchors)
        {
            var header = BuildReviewRevisionHeader(anchor.Revision, anchor.IsBlock);
            var preview = BuildReviewRevisionPreview(anchor.Revision, anchor.IsBlock);
            _reviewRevisionItems.Add(new ReviewRevisionItem(anchor.Revision, anchor.Anchor, anchor.IsBlock, header, preview));
        }

        if (_reviewChangesList is not null)
        {
            var revisionSelection = caretRevisionAnchor.HasValue
                ? FindReviewRevisionItem(caretRevisionAnchor.Value)
                : null;
            revisionSelection ??= FindReviewRevisionItem(selectedRevision);
            _reviewChangesList.SelectedItem = revisionSelection;
        }

        var proofingItems = BuildReviewProofingItems();
        var caretProofing = ResolveProofingSelection(caret);
        ReviewProofingItem? selectedProofing = null;
        if (caretProofing.HasValue)
        {
            selectedProofing = FindReviewProofingItem(proofingItems, caretProofing.Value);
        }

        selectedProofing ??= _reviewProofingList?.SelectedItem as ReviewProofingItem;
        _reviewProofingItems.Clear();
        foreach (var item in proofingItems)
        {
            _reviewProofingItems.Add(item);
        }

        if (_reviewProofingList is not null)
        {
            _reviewProofingList.SelectedItem = FindReviewProofingItem(selectedProofing);
        }

        _suppressReviewSelection = false;
        UpdateReviewCommentEditor(_reviewCommentsList?.SelectedItem as ReviewCommentItem, force: false);
        UpdateReviewPaneButtonState();
    }

    private static int? ResolveCommentSelectionId(Document document, DocumentLayout layout, TextPosition caret)
    {
        if (layout.CommentHighlightsByParagraph.TryGetValue(caret.ParagraphIndex, out var spans))
        {
            foreach (var span in spans)
            {
                if (caret.Offset >= span.StartOffset && caret.Offset <= span.EndOffset)
                {
                    return span.Id;
                }
            }
        }

        if (TryGetInlineAtPosition(document, caret, out var inline))
        {
            return inline switch
            {
                CommentRangeStartInline start => start.Id,
                CommentRangeEndInline end => end.Id,
                CommentReferenceInline reference => reference.Id,
                _ => null
            };
        }

        return null;
    }

    private static ReviewRevisionAnchor? ResolveRevisionAnchorAtCaret(
        IReadOnlyList<ReviewRevisionAnchor> anchors,
        TextPosition caret)
    {
        foreach (var anchor in anchors)
        {
            if (anchor.Anchor.Equals(caret))
            {
                return anchor;
            }
        }

        return null;
    }

    private static bool TryGetInlineAtPosition(Document document, TextPosition position, out Inline inline)
    {
        inline = null!;
        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= document.ParagraphCount)
        {
            return false;
        }

        var paragraph = document.GetParagraph(position.ParagraphIndex);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var offset = Math.Clamp(position.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        var current = 0;
        foreach (var item in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(item);
            var end = current + length;
            if (length == 0 && offset == current)
            {
                inline = item;
                return true;
            }

            if (offset < end)
            {
                inline = item;
                return true;
            }

            current = end;
        }

        return false;
    }

    private ReviewCommentItem? FindReviewCommentItem(int? id)
    {
        if (!id.HasValue)
        {
            return null;
        }

        foreach (var item in _reviewCommentItems)
        {
            if (item.Id == id.Value)
            {
                return item;
            }
        }

        return null;
    }

    private ReviewRevisionItem? FindReviewRevisionItem(ReviewRevisionItem? selected)
    {
        if (selected is null)
        {
            return null;
        }

        foreach (var item in _reviewRevisionItems)
        {
            if (item.Kind == selected.Kind && item.Id == selected.Id && item.Anchor.Equals(selected.Anchor))
            {
                return item;
            }
        }

        return null;
    }

    private ReviewRevisionItem? FindReviewRevisionItem(ReviewRevisionAnchor anchor)
    {
        foreach (var item in _reviewRevisionItems)
        {
            if (item.Kind == anchor.Revision.Kind
                && item.Id == anchor.Revision.Id
                && item.Anchor.Equals(anchor.Anchor))
            {
                return item;
            }
        }

        return null;
    }

    private List<ReviewCommentItem> BuildReviewCommentItems(
        Document document,
        IReadOnlyDictionary<int, TextPosition> anchors)
    {
        if (document.Comments.Count == 0)
        {
            return new List<ReviewCommentItem>();
        }

        var threads = new Dictionary<int, List<CommentDefinition>>();
        foreach (var comment in document.Comments.Values)
        {
            var threadId = CommentThreading.ResolveThreadId(comment, document.Comments);
            if (!threads.TryGetValue(threadId, out var threadComments))
            {
                threadComments = new List<CommentDefinition>();
                threads[threadId] = threadComments;
            }

            threadComments.Add(comment);
        }

        var threadItems = new List<ReviewCommentThread>(threads.Count);
        foreach (var pair in threads)
        {
            var root = ResolveThreadRoot(pair.Key, pair.Value, document);
            var anchor = anchors.TryGetValue(root.Id, out var anchorPosition)
                ? anchorPosition
                : new TextPosition(int.MaxValue, 0);
            threadItems.Add(new ReviewCommentThread(pair.Key, root, anchor, pair.Value));
        }

        threadItems.Sort(CompareReviewCommentThreads);

        var items = new List<ReviewCommentItem>(document.Comments.Count);
        foreach (var thread in threadItems)
        {
            var root = thread.Root;
            var rootText = ReviewingHelpers.BuildCommentText(root);
            var rootHeader = BuildReviewCommentHeader(root, isReply: false, isResolved: root.IsResolved);
            var rootPreview = BuildReviewPreview(rootText);
            items.Add(new ReviewCommentItem(
                root.Id,
                thread.ThreadId,
                rootHeader,
                rootPreview,
                rootText,
                thread.Anchor,
                root.IsResolved,
                new Thickness(0)));

            if (thread.Comments.Count == 1)
            {
                continue;
            }

            var replies = new List<CommentDefinition>();
            foreach (var comment in thread.Comments)
            {
                if (comment.Id != root.Id)
                {
                    replies.Add(comment);
                }
            }

            replies.Sort(CompareThreadCommentItems);

            foreach (var reply in replies)
            {
                var depth = Math.Max(1, CommentThreading.ResolveDepth(reply, document.Comments));
                var indent = new Thickness(depth * ReviewCommentReplyIndent, 0, 0, 0);
                var replyText = ReviewingHelpers.BuildCommentText(reply);
                var replyHeader = BuildReviewCommentHeader(reply, isReply: true, isResolved: false);
                var replyPreview = BuildReviewPreview(replyText);
                items.Add(new ReviewCommentItem(
                    reply.Id,
                    thread.ThreadId,
                    replyHeader,
                    replyPreview,
                    replyText,
                    thread.Anchor,
                    root.IsResolved,
                    indent));
            }
        }

        return items;
    }

    private List<ReviewProofingItem> BuildReviewProofingItems()
    {
        if (_editorView is null)
        {
            return new List<ReviewProofingItem>();
        }

        var proofing = _proofingService;
        if (proofing is null && !_editorView.TryGetService<IProofingService>(out proofing))
        {
            return new List<ReviewProofingItem>();
        }

        var document = _editorView.Document;
        if (document.ParagraphCount == 0)
        {
            return new List<ReviewProofingItem>();
        }

        var items = new List<ReviewProofingItem>();
        for (var i = 0; i < document.ParagraphCount; i++)
        {
            var diagnostics = proofing.GetParagraphDiagnostics(i);
            if (diagnostics.Count == 0)
            {
                continue;
            }

            foreach (var diagnostic in diagnostics)
            {
                var suggestions = diagnostic.Suggestions;
                if (suggestions is null && diagnostic.Kind == ProofingIssueKind.Spelling)
                {
                    suggestions = proofing.GetSuggestions(diagnostic, 3);
                }

                var header = $"{diagnostic.Kind}: {diagnostic.Message ?? diagnostic.Text}";
                var preview = diagnostic.Text;
                var suggestionPreview = BuildSuggestionPreview(suggestions);
                items.Add(new ReviewProofingItem(
                    diagnostic.ParagraphIndex,
                    diagnostic.StartOffset,
                    diagnostic.Length,
                    diagnostic.Kind,
                    header,
                    preview,
                    suggestionPreview,
                    suggestions ?? Array.Empty<string>()));
            }
        }

        items.Sort(static (left, right) =>
        {
            var cmp = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
            return cmp != 0 ? cmp : left.StartOffset.CompareTo(right.StartOffset);
        });
        return items;
    }

    private static string BuildSuggestionPreview(IReadOnlyList<string>? suggestions)
    {
        if (suggestions is null || suggestions.Count == 0)
        {
            return string.Empty;
        }

        var count = Math.Min(3, suggestions.Count);
        var preview = string.Join(", ", suggestions.Take(count));
        return $"Suggestions: {preview}";
    }

    private ProofingDiagnostic? ResolveProofingSelection(TextPosition caret)
    {
        if (_proofingService is null)
        {
            return null;
        }

        return _proofingService.TryGetDiagnosticAt(caret, out var diagnostic) ? diagnostic : null;
    }

    private static ReviewProofingItem? FindReviewProofingItem(
        IReadOnlyList<ReviewProofingItem> items,
        ProofingDiagnostic diagnostic)
    {
        foreach (var item in items)
        {
            if (item.Matches(diagnostic))
            {
                return item;
            }
        }

        return null;
    }

    private ReviewProofingItem? FindReviewProofingItem(ReviewProofingItem? selected)
    {
        if (selected is null)
        {
            return null;
        }

        foreach (var item in _reviewProofingItems)
        {
            if (item.Matches(selected))
            {
                return item;
            }
        }

        return null;
    }

    private static CommentDefinition ResolveThreadRoot(
        int threadId,
        IReadOnlyList<CommentDefinition> comments,
        Document document)
    {
        if (document.Comments.TryGetValue(threadId, out var root))
        {
            return root;
        }

        foreach (var comment in comments)
        {
            if (!comment.ParentId.HasValue)
            {
                return comment;
            }
        }

        return comments[0];
    }

    private static int CompareReviewCommentThreads(ReviewCommentThread left, ReviewCommentThread right)
    {
        var paragraphCompare = left.Anchor.ParagraphIndex.CompareTo(right.Anchor.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return left.Anchor.Offset.CompareTo(right.Anchor.Offset);
    }

    private static int CompareThreadCommentItems(CommentDefinition left, CommentDefinition right)
    {
        var leftDate = left.Date ?? DateTime.MinValue;
        var rightDate = right.Date ?? DateTime.MinValue;
        var dateCompare = leftDate.CompareTo(rightDate);
        if (dateCompare != 0)
        {
            return dateCompare;
        }

        return left.Id.CompareTo(right.Id);
    }

    private static int CompareRevisionAnchors(ReviewRevisionAnchor left, ReviewRevisionAnchor right)
    {
        var paragraphCompare = left.Anchor.ParagraphIndex.CompareTo(right.Anchor.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return left.Anchor.Offset.CompareTo(right.Anchor.Offset);
    }

    private static string BuildReviewCommentHeader(CommentDefinition comment, bool isReply, bool isResolved)
    {
        var author = string.IsNullOrWhiteSpace(comment.Author) ? $"Comment {comment.Id}" : comment.Author.Trim();
        var header = comment.Date.HasValue
            ? $"{author} ({comment.Date.Value.ToLocalTime():g})"
            : author;

        if (isReply)
        {
            header = $"Reply - {header}";
        }

        if (isResolved)
        {
            header = $"{header} [Resolved]";
        }

        return header;
    }

    private static string BuildReviewPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var span = text.AsSpan().Trim();
        var lineBreak = span.IndexOf('\n');
        if (lineBreak >= 0)
        {
            span = span[..lineBreak];
        }

        const int maxLength = 80;
        if (span.Length > maxLength)
        {
            return $"{new string(span[..maxLength])}...";
        }

        return new string(span);
    }

    private static string BuildReviewRevisionHeader(RevisionInfo revision, bool isBlock)
    {
        var kind = revision.Kind.ToString();
        var author = string.IsNullOrWhiteSpace(revision.Author) ? "Unknown" : revision.Author.Trim();
        var prefix = isBlock ? "Block" : "Inline";

        if (revision.Date.HasValue)
        {
            return $"{prefix} {kind} - {author} ({revision.Date.Value.ToLocalTime():g})";
        }

        return $"{prefix} {kind} - {author}";
    }

    private static string BuildReviewRevisionPreview(RevisionInfo revision, bool isBlock)
    {
        if (!string.IsNullOrWhiteSpace(revision.Name))
        {
            return revision.Name;
        }

        return isBlock ? "Block change" : "Inline change";
    }

    private void UpdateReviewCommentEditor(ReviewCommentItem? item, bool force)
    {
        if (_reviewCommentEditor is null)
        {
            return;
        }

        if (!force && _reviewCommentEditor.IsFocused)
        {
            return;
        }

        if (item is null)
        {
            _reviewCommentEditor.Text = string.Empty;
            _reviewCommentEditor.IsEnabled = false;
            return;
        }

        _reviewCommentEditor.Text = item.FullText;
        _reviewCommentEditor.IsEnabled = true;
    }

    private void UpdateReviewPaneButtonState()
    {
        var selectedComment = _reviewCommentsList?.SelectedItem as ReviewCommentItem;
        var hasCommentSelection = selectedComment is not null;
        if (_reviewCommentApplyButton is not null)
        {
            _reviewCommentApplyButton.IsEnabled = hasCommentSelection;
        }

        if (_reviewCommentReplyButton is not null)
        {
            _reviewCommentReplyButton.IsEnabled = hasCommentSelection;
        }

        if (_reviewCommentDeleteButton is not null)
        {
            _reviewCommentDeleteButton.IsEnabled = hasCommentSelection;
        }

        if (_reviewCommentResolveButton is not null)
        {
            _reviewCommentResolveButton.IsEnabled = hasCommentSelection;
            _reviewCommentResolveButton.Content = IsThreadResolved(selectedComment) ? "Reopen" : "Resolve";
        }

        var hasRevisions = _reviewRevisionItems.Count > 0;
        if (_reviewChangeAcceptButton is not null)
        {
            _reviewChangeAcceptButton.IsEnabled = hasRevisions;
        }

        if (_reviewChangeRejectButton is not null)
        {
            _reviewChangeRejectButton.IsEnabled = hasRevisions;
        }

        if (_reviewChangePreviousButton is not null)
        {
            _reviewChangePreviousButton.IsEnabled = hasRevisions;
        }

        if (_reviewChangeNextButton is not null)
        {
            _reviewChangeNextButton.IsEnabled = hasRevisions;
        }

        var selectedProofing = _reviewProofingList?.SelectedItem as ReviewProofingItem;
        var hasProofing = _reviewProofingItems.Count > 0;
        if (_reviewProofingPreviousButton is not null)
        {
            _reviewProofingPreviousButton.IsEnabled = hasProofing;
        }

        if (_reviewProofingNextButton is not null)
        {
            _reviewProofingNextButton.IsEnabled = hasProofing;
        }

        if (_reviewProofingApplyButton is not null)
        {
            _reviewProofingApplyButton.IsEnabled = selectedProofing?.HasSuggestion == true;
        }

        if (_reviewProofingIgnoreButton is not null)
        {
            _reviewProofingIgnoreButton.IsEnabled = selectedProofing is not null;
        }

        if (_reviewProofingAddButton is not null)
        {
            _reviewProofingAddButton.IsEnabled = selectedProofing is not null;
        }
    }

    private bool IsThreadResolved(ReviewCommentItem? item)
    {
        if (item is null)
        {
            return false;
        }

        if (_editorView?.Document is not { } document)
        {
            return item.IsResolved;
        }

        if (document.Comments.TryGetValue(item.ThreadId, out var root))
        {
            return root.IsResolved;
        }

        if (document.Comments.TryGetValue(item.Id, out var comment))
        {
            return CommentThreading.ResolveRootComment(comment, document.Comments).IsResolved;
        }

        return false;
    }

    private void OnReviewCommentSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressReviewSelection)
        {
            return;
        }

        if (_reviewCommentsList?.SelectedItem is ReviewCommentItem item)
        {
            _editorView?.GoToPosition(item.Anchor, ensureVisible: true);
            UpdateReviewCommentEditor(item, force: true);
            UpdateReviewPaneButtonState();
            return;
        }

        UpdateReviewCommentEditor(null, force: true);
        UpdateReviewPaneButtonState();
    }

    private void OnReviewChangeSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressReviewSelection)
        {
            return;
        }

        if (_reviewChangesList?.SelectedItem is ReviewRevisionItem item)
        {
            _editorView?.GoToPosition(item.Anchor, ensureVisible: true);
        }

        UpdateReviewPaneButtonState();
    }

    private void OnReviewProofingSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressReviewSelection)
        {
            return;
        }

        if (_reviewProofingList?.SelectedItem is ReviewProofingItem item)
        {
            _editorView?.SelectRange(item.Range, ensureVisible: true);
        }

        UpdateReviewPaneButtonState();
    }

    private void OnReviewProofingPreviousClicked(object? sender, RoutedEventArgs e)
    {
        SelectAdjacentProofingItem(-1);
    }

    private void OnReviewProofingNextClicked(object? sender, RoutedEventArgs e)
    {
        SelectAdjacentProofingItem(1);
    }

    private void SelectAdjacentProofingItem(int delta)
    {
        if (_reviewProofingItems.Count == 0 || _reviewProofingList is null)
        {
            return;
        }

        var index = _reviewProofingList.SelectedIndex;
        if (index < 0)
        {
            index = 0;
        }
        else
        {
            index = (index + delta + _reviewProofingItems.Count) % _reviewProofingItems.Count;
        }

        _reviewProofingList.SelectedIndex = index;
    }

    private void EnsureReviewChangeCaret()
    {
        if (_reviewChangesList?.SelectedItem is ReviewRevisionItem item)
        {
            _editorView?.GoToPosition(item.Anchor, ensureVisible: true);
        }
    }

    private void OnReviewCommentApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (_editorView is null || _reviewCommentEditor is null)
        {
            return;
        }

        if (_reviewCommentsList?.SelectedItem is not ReviewCommentItem item)
        {
            return;
        }

        if (!_editorView.Document.Comments.TryGetValue(item.Id, out var comment))
        {
            return;
        }

        ReviewingHelpers.UpdateCommentText(comment, _reviewCommentEditor.Text ?? string.Empty);
        _editorView.InvalidateVisual();
        RefreshReviewPaneItems();
    }

    private async void OnReviewCommentReplyClicked(object? sender, RoutedEventArgs e)
    {
        if (_reviewCommentsList?.SelectedItem is not ReviewCommentItem item)
        {
            return;
        }

        _editorView?.GoToPosition(item.Anchor, ensureVisible: true);
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Comments.ReplyComment, item.Id);
        RefreshReviewPaneItems();
    }

    private async void OnReviewCommentResolveClicked(object? sender, RoutedEventArgs e)
    {
        if (_reviewCommentsList?.SelectedItem is not ReviewCommentItem item)
        {
            return;
        }

        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Comments.ResolveComment, item.Id);
        RefreshReviewPaneItems();
    }

    private async void OnReviewCommentDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (_reviewCommentsList?.SelectedItem is not ReviewCommentItem item)
        {
            return;
        }

        _editorView?.GoToPosition(item.Anchor, ensureVisible: true);
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Comments.DeleteComment, item.Id);
        RefreshReviewPaneItems();
    }

    private async void OnReviewChangeAcceptClicked(object? sender, RoutedEventArgs e)
    {
        EnsureReviewChangeCaret();
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Changes.Accept);
    }

    private async void OnReviewChangeRejectClicked(object? sender, RoutedEventArgs e)
    {
        EnsureReviewChangeCaret();
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Changes.Reject);
    }

    private async void OnReviewChangePreviousClicked(object? sender, RoutedEventArgs e)
    {
        EnsureReviewChangeCaret();
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Changes.PreviousChange);
    }

    private async void OnReviewChangeNextClicked(object? sender, RoutedEventArgs e)
    {
        EnsureReviewChangeCaret();
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Changes.NextChange);
    }

    private async void OnReviewProofingApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (_reviewProofingList?.SelectedItem is not ReviewProofingItem item)
        {
            return;
        }

        if (!item.TryGetPrimarySuggestion(out var suggestion))
        {
            return;
        }

        _editorView?.SelectRange(item.Range, ensureVisible: true);
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Proofing.ApplySuggestion, suggestion);
        RefreshReviewPaneItems();
    }

    private async void OnReviewProofingIgnoreClicked(object? sender, RoutedEventArgs e)
    {
        if (_reviewProofingList?.SelectedItem is not ReviewProofingItem item)
        {
            return;
        }

        _editorView?.SelectRange(item.Range, ensureVisible: true);
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Proofing.IgnoreWord);
        RefreshReviewPaneItems();
    }

    private async void OnReviewProofingAddClicked(object? sender, RoutedEventArgs e)
    {
        if (_reviewProofingList?.SelectedItem is not ReviewProofingItem item)
        {
            return;
        }

        _editorView?.SelectRange(item.Range, ensureVisible: true);
        await ExecuteEditorCommandFromPaneAsync(EditorReviewCommandIds.Proofing.AddToDictionary);
        RefreshReviewPaneItems();
    }

    private async ValueTask ExecuteEditorCommandFromPaneAsync(string commandId, object? payload = null)
    {
        if (_editorView is null)
        {
            return;
        }

        if (!_editorView.TryGetService<IEditorCommandRouter>(out var router))
        {
            return;
        }

        if (_editorView.TryGetService<IRibbonContextSnapshotProvider>(out var provider))
        {
            await router.ExecuteAsync(commandId, payload, provider.GetSnapshot());
        }
        else
        {
            await router.ExecuteAsync(commandId, payload);
        }

        _ribbon?.RefreshState();
    }

    private static List<NavigationPaneItem> BuildNavigationItems(Document document)
    {
        var items = new List<NavigationPaneItem>();
        var tocDepth = 0;
        var paragraphIndex = 0;

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ContentControlStartBlock start when IsTocTag(start.Properties.Tag):
                    tocDepth++;
                    break;
                case ContentControlEndBlock end when tocDepth > 0:
                    tocDepth--;
                    break;
                case ParagraphBlock paragraph:
                    AppendNavigationItem(items, document, paragraph, paragraphIndex, tocDepth > 0);
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellParagraph in cell.Paragraphs)
                            {
                                AppendNavigationItem(items, document, cellParagraph, paragraphIndex, tocDepth > 0);
                                paragraphIndex++;
                            }
                        }
                    }

                    break;
            }
        }

        return items;
    }

    private static void AppendNavigationItem(
        List<NavigationPaneItem> items,
        Document document,
        ParagraphBlock paragraph,
        int paragraphIndex,
        bool insideToc)
    {
        if (insideToc)
        {
            return;
        }

        if (!TryGetHeadingLevel(document, paragraph, out var level))
        {
            return;
        }

        var text = DocumentEditHelpers.GetParagraphText(paragraph).Trim();
        if (text.Length == 0)
        {
            return;
        }

        items.Add(new NavigationPaneItem(text, level, paragraphIndex));
    }

    private static bool TryGetHeadingLevel(Document document, ParagraphBlock paragraph, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(paragraph.StyleId))
        {
            return false;
        }

        if (TryParseHeadingLevel(paragraph.StyleId, out level))
        {
            return true;
        }

        if (document.Styles.ParagraphStyles.TryGetValue(paragraph.StyleId, out var style))
        {
            return TryParseHeadingLevel(style.Name, out level);
        }

        return false;
    }

    private static bool TryParseHeadingLevel(string? value, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();
        if (!span.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "Heading".Length;
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }

        if (index >= span.Length || !char.IsDigit(span[index]))
        {
            return false;
        }

        var parsed = 0;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            parsed = (parsed * 10) + (span[index] - '0');
            index++;
        }

        if (parsed <= 0 || parsed > 9)
        {
            return false;
        }

        level = parsed;
        return true;
    }

    private static bool IsTocTag(string? tag)
    {
        return !string.IsNullOrWhiteSpace(tag)
               && tag.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveCurrentPage(DocumentLayout layout, TextPosition caret)
    {
        if (layout.Lines.Count == 0 || layout.Pages.Count == 0)
        {
            return 1;
        }

        var lineIndex = EditorSelectionService.FindLineIndexForPosition(layout, caret, out _);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        if (pageIndex < 0)
        {
            pageIndex = 0;
        }

        return pageIndex + 1;
    }

    private sealed class NavigationPaneItem
    {
        public string Title { get; }
        public int Level { get; }
        public int ParagraphIndex { get; }
        public string Display { get; }

        public NavigationPaneItem(string title, int level, int paragraphIndex)
        {
            Title = title;
            Level = Math.Clamp(level, 1, 9);
            ParagraphIndex = paragraphIndex;
            Display = $"{new string(' ', (Level - 1) * 2)}{Title}";
        }

        public override string ToString() => Display;
    }

    private sealed class PageNavigationItem : INotifyPropertyChanged, IDisposable
    {
        public int PageIndex { get; }
        public string Display { get; }
        private Bitmap? _thumbnail;
        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            private set
            {
                if (ReferenceEquals(_thumbnail, value))
                {
                    return;
                }

                _thumbnail?.Dispose();
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public PageNavigationItem(int pageIndex, Bitmap? thumbnail)
        {
            PageIndex = pageIndex;
            Display = $"Page {pageIndex + 1}";
            _thumbnail = thumbnail;
        }

        public void SetThumbnail(Bitmap? thumbnail)
        {
            Thumbnail = thumbnail;
        }

        public void Dispose()
        {
            _thumbnail?.Dispose();
            _thumbnail = null;
        }

        public override string ToString() => Display;
    }

    private sealed record QuickTableTemplate(
        string Label,
        int Rows,
        int Columns,
        string? StyleId,
        string[] CellText)
    {
        public static readonly QuickTableTemplate SimpleList = new(
            "Simple List",
            4,
            2,
            "LightShading",
            new[]
            {
                "Item",
                "Description",
                "Item 1",
                "Details",
                "Item 2",
                "Details",
                "Item 3",
                "Details"
            });

        public static readonly QuickTableTemplate Matrix = new(
            "Matrix",
            4,
            4,
            "TableGrid",
            new[]
            {
                string.Empty,
                "Column 1",
                "Column 2",
                "Column 3",
                "Row 1",
                string.Empty,
                string.Empty,
                string.Empty,
                "Row 2",
                string.Empty,
                string.Empty,
                string.Empty,
                "Row 3",
                string.Empty,
                string.Empty,
                string.Empty
            });

        public static readonly QuickTableTemplate WeekCalendar = new(
            "Calendar (Week)",
            2,
            7,
            "LightShading",
            new[]
            {
                "Mon",
                "Tue",
                "Wed",
                "Thu",
                "Fri",
                "Sat",
                "Sun",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty
            });
    }

    private sealed class ReviewCommentItem
    {
        public int Id { get; }
        public int ThreadId { get; }
        public string Header { get; }
        public string Preview { get; }
        public string FullText { get; }
        public TextPosition Anchor { get; }
        public bool IsResolved { get; }
        public Thickness Indent { get; }

        public ReviewCommentItem(
            int id,
            int threadId,
            string header,
            string preview,
            string fullText,
            TextPosition anchor,
            bool isResolved,
            Thickness indent)
        {
            Id = id;
            ThreadId = threadId;
            Header = header;
            Preview = preview;
            FullText = fullText;
            Anchor = anchor;
            IsResolved = isResolved;
            Indent = indent;
        }
    }

    private sealed class ReviewCommentThread
    {
        public int ThreadId { get; }
        public CommentDefinition Root { get; }
        public TextPosition Anchor { get; }
        public IReadOnlyList<CommentDefinition> Comments { get; }

        public ReviewCommentThread(
            int threadId,
            CommentDefinition root,
            TextPosition anchor,
            IReadOnlyList<CommentDefinition> comments)
        {
            ThreadId = threadId;
            Root = root;
            Anchor = anchor;
            Comments = comments;
        }
    }

    private sealed class ReviewRevisionItem
    {
        public RevisionKind Kind { get; }
        public int? Id { get; }
        public bool IsBlock { get; }
        public string Header { get; }
        public string Preview { get; }
        public TextPosition Anchor { get; }

        public ReviewRevisionItem(RevisionInfo revision, TextPosition anchor, bool isBlock, string header, string preview)
        {
            Kind = revision.Kind;
            Id = revision.Id;
            IsBlock = isBlock;
            Header = header;
            Preview = preview;
            Anchor = anchor;
        }
    }

    private sealed class ReviewProofingItem
    {
        public int ParagraphIndex { get; }
        public int StartOffset { get; }
        public int Length { get; }
        public ProofingIssueKind Kind { get; }
        public string Header { get; }
        public string Preview { get; }
        public string SuggestionPreview { get; }
        public IReadOnlyList<string> Suggestions { get; }
        public TextRange Range { get; }

        public bool HasSuggestion => Suggestions.Count > 0;

        public ReviewProofingItem(
            int paragraphIndex,
            int startOffset,
            int length,
            ProofingIssueKind kind,
            string header,
            string preview,
            string suggestionPreview,
            IReadOnlyList<string> suggestions)
        {
            ParagraphIndex = paragraphIndex;
            StartOffset = startOffset;
            Length = length;
            Kind = kind;
            Header = header;
            Preview = preview;
            SuggestionPreview = suggestionPreview;
            Suggestions = suggestions;
            Range = new TextRange(new TextPosition(paragraphIndex, startOffset), new TextPosition(paragraphIndex, startOffset + length));
        }

        public bool TryGetPrimarySuggestion(out string suggestion)
        {
            if (Suggestions.Count > 0)
            {
                suggestion = Suggestions[0];
                return true;
            }

            suggestion = string.Empty;
            return false;
        }

        public bool Matches(ReviewProofingItem other)
        {
            return ParagraphIndex == other.ParagraphIndex
                   && StartOffset == other.StartOffset
                   && Length == other.Length
                   && Kind == other.Kind;
        }

        public bool Matches(ProofingDiagnostic diagnostic)
        {
            return ParagraphIndex == diagnostic.ParagraphIndex
                   && StartOffset == diagnostic.StartOffset
                   && Length == diagnostic.Length
                   && Kind == diagnostic.Kind;
        }
    }
}
