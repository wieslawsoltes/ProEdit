namespace ProEdit.Reporting.Rdl;

/// <summary>
/// Supported RDL namespace versions.
/// </summary>
public enum ReportRdlVersion
{
    /// <summary>
    /// RDL 2008/01.
    /// </summary>
    Rdl2008,

    /// <summary>
    /// RDL 2010/01.
    /// </summary>
    Rdl2010,

    /// <summary>
    /// RDL 2016/01.
    /// </summary>
    Rdl2016
}

/// <summary>
/// Configures RDL write behavior.
/// </summary>
public sealed class ReportRdlWriteOptions
{
    /// <summary>
    /// Gets or sets the target RDL namespace version.
    /// </summary>
    public ReportRdlVersion Version { get; set; } = ReportRdlVersion.Rdl2016;

    /// <summary>
    /// Gets or sets a value indicating whether the generated XML should be indented.
    /// </summary>
    public bool Indent { get; set; } = true;
}
