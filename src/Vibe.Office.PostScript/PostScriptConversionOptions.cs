namespace Vibe.Office.FlowDocument.IO;

/// <summary>
/// Options controlling EPS/PS conversion through Ghostscript.
/// </summary>
public sealed class PostScriptConversionOptions
{
    /// <summary>
    /// Gets or sets explicit Ghostscript executable path or command name.
    /// When null or whitespace, the converter probes common executable names.
    /// </summary>
    public string? GhostscriptPath { get; set; }

    /// <summary>
    /// Gets or sets process timeout used for each conversion.
    /// Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable timeout.
    /// </summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
