using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.OpenXml;
using Vibe.Office.Primitives;
using Vibe.Office.Ribbon;
using Vibe.Office.Ribbon.Avalonia;

namespace Vibe.Word.App;

public partial class MainWindow : Window
{
    private readonly DocumentView? _editorView;
    private readonly Border? _loadingOverlay;
    private readonly TextBlock? _loadingText;
    private readonly RibbonControl? _ribbon;
    private readonly TextBlock? _statusPageText;
    private readonly Slider? _zoomSlider;
    private readonly Button? _zoomInButton;
    private readonly Button? _zoomOutButton;
    private readonly Button? _zoomResetButton;
    private FindReplaceDialog? _findReplaceDialog;
    private readonly RibbonQuickAccessStore _quickAccessStore = new();
    private readonly ObservableCollection<RibbonGalleryItem> _styleGalleryItems = new();
    private readonly Dictionary<string, RibbonGalleryItem> _styleGalleryItemMap = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentPath;
    private bool _isLoading;
    private bool _suppressQuickAccessSave;
    private bool _suppressZoomUpdate;
    private static readonly FilePickerFileType DocxFileType = new("Word Documents")
    {
        Patterns = new[] { "*.docx" }
    };
    private static readonly FilePickerFileType ImageFileType = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.svg" }
    };

    public MainWindow()
        : this(null, null)
    {
    }

    public MainWindow(Document? document, string? path)
    {
        InitializeComponent();

        _ribbon = this.FindControl<RibbonControl>("Ribbon");
        _editorView = this.FindControl<DocumentView>("EditorView");
        var equationEditor = this.FindControl<EquationEditor>("EquationEditor");
        var equationPanel = this.FindControl<Border>("EquationEditorPanel");
        _loadingOverlay = this.FindControl<Border>("LoadingOverlay");
        _loadingText = this.FindControl<TextBlock>("LoadingText");
        _statusPageText = this.FindControl<TextBlock>("StatusPageText");
        _zoomSlider = this.FindControl<Slider>("ZoomSlider");
        _zoomInButton = this.FindControl<Button>("ZoomInButton");
        _zoomOutButton = this.FindControl<Button>("ZoomOutButton");
        _zoomResetButton = this.FindControl<Button>("ZoomResetButton");

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
            _editorView.RegisterService<IStylePaneService>(new StylesPaneService(
                this,
                () => _editorView.TryGetService<IStyleService>(out var service) ? service : null));
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
                if (equationPanel is not null)
                {
                    equationPanel.IsVisible = visible;
                }

                _ribbon?.RefreshState();
            }

            _editorView.SelectedEquationChanged += (_, equation) => UpdateEquationEditor(equation);
            equationEditor.EquationEdited += (_, _) => _editorView.RefreshLayout();
            UpdateEquationEditor(_editorView.SelectedEquation);
        }

        if (_editorView is not null)
        {
            _editorView.EditorStateChanged += (_, _) =>
            {
                _ribbon?.RefreshState();
                UpdateStatusBar();
            };
            _editorView.ZoomChanged += (_, _) => UpdateZoomUi();
            UpdateZoomUi();
            UpdateStatusBar();
        }

        if (document is not null)
        {
            _editorView?.LoadDocument(document);
            _currentPath = path;
            RefreshStyleGalleryItems();
            _ribbon?.RefreshState();
        }
        else if (!string.IsNullOrWhiteSpace(path))
        {
            Opened += async (_, _) => await LoadDocumentAsync(path);
        }
    }

    private async Task OpenDocumentAsync()
    {
        if (_isLoading)
        {
            return;
        }

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { DocxFileType }
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

    private async Task SaveDocumentAsync()
    {
        if (_editorView is null || _isLoading)
        {
            return;
        }

        var path = _currentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                DefaultExtension = "docx",
                FileTypeChoices = new[] { DocxFileType }
            });

            path = file?.TryGetLocalPath();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        new DocxExporter().Save(_editorView.Document, path);
        _currentPath = path;
    }

    private async Task SaveDocumentAsAsync()
    {
        var previousPath = _currentPath;
        _currentPath = null;
        await SaveDocumentAsync();
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            _currentPath = previousPath;
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

        async ValueTask ExecuteEditorCommandAsync(string commandId, object? payload = null)
        {
            var router = GetCommandRouter();
            if (router is null)
            {
                return;
            }

            if (TryGetSnapshot(out var snapshot))
            {
                await router.ExecuteAsync(commandId, payload, snapshot);
                return;
            }

            await router.ExecuteAsync(commandId, payload);
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
            _findReplaceDialog.Show(this);
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
            var result = await dialog.ShowDialog<EditorParagraphSpacingOptions?>(this);
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
            var result = await dialog.ShowDialog<EditorParagraphDialogOptions?>(this);
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
            var result = await dialog.ShowDialog<EditorFontDialogOptions?>(this);
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
            var result = await dialog.ShowDialog<EditorTableInsertRequest?>(this);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Tables.InsertTable, result.Value);
            }
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
            var result = await dialog.ShowDialog<EditorHyperlinkInsertRequest?>(this);
            if (result.HasValue)
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Links.Hyperlink, result.Value);
            }
        }

        async Task OpenHeaderFooterDialogAsync(bool editHeader)
        {
            if (!canInteract() || _editorView is null)
            {
                return;
            }

            var document = _editorView.Document;
            var target = editHeader ? document.Header : document.Footer;
            var text = ResolveHeaderFooterText(target);
            var title = editHeader ? "Edit Header" : "Edit Footer";
            var dialog = new HeaderFooterEditDialog(title, text);
            var result = await dialog.ShowDialog<string?>(this);
            if (result is not null)
            {
                var command = editHeader ? EditorInsertCommandIds.HeaderFooter.Header : EditorInsertCommandIds.HeaderFooter.Footer;
                await ExecuteEditorCommandAsync(command, result);
            }
        }

        async Task InsertPictureAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

        async Task OpenShapePickerDialogAsync()
        {
            if (!canInteract())
            {
                return;
            }

            var items = BuildShapePickerItems();
            var dialog = new PickerDialog("Insert Shape", items);
            var result = await dialog.ShowDialog<PickerItem?>(this);
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
            var result = await dialog.ShowDialog<PickerItem?>(this);
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
            var result = await dialog.ShowDialog<PickerItem?>(this);
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
            var result = await dialog.ShowDialog<PickerItem?>(this);
            if (result is not null && payloads.TryGetValue(result.Id, out var request))
            {
                await ExecuteEditorCommandAsync(EditorInsertCommandIds.Illustrations.Chart, request);
            }
        }

        static string ResolveHeaderFooterText(HeaderFooter headerFooter)
        {
            foreach (var block in headerFooter.Blocks)
            {
                if (block is ParagraphBlock paragraph)
                {
                    return DocumentEditHelpers.GetParagraphText(paragraph);
                }
            }

            return string.Empty;
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
                characterPositionPoints);
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
            return size.ToString("0.#", CultureInfo.InvariantCulture);
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

            await ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.SizeSet, size);
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

        var openCommand = CreateAsyncCommand(OpenDocumentAsync, canInteract);
        var saveCommand = CreateAsyncCommand(SaveDocumentAsync, canInteract);
        var saveAsCommand = CreateAsyncCommand(SaveDocumentAsAsync, canInteract);

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
                openButton,
                saveSplit
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
                CreateEditorCommand(EditorHomeCommandIds.Clipboard.PasteTextOnly))
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

        var formatPainter = new RibbonToggleButton(
            "format-painter",
            "Format Painter",
            IsFormatPainterActive,
            command: CreateEditorCommand(EditorHomeCommandIds.Clipboard.FormatPainterToggle),
            keyTip: "FP",
            iconKey: "RibbonIcon.FormatPainter",
            size: RibbonControlSize.Small);

        var clipboardGroup = new RibbonGroup(
            "clipboard",
            "Clipboard",
            new IRibbonControl[]
            {
                pasteSplit,
                cutButton,
                copyButton,
                formatPainter
            },
            keyTip: "CL");

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
            size: RibbonControlSize.Small);

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
            CreateEditorCommandWithPayload(EditorHomeCommandIds.Font.HighlightSet, () => ResolveColorPayload(ResolveHighlightSelection(highlightPalette))),
            highlightPalette,
            selectedColorEvaluator: () => ResolveHighlightSelection(highlightPalette),
            selectionHandler: color => ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.HighlightSet, ResolveColorPayload(color)),
            keyTip: "H",
            iconKey: "RibbonIcon.Highlight",
            size: RibbonControlSize.Small);

        var fontColorButton = new RibbonColorSplitButton(
            "font-color",
            "Font Color",
            CreateEditorCommandWithPayload(EditorHomeCommandIds.Font.ColorSet, () => ResolveColorPayload(ResolveFontColorSelection(fontColorPalette))),
            fontColorPalette,
            selectedColorEvaluator: () => ResolveFontColorSelection(fontColorPalette),
            selectionHandler: color => ExecuteEditorCommandAsync(EditorHomeCommandIds.Font.ColorSet, ResolveColorPayload(color)),
            keyTip: "FC",
            iconKey: "RibbonIcon.FontColor",
            size: RibbonControlSize.Small);

        var fontLauncher = new RibbonGroupLauncher(
            "font-launcher",
            "Font Dialog",
            CreateAsyncCommand(OpenFontDialogAsync, () => CanExecuteEditorCommand(EditorHomeCommandIds.Font.DialogApply)),
            keyTip: "FO",
            iconKey: "RibbonIcon.Launcher");

        var fontGroup = new RibbonGroup(
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
            size: RibbonControlSize.Small);

        var alignCenter = new RibbonToggleButton(
            "para-align-center",
            "Center",
            () => IsParagraphValue(snapshot => snapshot.Alignment, ParagraphAlignment.Center),
            command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.AlignCenter),
            keyTip: "AC",
            iconKey: "RibbonIcon.AlignCenter",
            size: RibbonControlSize.Small);

        var alignRight = new RibbonToggleButton(
            "para-align-right",
            "Align Right",
            () => IsParagraphValue(snapshot => snapshot.Alignment, ParagraphAlignment.Right),
            command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.AlignRight),
            keyTip: "AR",
            iconKey: "RibbonIcon.AlignRight",
            size: RibbonControlSize.Small);

        var alignJustify = new RibbonToggleButton(
            "para-align-justify",
            "Justify",
            () => IsParagraphValue(snapshot => snapshot.Alignment, ParagraphAlignment.Justify),
            command: CreateEditorCommand(EditorHomeCommandIds.Paragraph.AlignJustify),
            keyTip: "AJ",
            iconKey: "RibbonIcon.AlignJustify",
            size: RibbonControlSize.Small);

        var lineSpacingMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "line-spacing-1",
                "1.0",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.LineSpacingSet, EditorLineSpacingRequest.FromMultiple(1f))),
            new RibbonMenuItem(
                "line-spacing-1-15",
                "1.15",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.LineSpacingSet, EditorLineSpacingRequest.FromMultiple(1.15f))),
            new RibbonMenuItem(
                "line-spacing-1-5",
                "1.5",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.LineSpacingSet, EditorLineSpacingRequest.FromMultiple(1.5f))),
            new RibbonMenuItem(
                "line-spacing-2",
                "2.0",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.LineSpacingSet, EditorLineSpacingRequest.FromMultiple(2f))),
            new RibbonMenuItem(
                "line-spacing-options",
                "Line Spacing Options...",
                CreateAsyncCommand(OpenLineSpacingOptionsAsync, () => CanExecuteEditorCommand(EditorHomeCommandIds.Paragraph.LineSpacingOptions)))
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
            CreateEditorCommandWithPayload(EditorHomeCommandIds.Paragraph.ShadingSet, () => ResolveColorPayload(ResolveShadingSelection(shadingPalette))),
            shadingPalette,
            selectedColorEvaluator: () => ResolveShadingSelection(shadingPalette),
            selectionHandler: color => ExecuteEditorCommandAsync(EditorHomeCommandIds.Paragraph.ShadingSet, ResolveColorPayload(color)),
            keyTip: "SD",
            iconKey: "RibbonIcon.Shading",
            size: RibbonControlSize.Small);

        var borderMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "para-border-none",
                "No Border",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet, new EditorParagraphBorderRequest(EditorParagraphBorderKind.None))),
            new RibbonMenuItem(
                "para-border-all",
                "All Borders",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet, new EditorParagraphBorderRequest(EditorParagraphBorderKind.All))),
            new RibbonMenuItem(
                "para-border-outside",
                "Outside Borders",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet, new EditorParagraphBorderRequest(EditorParagraphBorderKind.Outside))),
            new RibbonMenuSeparator(),
            new RibbonMenuItem(
                "para-border-top",
                "Top Border",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet, new EditorParagraphBorderRequest(EditorParagraphBorderKind.Top))),
            new RibbonMenuItem(
                "para-border-bottom",
                "Bottom Border",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet, new EditorParagraphBorderRequest(EditorParagraphBorderKind.Bottom))),
            new RibbonMenuItem(
                "para-border-left",
                "Left Border",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet, new EditorParagraphBorderRequest(EditorParagraphBorderKind.Left))),
            new RibbonMenuItem(
                "para-border-right",
                "Right Border",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet, new EditorParagraphBorderRequest(EditorParagraphBorderKind.Right)))
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
            CreateAsyncCommand(OpenParagraphDialogAsync, () => CanExecuteEditorCommand(EditorHomeCommandIds.Paragraph.DialogApply)),
            keyTip: "PP",
            iconKey: "RibbonIcon.Launcher");

        var paragraphGroup = new RibbonGroup(
            "paragraph",
            "Paragraph",
            new IRibbonControl[]
            {
                bulletsToggle,
                numberingToggle,
                multilevelToggle,
                indentDecrease,
                indentIncrease,
                sortParagraph,
                showParagraphMarks,
                alignLeft,
                alignCenter,
                alignRight,
                alignJustify,
                lineSpacing,
                shading,
                borders
            },
            keyTip: "PG",
            launcher: paragraphLauncher);

        var stylesGallery = new RibbonGallery(
            "styles-gallery",
            "Styles",
            _styleGalleryItems,
            selectedItemEvaluator: ResolveStyleSelection,
            selectionHandler: item => ExecuteEditorCommandAsync(EditorHomeCommandIds.Styles.Apply, item?.Id),
            showDropDown: true,
            keyTip: "SG",
            iconKey: "RibbonIcon.Styles",
            size: RibbonControlSize.Large);

        var stylesLauncher = new RibbonGroupLauncher(
            "styles-launcher",
            "Styles Pane",
            CreateEditorCommand(EditorHomeCommandIds.Styles.OpenPane),
            iconKey: "RibbonIcon.Launcher");

        var stylesGroup = new RibbonGroup(
            "styles",
            "Styles",
            new IRibbonControl[]
            {
                stylesGallery
            },
            keyTip: "ST",
            launcher: stylesLauncher);

        var findButton = new RibbonButton(
            "edit-find",
            "Find",
            CreateAsyncCommand(() => ShowFindReplaceDialogAsync(false), CanUseFindReplace),
            keyTip: "FD",
            iconKey: "RibbonIcon.Find",
            size: RibbonControlSize.Small);

        var replaceButton = new RibbonButton(
            "edit-replace",
            "Replace",
            CreateAsyncCommand(() => ShowFindReplaceDialogAsync(true), CanUseFindReplace),
            keyTip: "RP",
            iconKey: "RibbonIcon.Replace",
            size: RibbonControlSize.Small);

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
            size: RibbonControlSize.Small);

        var editingGroup = new RibbonGroup(
            "editing",
            "Editing",
            new IRibbonControl[]
            {
                findButton,
                replaceButton,
                selectSplit
            },
            keyTip: "ED");

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

        var tableButton = new RibbonSplitButton(
            "insert-table",
            "Table",
            CreateAsyncCommand(OpenTableInsertDialogAsync, canInteract),
            tableMenu,
            keyTip: "TB",
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Large);

        var tablesGroup = new RibbonGroup(
            "insert-tables",
            "Tables",
            new IRibbonControl[]
            {
                tableButton
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
            CreateEditorCommand(EditorInsertCommandIds.Illustrations.Models3D),
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
            CreateEditorCommand(EditorInsertCommandIds.Illustrations.Screenshot),
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
            CreateEditorCommand(EditorInsertCommandIds.Links.CrossReference),
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
                CreateAsyncCommand(() => OpenHeaderFooterDialogAsync(true), canInteract),
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
                CreateAsyncCommand(() => OpenHeaderFooterDialogAsync(false), canInteract),
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

        var headerFooterGroup = new RibbonGroup(
            "insert-header-footer",
            "Header & Footer",
            new IRibbonControl[]
            {
                headerButton,
                footerButton,
                pageNumberButton
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
                CreateEditorCommand(EditorInsertCommandIds.Text.Object),
                iconKey: "RibbonIcon.Object"),
            new RibbonMenuItem(
                "object-new",
                "New Object",
                CreateEditorCommand(EditorInsertCommandIds.Text.Object),
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

        var showLayout = new RibbonToggleButton(
            "show-layout",
            "Show Layout",
            () => _editorView?.ShowLayout ?? false,
            value => ToggleShowLayout(value),
            iconKey: "RibbonIcon.Layout",
            canExecute: canInteract,
            size: RibbonControlSize.Large);

        var showInvisibles = new RibbonToggleButton(
            "show-invisibles",
            "Show Invisibles",
            IsShowInvisiblesActive,
            value => ToggleShowInvisibles(value),
            iconKey: "RibbonIcon.Invisibles",
            canExecute: canInteract,
            size: RibbonControlSize.Large);

        var viewGroup = new RibbonGroup(
            "view",
            "View",
            new IRibbonControl[]
            {
                showLayout,
                showInvisibles
            },
            keyTip: "VW");

        var zoomInButton = new RibbonButton(
            "zoom-in",
            "Zoom In",
            CreateViewCommand(() => _editorView?.ZoomIn()),
            keyTip: "ZI",
            iconKey: "RibbonIcon.ZoomIn",
            size: RibbonControlSize.Medium);

        var zoomOutButton = new RibbonButton(
            "zoom-out",
            "Zoom Out",
            CreateViewCommand(() => _editorView?.ZoomOut()),
            keyTip: "ZO",
            iconKey: "RibbonIcon.ZoomOut",
            size: RibbonControlSize.Medium);

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

        var zoomGroup = new RibbonGroup(
            "zoom",
            "Zoom",
            new IRibbonControl[]
            {
                zoomInButton,
                zoomOutButton,
                zoom100Button,
                zoomPageWidthButton,
                zoomWholePageButton
            },
            keyTip: "ZM");

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

        var builder = new RibbonModelBuilder();
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
                linksGroup,
                headerFooterGroup,
                textInsertGroup,
                symbolsGroup
            });
        builder.AddTab("view", "View", keyTip: "V")
            .AddGroups(new[] { viewGroup, zoomGroup, textGroup });

        builder.AddQuickAccess(undoButton);
        builder.AddQuickAccess(redoButton);
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
            new TableRibbonExtension()
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
        if (_editorView is not null && _editorView.TryGetService<IStyleService>(out var resolvedService))
        {
            styleService = resolvedService;
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

        RibbonTextPreview BuildPreview(EditorParagraphStyleInfo info)
        {
            var previewText = string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name;
            if (styleService is null)
            {
                return new RibbonTextPreview(previewText);
            }

            var definition = styleService.GetParagraphStyle(info.Id);
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

        if (TryGetRibbonSnapshot(out var snapshot))
        {
            foreach (var style in snapshot.ParagraphStyles)
            {
                list.Add(new RibbonGalleryItem(style.Id, style.Name, BuildPreview(style)));
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
        var selection = await dialog.ShowDialog<IReadOnlyList<string>?>(this);
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

    private ValueTask ToggleShowLayout(bool value)
    {
        if (_editorView is null)
        {
            return ValueTask.CompletedTask;
        }

        _editorView.ShowLayout = value;
        return ValueTask.CompletedTask;
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

    private async Task LoadDocumentAsync(string path)
    {
        if (_editorView is null)
        {
            return;
        }

        SetLoadingState(true, $"Loading {Path.GetFileName(path)}...");
        var loaded = false;
        try
        {
            var document = await Task.Run(() => new DocxImporter().Load(path));
            await _editorView.LoadDocumentAsync(document);
            _currentPath = path;
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
            RefreshStyleGalleryItems();
            _ribbon?.RefreshState();
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
}
