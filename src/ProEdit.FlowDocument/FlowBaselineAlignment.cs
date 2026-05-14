namespace ProEdit.FlowDocument;

/// <summary>
/// Defines baseline alignment options for FlowDocument text.
/// </summary>
public enum FlowBaselineAlignment
{
    /// <summary>
    /// Align with the parent baseline.
    /// </summary>
    Baseline,

    /// <summary>
    /// Align with the top of the parent.
    /// </summary>
    Top,

    /// <summary>
    /// Align to the center of the parent.
    /// </summary>
    Center,

    /// <summary>
    /// Align with the bottom of the parent.
    /// </summary>
    Bottom,

    /// <summary>
    /// Align with the top of surrounding text.
    /// </summary>
    TextTop,

    /// <summary>
    /// Align with the bottom of surrounding text.
    /// </summary>
    TextBottom,

    /// <summary>
    /// Subscript alignment.
    /// </summary>
    Subscript,

    /// <summary>
    /// Superscript alignment.
    /// </summary>
    Superscript
}
