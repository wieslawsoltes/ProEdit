namespace ProEdit.Printing;

public enum PrintRangeKind
{
    All,
    CurrentPage,
    Selection,
    CustomPages
}

public enum PrintDuplexMode
{
    Default,
    OneSided,
    TwoSidedLongEdge,
    TwoSidedShortEdge
}

public enum PrintColorMode
{
    Color,
    Grayscale
}

public enum PrintScalingMode
{
    FitToPage,
    ActualSize,
    Custom
}

public enum PrintOrientationMode
{
    Auto,
    Portrait,
    Landscape
}

public enum PrintOutputKind
{
    Printer,
    Pdf
}
