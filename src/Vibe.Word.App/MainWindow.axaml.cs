using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vibe.Office.Documents;
using Vibe.Office.OpenXml;
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

        var showInvisibles = new RibbonToggleButton(
            "show-invisibles",
            "Show Invisibles",
            () => _editorView?.ShowInvisibles ?? false,
            value => ToggleShowInvisibles(value),
            iconKey: "RibbonIcon.Invisibles",
            canExecute: canInteract,
            size: RibbonControlSize.Large);

        var showLayout = new RibbonToggleButton(
            "show-layout",
            "Show Layout",
            () => _editorView?.ShowLayout ?? false,
            value => ToggleShowLayout(value),
            iconKey: "RibbonIcon.Layout",
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

        var equationContext = new RibbonContextualTabSet(
            "equation-tools",
            "Equation Tools",
            () => _editorView?.SelectedEquation is not null,
            accentKey: "Equation");

        var equationGroup = new RibbonGroup(
            "equation",
            "Equation",
            new IRibbonControl[]
            {
                new RibbonButton(
                    "equation-refresh",
                    "Refresh Layout",
                    new RibbonCommand(() => _editorView?.RefreshLayout()),
                    iconKey: "RibbonIcon.Equation",
                    size: RibbonControlSize.Medium)
            });

        var homeTab = new RibbonTab("home", "Home", new[] { fileGroup }, keyTip: "H");
        var viewTab = new RibbonTab("view", "View", new[] { viewGroup, textGroup }, keyTip: "V");
        var equationTab = new RibbonTab("equation-design", "Design", new[] { equationGroup }, contextualSet: equationContext, keyTip: "E");

        return new RibbonModel(
            new[] { homeTab, viewTab, equationTab },
            new[]
            {
                new RibbonQuickAccessItem(openButton),
                new RibbonQuickAccessItem(saveSplit)
            },
            new[] { equationContext });
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
