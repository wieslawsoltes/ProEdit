namespace ProEdit.Printing;

public sealed class PrintSettings
{
    public PrintOutputKind OutputKind { get; set; } = PrintOutputKind.Printer;
    public string? PrinterName { get; set; }
    public string? OutputPath { get; set; }
    public int Copies { get; set; } = 1;
    public bool Collate { get; set; } = true;
    public PrintRangeKind RangeKind { get; set; } = PrintRangeKind.All;
    public IReadOnlyList<PrintPageRange> CustomRanges { get; set; } = Array.Empty<PrintPageRange>();
    public PrintPaperSize? PaperSize { get; set; }
    public PrintDuplexMode Duplex { get; set; } = PrintDuplexMode.Default;
    public PrintColorMode ColorMode { get; set; } = PrintColorMode.Color;
    public PrintScalingMode Scaling { get; set; } = PrintScalingMode.FitToPage;
    public float CustomScale { get; set; } = 1f;
    public PrintOrientationMode Orientation { get; set; } = PrintOrientationMode.Auto;

    public PrintSettings Clone()
    {
        return new PrintSettings
        {
            OutputKind = OutputKind,
            PrinterName = PrinterName,
            OutputPath = OutputPath,
            Copies = Copies,
            Collate = Collate,
            RangeKind = RangeKind,
            CustomRanges = CustomRanges.ToArray(),
            PaperSize = PaperSize,
            Duplex = Duplex,
            ColorMode = ColorMode,
            Scaling = Scaling,
            CustomScale = CustomScale,
            Orientation = Orientation
        };
    }

    public void Normalize()
    {
        Copies = Math.Max(1, Copies);
        CustomScale = MathF.Max(0.1f, CustomScale);
        if (RangeKind != PrintRangeKind.CustomPages && RangeKind != PrintRangeKind.Selection)
        {
            CustomRanges = Array.Empty<PrintPageRange>();
        }
    }
}
