namespace ProEdit.FlowDocument.IO;

/// <summary>
/// Options controlling XPS/OXPS conversion through Ghostscript.
/// </summary>
public sealed class XpsConversionOptions
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

    /// <summary>
    /// Gets or sets a value indicating whether native package conversion is enabled.
    /// </summary>
    public bool EnableNativeConversion { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether native package conversion is attempted before Ghostscript bridging.
    /// </summary>
    public bool PreferNativeConversion { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether conversion should fall back to Ghostscript when native conversion fails.
    /// </summary>
    public bool FallbackToGhostscript { get; set; } = true;
}
