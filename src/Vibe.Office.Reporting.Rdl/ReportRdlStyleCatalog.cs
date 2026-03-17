using System.Xml.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Reporting.Rdl;

internal readonly record struct ReportRdlStyleProperties(
    string? FontFamily,
    float? FontSize,
    string? Foreground,
    string? ForegroundExpression,
    string? Background,
    string? BackgroundExpression,
    bool? Bold,
    bool? Italic,
    ParagraphAlignment? TextAlign)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(FontFamily)
        || FontSize.HasValue
        || !string.IsNullOrWhiteSpace(Foreground)
        || !string.IsNullOrWhiteSpace(ForegroundExpression)
        || !string.IsNullOrWhiteSpace(Background)
        || !string.IsNullOrWhiteSpace(BackgroundExpression)
        || Bold.HasValue
        || Italic.HasValue
        || TextAlign.HasValue;
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
            Bold = properties.Bold,
            Italic = properties.Italic,
            TextAlign = properties.TextAlign
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
            ParseFontSize(styleElement.Element(xmlNamespace + "FontSize")?.Value, xmlNamespace, path, diagnostics),
            ParseStaticScalar(styleElement.Element(xmlNamespace + "Color")?.Value),
            ParseExpressionScalar(styleElement.Element(xmlNamespace + "Color")?.Value),
            ParseStaticScalar(styleElement.Element(xmlNamespace + "BackgroundColor")?.Value),
            ParseExpressionScalar(styleElement.Element(xmlNamespace + "BackgroundColor")?.Value),
            ParseBold(styleElement.Element(xmlNamespace + "FontWeight")?.Value),
            ParseItalic(styleElement.Element(xmlNamespace + "FontStyle")?.Value),
            ParseAlignment(styleElement.Element(xmlNamespace + "TextAlign")?.Value));
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
            if (style.FontSize.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "FontSize", ReportRdlMeasurements.Format(style.FontSize.Value)));
            }

            AddOptionalElement(
                styleElement,
                xmlNamespace + "Color",
                !string.IsNullOrWhiteSpace(style.ForegroundExpression)
                    ? ReportRdlExpressions.ToRdlScalarValue(style.ForegroundExpression) ?? style.ForegroundExpression
                    : style.Foreground);
            AddOptionalElement(
                styleElement,
                xmlNamespace + "BackgroundColor",
                !string.IsNullOrWhiteSpace(style.BackgroundExpression)
                    ? ReportRdlExpressions.ToRdlScalarValue(style.BackgroundExpression) ?? style.BackgroundExpression
                    : style.Background);
            if (style.Bold == true)
            {
                styleElement.Add(new XElement(xmlNamespace + "FontWeight", "Bold"));
            }

            if (style.Italic == true)
            {
                styleElement.Add(new XElement(xmlNamespace + "FontStyle", "Italic"));
            }

            if (style.TextAlign.HasValue)
            {
                styleElement.Add(new XElement(xmlNamespace + "TextAlign", FormatAlignment(style.TextAlign.Value)));
            }
        }

        AddOptionalElement(styleElement, xmlNamespace + "Format", formatString);
        return styleElement.HasElements ? styleElement : null;
    }

    private static float? ParseFontSize(
        string? text,
        XNamespace xmlNamespace,
        string path,
        List<ReportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ReportRdlMeasurements.Parse(text, $"{path}/Style/FontSize", diagnostics);
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
