namespace Vibe.Office.WinUICompat.Documents;

public sealed class Hyperlink : Span
{
    public string? NavigateUri { get; set; }

    public string? TargetName { get; set; }

    public object? Command { get; set; }

    public object? CommandParameter { get; set; }

    public object? CommandTarget { get; set; }
}
