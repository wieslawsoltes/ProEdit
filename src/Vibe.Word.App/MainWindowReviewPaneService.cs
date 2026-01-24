using Avalonia.Controls;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public sealed class MainWindowReviewPaneService : IReviewPaneService
{
    private readonly DocumentView _view;
    private readonly Control? _reviewPane;
    private readonly Action _refreshPane;
    private ReviewMarkupMode _markupMode = ReviewMarkupMode.All;

    public MainWindowReviewPaneService(DocumentView view, Control? reviewPane, Action refreshPane)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _reviewPane = reviewPane;
        _refreshPane = refreshPane ?? throw new ArgumentNullException(nameof(refreshPane));
        _view.ReviewMarkupMode = _markupMode;
    }

    public ReviewMarkupMode MarkupMode
    {
        get => _markupMode;
        set
        {
            if (_markupMode == value)
            {
                return;
            }

            _markupMode = value;
            _view.ReviewMarkupMode = value;
        }
    }

    public void ToggleReviewingPane()
    {
        if (_reviewPane is null)
        {
            return;
        }

        var visible = !_reviewPane.IsVisible;
        _reviewPane.IsVisible = visible;
        if (visible)
        {
            _refreshPane();
        }
    }
}
