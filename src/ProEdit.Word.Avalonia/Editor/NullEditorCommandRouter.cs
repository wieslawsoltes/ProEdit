using System.Threading.Tasks;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

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
