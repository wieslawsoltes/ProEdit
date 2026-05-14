using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Primitives;

namespace ProEdit.Word.Avalonia;

public readonly record struct FontDialogState(
    string? FontFamily,
    float? FontSize,
    DocFontWeight? FontWeight,
    DocFontStyle? FontStyle,
    DocUnderlineStyle? UnderlineStyle,
    DocColor? UnderlineColor,
    DocColor? FontColor,
    bool? Strikethrough,
    bool? SmallCaps,
    bool? Caps,
    DocVerticalPosition? VerticalPosition,
    bool? TextOutline,
    bool? TextShadow,
    bool? TextEmboss,
    bool? TextImprint,
    float? CharacterScalePercent,
    float? CharacterSpacingPoints,
    float? CharacterPositionPoints,
    DocLigatureOptions? Ligatures,
    bool? ContextualAlternates,
    DocNumberForm? NumberForm,
    DocNumberSpacing? NumberSpacing,
    uint? StylisticSets);

public partial class FontDialog : Window
{
    private const float PointsToDipScale = 96f / 72f;
    private readonly ComboBox _fontFamilyCombo;
    private readonly ComboBox _fontStyleCombo;
    private readonly ComboBox _fontSizeCombo;
    private readonly ComboBox _underlineStyleCombo;
    private readonly ComboBox _underlineColorCombo;
    private readonly ComboBox _fontColorCombo;
    private readonly CheckBox _strikethroughCheckBox;
    private readonly CheckBox _smallCapsCheckBox;
    private readonly CheckBox _allCapsCheckBox;
    private readonly CheckBox _outlineCheckBox;
    private readonly CheckBox _shadowCheckBox;
    private readonly CheckBox _embossCheckBox;
    private readonly CheckBox _imprintCheckBox;
    private readonly ComboBox _verticalPositionCombo;
    private readonly ComboBox _characterScaleCombo;
    private readonly ComboBox _characterSpacingCombo;
    private readonly TextBox _characterSpacingByTextBox;
    private readonly ComboBox _characterPositionCombo;
    private readonly TextBox _characterPositionByTextBox;
    private readonly ComboBox _ligaturesCombo;
    private readonly CheckBox _contextualAlternatesCheckBox;
    private readonly ComboBox _numberFormCombo;
    private readonly ComboBox _numberSpacingCombo;
    private readonly ComboBox _stylisticSetsModeCombo;
    private readonly CheckBox[] _stylisticSets;
    private readonly Panel _stylisticSetsPanel;
    private readonly TextBlock _previewText;
    private readonly TextBlock? _previewTextAdvanced;

    private static readonly IReadOnlyList<FontStyleOption> FontStyleOptions = new[]
    {
        new FontStyleOption("Regular", DocFontWeight.Normal, DocFontStyle.Normal),
        new FontStyleOption("Italic", DocFontWeight.Normal, DocFontStyle.Italic),
        new FontStyleOption("Bold", DocFontWeight.Bold, DocFontStyle.Normal),
        new FontStyleOption("Bold Italic", DocFontWeight.Bold, DocFontStyle.Italic)
    };

    private static readonly IReadOnlyList<UnderlineStyleOption> UnderlineStyleOptions = new[]
    {
        new UnderlineStyleOption("None", DocUnderlineStyle.None),
        new UnderlineStyleOption("Single", DocUnderlineStyle.Single),
        new UnderlineStyleOption("Double", DocUnderlineStyle.Double),
        new UnderlineStyleOption("Dotted", DocUnderlineStyle.Dotted),
        new UnderlineStyleOption("Dash", DocUnderlineStyle.Dash),
        new UnderlineStyleOption("Dash Long", DocUnderlineStyle.DashLong),
        new UnderlineStyleOption("Dot Dash", DocUnderlineStyle.DotDash),
        new UnderlineStyleOption("Dot Dot Dash", DocUnderlineStyle.DotDotDash),
        new UnderlineStyleOption("Wave", DocUnderlineStyle.Wave)
    };

