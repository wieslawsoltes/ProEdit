using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vibe.Office.WinUICompat.Bridges;

namespace Vibe.Office.WinUICompat.Controls;

public sealed class RichTextBlockOverflow : UserControl
{
    public static readonly DependencyProperty OverflowContentTargetProperty =
        DependencyProperty.Register(
            nameof(OverflowContentTarget),
            typeof(RichTextBlockOverflow),
            typeof(RichTextBlockOverflow),
            new PropertyMetadata(null, OnOverflowContentTargetChanged));

    public static readonly DependencyProperty ContentSourceProperty =
        DependencyProperty.Register(
            nameof(ContentSource),
            typeof(FrameworkElement),
            typeof(RichTextBlockOverflow),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SourceProperty = ContentSourceProperty;

    public static readonly DependencyProperty HasOverflowContentProperty =
        DependencyProperty.Register(
            nameof(HasOverflowContent),
            typeof(bool),
            typeof(RichTextBlockOverflow),
            new PropertyMetadata(false));

    private readonly EngineRichEditHost _renderHost;
    private CompatDocumentContinuationLayout? _continuationLayout;
    private int _startLineIndex;
    private double _sourceWidthHint;

    public RichTextBlockOverflow()
    {
        _renderHost = new EngineRichEditHost
        {
            AutoUpdateViewport = false,
            IsCanvasInputEnabled = false,
            IsScrollEnabled = false,
            IsEmbeddedUiInteractive = IsEnabled,
            ShowCaretWhenReadOnly = false
        };

        Content = _renderHost;
        SizeChanged += OnLayoutSizeChanged;
        IsEnabledChanged += OnIsEnabledChanged;
    }

    public RichTextBlockOverflow? OverflowContentTarget
    {
        get => (RichTextBlockOverflow?)GetValue(OverflowContentTargetProperty);
        set => SetValue(OverflowContentTargetProperty, value);
    }

    public FrameworkElement? ContentSource
    {
        get => (FrameworkElement?)GetValue(ContentSourceProperty);
        set => SetValue(ContentSourceProperty, value);
    }

    public FrameworkElement? Source
    {
        get => ContentSource;
        set => ContentSource = value;
    }

    public bool HasOverflowContent
    {
        get => (bool)GetValue(HasOverflowContentProperty);
        private set => SetValue(HasOverflowContentProperty, value);
    }

    internal void AcceptContinuationFrom(
        FrameworkElement? source,
        CompatDocumentContinuationLayout? continuationLayout,
        int startLineIndex,
        double sourceWidthHint)
    {
        ContentSource = source;
        _sourceWidthHint = sourceWidthHint;
        _continuationLayout = continuationLayout;
        _startLineIndex = Math.Max(0, startLineIndex);
        _renderHost.Document = continuationLayout?.RenderDocument;
        RefreshOverflowLayout();
    }

    private static void OnOverflowContentTargetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not RichTextBlockOverflow overflow)
        {
            return;
        }

        if (args.OldValue is RichTextBlockOverflow previous && ReferenceEquals(previous.ContentSource, overflow))
        {
            previous.AcceptContinuationFrom(null, null, 0, overflow._sourceWidthHint);
        }

        if (args.NewValue is RichTextBlockOverflow next)
        {
            next.ContentSource = overflow;
        }

        overflow.RefreshOverflowLayout();
    }

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshOverflowLayout();
    }

    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _renderHost.IsEmbeddedUiInteractive = IsEnabled;
    }

    private void RefreshOverflowLayout()
    {
        if (_continuationLayout is null
            || !_continuationLayout.HasLines
            || _startLineIndex >= _continuationLayout.LineCount)
        {
            _renderHost.ClearRenderSegment();
            HasOverflowContent = false;
            OverflowContentTarget?.AcceptContinuationFrom(this, _continuationLayout, _startLineIndex, ResolveAvailableWidth());
            return;
        }

        var height = ResolveAvailableHeight();
        var segment = _continuationLayout.GetSegmentByHeight(_startLineIndex, (float)height);
        if (segment.IsEmpty)
        {
            _renderHost.ClearRenderSegment();
            HasOverflowContent = false;
            OverflowContentTarget?.AcceptContinuationFrom(this, _continuationLayout, _startLineIndex, ResolveAvailableWidth());
            return;
        }

        _renderHost.SetRenderSegment(segment.StartLineIndex, segment.LineCount, segment.StartY, segment.Height);
        _renderHost.InvalidateSurface();

        HasOverflowContent = segment.HasOverflow;
        OverflowContentTarget?.AcceptContinuationFrom(this, _continuationLayout, segment.EndLineIndex, ResolveAvailableWidth());
    }

    private double ResolveAvailableWidth()
    {
        var candidates = new[] { _renderHost.ActualWidth, ActualWidth, Width, MinWidth, _sourceWidthHint };
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (double.IsNaN(candidate) || double.IsInfinity(candidate))
            {
                continue;
            }

            if (candidate > 1d)
            {
                return candidate;
            }
        }

        return 640d;
    }

    private double ResolveAvailableHeight()
    {
        var candidates = new[] { _renderHost.ActualHeight, ActualHeight, Height, MinHeight };
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (double.IsNaN(candidate) || double.IsInfinity(candidate))
            {
                continue;
            }

            if (candidate > 1d)
            {
                return candidate;
            }
        }

        return 24d;
    }
}
