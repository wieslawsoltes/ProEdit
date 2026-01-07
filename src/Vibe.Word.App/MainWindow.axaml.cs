using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vibe.Office.Documents;
using Vibe.Office.OpenXml;

namespace Vibe.Word.App;

public partial class MainWindow : Window
{
    private readonly DocumentView? _editorView;
    private readonly Border? _loadingOverlay;
    private readonly TextBlock? _loadingText;
    private readonly Button? _openButton;
    private readonly Button? _saveButton;
    private string? _currentPath;
    private bool _isLoading;
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

        _editorView = this.FindControl<DocumentView>("EditorView");
        var equationEditor = this.FindControl<EquationEditor>("EquationEditor");
        var equationPanel = this.FindControl<Border>("EquationEditorPanel");
        _openButton = this.FindControl<Button>("OpenButton");
        _saveButton = this.FindControl<Button>("SaveButton");
        var invisiblesCheckBox = this.FindControl<CheckBox>("ShowInvisiblesCheckBox");
        var layoutCheckBox = this.FindControl<CheckBox>("ShowLayoutCheckBox");
        var harfBuzzCheckBox = this.FindControl<CheckBox>("UseHarfBuzzCheckBox");
        var pictureCacheCheckBox = this.FindControl<CheckBox>("UsePictureCacheCheckBox");
        _loadingOverlay = this.FindControl<Border>("LoadingOverlay");
        _loadingText = this.FindControl<TextBlock>("LoadingText");

        if (_openButton is not null)
        {
            _openButton.Click += OnOpenClicked;
        }

        if (_saveButton is not null)
        {
            _saveButton.Click += OnSaveClicked;
        }

        if (invisiblesCheckBox is not null)
        {
            invisiblesCheckBox.IsChecked = _editorView?.ShowInvisibles ?? false;
            invisiblesCheckBox.IsCheckedChanged += OnShowInvisiblesChanged;
        }

        if (layoutCheckBox is not null)
        {
            layoutCheckBox.IsChecked = _editorView?.ShowLayout ?? false;
            layoutCheckBox.IsCheckedChanged += OnShowLayoutChanged;
        }

        if (harfBuzzCheckBox is not null)
        {
            harfBuzzCheckBox.IsChecked = _editorView?.UseHarfBuzz ?? true;
            harfBuzzCheckBox.IsCheckedChanged += OnUseHarfBuzzChanged;
        }

        if (pictureCacheCheckBox is not null)
        {
            pictureCacheCheckBox.IsChecked = _editorView?.UsePictureCache ?? true;
            pictureCacheCheckBox.IsCheckedChanged += OnUsePictureCacheChanged;
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

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
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

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
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

    private void OnShowInvisiblesChanged(object? sender, RoutedEventArgs e)
    {
        if (_editorView is null)
        {
            return;
        }

        if (sender is CheckBox checkBox)
        {
            _editorView.ShowInvisibles = checkBox.IsChecked == true;
        }
    }

    private void OnShowLayoutChanged(object? sender, RoutedEventArgs e)
    {
        if (_editorView is null)
        {
            return;
        }

        if (sender is CheckBox checkBox)
        {
            _editorView.ShowLayout = checkBox.IsChecked == true;
        }
    }

    private void OnUseHarfBuzzChanged(object? sender, RoutedEventArgs e)
    {
        if (_editorView is null)
        {
            return;
        }

        if (sender is CheckBox checkBox)
        {
            _editorView.UseHarfBuzz = checkBox.IsChecked == true;
        }
    }

    private void OnUsePictureCacheChanged(object? sender, RoutedEventArgs e)
    {
        if (_editorView is null)
        {
            return;
        }

        if (sender is CheckBox checkBox)
        {
            _editorView.UsePictureCache = checkBox.IsChecked == true;
        }
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

        if (_openButton is not null)
        {
            _openButton.IsEnabled = !isLoading;
        }

        if (_saveButton is not null)
        {
            _saveButton.IsEnabled = !isLoading;
        }
    }
}
