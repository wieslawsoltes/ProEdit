using System.Globalization;

namespace Vibe.Office.Reporting.Rdl;

internal static class ReportRdlMeasurements
{
    private const float DipsPerInch = 96f;

    public static string Format(float value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value / DipsPerInch:0.####}in");
    }

    public static float Parse(
        string? text,
        string path,
        List<ReportDiagnostic> diagnostics,
        float fallback = 0f)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var trimmed = text.Trim();
        var unitStart = trimmed.Length;
        while (unitStart > 0 && (char.IsLetter(trimmed[unitStart - 1]) || trimmed[unitStart - 1] == '%'))
        {
            unitStart--;
        }

        var numberText = trimmed[..unitStart];
        var unitText = trimmed[unitStart..].Trim().ToLowerInvariant();
        if (!float.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RdlLengthInvalid,
                $"RDL size '{text}' is not a valid numeric value.",
                path));
            return fallback;
        }

        return unitText switch
        {
            "" or "in" => number * DipsPerInch,
            "cm" => number * DipsPerInch / 2.54f,
            "mm" => number * DipsPerInch / 25.4f,
            "pt" => number * DipsPerInch / 72f,
            "pc" => number * DipsPerInch / 6f,
            _ => AddInvalidUnit(text, unitText, path, diagnostics, fallback)
        };
    }

    private static float AddInvalidUnit(
        string rawText,
        string unitText,
        string path,
        List<ReportDiagnostic> diagnostics,
        float fallback)
    {
        diagnostics.Add(new ReportDiagnostic(
            ReportDiagnosticSeverity.Error,
            ReportDiagnosticCodes.RdlLengthInvalid,
            $"RDL size '{rawText}' uses unsupported unit '{unitText}'.",
            path));
        return fallback;
    }
}