    private static readonly IReadOnlyList<FontDialogColorItem> ColorOptions = new[]
    {
        new FontDialogColorItem("Automatic", null),
        new FontDialogColorItem("Black", new DocColor(0, 0, 0)),
        new FontDialogColorItem("Gray", new DocColor(102, 102, 102)),
        new FontDialogColorItem("Red", new DocColor(192, 0, 0)),
        new FontDialogColorItem("Orange", new DocColor(230, 145, 56)),
        new FontDialogColorItem("Yellow", new DocColor(241, 194, 50)),
        new FontDialogColorItem("Green", new DocColor(106, 168, 79)),
        new FontDialogColorItem("Teal", new DocColor(69, 129, 142)),
        new FontDialogColorItem("Blue", new DocColor(61, 133, 198)),
        new FontDialogColorItem("Purple", new DocColor(142, 124, 195))
    };

    private static readonly IReadOnlyList<VerticalPositionOption> VerticalPositionOptions = new[]
    {
        new VerticalPositionOption("Normal", DocVerticalPosition.Normal),
        new VerticalPositionOption("Superscript", DocVerticalPosition.Superscript),
        new VerticalPositionOption("Subscript", DocVerticalPosition.Subscript)
    };

    private static readonly IReadOnlyList<CharacterSpacingOption> CharacterSpacingOptions = new[]
    {
        new CharacterSpacingOption("Normal", CharacterSpacingKind.Normal),
        new CharacterSpacingOption("Expanded", CharacterSpacingKind.Expanded),
        new CharacterSpacingOption("Condensed", CharacterSpacingKind.Condensed)
    };

    private static readonly IReadOnlyList<CharacterPositionOption> CharacterPositionOptions = new[]
    {
        new CharacterPositionOption("Normal", CharacterPositionKind.Normal),
        new CharacterPositionOption("Raised", CharacterPositionKind.Raised),
        new CharacterPositionOption("Lowered", CharacterPositionKind.Lowered)
    };

    private static readonly IReadOnlyList<LigatureOption> LigatureOptions = new[]
    {
        new LigatureOption("Not set", null),
        new LigatureOption("None", DocLigatureOptions.None),
        new LigatureOption("Standard", DocLigatureOptions.Standard),
        new LigatureOption("Contextual", DocLigatureOptions.Contextual),
        new LigatureOption("Discretional", DocLigatureOptions.Discretional),
        new LigatureOption("Historical", DocLigatureOptions.Historical),
        new LigatureOption("Standard + Contextual", DocLigatureOptions.Standard | DocLigatureOptions.Contextual),
        new LigatureOption("Standard + Discretional", DocLigatureOptions.Standard | DocLigatureOptions.Discretional),
        new LigatureOption("Standard + Contextual + Historical", DocLigatureOptions.Standard | DocLigatureOptions.Contextual | DocLigatureOptions.Historical),
        new LigatureOption("All", DocLigatureOptions.Standard | DocLigatureOptions.Contextual | DocLigatureOptions.Discretional | DocLigatureOptions.Historical)
    };

    private static readonly IReadOnlyList<NumberFormOption> NumberFormOptions = new[]
    {
        new NumberFormOption("Not set", null),
        new NumberFormOption("Default", DocNumberForm.Default),
        new NumberFormOption("Lining", DocNumberForm.Lining),
        new NumberFormOption("Old-style", DocNumberForm.OldStyle)
    };

    private static readonly IReadOnlyList<NumberSpacingOption> NumberSpacingOptions = new[]
    {
        new NumberSpacingOption("Not set", null),
        new NumberSpacingOption("Default", DocNumberSpacing.Default),
        new NumberSpacingOption("Proportional", DocNumberSpacing.Proportional),
        new NumberSpacingOption("Tabular", DocNumberSpacing.Tabular)
    };

    private static readonly IReadOnlyList<StylisticSetsModeOption> StylisticSetsModeOptions = new[]
    {
        new StylisticSetsModeOption("Use defaults", StylisticSetsMode.Default),
        new StylisticSetsModeOption("None", StylisticSetsMode.None),
        new StylisticSetsModeOption("Custom", StylisticSetsMode.Custom)
    };

    private static readonly IReadOnlyList<ScaleOption> ScaleOptions = new[]
    {
        new ScaleOption("50%", 50f),
        new ScaleOption("75%", 75f),
        new ScaleOption("90%", 90f),
        new ScaleOption("100%", 100f),
        new ScaleOption("110%", 110f),
        new ScaleOption("120%", 120f),
        new ScaleOption("150%", 150f),
        new ScaleOption("200%", 200f)
    };

    public FontDialog()
        : this(Array.Empty<string>(), default)
    {
    }

