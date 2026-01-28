using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

public enum ParagraphSpecialIndentKind
{
    None,
    FirstLine,
    Hanging
}

public readonly record struct ParagraphDialogState(
    ParagraphAlignment? Alignment,
    DocTextDirection? TextDirection,
    float? IndentLeftPoints,
    float? IndentRightPoints,
    ParagraphSpecialIndentKind? SpecialIndent,
    float? SpecialIndentByPoints,
    float? SpacingBeforePoints,
    float? SpacingAfterPoints,
    LineSpacingOptionKind? LineSpacingKind,
    float? LineSpacingAtValue,
    bool? ContextualSpacing,
    bool? RightToLeft,
    bool? WidowControl,
    bool? KeepWithNext,
    bool? KeepLinesTogether,
    bool? PageBreakBefore,
    bool? SuppressLineNumbers);

public partial class ParagraphDialog : Window
{
    private const float PointsToDipScale = 96f / 72f;
    private const float TwipsPerLine = 240f;
    private const float TwipsPerPoint = 20f;
    private const float DefaultMultiple = 1f;

    private readonly ComboBox _alignmentCombo;
    private readonly ComboBox _textDirectionCombo;
    private readonly TextBox _indentLeftTextBox;
    private readonly TextBox _indentRightTextBox;
    private readonly ComboBox _specialIndentCombo;
    private readonly TextBox _specialIndentByTextBox;
    private readonly TextBox _spacingBeforeTextBox;
    private readonly TextBox _spacingAfterTextBox;
    private readonly ComboBox _lineSpacingCombo;
    private readonly TextBox _lineSpacingAtTextBox;
    private readonly CheckBox _contextualSpacingCheckBox;
    private readonly CheckBox _rightToLeftCheckBox;
    private readonly CheckBox _widowControlCheckBox;
    private readonly CheckBox _keepWithNextCheckBox;
    private readonly CheckBox _keepLinesTogetherCheckBox;
    private readonly CheckBox _pageBreakBeforeCheckBox;
    private readonly CheckBox _suppressLineNumbersCheckBox;
    private readonly Border _previewBorder;
    private readonly Border _previewSpacingBefore;
    private readonly Border _previewSpacingAfter;
    private readonly TextBlock _previewText;

    public ParagraphDialog()
        : this(new ParagraphDialogState(
            ParagraphAlignment.Left,
            DocTextDirection.LeftToRightTopToBottom,
            0f,
            0f,
            ParagraphSpecialIndentKind.None,
            0f,
            0f,
            0f,
            LineSpacingOptionKind.Single,
            DefaultMultiple,
            false,
            false,
            true,
            false,
            false,
            false,
            false))
    {
    }

    public ParagraphDialog(ParagraphDialogState state)
    {
        InitializeComponent();
        _alignmentCombo = this.FindControl<ComboBox>("AlignmentCombo")!;
        _textDirectionCombo = this.FindControl<ComboBox>("TextDirectionCombo")!;
        _indentLeftTextBox = this.FindControl<TextBox>("IndentLeftTextBox")!;
        _indentRightTextBox = this.FindControl<TextBox>("IndentRightTextBox")!;
        _specialIndentCombo = this.FindControl<ComboBox>("SpecialIndentCombo")!;
        _specialIndentByTextBox = this.FindControl<TextBox>("SpecialIndentByTextBox")!;
        _spacingBeforeTextBox = this.FindControl<TextBox>("SpacingBeforeTextBox")!;
        _spacingAfterTextBox = this.FindControl<TextBox>("SpacingAfterTextBox")!;
        _lineSpacingCombo = this.FindControl<ComboBox>("LineSpacingCombo")!;
        _lineSpacingAtTextBox = this.FindControl<TextBox>("LineSpacingAtTextBox")!;
        _contextualSpacingCheckBox = this.FindControl<CheckBox>("ContextualSpacingCheckBox")!;
        _rightToLeftCheckBox = this.FindControl<CheckBox>("RightToLeftCheckBox")!;
        _widowControlCheckBox = this.FindControl<CheckBox>("WidowControlCheckBox")!;
        _keepWithNextCheckBox = this.FindControl<CheckBox>("KeepWithNextCheckBox")!;
        _keepLinesTogetherCheckBox = this.FindControl<CheckBox>("KeepLinesTogetherCheckBox")!;
        _pageBreakBeforeCheckBox = this.FindControl<CheckBox>("PageBreakBeforeCheckBox")!;
        _suppressLineNumbersCheckBox = this.FindControl<CheckBox>("SuppressLineNumbersCheckBox")!;
        _previewBorder = this.FindControl<Border>("PreviewBorder")!;
        _previewSpacingBefore = this.FindControl<Border>("PreviewSpacingBefore")!;
        _previewSpacingAfter = this.FindControl<Border>("PreviewSpacingAfter")!;
        _previewText = this.FindControl<TextBlock>("PreviewText")!;
        SetState(state);
    }

