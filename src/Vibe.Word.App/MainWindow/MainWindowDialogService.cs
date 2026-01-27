using Avalonia.Controls;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

internal sealed class MainWindowDialogService : IEditorDialogService
{
    private readonly Window _owner;

    public MainWindowDialogService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public ValueTask ShowMessageAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message);
        return new ValueTask(dialog.ShowDialog(_owner));
    }

    public async ValueTask<string?> PromptAsync(string title, string prompt, string? initialValue = null)
    {
        var dialog = new TextInputDialog(title, prompt, initialValue);
        return await dialog.ShowDialog<string?>(_owner);
    }
}
