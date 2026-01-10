using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.App;

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
    DocVerticalPosition? VerticalPosition,
    bool? TextOutline,
    bool? TextShadow,
    bool? TextEmboss,
    bool? TextImprint);

public partial class FontDialog : Window
{
    private readonly ComboBox _fontFamilyCombo;
    private readonly ComboBox _fontStyleCombo;
    private readonly ComboBox _fontSizeCombo;
    private readonly ComboBox _underlineStyleCombo;
    private readonly ComboBox _underlineColorCombo;
    private readonly ComboBox _fontColorCombo;
    private readonly CheckBox _strikethroughCheckBox;
    private readonly CheckBox _smallCapsCheckBox;
    private readonly CheckBox _outlineCheckBox;
    private readonly CheckBox _shadowCheckBox;
    private readonly CheckBox _embossCheckBox;
    private readonly CheckBox _imprintCheckBox;
    private readonly ComboBox _positionCombo;
    private readonly TextBlock _previewText;

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

    private static readonly IReadOnlyList<PositionOption> PositionOptions = new[]
    {
        new PositionOption("Normal", DocVerticalPosition.Normal),
        new PositionOption("Superscript", DocVerticalPosition.Superscript),
        new PositionOption("Subscript", DocVerticalPosition.Subscript)
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
        _outlineCheckBox = this.FindControl<CheckBox>("OutlineCheckBox")!;
        _shadowCheckBox = this.FindControl<CheckBox>("ShadowCheckBox")!;
        _embossCheckBox = this.FindControl<CheckBox>("EmbossCheckBox")!;
        _imprintCheckBox = this.FindControl<CheckBox>("ImprintCheckBox")!;
        _positionCombo = this.FindControl<ComboBox>("PositionCombo")!;
        _previewText = this.FindControl<TextBlock>("PreviewText")!;

        _fontFamilyCombo.ItemsSource = fonts;
        _fontStyleCombo.ItemsSource = FontStyleOptions;
        _fontSizeCombo.ItemsSource = BuildStandardFontSizes();
        _underlineStyleCombo.ItemsSource = UnderlineStyleOptions;
        _underlineColorCombo.ItemsSource = ColorOptions;
        _fontColorCombo.ItemsSource = ColorOptions;
        _positionCombo.ItemsSource = PositionOptions;

        SetState(state);

        _fontFamilyCombo.PropertyChanged += OnComboTextChanged;
        _fontSizeCombo.PropertyChanged += OnComboTextChanged;
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
        SetComboSelection(_fontSizeCombo, state.FontSize?.ToString("0.#", CultureInfo.CurrentCulture));
        SetUnderlineStyleSelection(state.UnderlineStyle);
        SetColorSelection(_underlineColorCombo, state.UnderlineColor);
        SetColorSelection(_fontColorCombo, state.FontColor);
        _strikethroughCheckBox.IsChecked = state.Strikethrough;
        _smallCapsCheckBox.IsChecked = state.SmallCaps;
        _outlineCheckBox.IsChecked = state.TextOutline;
        _shadowCheckBox.IsChecked = state.TextShadow;
        _embossCheckBox.IsChecked = state.TextEmboss;
        _imprintCheckBox.IsChecked = state.TextImprint;
        SetPositionSelection(state.VerticalPosition);
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

    private void OnSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void OnComboTextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ComboBox.TextProperty)
        {
            UpdatePreview();
        }
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
            fontSize = size;
        }

        var styleOption = _fontStyleCombo.SelectedItem as FontStyleOption;
        var underlineStyle = (_underlineStyleCombo.SelectedItem as UnderlineStyleOption)?.Style;
        var underlineColor = (_underlineColorCombo.SelectedItem as FontDialogColorItem)?.Color;
        var fontColor = (_fontColorCombo.SelectedItem as FontDialogColorItem)?.Color;
        var position = (_positionCombo.SelectedItem as PositionOption)?.Position;

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
            position,
            _outlineCheckBox.IsChecked,
            _shadowCheckBox.IsChecked,
            _embossCheckBox.IsChecked,
            _imprintCheckBox.IsChecked);
    }

    private void UpdatePreview()
    {
        if (!string.IsNullOrWhiteSpace(_fontFamilyCombo.Text))
        {
            _previewText.FontFamily = new FontFamily(_fontFamilyCombo.Text);
        }

        if (float.TryParse(_fontSizeCombo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size))
        {
            _previewText.FontSize = Math.Max(1f, size);
        }

        if (_fontStyleCombo.SelectedItem is FontStyleOption styleOption)
        {
            _previewText.FontWeight = styleOption.Weight == DocFontWeight.Bold ? FontWeight.Bold : FontWeight.Normal;
            _previewText.FontStyle = styleOption.Style == DocFontStyle.Italic ? FontStyle.Italic : FontStyle.Normal;
        }

        _previewText.TextDecorations = BuildTextDecorations();

        if (_fontColorCombo.SelectedItem is FontDialogColorItem colorItem && colorItem.Color.HasValue)
        {
            _previewText.Foreground = new SolidColorBrush(ToColor(colorItem.Color.Value));
        }
        else
        {
            _previewText.ClearValue(TextBlock.ForegroundProperty);
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
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        }

        if (strike)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        }

        return decorations;
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

    private void SetPositionSelection(DocVerticalPosition? position)
    {
        if (!position.HasValue)
        {
            _positionCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in PositionOptions)
        {
            if (item.Position == position.Value)
            {
                _positionCombo.SelectedItem = item;
                return;
            }
        }
    }

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

    private sealed record PositionOption(string Label, DocVerticalPosition Position)
    {
        public override string ToString() => Label;
    }
}
