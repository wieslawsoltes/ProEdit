using Avalonia;
using Avalonia.Metadata;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a FlowDocument root element.
/// </summary>
public sealed class FlowDocument : FlowElement
{
    private readonly FlowTextStyleProperties _style = new FlowTextStyleProperties();
    private readonly object _contentStart = new();
    private readonly object _contentEnd = new();
    private bool _suppressStyleChanged;

    /// <summary>
    /// Identifies the <see cref="FontFamily"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FontFamilyProperty =
        AvaloniaProperty.Register<FlowDocument, string?>(nameof(FontFamily));

    /// <summary>
    /// Identifies the <see cref="FontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> FontSizeProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(FontSize));

    /// <summary>
    /// Identifies the <see cref="FontWeight"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowFontWeight?> FontWeightProperty =
        AvaloniaProperty.Register<FlowDocument, FlowFontWeight?>(nameof(FontWeight));

    /// <summary>
    /// Identifies the <see cref="FontStyle"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowFontStyle?> FontStyleProperty =
        AvaloniaProperty.Register<FlowDocument, FlowFontStyle?>(nameof(FontStyle));

    /// <summary>
    /// Identifies the <see cref="FontStretch"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FontStretchProperty =
        AvaloniaProperty.Register<FlowDocument, string?>(nameof(FontStretch));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ForegroundProperty =
        AvaloniaProperty.Register<FlowDocument, string?>(nameof(Foreground));

    /// <summary>
    /// Identifies the <see cref="Background"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BackgroundProperty =
        AvaloniaProperty.Register<FlowDocument, string?>(nameof(Background));

    /// <summary>
    /// Identifies the <see cref="TextEffects"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> TextEffectsProperty =
        AvaloniaProperty.Register<FlowDocument, object?>(nameof(TextEffects));

    /// <summary>
    /// Identifies the <see cref="Typography"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> TypographyProperty =
        AvaloniaProperty.Register<FlowDocument, object?>(nameof(Typography));

    /// <summary>
    /// Identifies the <see cref="PageWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> PageWidthProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(PageWidth));

    /// <summary>
    /// Identifies the <see cref="PageHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> PageHeightProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(PageHeight));

    /// <summary>
    /// Identifies the <see cref="PagePadding"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowThickness> PagePaddingProperty =
        AvaloniaProperty.Register<FlowDocument, FlowThickness>(nameof(PagePadding));

    /// <summary>
    /// Identifies the <see cref="ColumnWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> ColumnWidthProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(ColumnWidth));

    /// <summary>
    /// Identifies the <see cref="ColumnGap"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> ColumnGapProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(ColumnGap));

    /// <summary>
    /// Identifies the <see cref="TextAlignment"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowTextAlignment?> TextAlignmentProperty =
        AvaloniaProperty.Register<FlowDocument, FlowTextAlignment?>(nameof(TextAlignment));

    /// <summary>
    /// Identifies the <see cref="ColumnRuleBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ColumnRuleBrushProperty =
        AvaloniaProperty.Register<FlowDocument, string?>(nameof(ColumnRuleBrush));

    /// <summary>
    /// Identifies the <see cref="ColumnRuleWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> ColumnRuleWidthProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(ColumnRuleWidth));

    /// <summary>
    /// Identifies the <see cref="FlowDirection"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FlowDirectionProperty =
        AvaloniaProperty.Register<FlowDocument, string?>(nameof(FlowDirection));

    /// <summary>
    /// Identifies the <see cref="IsColumnWidthFlexible"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> IsColumnWidthFlexibleProperty =
        AvaloniaProperty.Register<FlowDocument, bool?>(nameof(IsColumnWidthFlexible));

    /// <summary>
    /// Identifies the <see cref="IsHyphenationEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> IsHyphenationEnabledProperty =
        AvaloniaProperty.Register<FlowDocument, bool?>(nameof(IsHyphenationEnabled));

    /// <summary>
    /// Identifies the <see cref="IsOptimalParagraphEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool?> IsOptimalParagraphEnabledProperty =
        AvaloniaProperty.Register<FlowDocument, bool?>(nameof(IsOptimalParagraphEnabled));

    /// <summary>
    /// Identifies the <see cref="LineHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> LineHeightProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(LineHeight));

    /// <summary>
    /// Identifies the <see cref="LineStackingStrategy"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LineStackingStrategyProperty =
        AvaloniaProperty.Register<FlowDocument, string?>(nameof(LineStackingStrategy));

    /// <summary>
    /// Identifies the <see cref="MaxPageHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> MaxPageHeightProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(MaxPageHeight));

    /// <summary>
    /// Identifies the <see cref="MaxPageWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> MaxPageWidthProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(MaxPageWidth));

    /// <summary>
    /// Identifies the <see cref="MinPageHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> MinPageHeightProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(MinPageHeight));

    /// <summary>
    /// Identifies the <see cref="MinPageWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> MinPageWidthProperty =
        AvaloniaProperty.Register<FlowDocument, double?>(nameof(MinPageWidth));

    /// <summary>
    /// Occurs when the document content changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the collection of top-level blocks.
    /// </summary>
    [Content]
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Gets a token representing the document content start.
    /// </summary>
    public object ContentStart => _contentStart;

    /// <summary>
    /// Gets a token representing the document content end.
    /// </summary>
    public object ContentEnd => _contentEnd;

    /// <summary>
    /// Gets internal style values used by converters.
    /// </summary>
    internal FlowTextStyleProperties Style => _style;

