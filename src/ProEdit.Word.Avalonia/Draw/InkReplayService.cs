using System;
using System.Threading.Tasks;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

public sealed class InkReplayService : IInkReplayService
{
    private readonly DocumentView _view;

    public InkReplayService(DocumentView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public ValueTask ReplaySelectedInkAsync()
    {
        return _view.ReplaySelectedInkAsync();
    }
}
