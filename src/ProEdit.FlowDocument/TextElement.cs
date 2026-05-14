using Avalonia;

namespace ProEdit.FlowDocument;

/// <summary>
/// Base type for FlowDocument elements that carry text styling.
/// </summary>
public abstract class TextElement : FlowElement
{
    private readonly FlowTextStyleProperties _style = new FlowTextStyleProperties();
    private readonly object _elementStart = new();
    private readonly object _elementEnd = new();
    private readonly object _contentStart = new();
    private readonly object _contentEnd = new();
    private bool _suppressStyleChanged;

    /// <summary>
    /// Identifies the <see cref="FontFamily"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FontFamilyProperty =
        AvaloniaProperty.Register<TextElement, string?>(nameof(FontFamily));

    /// <summary>
    /// Identifies the <see cref="FontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> FontSizeProperty =
        AvaloniaProperty.Register<TextElement, double?>(nameof(FontSize));

    /// <summary>
    /// Identifies the <see cref="FontWeight"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowFontWeight?> FontWeightProperty =
        AvaloniaProperty.Register<TextElement, FlowFontWeight?>(nameof(FontWeight));

    /// <summary>
    /// Identifies the <see cref="FontStyle"/> property.
    /// </summary>
    public static readonly StyledProperty<FlowFontStyle?> FontStyleProperty =
        AvaloniaProperty.Register<TextElement, FlowFontStyle?>(nameof(FontStyle));

    /// <summary>
    /// Identifies the <see cref="FontStretch"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> FontStretchProperty =
        AvaloniaProperty.Register<TextElement, string?>(nameof(FontStretch));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ForegroundProperty =
        AvaloniaProperty.Register<TextElement, string?>(nameof(Foreground));

    /// <summary>
    /// Identifies the <see cref="Background"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> BackgroundProperty =
        AvaloniaProperty.Register<TextElement, string?>(nameof(Background));

    /// <summary>
    /// Identifies the <see cref="TextEffects"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> TextEffectsProperty =
        AvaloniaProperty.Register<TextElement, object?>(nameof(TextEffects));

    /// <summary>
    /// Identifies the <see cref="Typography"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> TypographyProperty =
        AvaloniaProperty.Register<TextElement, object?>(nameof(Typography));

    /// <summary>
    /// Gets the style container for this element.
    /// </summary>
    internal FlowTextStyleProperties Style => _style;

    /// <summary>
    /// Gets a token representing the element start.
    /// </summary>
    public object ElementStart => _elementStart;

    /// <summary>
    /// Gets a token representing the element end.
    /// </summary>
    public object ElementEnd => _elementEnd;

    /// <summary>
    /// Gets a token representing the content start.
    /// </summary>
    public object ContentStart => _contentStart;

    /// <summary>
    /// Gets a token representing the content end.
    /// </summary>
    public object ContentEnd => _contentEnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextElement"/> class.
    /// </summary>
    protected TextElement()
    {
        _style.Changed += OnStyleChanged;
    }

    /// <summary>
    /// Gets or sets the font family name.
    /// </summary>
    public string? FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    public double? FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    public FlowFontWeight? FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    public FlowFontStyle? FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font stretch.
    /// </summary>
    public string? FontStretch
    {
        get => GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground color as a string.
    /// </summary>
    public string? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background color as a string.
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

    protected void SyncStyleValue<T>(StyledProperty<T?> property, Action<T?> assign)
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