    /// <summary>
    /// Gets or sets the font family name.
    /// </summary>
    public string? FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets font size in points.
    /// </summary>
    public double? FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets font weight metadata.
    /// </summary>
    public FlowFontWeight? FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets font style metadata.
    /// </summary>
    public FlowFontStyle? FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets font stretch metadata.
    /// </summary>
    public string? FontStretch
    {
        get => GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>
    /// Gets or sets foreground color metadata.
    /// </summary>
    public string? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets background color metadata.
    /// </summary>
    public string? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets text effects metadata.
    /// </summary>
    public object? TextEffects
    {
        get => GetValue(TextEffectsProperty);
        set => SetValue(TextEffectsProperty, value);
    }

    /// <summary>
    /// Gets or sets typography metadata.
    /// </summary>
    public object? Typography
    {
        get => GetValue(TypographyProperty);
        set => SetValue(TypographyProperty, value);
    }

    /// <summary>
    /// Gets or sets the page width in points.
    /// </summary>
    public double? PageWidth
    {
        get => GetValue(PageWidthProperty);
        set => SetValue(PageWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the page height in points.
    /// </summary>
    public double? PageHeight
    {
        get => GetValue(PageHeightProperty);
        set => SetValue(PageHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the page padding.
    /// </summary>
    public FlowThickness PagePadding
    {
        get => GetValue(PagePaddingProperty);
        set => SetValue(PagePaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the preferred column width.
    /// </summary>
    public double? ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the gap between columns.
    /// </summary>
    public double? ColumnGap
    {
        get => GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    /// <summary>
    /// Gets or sets the default text alignment for the document.
    /// </summary>
    public FlowTextAlignment? TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the column rule brush.
    /// </summary>
    public string? ColumnRuleBrush
    {
        get => GetValue(ColumnRuleBrushProperty);
        set => SetValue(ColumnRuleBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the column rule width.
    /// </summary>
    public double? ColumnRuleWidth
    {
        get => GetValue(ColumnRuleWidthProperty);
        set => SetValue(ColumnRuleWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets flow direction.
    /// </summary>
    public string? FlowDirection
    {
        get => GetValue(FlowDirectionProperty);
        set => SetValue(FlowDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether column width is flexible.
    /// </summary>
    public bool? IsColumnWidthFlexible
    {
        get => GetValue(IsColumnWidthFlexibleProperty);
        set => SetValue(IsColumnWidthFlexibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether hyphenation is enabled.
    /// </summary>
    public bool? IsHyphenationEnabled
    {
        get => GetValue(IsHyphenationEnabledProperty);
        set => SetValue(IsHyphenationEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether optimal paragraph layout is enabled.
    /// </summary>
    public bool? IsOptimalParagraphEnabled
    {
        get => GetValue(IsOptimalParagraphEnabledProperty);
        set => SetValue(IsOptimalParagraphEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets default line height.
    /// </summary>
    public double? LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets line stacking strategy metadata.
    /// </summary>
    public string? LineStackingStrategy
    {
        get => GetValue(LineStackingStrategyProperty);
        set => SetValue(LineStackingStrategyProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum page height.
    /// </summary>
    public double? MaxPageHeight
    {
        get => GetValue(MaxPageHeightProperty);
        set => SetValue(MaxPageHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum page width.
    /// </summary>
    public double? MaxPageWidth
    {
        get => GetValue(MaxPageWidthProperty);
        set => SetValue(MaxPageWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum page height.
    /// </summary>
    public double? MinPageHeight
    {
        get => GetValue(MinPageHeightProperty);
        set => SetValue(MinPageHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum page width.
    /// </summary>
    public double? MinPageWidth
    {
        get => GetValue(MinPageWidthProperty);
        set => SetValue(MinPageWidthProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocument"/> class.
    /// </summary>
    public FlowDocument()
    {
        Blocks = new BlockCollection(this);
        _style.Changed += OnStyleChanged;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == FontFamilyProperty)
        {
            SyncStyleValue(FontFamilyProperty, value => _style.FontFamily = value);
        }
        else if (change.Property == FontSizeProperty)
        {
            SyncStyleValue(FontSizeProperty, value => _style.FontSize = value);
        }
        else if (change.Property == FontWeightProperty)
        {
            SyncStyleValue(FontWeightProperty, value => _style.FontWeight = value);
        }
        else if (change.Property == FontStyleProperty)
        {
            SyncStyleValue(FontStyleProperty, value => _style.FontStyle = value);
        }
        else if (change.Property == FontStretchProperty)
        {
            SyncStyleValue(FontStretchProperty, value => _style.FontStretch = value);
        }
        else if (change.Property == ForegroundProperty)
        {
            SyncStyleValue(ForegroundProperty, value => _style.Foreground = value);
        }
        else if (change.Property == BackgroundProperty)
        {
            SyncStyleValue(BackgroundProperty, value => _style.Background = value);
        }

        base.OnPropertyChanged(change);
    }

    internal void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SyncStyleValue<T>(StyledProperty<T?> property, Action<T?> assign)
    {
        _suppressStyleChanged = true;
        var hasValue = IsSet(property);
        assign(hasValue ? GetValue(property) : default);
        _suppressStyleChanged = false;
    }

    private void OnStyleChanged(object? sender, EventArgs e)
    {
        if (_suppressStyleChanged)
        {
            return;
        }

        NotifyChanged();
    }
}
