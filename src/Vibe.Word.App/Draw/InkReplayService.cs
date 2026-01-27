using System;
using System.Threading.Tasks;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

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