    public FontDialog(IReadOnlyList<string> fonts, FontDialogState state)
    {
        InitializeComponent();
        _fontFamilyCombo = this.FindControl<ComboBox>("FontFamilyCombo")!;
        _fontStyleCombo = this.FindControl<ComboBox>("FontStyleCombo")!;
        _fontSizeCombo = this.FindControl<ComboBox>("FontSizeCombo")!;
        _underlineStyleCombo = this.FindControl<ComboBox>("UnderlineStyleCombo")!;
        _underlineColorCombo = this.FindControl<ComboBox>("UnderlineColorCombo")!;
        _fontColorCombo = this.FindControl<ComboBox>("FontColorCombo")!;
        _strikethroughCheckBox = this.FindControl<CheckBox>("StrikethroughCheckBox")!;
        _smallCapsCheckBox = this.FindControl<CheckBox>("SmallCapsCheckBox")!;
        _allCapsCheckBox = this.FindControl<CheckBox>("AllCapsCheckBox")!;
        _outlineCheckBox = this.FindControl<CheckBox>("OutlineCheckBox")!;
        _shadowCheckBox = this.FindControl<CheckBox>("ShadowCheckBox")!;
        _embossCheckBox = this.FindControl<CheckBox>("EmbossCheckBox")!;
        _imprintCheckBox = this.FindControl<CheckBox>("ImprintCheckBox")!;
        _verticalPositionCombo = this.FindControl<ComboBox>("VerticalPositionCombo")!;
        _characterScaleCombo = this.FindControl<ComboBox>("CharacterScaleCombo")!;
        _characterSpacingCombo = this.FindControl<ComboBox>("CharacterSpacingCombo")!;
        _characterSpacingByTextBox = this.FindControl<TextBox>("CharacterSpacingByTextBox")!;
        _characterPositionCombo = this.FindControl<ComboBox>("CharacterPositionCombo")!;
        _characterPositionByTextBox = this.FindControl<TextBox>("CharacterPositionByTextBox")!;
        _ligaturesCombo = this.FindControl<ComboBox>("LigaturesCombo")!;
        _contextualAlternatesCheckBox = this.FindControl<CheckBox>("ContextualAlternatesCheckBox")!;
        _numberFormCombo = this.FindControl<ComboBox>("NumberFormCombo")!;
        _numberSpacingCombo = this.FindControl<ComboBox>("NumberSpacingCombo")!;
        _stylisticSetsModeCombo = this.FindControl<ComboBox>("StylisticSetsModeCombo")!;
        _stylisticSetsPanel = this.FindControl<Panel>("StylisticSetsPanel")!;
        _stylisticSets = BuildStylisticSets();
        _previewText = this.FindControl<TextBlock>("PreviewText")!;
        _previewTextAdvanced = this.FindControl<TextBlock>("PreviewTextAdvanced");

        _fontFamilyCombo.ItemsSource = fonts;
        _fontStyleCombo.ItemsSource = FontStyleOptions;
        _fontSizeCombo.ItemsSource = BuildStandardFontSizes();
        _underlineStyleCombo.ItemsSource = UnderlineStyleOptions;
        _underlineColorCombo.ItemsSource = ColorOptions;
        _fontColorCombo.ItemsSource = ColorOptions;
        _verticalPositionCombo.ItemsSource = VerticalPositionOptions;
        _characterScaleCombo.ItemsSource = ScaleOptions;
        _characterSpacingCombo.ItemsSource = CharacterSpacingOptions;
        _characterPositionCombo.ItemsSource = CharacterPositionOptions;
        _ligaturesCombo.ItemsSource = LigatureOptions;
        _numberFormCombo.ItemsSource = NumberFormOptions;
        _numberSpacingCombo.ItemsSource = NumberSpacingOptions;
        _stylisticSetsModeCombo.ItemsSource = StylisticSetsModeOptions;

        SetState(state);

        _fontFamilyCombo.PropertyChanged += OnComboTextChanged;
        _fontSizeCombo.PropertyChanged += OnComboTextChanged;
        _characterScaleCombo.PropertyChanged += OnComboTextChanged;
    }

    private static IReadOnlyList<string> BuildStandardFontSizes()
    {
        return new[]
        {
            "8", "9", "10", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "36", "48", "72"
        };
    }