    public void SetState(ParagraphDialogState state)
    {
        SelectAlignment(state.Alignment);
        SelectTextDirection(state.TextDirection);
        _indentLeftTextBox.Text = FormatValue(state.IndentLeftPoints);
        _indentRightTextBox.Text = FormatValue(state.IndentRightPoints);
        SelectSpecialIndent(state.SpecialIndent);
        _specialIndentByTextBox.Text = FormatValue(state.SpecialIndentByPoints);
        _spacingBeforeTextBox.Text = FormatValue(state.SpacingBeforePoints);
        _spacingAfterTextBox.Text = FormatValue(state.SpacingAfterPoints);
        SelectLineSpacingKind(state.LineSpacingKind);
        _lineSpacingAtTextBox.Text = FormatValue(state.LineSpacingAtValue);
        _contextualSpacingCheckBox.IsChecked = state.ContextualSpacing;
        _rightToLeftCheckBox.IsChecked = state.RightToLeft;
        _widowControlCheckBox.IsChecked = state.WidowControl;
        _keepWithNextCheckBox.IsChecked = state.KeepWithNext;
        _keepLinesTogetherCheckBox.IsChecked = state.KeepLinesTogether;
        _pageBreakBeforeCheckBox.IsChecked = state.PageBreakBefore;
        _suppressLineNumbersCheckBox.IsChecked = state.SuppressLineNumbers;
        UpdateLineSpacingState();
        UpdateSpecialIndentState();
        UpdatePreview();
    }

