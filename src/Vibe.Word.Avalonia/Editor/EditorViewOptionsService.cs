using System;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Avalonia;

public sealed class EditorViewOptionsService : IEditorViewOptionsService
{
    private readonly DocumentView _view;
    private bool _showRuler = true;
    private bool _showNavigationPane;
    private EditorViewMode _viewMode;
    private float _savedZoomFactor = 1f;
    private DocumentZoomMode _savedZoomMode = DocumentZoomMode.Custom;
    private bool _hasSavedZoom;

    public EditorViewOptionsService(DocumentView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
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
        set => _showRuler = value;
    }

    public bool ShowGridlines
    {
        get => _view.ShowGridlines;
        set => _view.ShowGridlines = value;
    }

    public bool ShowNavigationPane
    {
        get => _showNavigationPane;
        set => _showNavigationPane = value;
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

    private static EditorViewMode ResolveViewMode(DocumentView view)
    {
        return view.UsePagination ? EditorViewMode.PrintLayout : EditorViewMode.Draft;
    }
}
