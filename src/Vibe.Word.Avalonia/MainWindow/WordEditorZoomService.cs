using System;
using System.Threading.Tasks;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

public sealed class WordEditorZoomService : IEditorZoomService
{
    private readonly DocumentView _view;
    private readonly Func<Task> _openDialog;

    public WordEditorZoomService(DocumentView view, Func<Task> openDialog)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _openDialog = openDialog ?? throw new ArgumentNullException(nameof(openDialog));
    }

    public ValueTask OpenZoomDialogAsync() => new ValueTask(_openDialog());

    public void ZoomToPercent(float percent) => _view.ZoomToPercent(percent);

    public void ZoomToPageWidth() => _view.ZoomToPageWidth();

    public void ZoomToWholePage() => _view.ZoomToWholePage();

    public void ZoomToMultiplePages(int pagesPerRow) => _view.ZoomToMultiplePages(pagesPerRow);

    public void ZoomToDefault() => _view.ZoomToDefault();
}
