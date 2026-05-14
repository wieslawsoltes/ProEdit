using Avalonia.Controls;
using ProEdit.Documents;
using ProEdit.Word.Avalonia;

namespace ProEdit.Word.App;

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
