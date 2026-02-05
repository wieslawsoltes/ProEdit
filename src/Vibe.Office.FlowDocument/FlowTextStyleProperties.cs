namespace Vibe.Office.FlowDocument;

/// <summary>
/// Optional text style properties that can be applied to FlowDocument elements.
/// </summary>
internal sealed class FlowTextStyleProperties
{
    private string? _fontFamily;
    private double? _fontSize;
    private FlowFontWeight? _fontWeight;
    private FlowFontStyle? _fontStyle;
    private string? _fontStretch;
    private string? _foreground;
    private string? _background;
    private FlowTextDecorations? _textDecorations;
    private FlowBaselineAlignment? _baselineAlignment;

    /// <summary>
    /// Raised when a style value changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets or sets the font family name.
    /// </summary>
    public string? FontFamily
    {
        get => _fontFamily;
        set => SetValue(ref _fontFamily, value);
    }

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    public double? FontSize
    {
        get => _fontSize;
        set => SetValue(ref _fontSize, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    public FlowFontWeight? FontWeight
    {
        get => _fontWeight;
        set => SetValue(ref _fontWeight, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    public FlowFontStyle? FontStyle
    {
        get => _fontStyle;
        set => SetValue(ref _fontStyle, value);
    }

    /// <summary>
    /// Gets or sets the font stretch.
    /// </summary>
    public string? FontStretch
    {
        get => _fontStretch;
        set => SetValue(ref _fontStretch, value);
    }

    /// <summary>
    /// Gets or sets the foreground color as a string.
    /// </summary>
    public string? Foreground
    {
        get => _foreground;
        set => SetValue(ref _foreground, value);
    }

    /// <summary>
    /// Gets or sets the background color as a string.
    /// </summary>
    public string? Background
    {
        get => _background;
        set => SetValue(ref _background, value);
    }

    /// <summary>
    /// Gets or sets text decorations.
    /// </summary>
    public FlowTextDecorations? TextDecorations
    {
        get => _textDecorations;
        set => SetValue(ref _textDecorations, value);
    }

    /// <summary>
    /// Gets or sets the baseline alignment.
    /// </summary>
    public FlowBaselineAlignment? BaselineAlignment
    {
        get => _baselineAlignment;
        set => SetValue(ref _baselineAlignment, value);
    }

    /// <summary>
    /// Gets a value indicating whether any style values are set.
    /// </summary>
    public bool HasValues => !string.IsNullOrWhiteSpace(_fontFamily)
                             || _fontSize.HasValue
                             || _fontWeight.HasValue
                             || _fontStyle.HasValue
                             || !string.IsNullOrWhiteSpace(_fontStretch)
                             || !string.IsNullOrWhiteSpace(_foreground)
                             || !string.IsNullOrWhiteSpace(_background)
                             || _textDecorations.HasValue
                             || _baselineAlignment.HasValue;

    /// <summary>
    /// Creates a copy of the current style properties.
    /// </summary>
    public FlowTextStyleProperties Clone()
    {
        return new FlowTextStyleProperties
        {
            FontFamily = _fontFamily,
            FontSize = _fontSize,
            FontWeight = _fontWeight,
            FontStyle = _fontStyle,
            FontStretch = _fontStretch,
            Foreground = _foreground,
            Background = _background,
            TextDecorations = _textDecorations,
            BaselineAlignment = _baselineAlignment
        };
    }

    /// <summary>
    /// Applies non-null values from the override style to this instance.
    /// </summary>
    /// <param name="overrides">The style values to apply.</param>
    public void ApplyOverrides(FlowTextStyleProperties overrides)
    {
        if (overrides is null)
        {
            return;
        }

        if (overrides.FontFamily is not null)
        {
            FontFamily = overrides.FontFamily;
        }

        if (overrides.FontSize.HasValue)
        {
            FontSize = overrides.FontSize;
        }

        if (overrides.FontWeight.HasValue)
        {
            FontWeight = overrides.FontWeight;
        }

        if (overrides.FontStyle.HasValue)
        {
            FontStyle = overrides.FontStyle;
        }

        if (overrides.FontStretch is not null)
        {
            FontStretch = overrides.FontStretch;
        }

        if (overrides.Foreground is not null)
        {
            Foreground = overrides.Foreground;
        }

        if (overrides.Background is not null)
        {
            Background = overrides.Background;
        }

        if (overrides.TextDecorations.HasValue)
        {
            TextDecorations = overrides.TextDecorations;
        }

        if (overrides.BaselineAlignment.HasValue)
        {
            BaselineAlignment = overrides.BaselineAlignment;
        }
    }

    private void SetValue<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
