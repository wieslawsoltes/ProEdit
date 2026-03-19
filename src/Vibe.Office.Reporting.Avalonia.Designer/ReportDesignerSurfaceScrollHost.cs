using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Hosts the designer page surface with persistent scroll bars instead of overlay auto-hide chrome.
/// </summary>
public sealed class ReportDesignerSurfaceScrollHost : ContentControl
{
    private readonly SerialDisposable _horizontalSubscription = new();
    private readonly SerialDisposable _verticalSubscription = new();
    private ScrollViewer? _scrollViewer;
    private ScrollBar? _horizontalScrollBar;
    private ScrollBar? _verticalScrollBar;
    private bool _isSynchronizing;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        DetachParts();

        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        _horizontalScrollBar = e.NameScope.Find<ScrollBar>("PART_HorizontalScrollBar");
        _verticalScrollBar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar");

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        }

        if (_horizontalScrollBar is not null)
        {
            _horizontalSubscription.Disposable = _horizontalScrollBar.GetObservable(RangeBase.ValueProperty)
                .Subscribe(OnHorizontalScrollBarValueChanged);
        }

        if (_verticalScrollBar is not null)
        {
            _verticalSubscription.Disposable = _verticalScrollBar.GetObservable(RangeBase.ValueProperty)
                .Subscribe(OnVerticalScrollBarValueChanged);
        }

        UpdateScrollBars();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachParts();
        base.OnDetachedFromVisualTree(e);
    }

    private void DetachParts()
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
            _scrollViewer = null;
        }

        _horizontalSubscription.Disposable = Disposable.Empty;
        _verticalSubscription.Disposable = Disposable.Empty;
        _horizontalScrollBar = null;
        _verticalScrollBar = null;
    }

    private void OnScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateScrollBars();
    }

    private void OnHorizontalScrollBarValueChanged(double value)
    {
        if (_isSynchronizing || _scrollViewer is null)
        {
            return;
        }

        _scrollViewer.Offset = _scrollViewer.Offset.WithX(value);
    }

    private void OnVerticalScrollBarValueChanged(double value)
    {
        if (_isSynchronizing || _scrollViewer is null)
        {
            return;
        }

        _scrollViewer.Offset = _scrollViewer.Offset.WithY(value);
    }

    private void UpdateScrollBars()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            UpdateScrollBar(
                _horizontalScrollBar,
                _scrollViewer.Offset.X,
                _scrollViewer.ScrollBarMaximum.X,
                _scrollViewer.Viewport.Width,
                _scrollViewer.SmallChange.Width,
                _scrollViewer.LargeChange.Width);
            UpdateScrollBar(
                _verticalScrollBar,
                _scrollViewer.Offset.Y,
                _scrollViewer.ScrollBarMaximum.Y,
                _scrollViewer.Viewport.Height,
                _scrollViewer.SmallChange.Height,
                _scrollViewer.LargeChange.Height);
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private static void UpdateScrollBar(
        ScrollBar? scrollBar,
        double value,
        double maximum,
        double viewport,
        double smallChange,
        double largeChange)
    {
        if (scrollBar is null)
        {
            return;
        }

        scrollBar.Minimum = 0d;
        scrollBar.Maximum = Math.Max(0d, maximum);
        scrollBar.ViewportSize = Math.Max(0d, viewport);
        scrollBar.SmallChange = Math.Max(1d, smallChange);
        scrollBar.LargeChange = Math.Max(1d, largeChange);
        scrollBar.Value = Math.Clamp(value, 0d, scrollBar.Maximum);
    }
}
