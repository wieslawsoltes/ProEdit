namespace ProEdit.Printing;

public sealed class PrintJobResult
{
    private PrintJobResult(bool succeeded, string? message, string? outputPath)
    {
        Succeeded = succeeded;
        Message = message;
        OutputPath = outputPath;
    }

    public bool Succeeded { get; }
    public string? Message { get; }
    public string? OutputPath { get; }

    public static PrintJobResult Success(string? outputPath = null)
    {
        return new PrintJobResult(true, null, outputPath);
    }

    public static PrintJobResult Failed(string message)
    {
        return new PrintJobResult(false, message, null);
    }
}
