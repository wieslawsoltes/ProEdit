using Avalonia.Controls;
using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

internal sealed class WordEditorWindowService : IEditorWindowService
{
    private readonly Func<Window?> _ownerProvider;
    private readonly Func<Document?> _documentProvider;
    private readonly IEditorDialogService _dialogService;
    private readonly Func<Document?, Window?> _windowFactoryProvider;

    public WordEditorWindowService(
        Func<Window?> ownerProvider,
        Func<Document?> documentProvider,
        IEditorDialogService dialogService,
        Func<Document?, Window?> windowFactoryProvider)
    {
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        _documentProvider = documentProvider ?? throw new ArgumentNullException(nameof(documentProvider));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _windowFactoryProvider = windowFactoryProvider ?? throw new ArgumentNullException(nameof(windowFactoryProvider));
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
        var window = _windowFactoryProvider(clone);
        if (window is null)
        {
            return;
        }
        var owner = _ownerProvider();
        if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        window.Activate();
    }
}