    public void SetState(FontDialogState state)
    {
        SetComboSelection(_fontFamilyCombo, state.FontFamily);
        SetFontStyleSelection(state.FontWeight, state.FontStyle);
        SetComboSelection(
            _fontSizeCombo,
            state.FontSize.HasValue ? DipToPoints(state.FontSize.Value).ToString("0.#", CultureInfo.CurrentCulture) : null);
        SetUnderlineStyleSelection(state.UnderlineStyle);
        SetColorSelection(_underlineColorCombo, state.UnderlineColor);
        SetColorSelection(_fontColorCombo, state.FontColor);
        _strikethroughCheckBox.IsChecked = state.Strikethrough;
        _smallCapsCheckBox.IsChecked = state.SmallCaps;
        _allCapsCheckBox.IsChecked = state.Caps;
        _outlineCheckBox.IsChecked = state.TextOutline;
        _shadowCheckBox.IsChecked = state.TextShadow;
        _embossCheckBox.IsChecked = state.TextEmboss;
        _imprintCheckBox.IsChecked = state.TextImprint;
        SetVerticalPositionSelection(state.VerticalPosition);
        SetCharacterScaleSelection(state.CharacterScalePercent);
        SetCharacterSpacingSelection(state.CharacterSpacingPoints);
        SetCharacterPositionSelection(state.CharacterPositionPoints);
        SetLigatureSelection(state.Ligatures);
        _contextualAlternatesCheckBox.IsChecked = state.ContextualAlternates;
        SetNumberFormSelection(state.NumberForm);
        SetNumberSpacingSelection(state.NumberSpacing);
        SetStylisticSetsSelection(state.StylisticSets);
        UpdateCharacterSpacingState();
        UpdateCharacterPositionState();
        UpdateStylisticSetsState();
        UpdatePreview();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var options = BuildOptions();
        if (options.HasValue)
        {
            Close(options.Value);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateStylisticSetsState();
        UpdatePreview();
    }

    private void OnCharacterSpacingChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateCharacterSpacingState();
        UpdatePreview();
    }

