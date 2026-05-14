using ProEdit.Pdf;

namespace ProEdit.Word.Avalonia;

internal static class PdfImportOptionHelpers
{
    public static PdfImportOptions Create(PdfImportMode mode, PdfPreservationMode preservationMode)
    {
        var normalizedMode = NormalizeImportMode(mode);
        var normalizedPreservationMode = NormalizePreservationMode(preservationMode);

        var options = new PdfImportOptions
        {
            Mode = normalizedMode,
            PreservationMode = normalizedPreservationMode
        };

        if (options.PreservationMode != PdfPreservationMode.None)
        {
            options.ParserOptions.PreserveSourceBytes = true;
        }

        if (options.Mode == PdfImportMode.FixedLayout)
        {
            options.ParserOptions.ExtractPaths = true;
            options.ParserOptions.NormalizeFontNames = true;
        }

        return options;
    }

    public static PdfImportMode NormalizeImportMode(PdfImportMode mode)
    {
        return Enum.IsDefined(mode)
            ? mode
            : PdfImportMode.Reflow;
    }

    public static PdfPreservationMode NormalizePreservationMode(PdfPreservationMode preservationMode)
    {
        return Enum.IsDefined(preservationMode)
            ? preservationMode
            : PdfPreservationMode.None;
    }

    public static bool IsPdfPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = path;
        }

        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pdx", StringComparison.OrdinalIgnoreCase);
    }
}
