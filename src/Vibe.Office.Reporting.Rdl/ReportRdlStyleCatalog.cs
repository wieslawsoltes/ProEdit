using System.Xml.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Reporting.Rdl;

internal readonly record struct ReportRdlBorderProperties(
    string? Color,
    string? ColorExpression,
    ReportBorderLineStyle? Style,
    float? Width)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(Color)
        || !string.IsNullOrWhiteSpace(ColorExpression)
        || Style.HasValue
        || Width.HasValue;

    public ReportBorderDefinition ToDefinition()
    {
        return new ReportBorderDefinition
        {
            Color = Color,
            ColorExpression = ColorExpression,
            Style = Style,
            Width = Width
        };
    }
}

internal readonly record struct ReportRdlStyleProperties(
    string? FontFamily,
    float? FontSize,
    string? Foreground,
    string? ForegroundExpression,
    string? Background,
    string? BackgroundExpression,
    ReportBackgroundGradientType? BackgroundGradientType,
    string? BackgroundGradientEndColor,
    string? BackgroundGradientEndColorExpression,
    bool? Bold,
    bool? Italic,
    ReportRdlBorderProperties Border,
    ReportRdlBorderProperties TopBorder,
    ReportRdlBorderProperties BottomBorder,
    ReportRdlBorderProperties LeftBorder,
    ReportRdlBorderProperties RightBorder,
    float? PaddingLeft,
    float? PaddingRight,
    float? PaddingTop,
    float? PaddingBottom,
    ParagraphAlignment? TextAlign,
    ReportVerticalAlignment? VerticalAlign,
    ReportTextDecoration? TextDecoration)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(FontFamily)
        || FontSize.HasValue
        || !string.IsNullOrWhiteSpace(Foreground)
        || !string.IsNullOrWhiteSpace(ForegroundExpression)
        || !string.IsNullOrWhiteSpace(Background)
        || !string.IsNullOrWhiteSpace(BackgroundExpression)
        || BackgroundGradientType.HasValue
        || !string.IsNullOrWhiteSpace(BackgroundGradientEndColor)
        || !string.IsNullOrWhiteSpace(BackgroundGradientEndColorExpression)
        || Bold.HasValue
        || Italic.HasValue
        || Border.HasValues
        || TopBorder.HasValues
        || BottomBorder.HasValues
        || LeftBorder.HasValues
        || RightBorder.HasValues
        || PaddingLeft.HasValue
        || PaddingRight.HasValue
        || PaddingTop.HasValue
        || PaddingBottom.HasValue
        || TextAlign.HasValue
        || VerticalAlign.HasValue
        || TextDecoration.HasValue;
}

internal sealed class ReportRdlStyleCatalog
{
    private readonly Dictionary<ReportRdlStyleProperties, string> _styleIds = new();
    private readonly List<ReportStyleDefinition> _styles = new();

    public string? Intern(XElement? styleElement, XNamespace xmlNamespace, string path, List<ReportDiagnostic> diagnostics)
    {
        var properties = Parse(styleElement, xmlNamespace, path, diagnostics, out _);
        if (!properties.HasValues)
        {
            return null;
        }

        if (_styleIds.TryGetValue(properties, out var styleId))
        {
            return styleId;
        }

        styleId = BuildStyleId(_styles.Count);
        _styleIds.Add(properties, styleId);
        _styles.Add(new ReportStyleDefinition
        {
            Id = styleId,
            FontFamily = properties.FontFamily,
            FontSize = properties.FontSize,
            Foreground = properties.Foreground,
            ForegroundExpression = properties.ForegroundExpression,
            Background = properties.Background,
            BackgroundExpression = properties.BackgroundExpression,
            BackgroundGradientType = properties.BackgroundGradientType,
            BackgroundGradientEndColor = properties.BackgroundGradientEndColor,
            BackgroundGradientEndColorExpression = properties.BackgroundGradientEndColorExpression,
            Bold = properties.Bold,
            Italic = properties.Italic,
            Border = properties.Border.HasValues ? properties.Border.ToDefinition() : null,
            TopBorder = properties.TopBorder.HasValues ? properties.TopBorder.ToDefinition() : null,
            BottomBorder = properties.BottomBorder.HasValues ? properties.BottomBorder.ToDefinition() : null,
            LeftBorder = properties.LeftBorder.HasValues ? properties.LeftBorder.ToDefinition() : null,
            RightBorder = properties.RightBorder.HasValues ? properties.RightBorder.ToDefinition() : null,
            PaddingLeft = properties.PaddingLeft,
            PaddingRight = properties.PaddingRight,
            PaddingTop = properties.PaddingTop,
            PaddingBottom = properties.PaddingBottom,
            TextAlign = properties.TextAlign,
            VerticalAlign = properties.VerticalAlign,
            TextDecoration = properties.TextDecoration
        });

        return styleId;
    }

