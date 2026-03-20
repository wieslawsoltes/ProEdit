using System.ComponentModel;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// Hosts the designer page surface with persistent scroll bars instead of overlay auto-hide chrome.
/// </summary>
public sealed class ReportDesignerSurfaceScrollHost : ContentControl
{
    private readonly SerialDisposable _horizontalSubscription = new();
    private readonly SerialDisposable _verticalSubscription = new();
    private readonly SerialDisposable _layoutSubscription = new();
    private readonly SerialDisposable _contentRootSubscription = new();
    private readonly SerialDisposable _viewModelSubscription = new();
    private Border? _viewport;
    private ContentPresenter? _contentPresenter;
    private TranslateTransform? _contentTransform;
    private Visual? _contentRoot;
    private ScrollBar? _horizontalScrollBar;
    private ScrollBar? _verticalScrollBar;
    private bool _isSynchronizing;
    private double _currentViewportWidth;
    private double _currentViewportHeight;
    private double _currentContentWidth;
    private double _currentContentHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);

        var width = double.IsInfinity(availableSize.Width)
            ? measured.Width
            : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height)
            ? measured.Height
            : availableSize.Height;

        return new Size(
            Math.Max(0d, width),
            Math.Max(0d, height));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachViewModelSubscription();
        Dispatcher.UIThread.Post(UpdateScrollBars, DispatcherPriority.Loaded);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        DetachParts();

        _viewport = e.NameScope.Find<Border>("PART_Viewport");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
        _contentTransform = _contentPresenter?.RenderTransform as TranslateTransform;
        _horizontalScrollBar = e.NameScope.Find<ScrollBar>("PART_HorizontalScrollBar");
        _verticalScrollBar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar");

        if (_viewport is not null && _contentPresenter is not null)
        {
            var layoutSubscriptions = new CompositeDisposable
            {
                _viewport.GetObservable(BoundsProperty)
                    .Subscribe(_ => UpdateScrollBars()),
                _contentPresenter.GetObservable(BoundsProperty)
                    .Subscribe(_ => UpdateScrollBars()),
                _contentPresenter.GetObservable(IsVisibleProperty)
                    .Subscribe(_ => UpdateScrollBars())
            };
            _layoutSubscription.Disposable = layoutSubscriptions;
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

        AttachViewModelSubscription();
        Dispatcher.UIThread.Post(ResolveContentRootAndUpdate, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachParts();
        base.OnDetachedFromVisualTree(e);
    }

    private void DetachParts()
    {
        _horizontalSubscription.Disposable = Disposable.Empty;
        _verticalSubscription.Disposable = Disposable.Empty;
        _layoutSubscription.Disposable = Disposable.Empty;
        _contentRootSubscription.Disposable = Disposable.Empty;
        _viewModelSubscription.Disposable = Disposable.Empty;
        _viewport = null;
        _contentPresenter = null;
        _contentTransform = null;
        _contentRoot = null;
        _horizontalScrollBar = null;
        _verticalScrollBar = null;
    }

    private void OnHorizontalScrollBarValueChanged(double value)
    {
        if (_isSynchronizing)
        {
            return;
        }

        ApplyContentOffset();
    }

    private void OnVerticalScrollBarValueChanged(double value)
    {
        if (_isSynchronizing)
        {
            return;
        }

        ApplyContentOffset();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_verticalScrollBar is null)
        {
            return;
        }

        var delta = e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        var step = Math.Max(24d, _verticalScrollBar.SmallChange);
        _verticalScrollBar.Value = Math.Clamp(
            _verticalScrollBar.Value - (delta * step),
            _verticalScrollBar.Minimum,
            _verticalScrollBar.Maximum);
        e.Handled = true;
    }

    private void UpdateScrollBars()
    {
        if (_viewport is null || _contentPresenter is null)
        {
            return;
        }

        var viewportWidth = Math.Max(0d, _viewport.Bounds.Width);
        var viewportHeight = Math.Max(0d, _viewport.Bounds.Height);
        var contentWidth = Math.Max(0d, _contentRoot?.Bounds.Width ?? _contentPresenter.Bounds.Width);
        var contentHeight = Math.Max(0d, _contentRoot?.Bounds.Height ?? _contentPresenter.Bounds.Height);
        if (DataContext is ReportDesignerViewModel designer)
        {
            contentWidth = Math.Max(contentWidth, designer.SurfaceStageWidth);
            contentHeight = Math.Max(contentHeight, designer.SurfaceStageHeight);
        }

        _currentViewportWidth = viewportWidth;
        _currentViewportHeight = viewportHeight;
        _currentContentWidth = contentWidth;
        _currentContentHeight = contentHeight;

        var horizontalMaximum = Math.Max(0d, contentWidth - viewportWidth);
        var verticalMaximum = Math.Max(0d, contentHeight - viewportHeight);

        _isSynchronizing = true;
        try
        {
            UpdateScrollBar(
                _horizontalScrollBar,
                _horizontalScrollBar?.Value ?? 0d,
                horizontalMaximum,
                viewportWidth,
                32d,
                Math.Max(48d, viewportWidth * 0.85d));
            UpdateScrollBar(
                _verticalScrollBar,
                _verticalScrollBar?.Value ?? 0d,
                verticalMaximum,
                viewportHeight,
                32d,
                Math.Max(48d, viewportHeight * 0.85d));
        }
        finally
        {
            _isSynchronizing = false;
        }

        ApplyContentOffset();
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

    private void ApplyContentOffset()
    {
        if (_contentTransform is null)
        {
            return;
        }

        var horizontalOverflow = Math.Max(0d, _currentContentWidth - _currentViewportWidth);
        var verticalOverflow = Math.Max(0d, _currentContentHeight - _currentViewportHeight);

        _contentTransform.X = horizontalOverflow <= 0d
            ? Math.Max(0d, (_currentViewportWidth - _currentContentWidth) * 0.5d)
            : -(_horizontalScrollBar?.Value ?? 0d);
        _contentTransform.Y = verticalOverflow <= 0d
            ? 0d
            : -(_verticalScrollBar?.Value ?? 0d);
    }

    private void ResolveContentRootAndUpdate()
    {
        var contentRoot = _contentPresenter?.GetVisualChildren().FirstOrDefault();
        if (!ReferenceEquals(_contentRoot, contentRoot))
        {
            _contentRoot = contentRoot;
            _contentRootSubscription.Disposable = _contentRoot is not null
                ? _contentRoot.GetObservable(Visual.BoundsProperty).Subscribe(_ => UpdateScrollBars())
                : Disposable.Empty;
        }

        UpdateScrollBars();
    }

    private void AttachViewModelSubscription()
    {
        if (DataContext is not INotifyPropertyChanged notifyPropertyChanged)
        {
            _viewModelSubscription.Disposable = Disposable.Empty;
            return;
        }

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (string.IsNullOrEmpty(args.PropertyName)
                || string.Equals(args.PropertyName, nameof(ReportDesignerViewModel.SurfaceScaledWidth), StringComparison.Ordinal)
                || string.Equals(args.PropertyName, nameof(ReportDesignerViewModel.SurfaceScaledHeight), StringComparison.Ordinal)
                || string.Equals(args.PropertyName, nameof(ReportDesignerViewModel.SurfaceStageWidth), StringComparison.Ordinal)
                || string.Equals(args.PropertyName, nameof(ReportDesignerViewModel.SurfaceStageHeight), StringComparison.Ordinal))
            {
                UpdateScrollBars();
            }
        };

        notifyPropertyChanged.PropertyChanged += handler;
        _viewModelSubscription.Disposable = Disposable.Create(() => notifyPropertyChanged.PropertyChanged -= handler);
    }
}
