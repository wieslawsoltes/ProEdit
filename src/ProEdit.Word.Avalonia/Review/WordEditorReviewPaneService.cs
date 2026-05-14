using Avalonia.Controls;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

public sealed class WordEditorReviewPaneService : IReviewPaneService
{
    private readonly DocumentView _view;
    private readonly Control? _reviewPane;
    private readonly Action _refreshPane;
    private readonly Action<bool>? _setVisibility;
    private ReviewMarkupMode _markupMode = ReviewMarkupMode.All;

    public WordEditorReviewPaneService(
        DocumentView view,
        Control? reviewPane,
        Action refreshPane,
        Action<bool>? setVisibility = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _reviewPane = reviewPane;
        _refreshPane = refreshPane ?? throw new ArgumentNullException(nameof(refreshPane));
        _setVisibility = setVisibility;
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
        if (_setVisibility is not null)
        {
            _setVisibility(visible);
            return;
        }

        _reviewPane.IsVisible = visible;
        if (visible)
        {
            _refreshPane();
        }
    }
}