    private void OnLineSpacingChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateLineSpacingState();
        UpdatePreview();
    }

    private void OnSpecialIndentChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateSpecialIndentState();
        UpdatePreview();
    }

    private void OnPreviewSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void OnPreviewTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void OnPreviewToggleChanged(object? sender, RoutedEventArgs e)
    {
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

    private EditorParagraphDialogOptions? BuildOptions()
    {
        if (!TryParseNullable(_indentLeftTextBox.Text, out var indentLeftPoints)
            || !TryParseNullable(_indentRightTextBox.Text, out var indentRightPoints)
            || !TryParseNullable(_spacingBeforeTextBox.Text, out var spacingBeforePoints)
            || !TryParseNullable(_spacingAfterTextBox.Text, out var spacingAfterPoints)
            || !TryParseNullable(_specialIndentByTextBox.Text, out var specialByPoints)
            || !TryParseNullable(_lineSpacingAtTextBox.Text, out var lineSpacingAt))
        {
            return null;
        }

        var alignment = GetSelectedAlignment();
        var textDirection = GetSelectedTextDirection();
        var specialIndent = GetSelectedSpecialIndent();
        float? firstLineIndent = null;
        if (specialIndent.HasValue)
        {
            var byValue = specialByPoints ?? 0f;
            var byDip = PointsToDip(Math.Max(0f, byValue));
            firstLineIndent = specialIndent.Value switch
            {
                ParagraphSpecialIndentKind.FirstLine => byDip,
                ParagraphSpecialIndentKind.Hanging => -byDip,
                _ => 0f
            };
        }

        var indentLeft = indentLeftPoints.HasValue ? PointsToDip(indentLeftPoints.Value) : (float?)null;
        var indentRight = indentRightPoints.HasValue ? PointsToDip(indentRightPoints.Value) : (float?)null;
        var spacingBefore = spacingBeforePoints.HasValue ? PointsToDip(spacingBeforePoints.Value) : (float?)null;
        var spacingAfter = spacingAfterPoints.HasValue ? PointsToDip(spacingAfterPoints.Value) : (float?)null;

        var lineSpacingKind = GetSelectedLineSpacingKind();
        var lineSpacing = ResolveLineSpacing(lineSpacingKind, lineSpacingAt, out var lineSpacingRule);

        return new EditorParagraphDialogOptions(
            alignment,
            indentLeft,
            indentRight,
            firstLineIndent,
            spacingBefore,
            spacingAfter,
            lineSpacing,
            lineSpacingRule,
            _contextualSpacingCheckBox.IsChecked,
            _keepWithNextCheckBox.IsChecked,
            _keepLinesTogetherCheckBox.IsChecked,
            _widowControlCheckBox.IsChecked,
            _pageBreakBeforeCheckBox.IsChecked,
            _suppressLineNumbersCheckBox.IsChecked,
            _rightToLeftCheckBox.IsChecked,
            textDirection);
    }

    private static int? ResolveLineSpacing(LineSpacingOptionKind? kind, float? atValue, out DocLineSpacingRule? rule)
    {
        rule = null;
        if (!kind.HasValue)
        {
            return null;
        }

        switch (kind.Value)
        {
            case LineSpacingOptionKind.Single:
                rule = DocLineSpacingRule.Auto;
                return (int)MathF.Round(TwipsPerLine);
            case LineSpacingOptionKind.One15:
                rule = DocLineSpacingRule.Auto;
                return (int)MathF.Round(TwipsPerLine * 1.15f);
            case LineSpacingOptionKind.One5:
                rule = DocLineSpacingRule.Auto;
                return (int)MathF.Round(TwipsPerLine * 1.5f);
            case LineSpacingOptionKind.Double:
                rule = DocLineSpacingRule.Auto;
                return (int)MathF.Round(TwipsPerLine * 2f);
            case LineSpacingOptionKind.AtLeast:
                rule = DocLineSpacingRule.AtLeast;
                return (int)MathF.Round(Math.Max(0f, atValue ?? 0f) * TwipsPerPoint);
            case LineSpacingOptionKind.Exactly:
                rule = DocLineSpacingRule.Exactly;
                return (int)MathF.Round(Math.Max(0f, atValue ?? 0f) * TwipsPerPoint);
            case LineSpacingOptionKind.Multiple:
                rule = DocLineSpacingRule.Auto;
                var multiple = MathF.Max(0f, atValue ?? DefaultMultiple);
                return (int)MathF.Round(TwipsPerLine * multiple);
            default:
                return null;
        }
    }

    private static float PointsToDip(float points) => points * PointsToDipScale;

    private static string? FormatValue(float? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.##", CultureInfo.CurrentCulture)
            : null;
    }

    private static bool TryParseNullable(string? text, out float? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private void SelectAlignment(ParagraphAlignment? alignment)
    {
        if (!alignment.HasValue)
        {
            _alignmentCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in _alignmentCombo.Items)
        {
            if (item is ComboBoxItem { Tag: ParagraphAlignment tag } && tag == alignment.Value)
            {
                _alignmentCombo.SelectedItem = item;
                return;
            }
        }
    }

    private ParagraphAlignment? GetSelectedAlignment()
    {
        return _alignmentCombo.SelectedItem is ComboBoxItem { Tag: ParagraphAlignment tag }
            ? tag
            : null;
    }

    private void SelectTextDirection(DocTextDirection? direction)
    {
        if (!direction.HasValue)
        {
            _textDirectionCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in _textDirectionCombo.Items)
        {
            if (item is ComboBoxItem { Tag: DocTextDirection tag } && tag == direction.Value)
            {
                _textDirectionCombo.SelectedItem = item;
                return;
            }
        }
    }

    private DocTextDirection? GetSelectedTextDirection()
    {
        return _textDirectionCombo.SelectedItem is ComboBoxItem { Tag: DocTextDirection tag }
            ? tag
            : null;
    }

    private void SelectSpecialIndent(ParagraphSpecialIndentKind? kind)
    {
        if (!kind.HasValue)
        {
            _specialIndentCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in _specialIndentCombo.Items)
        {
            if (item is ComboBoxItem { Tag: ParagraphSpecialIndentKind tag } && tag == kind.Value)
            {
                _specialIndentCombo.SelectedItem = item;
                return;
            }
        }
    }

    private ParagraphSpecialIndentKind? GetSelectedSpecialIndent()
    {
        return _specialIndentCombo.SelectedItem is ComboBoxItem { Tag: ParagraphSpecialIndentKind tag }
            ? tag
            : null;
    }

    private void SelectLineSpacingKind(LineSpacingOptionKind? kind)
    {
        if (!kind.HasValue)
        {
            _lineSpacingCombo.SelectedIndex = -1;
            return;
        }

        foreach (var item in _lineSpacingCombo.Items)
        {
            if (item is ComboBoxItem { Tag: LineSpacingOptionKind tag } && tag == kind.Value)
            {
                _lineSpacingCombo.SelectedItem = item;
                return;
            }
        }
    }

    private LineSpacingOptionKind? GetSelectedLineSpacingKind()
    {
        return _lineSpacingCombo.SelectedItem is ComboBoxItem { Tag: LineSpacingOptionKind tag }
            ? tag
            : null;
    }

    private void UpdatePreview()
    {
        var alignment = GetSelectedAlignment() ?? ParagraphAlignment.Left;
        _previewText.TextAlignment = alignment switch
        {
            ParagraphAlignment.Center => TextAlignment.Center,
            ParagraphAlignment.Right => TextAlignment.Right,
            ParagraphAlignment.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

        _previewText.FlowDirection = _rightToLeftCheckBox.IsChecked == true
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        var indentLeftPoints = ParsePointsOrZero(_indentLeftTextBox.Text);
        var indentRightPoints = ParsePointsOrZero(_indentRightTextBox.Text);
        _previewBorder.Padding = new Thickness(
            PointsToDip(MathF.Max(0f, indentLeftPoints)),
            _previewBorder.Padding.Top,
            PointsToDip(MathF.Max(0f, indentRightPoints)),
            _previewBorder.Padding.Bottom);

        var spacingBeforePoints = ParsePointsOrZero(_spacingBeforeTextBox.Text);
        var spacingAfterPoints = ParsePointsOrZero(_spacingAfterTextBox.Text);
        _previewSpacingBefore.Height = Math.Max(0f, PointsToDip(spacingBeforePoints));
        _previewSpacingAfter.Height = Math.Max(0f, PointsToDip(spacingAfterPoints));

        var lineSpacingKind = GetSelectedLineSpacingKind();
        var lineSpacingAt = ParsePoints(_lineSpacingAtTextBox.Text);
        var lineHeight = ResolvePreviewLineHeight(lineSpacingKind, lineSpacingAt);
        if (lineHeight.HasValue)
        {
            _previewText.LineHeight = lineHeight.Value;
        }
        else
        {
            _previewText.ClearValue(TextBlock.LineHeightProperty);
        }
    }

    private void UpdateLineSpacingState()
    {
        var kind = GetSelectedLineSpacingKind();
        _lineSpacingAtTextBox.IsEnabled = kind is LineSpacingOptionKind.AtLeast
            or LineSpacingOptionKind.Exactly
            or LineSpacingOptionKind.Multiple;
    }

    private void UpdateSpecialIndentState()
    {
        var kind = GetSelectedSpecialIndent();
        _specialIndentByTextBox.IsEnabled = kind is ParagraphSpecialIndentKind.FirstLine or ParagraphSpecialIndentKind.Hanging;
    }

    private double? ResolvePreviewLineHeight(LineSpacingOptionKind? kind, float? atValue)
    {
        if (!kind.HasValue)
        {
            return null;
        }

        var fontSize = _previewText.FontSize;
        return kind.Value switch
        {
            LineSpacingOptionKind.Single => fontSize,
            LineSpacingOptionKind.One15 => fontSize * 1.15,
            LineSpacingOptionKind.One5 => fontSize * 1.5,
            LineSpacingOptionKind.Double => fontSize * 2,
            LineSpacingOptionKind.AtLeast => PointsToDip(MathF.Max(0f, atValue ?? 0f)),
            LineSpacingOptionKind.Exactly => PointsToDip(MathF.Max(0f, atValue ?? 0f)),
            LineSpacingOptionKind.Multiple => fontSize * (atValue ?? DefaultMultiple),
            _ => null
        };
    }

    private static float ParsePointsOrZero(string? text)
    {
        return TryParseNullable(text, out var value) && value.HasValue
            ? value.Value
            : 0f;
    }

    private static float? ParsePoints(string? text)
    {
        return TryParseNullable(text, out var value)
            ? value
            : null;
    }
}