    private void OnCharacterPositionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateCharacterPositionState();
        UpdatePreview();
    }

    private void OnComboTextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ComboBox.TextProperty)
        {
            UpdatePreview();
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void OnToggleChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private EditorFontDialogOptions? BuildOptions()
    {
        var fontFamily = _fontFamilyCombo.Text;
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            fontFamily = null;
        }

        float? fontSize = null;
        if (!string.IsNullOrWhiteSpace(_fontSizeCombo.Text)
            && float.TryParse(_fontSizeCombo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size))
        {
            fontSize = PointsToDip(Math.Max(1f, size));
        }

        var styleOption = _fontStyleCombo.SelectedItem as FontStyleOption;
        var underlineStyle = (_underlineStyleCombo.SelectedItem as UnderlineStyleOption)?.Style;
        var underlineColor = (_underlineColorCombo.SelectedItem as FontDialogColorItem)?.Color;
        var fontColor = (_fontColorCombo.SelectedItem as FontDialogColorItem)?.Color;
        var position = (_verticalPositionCombo.SelectedItem as VerticalPositionOption)?.Position;
        var characterScale = TryParsePercent(_characterScaleCombo.Text, out var scalePercent)
            ? scalePercent / 100f
            : (float?)null;

        var letterSpacing = ResolveLetterSpacing();
        var baselineOffset = ResolveBaselineOffset();
        var openTypeFeatures = BuildOpenTypeFeatures();

        return new EditorFontDialogOptions(
            fontFamily,
            fontSize,
            styleOption?.Weight,
            styleOption?.Style,
            underlineStyle,
            underlineColor,
            fontColor,
            _strikethroughCheckBox.IsChecked,
            _smallCapsCheckBox.IsChecked,
            _allCapsCheckBox.IsChecked,
            position,
            _outlineCheckBox.IsChecked,
            _shadowCheckBox.IsChecked,
            _embossCheckBox.IsChecked,
            _imprintCheckBox.IsChecked,
            letterSpacing,
            characterScale,
            baselineOffset,
            openTypeFeatures);
    }

    private void UpdatePreview()
    {
        ApplyPreview(_previewText);
        if (_previewTextAdvanced is not null)
        {
            ApplyPreview(_previewTextAdvanced);
        }
    }

    private void ApplyPreview(TextBlock target)
    {
        if (!string.IsNullOrWhiteSpace(_fontFamilyCombo.Text))
        {
            target.FontFamily = new FontFamily(_fontFamilyCombo.Text);
        }

        if (float.TryParse(_fontSizeCombo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size))
        {
            target.FontSize = Math.Max(1f, PointsToDip(size));
        }

        if (_fontStyleCombo.SelectedItem is FontStyleOption styleOption)
        {
            target.FontWeight = styleOption.Weight == DocFontWeight.Bold ? FontWeight.Bold : FontWeight.Normal;
            target.FontStyle = styleOption.Style == DocFontStyle.Italic ? FontStyle.Italic : FontStyle.Normal;
        }

        var previewText = "AaBbYyZz";
        if (_allCapsCheckBox.IsChecked == true || _smallCapsCheckBox.IsChecked == true)
        {
            previewText = previewText.ToUpper(CultureInfo.CurrentCulture);
        }

        target.Text = previewText;
        target.TextDecorations = BuildTextDecorations();

        if (_fontColorCombo.SelectedItem is FontDialogColorItem colorItem && colorItem.Color.HasValue)
        {
            target.Foreground = new SolidColorBrush(ToColor(colorItem.Color.Value));
        }
        else
        {
            target.ClearValue(TextBlock.ForegroundProperty);
        }

        if (TryParsePercent(_characterScaleCombo.Text, out var scalePercent))
        {
            var scale = MathF.Max(0.1f, scalePercent / 100f);
            if (MathF.Abs(scale - 1f) > 0.01f)
            {
                target.RenderTransform = new ScaleTransform(scale, 1f);
                target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            }
            else
            {
                target.RenderTransform = null;
            }
        }
        else
        {
            target.RenderTransform = null;
        }
    }

    private TextDecorationCollection? BuildTextDecorations()
    {
        var underlineStyle = (_underlineStyleCombo.SelectedItem as UnderlineStyleOption)?.Style ?? DocUnderlineStyle.None;
        var underline = underlineStyle != DocUnderlineStyle.None;
        var strike = _strikethroughCheckBox.IsChecked == true;
        if (!underline && !strike)
        {
            return null;
        }

        var decorations = new TextDecorationCollection();
        if (underline)
        {
            var underlineStroke = TryGetUnderlineStroke();
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline, Stroke = underlineStroke });
        }

        if (strike)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        }

        return decorations;
    }

    private IBrush? TryGetUnderlineStroke()
    {
        if (_underlineColorCombo.SelectedItem is FontDialogColorItem colorItem && colorItem.Color.HasValue)
        {
            return new SolidColorBrush(ToColor(colorItem.Color.Value));
        }

        return null;
    }

    private static Color ToColor(DocColor color)
    {
        return Color.FromArgb(255, color.R, color.G, color.B);
    }

    private void SetFontStyleSelection(DocFontWeight? weight, DocFontStyle? style)
    {
        if (!weight.HasValue || !style.HasValue)
        {
            _fontStyleCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in FontStyleOptions)
        {
            if (item.Weight == weight.Value && item.Style == style.Value)
            {
                _fontStyleCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SetUnderlineStyleSelection(DocUnderlineStyle? style)
    {
        if (!style.HasValue)
        {
            _underlineStyleCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in UnderlineStyleOptions)
        {
            if (item.Style == style.Value)
            {
                _underlineStyleCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SetColorSelection(ComboBox combo, DocColor? color)
    {
        if (!color.HasValue)
        {
            combo.SelectedIndex = 0;
            return;
        }

        foreach (var item in ColorOptions)
        {
            if (item.Color.HasValue && item.Color.Value == color.Value)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void SetVerticalPositionSelection(DocVerticalPosition? position)
    {
        if (!position.HasValue)
        {
            _verticalPositionCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in VerticalPositionOptions)
        {
            if (item.Position == position.Value)
            {
                _verticalPositionCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SetCharacterScaleSelection(float? percent)
    {
        if (!percent.HasValue)
        {
            _characterScaleCombo.SelectedIndex = -1;
            _characterScaleCombo.Text = string.Empty;
            return;
        }

        foreach (var option in ScaleOptions)
        {
            if (IsClose(option.Percent, percent.Value))
            {
                _characterScaleCombo.SelectedItem = option;
                _characterScaleCombo.Text = option.Label;
                return;
            }
        }

        _characterScaleCombo.SelectedIndex = -1;
        _characterScaleCombo.Text = FormatPercent(percent.Value);
    }

    private void SetCharacterSpacingSelection(float? spacingPoints)
    {
        if (!spacingPoints.HasValue)
        {
            _characterSpacingCombo.SelectedIndex = -1;
            _characterSpacingByTextBox.Text = string.Empty;
            return;
        }

        var spacing = spacingPoints.Value;
        var kind = CharacterSpacingKind.Normal;
        var byPoints = MathF.Abs(spacing);
        if (MathF.Abs(spacing) < 0.01f)
        {
            kind = CharacterSpacingKind.Normal;
            byPoints = 0f;
        }
        else
        {
            kind = spacing > 0f ? CharacterSpacingKind.Expanded : CharacterSpacingKind.Condensed;
        }

        SetCharacterSpacingKind(kind);
        _characterSpacingByTextBox.Text = byPoints.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void SetCharacterPositionSelection(float? positionPoints)
    {
        if (!positionPoints.HasValue)
        {
            _characterPositionCombo.SelectedIndex = -1;
            _characterPositionByTextBox.Text = string.Empty;
            return;
        }

        var offset = positionPoints.Value;
        var kind = CharacterPositionKind.Normal;
        var byPoints = MathF.Abs(offset);
        if (MathF.Abs(offset) < 0.01f)
        {
            kind = CharacterPositionKind.Normal;
            byPoints = 0f;
        }
        else
        {
            kind = offset > 0f ? CharacterPositionKind.Raised : CharacterPositionKind.Lowered;
        }

        SetCharacterPositionKind(kind);
        _characterPositionByTextBox.Text = byPoints.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void SetCharacterSpacingKind(CharacterSpacingKind kind)
    {
        foreach (var option in CharacterSpacingOptions)
        {
            if (option.Kind == kind)
            {
                _characterSpacingCombo.SelectedItem = option;
                return;
            }
        }

        _characterSpacingCombo.SelectedIndex = -1;
    }

    private void SetCharacterPositionKind(CharacterPositionKind kind)
    {
        foreach (var option in CharacterPositionOptions)
        {
            if (option.Kind == kind)
            {
                _characterPositionCombo.SelectedItem = option;
                return;
            }
        }

        _characterPositionCombo.SelectedIndex = -1;
    }

    private void UpdateCharacterSpacingState()
    {
        var kind = (_characterSpacingCombo.SelectedItem as CharacterSpacingOption)?.Kind;
        _characterSpacingByTextBox.IsEnabled = kind is CharacterSpacingKind.Expanded or CharacterSpacingKind.Condensed;
    }

    private void UpdateCharacterPositionState()
    {
        var kind = (_characterPositionCombo.SelectedItem as CharacterPositionOption)?.Kind;
        _characterPositionByTextBox.IsEnabled = kind is CharacterPositionKind.Raised or CharacterPositionKind.Lowered;
    }

    private void SetLigatureSelection(DocLigatureOptions? value)
    {
        SetOptionSelection(_ligaturesCombo, LigatureOptions, value);
    }

    private void SetNumberFormSelection(DocNumberForm? value)
    {
        SetOptionSelection(_numberFormCombo, NumberFormOptions, value);
    }

    private void SetNumberSpacingSelection(DocNumberSpacing? value)
    {
        SetOptionSelection(_numberSpacingCombo, NumberSpacingOptions, value);
    }

    private void SetStylisticSetsSelection(uint? sets)
    {
        if (!sets.HasValue)
        {
            _stylisticSetsModeCombo.SelectedItem = StylisticSetsModeOptions[0];
            SetStylisticSetsIndeterminate();
            return;
        }

        if (sets.Value == 0u)
        {
            _stylisticSetsModeCombo.SelectedItem = StylisticSetsModeOptions[1];
            SetStylisticSetsChecked(false);
            return;
        }

        _stylisticSetsModeCombo.SelectedItem = StylisticSetsModeOptions[2];
        for (var i = 0; i < _stylisticSets.Length; i++)
        {
            var bit = 1u << i;
            _stylisticSets[i].IsChecked = (sets.Value & bit) != 0u;
        }
    }

    private void SetStylisticSetsChecked(bool value)
    {
        for (var i = 0; i < _stylisticSets.Length; i++)
        {
            _stylisticSets[i].IsChecked = value;
        }
    }

    private void SetStylisticSetsIndeterminate()
    {
        for (var i = 0; i < _stylisticSets.Length; i++)
        {
            _stylisticSets[i].IsChecked = null;
        }
    }

    private void UpdateStylisticSetsState()
    {
        var mode = (_stylisticSetsModeCombo.SelectedItem as StylisticSetsModeOption)?.Mode ?? StylisticSetsMode.Default;
        var enable = mode == StylisticSetsMode.Custom;
        _stylisticSetsPanel.IsEnabled = enable;
        if (enable)
        {
            return;
        }

        if (mode == StylisticSetsMode.None)
        {
            SetStylisticSetsChecked(false);
        }
        else if (mode == StylisticSetsMode.Default)
        {
            SetStylisticSetsIndeterminate();
        }
    }

    private float? ResolveLetterSpacing()
    {
        if (_characterSpacingCombo.SelectedItem is not CharacterSpacingOption spacingOption)
        {
            return null;
        }

        var byPoints = ParsePoints(_characterSpacingByTextBox.Text) ?? 0f;
        var spacingDip = PointsToDip(Math.Max(0f, byPoints));
        return spacingOption.Kind switch
        {
            CharacterSpacingKind.Normal => 0f,
            CharacterSpacingKind.Expanded => spacingDip,
            CharacterSpacingKind.Condensed => -spacingDip,
            _ => null
        };
    }

    private float? ResolveBaselineOffset()
    {
        if (_characterPositionCombo.SelectedItem is not CharacterPositionOption positionOption)
        {
            return null;
        }

        var byPoints = ParsePoints(_characterPositionByTextBox.Text) ?? 0f;
        var offsetDip = PointsToDip(Math.Max(0f, byPoints));
        return positionOption.Kind switch
        {
            CharacterPositionKind.Normal => 0f,
            CharacterPositionKind.Raised => offsetDip,
            CharacterPositionKind.Lowered => -offsetDip,
            _ => null
        };
    }

    private static bool TryParsePercent(string? text, out float percent)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            percent = 0f;
            return false;
        }

        var trimmed = text.Trim().Replace("%", string.Empty, StringComparison.Ordinal);
        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out percent);
    }

    private static float? ParsePoints(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            ? value
            : null;
    }

    private static float PointsToDip(float points) => points * PointsToDipScale;
    private static float DipToPoints(float dips) => dips / PointsToDipScale;

    private static bool IsClose(float value, float target) => MathF.Abs(value - target) < 0.1f;

    private static string FormatPercent(float value) =>
        value.ToString("0.#", CultureInfo.CurrentCulture) + "%";

    private static void SetComboSelection(ComboBox combo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            combo.SelectedIndex = -1;
            combo.Text = string.Empty;
            return;
        }

        foreach (var item in combo.Items)
        {
            if (item is string text && string.Equals(text, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                combo.Text = text;
                return;
            }
        }

        combo.SelectedIndex = -1;
        combo.Text = value;
    }

    private sealed record FontStyleOption(string Label, DocFontWeight Weight, DocFontStyle Style)
    {
        public override string ToString() => Label;
    }

    private sealed record UnderlineStyleOption(string Label, DocUnderlineStyle Style)
    {
        public override string ToString() => Label;
    }

    private sealed record FontDialogColorItem(string Label, DocColor? Color)
    {
        public override string ToString() => Label;
    }

    private sealed record LigatureOption(string Label, DocLigatureOptions? Options)
    {
        public override string ToString() => Label;
    }

    private sealed record NumberFormOption(string Label, DocNumberForm? Form)
    {
        public override string ToString() => Label;
    }

    private sealed record NumberSpacingOption(string Label, DocNumberSpacing? Spacing)
    {
        public override string ToString() => Label;
    }

    private enum StylisticSetsMode
    {
        Default,
        None,
        Custom
    }

    private sealed record StylisticSetsModeOption(string Label, StylisticSetsMode Mode)
    {
        public override string ToString() => Label;
    }

    private enum CharacterSpacingKind
    {
        Normal,
        Expanded,
        Condensed
    }

    private enum CharacterPositionKind
    {
        Normal,
        Raised,
        Lowered
    }

    private sealed record VerticalPositionOption(string Label, DocVerticalPosition Position)
    {
        public override string ToString() => Label;
    }

    private sealed record CharacterSpacingOption(string Label, CharacterSpacingKind Kind)
    {
        public override string ToString() => Label;
    }

    private sealed record CharacterPositionOption(string Label, CharacterPositionKind Kind)
    {
        public override string ToString() => Label;
    }

    private sealed record ScaleOption(string Label, float Percent)
    {
        public override string ToString() => Label;
    }

    private static void SetOptionSelection<TOption, TValue>(
        ComboBox combo,
        IReadOnlyList<TOption> options,
        TValue? value)
        where TValue : struct
        where TOption : class
    {
        if (!value.HasValue)
        {
            combo.SelectedItem = options[0];
            return;
        }

        foreach (var option in options)
        {
            switch (option)
            {
                case LigatureOption ligatureOption when typeof(TValue) == typeof(DocLigatureOptions)
                                                     && ligatureOption.Options.HasValue
                                                     && EqualityComparer<TValue>.Default.Equals(
                                                         value.Value,
                                                         (TValue)(object)ligatureOption.Options.Value):
                    combo.SelectedItem = option;
                    return;
                case NumberFormOption numberFormOption when typeof(TValue) == typeof(DocNumberForm)
                                                        && numberFormOption.Form.HasValue
                                                        && EqualityComparer<TValue>.Default.Equals(
                                                            value.Value,
                                                            (TValue)(object)numberFormOption.Form.Value):
                    combo.SelectedItem = option;
                    return;
                case NumberSpacingOption numberSpacingOption when typeof(TValue) == typeof(DocNumberSpacing)
                                                              && numberSpacingOption.Spacing.HasValue
                                                              && EqualityComparer<TValue>.Default.Equals(
                                                                  value.Value,
                                                                  (TValue)(object)numberSpacingOption.Spacing.Value):
                    combo.SelectedItem = option;
                    return;
            }
        }

        combo.SelectedItem = options[0];
    }

    private CheckBox[] BuildStylisticSets()
    {
        return new[]
        {
            this.FindControl<CheckBox>("StylisticSet01CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet02CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet03CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet04CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet05CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet06CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet07CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet08CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet09CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet10CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet11CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet12CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet13CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet14CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet15CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet16CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet17CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet18CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet19CheckBox")!,
            this.FindControl<CheckBox>("StylisticSet20CheckBox")!
        };
    }

    private TextOpenTypeFeatures? BuildOpenTypeFeatures()
    {
        TextOpenTypeFeatures? features = null;

        if (_ligaturesCombo.SelectedItem is LigatureOption ligatureOption && ligatureOption.Options.HasValue)
        {
            features ??= new TextOpenTypeFeatures();
            features.Ligatures = ligatureOption.Options.Value;
        }

        if (_contextualAlternatesCheckBox.IsChecked.HasValue)
        {
            features ??= new TextOpenTypeFeatures();
            features.ContextualAlternates = _contextualAlternatesCheckBox.IsChecked.Value;
        }

        if (_numberFormCombo.SelectedItem is NumberFormOption numberFormOption && numberFormOption.Form.HasValue)
        {
            features ??= new TextOpenTypeFeatures();
            features.NumberForm = numberFormOption.Form.Value;
        }

        if (_numberSpacingCombo.SelectedItem is NumberSpacingOption numberSpacingOption && numberSpacingOption.Spacing.HasValue)
        {
            features ??= new TextOpenTypeFeatures();
            features.NumberSpacing = numberSpacingOption.Spacing.Value;
        }

        var stylisticSets = ResolveStylisticSets();
        if (stylisticSets.HasValue)
        {
            features ??= new TextOpenTypeFeatures();
            features.StylisticSets = stylisticSets.Value;
        }

        return features;
    }

    private uint? ResolveStylisticSets()
    {
        if (_stylisticSetsModeCombo.SelectedItem is not StylisticSetsModeOption modeOption)
        {
            return null;
        }

        return modeOption.Mode switch
        {
            StylisticSetsMode.Default => null,
            StylisticSetsMode.None => 0u,
            StylisticSetsMode.Custom => BuildStylisticSetMask(),
            _ => null
        };
    }

    private uint BuildStylisticSetMask()
    {
        var mask = 0u;
        for (var i = 0; i < _stylisticSets.Length; i++)
        {
            if (_stylisticSets[i].IsChecked == true)
            {
                mask |= 1u << i;
            }
        }

        return mask;
    }
}
