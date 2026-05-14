using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ProEdit.Word.Avalonia;

public partial class WordEditorResources : ResourceDictionary
{
    public WordEditorResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
