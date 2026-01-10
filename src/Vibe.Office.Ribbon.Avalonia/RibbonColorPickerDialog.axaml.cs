using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Vibe.Office.Primitives;

namespace Vibe.Office.Ribbon.Avalonia;

public sealed partial class RibbonColorPickerDialog : Window
{
    private readonly ItemsControl _standardColorsList;
    private readonly Border _previewBorder;
    private readonly TextBox _hexTextBox;
    private readonly TextBox _redTextBox;
    private readonly TextBox _greenTextBox;
    private readonly TextBox _blueTextBox;
    private bool _isUpdating;
    private DocColor _selectedColor;

    private static readonly IReadOnlyList<ColorSwatch> StandardSwatches = new[]
    {
        new ColorSwatch("Black", new DocColor(0, 0, 0)),
        new ColorSwatch("Dark Gray", new DocColor(64, 64, 64)),
        new ColorSwatch("Gray", new DocColor(128, 128, 128)),
        new ColorSwatch("Light Gray", new DocColor(192, 192, 192)),
        new ColorSwatch("White", new DocColor(255, 255, 255)),
        new ColorSwatch("Dark Red", new DocColor(128, 0, 0)),
        new ColorSwatch("Red", new DocColor(192, 0, 0)),
        new ColorSwatch("Orange", new DocColor(230, 145, 56)),
        new ColorSwatch("Yellow", new DocColor(241, 194, 50)),
        new ColorSwatch("Green", new DocColor(106, 168, 79)),
        new ColorSwatch("Dark Green", new DocColor(0, 100, 0)),
        new ColorSwatch("Teal", new DocColor(69, 129, 142)),
        new ColorSwatch("Blue", new DocColor(61, 133, 198)),
        new ColorSwatch("Dark Blue", new DocColor(0, 51, 102)),
        new ColorSwatch("Purple", new DocColor(142, 124, 195)),
        new ColorSwatch("Dark Purple", new DocColor(76, 0, 130))
    };

    public RibbonColorPickerDialog()
        : this(null)
    {
    }

    public RibbonColorPickerDialog(DocColor? initialColor)
    {
        InitializeComponent();
        _standardColorsList = this.FindControl<ItemsControl>("StandardColorsList")!;
        _previewBorder = this.FindControl<Border>("PreviewBorder")!;
        _hexTextBox = this.FindControl<TextBox>("HexTextBox")!;
        _redTextBox = this.FindControl<TextBox>("RedTextBox")!;
        _greenTextBox = this.FindControl<TextBox>("GreenTextBox")!;
        _blueTextBox = this.FindControl<TextBox>("BlueTextBox")!;
        _standardColorsList.ItemsSource = StandardSwatches;
        SetColor(initialColor ?? DocColor.Black);
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(_selectedColor);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ColorSwatch swatch })
        {
            SetColor(swatch.Color);
        }
    }

    private void OnHexChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        if (TryParseHex(_hexTextBox.Text, out var color))
        {
            SetColor(color);
        }
    }

    private void OnRgbChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        if (TryParseByte(_redTextBox.Text, out var r)
            && TryParseByte(_greenTextBox.Text, out var g)
            && TryParseByte(_blueTextBox.Text, out var b))
        {
            SetColor(new DocColor(r, g, b));
        }
    }

    private void SetColor(DocColor color)
    {
        _selectedColor = color;
        _previewBorder.Background = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));

        _isUpdating = true;
        _hexTextBox.Text = FormatHex(color);
        _redTextBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
        _greenTextBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
        _blueTextBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
        _isUpdating = false;
    }

    private static string FormatHex(DocColor color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static bool TryParseHex(string? text, out DocColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            value = value[1..];
        }

        if (value.Length != 6)
        {
            return false;
        }

        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            return false;
        }

        var r = (byte)((hex >> 16) & 0xFF);
        var g = (byte)((hex >> 8) & 0xFF);
        var b = (byte)(hex & 0xFF);
        color = new DocColor(r, g, b);
        return true;
    }

    private static bool TryParseByte(string? text, out byte value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private sealed record ColorSwatch(string Label, DocColor Color);
}
