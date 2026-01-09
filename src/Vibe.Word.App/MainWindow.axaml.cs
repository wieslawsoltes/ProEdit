using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
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
    private readonly RibbonQuickAccessStore _quickAccessStore = new();
    private string? _currentPath;
    private bool _isLoading;
    private bool _suppressQuickAccessSave;
    private static readonly FilePickerFileType DocxFileType = new("Word Documents")
    {
        Patterns = new[] { "*.docx" }
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
            _editorView.EditorStateChanged += (_, _) => _ribbon?.RefreshState();
        }

        if (document is not null)
        {
            _editorView?.LoadDocument(document);
            _currentPath = path;
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

            return GetCommandRouter()?.CanExecute(commandId, payload) ?? false;
        }

        async ValueTask ExecuteEditorCommandAsync(string commandId, object? payload = null)
        {
            var router = GetCommandRouter();
            if (router is null)
            {
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

        bool IsFormatPainterActive()
        {
            if (_editorView is null)
            {
                return false;
            }

            return _editorView.TryGetService<IFormatPainterService>(out var service) && service.IsActive;
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

        List<RibbonGalleryItem> BuildStyleItems()
        {
            var list = new List<RibbonGalleryItem>();
            if (TryGetSnapshot(out var snapshot))
            {
                foreach (var style in snapshot.ParagraphStyles)
                {
                    list.Add(new RibbonGalleryItem(style.Id, style.Name));
                }
            }

            if (list.Count == 0)
            {
                list.Add(new RibbonGalleryItem("style-normal", "Normal", isEnabled: false));
                list.Add(new RibbonGalleryItem("style-no-spacing", "No Spacing", isEnabled: false));
                list.Add(new RibbonGalleryItem("style-heading-1", "Heading 1", isEnabled: false));
                list.Add(new RibbonGalleryItem("style-heading-2", "Heading 2", isEnabled: false));
                list.Add(new RibbonGalleryItem("style-title", "Title", isEnabled: false));
            }

            return list;
        }

        static RibbonColorItem? FindColorItem(IReadOnlyList<RibbonColorItem> palette, DocColor? color, RibbonColorKind? fallbackKind = null)
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
            if (item is null || item.Kind == RibbonColorKind.None)
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
                return FindColorItem(palette, value.Value, RibbonColorKind.Automatic);
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
                return FindColorItem(palette, value.Value, RibbonColorKind.None);
            }

            return FindColorItem(palette, null, RibbonColorKind.None);
        }

        List<RibbonColorItem> BuildFontColorPalette()
        {
            return new List<RibbonColorItem>
            {
                new RibbonColorItem("font-color-auto", "Automatic", RibbonColorKind.Automatic, DocColor.Black),
                new RibbonColorItem("font-color-black", "Black", RibbonColorKind.Rgb, new DocColor(0, 0, 0)),
                new RibbonColorItem("font-color-gray", "Gray", RibbonColorKind.Rgb, new DocColor(102, 102, 102)),
                new RibbonColorItem("font-color-red", "Red", RibbonColorKind.Rgb, new DocColor(192, 0, 0)),
                new RibbonColorItem("font-color-orange", "Orange", RibbonColorKind.Rgb, new DocColor(230, 145, 56)),
                new RibbonColorItem("font-color-yellow", "Yellow", RibbonColorKind.Rgb, new DocColor(241, 194, 50)),
                new RibbonColorItem("font-color-green", "Green", RibbonColorKind.Rgb, new DocColor(106, 168, 79)),
                new RibbonColorItem("font-color-teal", "Teal", RibbonColorKind.Rgb, new DocColor(69, 129, 142)),
                new RibbonColorItem("font-color-blue", "Blue", RibbonColorKind.Rgb, new DocColor(61, 133, 198)),
                new RibbonColorItem("font-color-purple", "Purple", RibbonColorKind.Rgb, new DocColor(142, 124, 195))
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
                new RibbonColorItem("highlight-dark-yellow", "Dark Yellow", RibbonColorKind.Rgb, new DocColor(128, 128, 0)),
                new RibbonColorItem("highlight-gray", "Gray", RibbonColorKind.Rgb, new DocColor(128, 128, 128))
            };
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

        var fileGroup = new RibbonGroup(
            "file",
            "File",
            new IRibbonControl[]
            {
                openButton,
                saveSplit
            });

        var fontFamilyItems = BuildFontFamilyItems();
        var fontSizeItems = BuildFontSizeItems();
        var fontColorPalette = BuildFontColorPalette();
        var highlightPalette = BuildHighlightPalette();
        var styleItems = BuildStyleItems();
        var styleItemMap = new Dictionary<string, RibbonGalleryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in styleItems)
        {
            styleItemMap[item.Id] = item;
        }

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

            return styleItemMap.TryGetValue(value.Value, out var item) ? item : null;
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
            });

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
            });

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
            () => _editorView?.ShowInvisibles ?? false,
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
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.LineSpacingOptions))
        });

        var lineSpacing = new RibbonDropdownButton(
            "para-line-spacing",
            "Line Spacing",
            lineSpacingMenu,
            keyTip: "LS",
            iconKey: "RibbonIcon.LineSpacing",
            size: RibbonControlSize.Small);

        var shadingMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "para-shading",
                "Shading",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.ShadingSet))
        });

        var shading = new RibbonDropdownButton(
            "para-shading",
            "Shading",
            shadingMenu,
            keyTip: "SD",
            iconKey: "RibbonIcon.Shading",
            size: RibbonControlSize.Small);

        var borderMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "para-border",
                "Borders",
                CreateEditorCommand(EditorHomeCommandIds.Paragraph.BorderSet))
        });

        var borders = new RibbonDropdownButton(
            "para-borders",
            "Borders",
            borderMenu,
            keyTip: "BR",
            iconKey: "RibbonIcon.Borders",
            size: RibbonControlSize.Small);

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
            });

        var stylesGallery = new RibbonGallery(
            "styles-gallery",
            "Styles",
            styleItems,
            selectedItemEvaluator: ResolveStyleSelection,
            selectionHandler: item => ExecuteEditorCommandAsync(EditorHomeCommandIds.Styles.Apply, item?.Id),
            keyTip: "SG",
            iconKey: "RibbonIcon.Styles",
            size: RibbonControlSize.Large);

        var stylesMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "styles-pane",
                "Styles Pane",
                CreateEditorCommand(EditorHomeCommandIds.Styles.OpenPane)),
            new RibbonMenuItem(
                "styles-manage",
                "Manage Styles",
                CreateEditorCommand(EditorHomeCommandIds.Styles.Manage))
        });

        var stylesMore = new RibbonDropdownButton(
            "styles-more",
            "Styles",
            stylesMenu,
            keyTip: "SM",
            iconKey: "RibbonIcon.Styles",
            size: RibbonControlSize.Small);

        var stylesGroup = new RibbonGroup(
            "styles",
            "Styles",
            new IRibbonControl[]
            {
                stylesGallery,
                stylesMore
            });

        var findButton = new RibbonButton(
            "edit-find",
            "Find",
            CreateEditorCommandWithPayload(EditorHomeCommandIds.Editing.Find, () => new EditorFindQuery(string.Empty)),
            keyTip: "FD",
            iconKey: "RibbonIcon.Find",
            size: RibbonControlSize.Small);

        var replaceButton = new RibbonButton(
            "edit-replace",
            "Replace",
            CreateEditorCommandWithPayload(EditorHomeCommandIds.Editing.Replace, () => new EditorReplaceQuery(string.Empty, string.Empty)),
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
            });

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
            () => _editorView?.ShowInvisibles ?? false,
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
            });

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
            });

        var builder = new RibbonModelBuilder();
        builder.AddTab("file", "File", keyTip: "F")
            .AddGroup(fileGroup);
        builder.AddTab("home", "Home", keyTip: "H")
            .AddGroups(new[] { clipboardGroup, fontGroup, paragraphGroup, stylesGroup, editingGroup });
        builder.AddTab("view", "View", keyTip: "V")
            .AddGroups(new[] { viewGroup, textGroup });

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

    private ValueTask ToggleShowInvisibles(bool value)
    {
        if (_editorView is null)
        {
            return ValueTask.CompletedTask;
        }

        _editorView.ShowInvisibles = value;
        return ValueTask.CompletedTask;
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
        try
        {
            var document = await Task.Run(() => new DocxImporter().Load(path));
            await _editorView.LoadDocumentAsync(document);
            _currentPath = path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load document: {ex.Message}");
        }
        finally
        {
            SetLoadingState(false);
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
}
