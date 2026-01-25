using Avalonia.Controls;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

internal sealed class MainWindowWindowService : IEditorWindowService
{
    private readonly Window _owner;
    private readonly Func<Document?> _documentProvider;
    private readonly IEditorDialogService _dialogService;

    public MainWindowWindowService(Window owner, Func<Document?> documentProvider, IEditorDialogService dialogService)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _documentProvider = documentProvider ?? throw new ArgumentNullException(nameof(documentProvider));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    public async ValueTask ExecuteAsync(EditorWindowCommand command)
    {
        switch (command)
        {
            case EditorWindowCommand.NewWindow:
                OpenNewWindow();
                return;
            case EditorWindowCommand.ArrangeAll:
                await _dialogService.ShowMessageAsync("Arrange All", "Arrange All is not available yet.");
                return;
            case EditorWindowCommand.Split:
                await _dialogService.ShowMessageAsync("Split", "Split view is not available yet.");
                return;
            case EditorWindowCommand.ViewSideBySide:
                await _dialogService.ShowMessageAsync("View Side by Side", "Side-by-side view is not available yet.");
                return;
            case EditorWindowCommand.SynchronousScrolling:
                await _dialogService.ShowMessageAsync("Synchronous Scrolling", "Synchronous scrolling is not available yet.");
                return;
            case EditorWindowCommand.ResetPosition:
                await _dialogService.ShowMessageAsync("Reset Window Position", "Reset window position is not available yet.");
                return;
            case EditorWindowCommand.SwitchWindows:
                await _dialogService.ShowMessageAsync("Switch Windows", "Window switching is not available yet.");
                return;
            default:
                await _dialogService.ShowMessageAsync("Window", "This window command is not available yet.");
                return;
        }
    }

    private void OpenNewWindow()
    {
        var document = _documentProvider();
        if (document is null)
        {
            return;
        }

        var clone = DocumentClone.Clone(document);
        var window = new MainWindow(clone, null);
        window.Show(_owner);
        window.Activate();
    }
}
