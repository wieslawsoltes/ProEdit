using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

public enum LineSpacingOptionKind
{
    Single,
    One15,
    One5,
    Double,
    AtLeast,
    Exactly,
    Multiple
}

public readonly record struct LineSpacingDialogState(
    LineSpacingOptionKind Kind,
    float AtValue,
    float SpacingBeforePoints,
    float SpacingAfterPoints);

public partial class LineSpacingOptionsDialog : Window
{
    private const float PointsToDipScale = 96f / 72f;
    private const float TwipsPerLine = 240f;
    private const float TwipsPerPoint = 20f;
    private const float DefaultMultiple = 1f;

    private readonly ComboBox _lineSpacingCombo;
    private readonly TextBox _atTextBox;
    private readonly TextBox _beforeTextBox;
    private readonly TextBox _afterTextBox;

    public LineSpacingOptionsDialog()
        : this(new LineSpacingDialogState(LineSpacingOptionKind.Single, DefaultMultiple, 0f, 0f))
    {
    }

    public LineSpacingOptionsDialog(LineSpacingDialogState state)
    {
        InitializeComponent();
        _lineSpacingCombo = this.FindControl<ComboBox>("LineSpacingCombo")!;
        _atTextBox = this.FindControl<TextBox>("AtTextBox")!;
        _beforeTextBox = this.FindControl<TextBox>("BeforeTextBox")!;
        _afterTextBox = this.FindControl<TextBox>("AfterTextBox")!;
        SetState(state);
    }

    public void SetState(LineSpacingDialogState state)
    {
        SelectKind(state.Kind);
        _atTextBox.Text = FormatValue(state.AtValue);
        _beforeTextBox.Text = FormatValue(state.SpacingBeforePoints);
        _afterTextBox.Text = FormatValue(state.SpacingAfterPoints);
        UpdateAtFieldState();
    }

    private void OnLineSpacingSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateAtFieldState();
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

    private EditorParagraphSpacingOptions? BuildOptions()
    {
        var kind = GetSelectedKind();
        if (!TryParseNullable(_beforeTextBox.Text, out var beforePoints)
            || !TryParseNullable(_afterTextBox.Text, out var afterPoints))
        {
            return null;
        }

        var lineSpacing = ResolveLineSpacing(kind, _atTextBox.Text, out var rule);
        var spacingBefore = beforePoints.HasValue ? PointsToDip(beforePoints.Value) : (float?)null;
        var spacingAfter = afterPoints.HasValue ? PointsToDip(afterPoints.Value) : (float?)null;

        return new EditorParagraphSpacingOptions(
            spacingBefore,
            spacingAfter,
            lineSpacing,
            rule);
    }

    private static int? ResolveLineSpacing(LineSpacingOptionKind kind, string? atText, out DocLineSpacingRule? rule)
    {
        rule = DocLineSpacingRule.Auto;
        switch (kind)
        {
            case LineSpacingOptionKind.Single:
                return (int)MathF.Round(TwipsPerLine);
            case LineSpacingOptionKind.One15:
                return (int)MathF.Round(TwipsPerLine * 1.15f);
            case LineSpacingOptionKind.One5:
                return (int)MathF.Round(TwipsPerLine * 1.5f);
            case LineSpacingOptionKind.Double:
                return (int)MathF.Round(TwipsPerLine * 2f);
            case LineSpacingOptionKind.AtLeast:
                rule = DocLineSpacingRule.AtLeast;
                return (int)MathF.Round(Math.Max(0f, ParseOrDefault(atText, 0f)) * TwipsPerPoint);
            case LineSpacingOptionKind.Exactly:
                rule = DocLineSpacingRule.Exactly;
                return (int)MathF.Round(Math.Max(0f, ParseOrDefault(atText, 0f)) * TwipsPerPoint);
            case LineSpacingOptionKind.Multiple:
                rule = DocLineSpacingRule.Auto;
                var multiple = MathF.Max(0f, ParseOrDefault(atText, DefaultMultiple));
                return (int)MathF.Round(TwipsPerLine * multiple);
            default:
                return null;
        }
    }

    private static float PointsToDip(float points)
    {
        return points * PointsToDipScale;
    }

    private static string FormatValue(float value)
    {
        return value.ToString("0.##", CultureInfo.CurrentCulture);
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

    private static float ParseOrDefault(string? text, float fallback)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private void SelectKind(LineSpacingOptionKind kind)
    {
        foreach (var item in _lineSpacingCombo.Items)
        {
            if (item is ComboBoxItem { Tag: LineSpacingOptionKind tag } && tag == kind)
            {
                _lineSpacingCombo.SelectedItem = item;
                return;
            }
        }

        _lineSpacingCombo.SelectedIndex = 0;
    }

    private LineSpacingOptionKind GetSelectedKind()
    {
        if (_lineSpacingCombo.SelectedItem is ComboBoxItem { Tag: LineSpacingOptionKind tag })
        {
            return tag;
        }

        return LineSpacingOptionKind.Single;
    }

    private void UpdateAtFieldState()
    {
        var kind = GetSelectedKind();
        _atTextBox.IsEnabled = kind is LineSpacingOptionKind.AtLeast
            or LineSpacingOptionKind.Exactly
            or LineSpacingOptionKind.Multiple;
    }
}
