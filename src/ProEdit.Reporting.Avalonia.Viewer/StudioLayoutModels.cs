namespace ProEdit.Reporting.Avalonia;

/// <summary>
/// Identifies the active work mode hosted by the reporting studio shell.
/// </summary>
public enum ReportingStudioMode
{
    /// <summary>
    /// Report definition authoring mode.
    /// </summary>
    Design,

    /// <summary>
    /// Live report execution and preview mode.
    /// </summary>
    Run
}

/// <summary>
/// Describes one drawer or tray visibility state in the reporting workbench.
/// </summary>
public enum PaneVisibilityState
{
    /// <summary>
    /// The pane is closed.
    /// </summary>
    Closed,

    /// <summary>
    /// The pane is open as an auto-hide drawer.
    /// </summary>
    Open,

    /// <summary>
    /// The pane is pinned open.
    /// </summary>
    Pinned
}

/// <summary>
/// Identifies the designer drawer or tool pane currently in focus.
/// </summary>
public enum DesignerPaneKind
{
    /// <summary>
    /// The report-data workspace.
    /// </summary>
    ReportData,

    /// <summary>
    /// The outline/explorer view.
    /// </summary>
    Outline,

    /// <summary>
    /// The shared-template library.
    /// </summary>
    Templates,

    /// <summary>
    /// The properties inspector.
    /// </summary>
    Properties,

    /// <summary>
    /// The parameter inspector.
    /// </summary>
    Parameters,

    /// <summary>
    /// The data inspector.
    /// </summary>
    Data,

    /// <summary>
    /// The expression inspector.
    /// </summary>
    Expressions
}

/// <summary>
/// Identifies the contextual bottom tray shown by the designer.
/// </summary>
public enum DesignerContextPaneKind
{
    /// <summary>
    /// No contextual tray is active.
    /// </summary>
    None,

    /// <summary>
    /// The tablix grouping tray.
    /// </summary>
    Grouping,

    /// <summary>
    /// The chart-data tray.
    /// </summary>
    ChartData,

    /// <summary>
    /// The parameter layout tray.
    /// </summary>
    ParameterLayout
}
