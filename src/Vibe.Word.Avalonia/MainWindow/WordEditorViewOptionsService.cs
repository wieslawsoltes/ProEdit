using Avalonia.Controls;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Avalonia;

public sealed class WordEditorViewOptionsService : IEditorViewOptionsService
{
    private readonly DocumentView _view;
    private readonly Control? _horizontalRuler;
    private readonly Control? _verticalRuler;
    private readonly Control? _rulerCorner;
    private readonly Control? _navigationPane;
    private readonly ColumnDefinition? _navigationColumn;
    private bool _showRuler;
    private bool _showNavigationPane;
    private EditorViewMode _viewMode;
    private float _savedZoomFactor = 1f;
    private DocumentZoomMode _savedZoomMode = DocumentZoomMode.Custom;
    private bool _hasSavedZoom;

    public WordEditorViewOptionsService(
        DocumentView view,
        Control? horizontalRuler,
        Control? verticalRuler,
        Control? rulerCorner,
        Control? navigationPane,
        ColumnDefinition? navigationColumn)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _horizontalRuler = horizontalRuler;
        _verticalRuler = verticalRuler;
        _rulerCorner = rulerCorner;
        _navigationPane = navigationPane;
        _navigationColumn = navigationColumn;
        _showRuler = ResolveVisibility(horizontalRuler, verticalRuler, rulerCorner);
        _showNavigationPane = _navigationPane?.IsVisible ?? false;
        _viewMode = ResolveViewMode(view);
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
            if (_navigationColumn is not null)
            {
                _navigationColumn.Width = value ? GridLength.Auto : new GridLength(0);
            }
        }
    }

    public PageFlowDirection PageMovement
    {
        get => _view.PageFlow;
        set => _view.PageFlow = value;
    }

    public EditorViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value)
            {
                return;
            }

            var previous = _viewMode;
            _viewMode = value;
            ApplyViewMode(previous, value);
        }
    }

    private void ApplyViewMode(EditorViewMode previous, EditorViewMode mode)
    {
        if (previous == EditorViewMode.ReadMode)
        {
            RestoreZoom();
        }

        switch (mode)
        {
            case EditorViewMode.ReadMode:
                SaveZoom();
                _view.UsePagination = true;
                _view.ShowLayout = true;
                _view.PageFlow = PageFlowDirection.Horizontal;
                _view.ZoomToWholePage();
                break;
            case EditorViewMode.PrintLayout:
                _view.UsePagination = true;
                _view.ShowLayout = true;
                _view.PageFlow = PageFlowDirection.Vertical;
                break;
            case EditorViewMode.WebLayout:
            case EditorViewMode.Outline:
            case EditorViewMode.Draft:
                _view.UsePagination = false;
                _view.ShowLayout = false;
                _view.PageFlow = PageFlowDirection.Vertical;
                break;
            default:
                _view.UsePagination = true;
                _view.ShowLayout = true;
                _view.PageFlow = PageFlowDirection.Vertical;
                break;
        }
    }

    private void SaveZoom()
    {
        _savedZoomFactor = _view.ZoomFactor;
        _savedZoomMode = _view.ZoomMode;
        _hasSavedZoom = true;
    }

    private void RestoreZoom()
    {
        if (!_hasSavedZoom)
        {
            return;
        }

        switch (_savedZoomMode)
        {
            case DocumentZoomMode.PageWidth:
                _view.ZoomToPageWidth();
                break;
            case DocumentZoomMode.WholePage:
                _view.ZoomToWholePage();
                break;
            default:
                _view.ZoomToPercent(_savedZoomFactor * 100f);
                break;
        }

        _hasSavedZoom = false;
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

    private static EditorViewMode ResolveViewMode(DocumentView view)
    {
        return view.UsePagination ? EditorViewMode.PrintLayout : EditorViewMode.Draft;
    }
}
