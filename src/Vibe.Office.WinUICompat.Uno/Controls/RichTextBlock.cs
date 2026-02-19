using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vibe.Office.WinUICompat.Bridges;
using Vibe.Office.WinUICompat.Documents;

namespace Vibe.Office.WinUICompat.Controls;

public sealed class RichTextBlock : UserControl
{
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(RichTextDocument),
            typeof(RichTextBlock),
            new PropertyMetadata(null, OnDocumentPropertyChanged));

    public static readonly DependencyProperty OverflowContentTargetProperty =
        DependencyProperty.Register(
            nameof(OverflowContentTarget),
            typeof(RichTextBlockOverflow),
            typeof(RichTextBlock),
            new PropertyMetadata(null, OnOverflowContentTargetChanged));

    public static readonly DependencyProperty MaxLinesProperty =
        DependencyProperty.Register(
            nameof(MaxLines),
            typeof(int),
            typeof(RichTextBlock),
            new PropertyMetadata(0, OnOverflowLayoutPropertyChanged));

    public static readonly DependencyProperty HasOverflowContentProperty =
        DependencyProperty.Register(
            nameof(HasOverflowContent),
            typeof(bool),
            typeof(RichTextBlock),
            new PropertyMetadata(false));

    private readonly EngineRichEditHost _renderHost;
    private readonly CompatDocumentContinuationLayout _continuationLayout;
    private BlockCollection? _attachedDocumentBlocks;

    public RichTextBlock()
    {
        _continuationLayout = new CompatDocumentContinuationLayout();
        EmbeddedUiDocumentConfigurator.Configure(_continuationLayout.RenderDocument);

        _renderHost = new EngineRichEditHost
        {
            Document = _continuationLayout.RenderDocument,
            AutoUpdateViewport = false,
            IsCanvasInputEnabled = false,
            IsScrollEnabled = false,
            IsEmbeddedUiInteractive = IsEnabled,
            ShowCaretWhenReadOnly = false
        };

        Content = _renderHost;
        SizeChanged += OnLayoutSizeChanged;
        IsEnabledChanged += OnIsEnabledChanged;
        Blocks = new BlockCollection();
        Blocks.CollectionChanged += OnBlocksChanged;
        RefreshOverflowLayout();
    }

    public BlockCollection Blocks { get; }

    public RichTextDocument? Document
    {
        get => (RichTextDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public RichTextBlockOverflow? OverflowContentTarget
    {
        get => (RichTextBlockOverflow?)GetValue(OverflowContentTargetProperty);
        set => SetValue(OverflowContentTargetProperty, value);
    }

    public int MaxLines
    {
        get => (int)GetValue(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    public bool HasOverflowContent
    {
        get => (bool)GetValue(HasOverflowContentProperty);
        private set => SetValue(HasOverflowContentProperty, value);
    }

    public void RefreshOverflowLayout()
    {
        var sourceDocument = GetSourceDocument();
        var width = ResolveAvailableWidth();
        _continuationLayout.UpdateSource(sourceDocument, (float)width);

        var segment = MaxLines > 0
            ? _continuationLayout.GetSegmentByMaxLines(0, MaxLines)
            : _continuationLayout.GetRemainingSegment(0);
        ApplySegment(segment);

        HasOverflowContent = segment.HasOverflow;
        OverflowContentTarget?.AcceptContinuationFrom(
            this,
            _continuationLayout,
            segment.EndLineIndex,
            width);
    }

    private static void OnDocumentPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not RichTextBlock richTextBlock)
        {
            return;
        }

        richTextBlock.AttachDocumentBlocks(args.OldValue as RichTextDocument, args.NewValue as RichTextDocument);
        richTextBlock.RefreshOverflowLayout();
    }

    private static void OnOverflowContentTargetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not RichTextBlock richTextBlock)
        {
            return;
        }

        if (args.OldValue is RichTextBlockOverflow previous && ReferenceEquals(previous.ContentSource, richTextBlock))
        {
            previous.AcceptContinuationFrom(null, null, 0, richTextBlock.ResolveAvailableWidth());
        }

        if (args.NewValue is RichTextBlockOverflow next)
        {
            next.ContentSource = richTextBlock;
        }

        richTextBlock.RefreshOverflowLayout();
    }

    private static void OnOverflowLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is RichTextBlock richTextBlock)
        {
            richTextBlock.RefreshOverflowLayout();
        }
    }

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Document is null)
        {
            RefreshOverflowLayout();
        }
    }

    private void OnDocumentBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshOverflowLayout();
    }

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshOverflowLayout();
    }

    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _renderHost.IsEmbeddedUiInteractive = IsEnabled;
    }

    private void AttachDocumentBlocks(RichTextDocument? oldDocument, RichTextDocument? newDocument)
    {
        if (oldDocument is not null && ReferenceEquals(_attachedDocumentBlocks, oldDocument.Blocks))
        {
            _attachedDocumentBlocks.CollectionChanged -= OnDocumentBlocksChanged;
        }

        _attachedDocumentBlocks = newDocument?.Blocks;
        if (_attachedDocumentBlocks is not null)
        {
            _attachedDocumentBlocks.CollectionChanged += OnDocumentBlocksChanged;
        }
    }

    private RichTextDocument GetSourceDocument()
    {
        if (Document is not null)
        {
            return Document;
        }

        var source = new RichTextDocument();
        for (var i = 0; i < Blocks.Count; i++)
        {
            source.Blocks.Add(Blocks[i]);
        }

        if (source.Blocks.Count == 0)
        {
            source.Blocks.Add(new Paragraph());
        }

        return source;
    }

    private void ApplySegment(CompatDocumentContinuationSegment segment)
    {
        if (segment.IsEmpty)
        {
            _renderHost.ClearRenderSegment();
            _renderHost.InvalidateSurface();
            return;
        }

        _renderHost.SetRenderSegment(segment.StartLineIndex, segment.LineCount, segment.StartY, segment.Height);
        _renderHost.InvalidateSurface();
    }

    private double ResolveAvailableWidth()
    {
        var candidates = new[] { _renderHost.ActualWidth, ActualWidth, Width, MinWidth };
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
}