    public void CopyTo(ReportDefinition reportDefinition)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);

        for (var index = 0; index < _styles.Count; index++)
        {
            reportDefinition.Styles.Add(_styles[index]);
        }
    }

    public static ReportRdlStyleProperties Parse(
        XElement? styleElement,
        XNamespace xmlNamespace,
        string path,
        List<ReportDiagnostic> diagnostics,
        out string? formatString)
    {
        formatString = styleElement?.Element(xmlNamespace + "Format")?.Value;
        if (styleElement is null)
        {
            return default;
        }

        return new ReportRdlStyleProperties(
            styleElement.Element(xmlNamespace + "FontFamily")?.Value,
            ParseOptionalMeasurement(styleElement.Element(xmlNamespace + "FontSize")?.Value, path + "/Style/FontSize", diagnostics),
            ParseStaticScalar(styleElement.Element(xmlNamespace + "Color")?.Value),
            ParseExpressionScalar(styleElement.Element(xmlNamespace + "Color")?.Value),
            ParseStaticScalar(styleElement.Element(xmlNamespace + "BackgroundColor")?.Value),
            ParseExpressionScalar(styleElement.Element(xmlNamespace + "BackgroundColor")?.Value),
            ParseBackgroundGradientType(styleElement.Element(xmlNamespace + "BackgroundGradientType")?.Value),
            ParseStaticScalar(styleElement.Element(xmlNamespace + "BackgroundGradientEndColor")?.Value),
            ParseExpressionScalar(styleElement.Element(xmlNamespace + "BackgroundGradientEndColor")?.Value),
            ParseBold(styleElement.Element(xmlNamespace + "FontWeight")?.Value),
            ParseItalic(styleElement.Element(xmlNamespace + "FontStyle")?.Value),
            ParseBorder(styleElement.Element(xmlNamespace + "Border"), xmlNamespace, path + "/Style/Border", diagnostics),
            ParseBorder(styleElement.Element(xmlNamespace + "TopBorder"), xmlNamespace, path + "/Style/TopBorder", diagnostics),
            ParseBorder(styleElement.Element(xmlNamespace + "BottomBorder"), xmlNamespace, path + "/Style/BottomBorder", diagnostics),
            ParseBorder(styleElement.Element(xmlNamespace + "LeftBorder"), xmlNamespace, path + "/Style/LeftBorder", diagnostics),
            ParseBorder(styleElement.Element(xmlNamespace + "RightBorder"), xmlNamespace, path + "/Style/RightBorder", diagnostics),
            ParseOptionalMeasurement(styleElement.Element(xmlNamespace + "PaddingLeft")?.Value, path + "/Style/PaddingLeft", diagnostics),
            ParseOptionalMeasurement(styleElement.Element(xmlNamespace + "PaddingRight")?.Value, path + "/Style/PaddingRight", diagnostics),
            ParseOptionalMeasurement(styleElement.Element(xmlNamespace + "PaddingTop")?.Value, path + "/Style/PaddingTop", diagnostics),
            ParseOptionalMeasurement(styleElement.Element(xmlNamespace + "PaddingBottom")?.Value, path + "/Style/PaddingBottom", diagnostics),
            ParseAlignment(styleElement.Element(xmlNamespace + "TextAlign")?.Value),
            ParseVerticalAlignment(styleElement.Element(xmlNamespace + "VerticalAlign")?.Value),
            ParseTextDecoration(styleElement.Element(xmlNamespace + "TextDecoration")?.Value));
    }

    public static XElement? CreateStyleElement(
        ReportStyleDefinition? style,
        string? formatString,
        XNamespace xmlNamespace)
    {
        if (style is null && string.IsNullOrWhiteSpace(formatString))
        {
            return null;
        }

        var styleElement = new XElement(xmlNamespace + "Style");
        if (style is not null)
        {
            AddOptionalElement(styleElement, xmlNamespace + "FontFamily", style.FontFamily);
            AddOptionalMeasurement(styleElement, xmlNamespace + "FontSize", style.FontSize);
            AddOptionalColorElement(
                styleElement,
                xmlNamespace + "Color",
                style.Foreground,
                style.ForegroundExpression);
            AddOptionalColorElement(
                styleElement,
                xmlNamespace + "BackgroundColor",
                style.Background,
                style.BackgroundExpression);
            if (style.BackgroundGradientType.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "BackgroundGradientType", FormatBackgroundGradientType(style.BackgroundGradientType.Value)));
            }

            AddOptionalColorElement(
                styleElement,
                xmlNamespace + "BackgroundGradientEndColor",
                style.BackgroundGradientEndColor,
                style.BackgroundGradientEndColorExpression);

            if (style.Bold.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "FontWeight", style.Bold.Value ? "Bold" : "Normal"));
            }

            if (style.Italic.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "FontStyle", style.Italic.Value ? "Italic" : "Normal"));
            }

            AddBorderElement(styleElement, xmlNamespace + "Border", style.Border, xmlNamespace);
            AddBorderElement(styleElement, xmlNamespace + "TopBorder", style.TopBorder, xmlNamespace);
            AddBorderElement(styleElement, xmlNamespace + "BottomBorder", style.BottomBorder, xmlNamespace);
            AddBorderElement(styleElement, xmlNamespace + "LeftBorder", style.LeftBorder, xmlNamespace);
            AddBorderElement(styleElement, xmlNamespace + "RightBorder", style.RightBorder, xmlNamespace);

            AddOptionalMeasurement(styleElement, xmlNamespace + "PaddingLeft", style.PaddingLeft);
            AddOptionalMeasurement(styleElement, xmlNamespace + "PaddingRight", style.PaddingRight);
            AddOptionalMeasurement(styleElement, xmlNamespace + "PaddingTop", style.PaddingTop);
            AddOptionalMeasurement(styleElement, xmlNamespace + "PaddingBottom", style.PaddingBottom);

            if (style.TextAlign.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "TextAlign", FormatAlignment(style.TextAlign.Value)));
            }

            if (style.VerticalAlign.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "VerticalAlign", FormatVerticalAlignment(style.VerticalAlign.Value)));
            }

            if (style.TextDecoration.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "TextDecoration", FormatTextDecoration(style.TextDecoration.Value)));
            }
        }

        AddOptionalElement(styleElement, xmlNamespace + "Format", formatString);
        return styleElement.HasElements ? styleElement : null;
    }

    private static ReportRdlBorderProperties ParseBorder(
        XElement? borderElement,
        XNamespace xmlNamespace,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (borderElement is null)
        {
            return default;
        }

        return new ReportRdlBorderProperties(
            ParseStaticScalar(borderElement.Element(xmlNamespace + "Color")?.Value),
            ParseExpressionScalar(borderElement.Element(xmlNamespace + "Color")?.Value),
            ParseBorderStyle(borderElement.Element(xmlNamespace + "Style")?.Value),
            ParseOptionalMeasurement(borderElement.Element(xmlNamespace + "Width")?.Value, path + "/Width", diagnostics));
    }

    private static void AddBorderElement(
        XElement parent,
        XName elementName,
        ReportBorderDefinition? border,
        XNamespace xmlNamespace)
    {
        if (border is null)
        {
            return;
        }

        var borderElement = new XElement(elementName);
        AddOptionalColorElement(borderElement, xmlNamespace + "Color", border.Color, border.ColorExpression);
        if (border.Style.HasValue)
        {
            borderElement.Add(new XElement(xmlNamespace + "Style", FormatBorderStyle(border.Style.Value)));
        }

        AddOptionalMeasurement(borderElement, xmlNamespace + "Width", border.Width);
        if (borderElement.HasElements)
        {
            parent.Add(borderElement);
        }
    }

    private static void AddOptionalColorElement(XElement parent, XName name, string? color, string? expression)
    {
        if (!string.IsNullOrWhiteSpace(expression))
        {
            AddOptionalElement(
                parent,
                name,
                ReportRdlExpressions.ToRdlScalarValue(expression) ?? expression);
            return;
        }

        AddOptionalElement(parent, name, color);
    }

    private static void AddOptionalMeasurement(XElement parent, XName name, float? value)
    {
        if (value.HasValue)
        {
            parent.Add(new XElement(name, ReportRdlMeasurements.Format(value.Value)));
        }
    }

    private static float? ParseOptionalMeasurement(
        string? text,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ReportRdlMeasurements.Parse(text, path, diagnostics);
    }

    private static bool? ParseBold(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "bold" => true,
            "normal" => false,
            _ => null
        };
    }

    private static bool? ParseItalic(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "italic" => true,
            "normal" => false,
            _ => null
        };
    }

    private static ReportBackgroundGradientType? ParseBackgroundGradientType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "default" => ReportBackgroundGradientType.Default,
            "none" => ReportBackgroundGradientType.None,
            "leftright" => ReportBackgroundGradientType.LeftRight,
            "topbottom" => ReportBackgroundGradientType.TopBottom,
            "center" => ReportBackgroundGradientType.Center,
            "diagonalleft" => ReportBackgroundGradientType.DiagonalLeft,
            "diagonalright" => ReportBackgroundGradientType.DiagonalRight,
            "horizontalcenter" => ReportBackgroundGradientType.HorizontalCenter,
            "verticalcenter" => ReportBackgroundGradientType.VerticalCenter,
            _ => null
        };
    }

    private static ParagraphAlignment? ParseAlignment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "center" => ParagraphAlignment.Center,
            "right" => ParagraphAlignment.Right,
            "justify" => ParagraphAlignment.Justify,
            "left" or "general" => ParagraphAlignment.Left,
            _ => null
        };
    }

    private static ReportVerticalAlignment? ParseVerticalAlignment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "middle" or "center" => ReportVerticalAlignment.Middle,
            "bottom" => ReportVerticalAlignment.Bottom,
            "top" => ReportVerticalAlignment.Top,
            _ => null
        };
    }

    private static ReportTextDecoration? ParseTextDecoration(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "underline" => ReportTextDecoration.Underline,
            "linethrough" => ReportTextDecoration.LineThrough,
            "none" => ReportTextDecoration.None,
            _ => null
        };
    }

    private static ReportBorderLineStyle? ParseBorderStyle(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "none" => ReportBorderLineStyle.None,
            "dashed" => ReportBorderLineStyle.Dashed,
            "dotted" => ReportBorderLineStyle.Dotted,
            "double" => ReportBorderLineStyle.Double,
            "default" or "solid" or "ridge" or "groove" or "outset" or "inset" or "windowinset" => ReportBorderLineStyle.Solid,
            _ => null
        };
    }

    private static string FormatAlignment(ParagraphAlignment alignment)
    {
        return alignment switch
        {
            ParagraphAlignment.Center => "Center",
            ParagraphAlignment.Right => "Right",
            ParagraphAlignment.Justify => "Justify",
            _ => "Left"
        };
    }

    private static string FormatBackgroundGradientType(ReportBackgroundGradientType gradientType)
    {
        return gradientType switch
        {
            ReportBackgroundGradientType.None => "None",
            ReportBackgroundGradientType.LeftRight => "LeftRight",
            ReportBackgroundGradientType.TopBottom => "TopBottom",
            ReportBackgroundGradientType.Center => "Center",
            ReportBackgroundGradientType.DiagonalLeft => "DiagonalLeft",
            ReportBackgroundGradientType.DiagonalRight => "DiagonalRight",
            ReportBackgroundGradientType.HorizontalCenter => "HorizontalCenter",
            ReportBackgroundGradientType.VerticalCenter => "VerticalCenter",
            _ => "Default"
        };
    }

    private static string FormatVerticalAlignment(ReportVerticalAlignment alignment)
    {
        return alignment switch
        {
            ReportVerticalAlignment.Middle => "Middle",
            ReportVerticalAlignment.Bottom => "Bottom",
            _ => "Top"
        };
    }

    private static string FormatTextDecoration(ReportTextDecoration decoration)
    {
        return decoration switch
        {
            ReportTextDecoration.Underline => "Underline",
            ReportTextDecoration.LineThrough => "LineThrough",
            _ => "None"
        };
    }

    private static string FormatBorderStyle(ReportBorderLineStyle style)
    {
        return style switch
        {
            ReportBorderLineStyle.None => "None",
            ReportBorderLineStyle.Dashed => "Dashed",
            ReportBorderLineStyle.Dotted => "Dotted",
            ReportBorderLineStyle.Double => "Double",
            _ => "Solid"
        };
    }

    private static void AddOptionalElement(XElement parent, XName name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parent.Add(new XElement(name, value));
        }
    }

    private static string? ParseStaticScalar(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.TrimStart().StartsWith('=')
            ? null
            : value;
    }

    private static string? ParseExpressionScalar(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || !value.TrimStart().StartsWith('=')
            ? null
            : ReportRdlExpressions.ToNativeScalarExpression(value);
    }

    private static string BuildStyleId(int index)
    {
        return index == 0 ? "rdl-style" : $"rdl-style{index}";
    }
}
