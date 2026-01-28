using System.Threading.Tasks;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

internal sealed class NullEditorCommandRouter : IEditorCommandRouter
{
    public bool CanExecute(string commandId, object? payload = null, RibbonContextSnapshot? context = null) => false;

    public ValueTask<bool> ExecuteAsync(
        string commandId,
        object? payload = null,
        RibbonContextSnapshot? context = null,
        bool recordHistory = true)
    {
        return new ValueTask<bool>(false);
    }
}
