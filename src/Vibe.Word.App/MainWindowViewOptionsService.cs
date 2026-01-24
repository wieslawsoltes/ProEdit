using Avalonia.Controls;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.App;

public sealed class MainWindowViewOptionsService : IEditorViewOptionsService
{
    private readonly DocumentView _view;
    private readonly Control? _horizontalRuler;
    private readonly Control? _verticalRuler;
    private readonly Control? _rulerCorner;
    private readonly Control? _navigationPane;
    private bool _showRuler;
    private bool _showNavigationPane;

    public MainWindowViewOptionsService(
        DocumentView view,
        Control? horizontalRuler,
        Control? verticalRuler,
        Control? rulerCorner,
        Control? navigationPane)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _horizontalRuler = horizontalRuler;
        _verticalRuler = verticalRuler;
        _rulerCorner = rulerCorner;
        _navigationPane = navigationPane;
        _showRuler = ResolveVisibility(horizontalRuler, verticalRuler, rulerCorner);
        _showNavigationPane = _navigationPane?.IsVisible ?? false;
    }

    public bool ShowInvisibles
    {
        get => _view.ShowInvisibles;
        set => _view.ShowInvisibles = value;
    }

    public bool ShowRuler
    {
        get => _showRuler;
        set
        {
            _showRuler = value;
            SetVisibility(_horizontalRuler, value);
            SetVisibility(_verticalRuler, value);
            SetVisibility(_rulerCorner, value);
        }
    }

    public bool ShowGridlines
    {
        get => _view.ShowGridlines;
        set => _view.ShowGridlines = value;
    }

    public bool ShowNavigationPane
    {
        get => _showNavigationPane;
        set
        {
            _showNavigationPane = value;
            SetVisibility(_navigationPane, value);
        }
    }

    public PageFlowDirection PageMovement
    {
        get => _view.PageFlow;
        set => _view.PageFlow = value;
    }

    private static bool ResolveVisibility(Control? horizontal, Control? vertical, Control? corner)
    {
        if (horizontal is null && vertical is null && corner is null)
        {
            return true;
        }

        return (horizontal?.IsVisible ?? true)
               || (vertical?.IsVisible ?? true)
               || (corner?.IsVisible ?? true);
    }

    private static void SetVisibility(Control? control, bool value)
    {
        if (control is not null)
        {
            control.IsVisible = value;
        }
    }
}
