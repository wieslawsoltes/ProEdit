using Avalonia.Controls;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

internal sealed class WordEditorDialogService : IEditorDialogService
{
    private readonly Func<Window?> _ownerProvider;

    public WordEditorDialogService(Func<Window?> ownerProvider)
    {
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
    }

    public ValueTask ShowMessageAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message);
        var owner = _ownerProvider();
        if (owner is null)
        {
            dialog.Show();
            return ValueTask.CompletedTask;
        }

        return new ValueTask(dialog.ShowDialog(owner));
    }

    public async ValueTask<string?> PromptAsync(string title, string prompt, string? initialValue = null)
    {
        var dialog = new TextInputDialog(title, prompt, initialValue);
        var owner = _ownerProvider();
        if (owner is null)
        {
            dialog.Show();
            return null;
        }

        return await dialog.ShowDialog<string?>(owner);
    }
}
