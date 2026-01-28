using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Vibe.Word.Avalonia;

public partial class WordEditorResources : ResourceDictionary
{
    public WordEditorResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
