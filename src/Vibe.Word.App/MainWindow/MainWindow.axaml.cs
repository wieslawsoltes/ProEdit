using Avalonia.Controls;
using Vibe.Office.Documents;
using Vibe.Word.Avalonia;

namespace Vibe.Word.App;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(null, null)
    {
    }

    public MainWindow(Document? document, string? path)
    {
        InitializeComponent();

        var editor = this.FindControl<WordEditorControl>("EditorControl");
        if (editor is null)
        {
            return;
        }

        editor.OwnerWindow = this;
        editor.WindowFactory = doc => new MainWindow(doc, null);
        editor.SetInitialDocument(document, path);
    }
}
