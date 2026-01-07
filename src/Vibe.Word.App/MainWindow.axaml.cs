using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vibe.Office.Documents;
using Vibe.Office.OpenXml;

namespace Vibe.Word.App;

public partial class MainWindow : Window
{
    private readonly DocumentView? _editorView;
    private string? _currentPath;
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
        var openButton = this.FindControl<Button>("OpenButton");
        var saveButton = this.FindControl<Button>("SaveButton");
        var invisiblesCheckBox = this.FindControl<CheckBox>("ShowInvisiblesCheckBox");
        var harfBuzzCheckBox = this.FindControl<CheckBox>("UseHarfBuzzCheckBox");
        var pictureCacheCheckBox = this.FindControl<CheckBox>("UsePictureCacheCheckBox");

        if (openButton is not null)
        {
            openButton.Click += OnOpenClicked;
        }

        if (saveButton is not null)
        {
            saveButton.Click += OnSaveClicked;
        }

        if (invisiblesCheckBox is not null)
        {
            invisiblesCheckBox.IsChecked = _editorView?.ShowInvisibles ?? false;
            invisiblesCheckBox.IsCheckedChanged += OnShowInvisiblesChanged;
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
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
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

        var document = new DocxImporter().Load(path);
        _editorView?.LoadDocument(document);
        _currentPath = path;
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_editorView is null)
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
}
