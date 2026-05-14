using System.Globalization;

namespace ProEdit.FlowDocument.IO;

/// <summary>
/// Helpers for applying runtime XPS conversion overrides.
/// </summary>
public static class XpsRuntimeOptions
{
    /// <summary>
    /// Environment variable used to override Ghostscript executable path.
    /// </summary>
    public const string GhostscriptPathVariable = "PROEDIT_XPS_GHOSTSCRIPT_PATH";

    /// <summary>
    /// Environment variable used to override Ghostscript timeout in seconds.
    /// </summary>
    public const string ProcessTimeoutSecondsVariable = "PROEDIT_XPS_TIMEOUT_SECONDS";

    /// <summary>
    /// Creates conversion options from defaults and environment variable overrides.
    /// </summary>
    public static XpsConversionOptions CreateFromEnvironment(XpsConversionOptions? defaults = null)
    {
        var options = Clone(defaults ?? new XpsConversionOptions());
        var ghostscriptPath = Environment.GetEnvironmentVariable(GhostscriptPathVariable);
        var timeoutSeconds = Environment.GetEnvironmentVariable(ProcessTimeoutSecondsVariable);
        return ApplyOverrides(options, ghostscriptPath, timeoutSeconds);
    }

    /// <summary>
    /// Applies environment variable overrides to an existing options instance.
    /// </summary>
    public static XpsConversionOptions ApplyEnvironmentOverrides(XpsConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ghostscriptPath = Environment.GetEnvironmentVariable(GhostscriptPathVariable);
        var timeoutSeconds = Environment.GetEnvironmentVariable(ProcessTimeoutSecondsVariable);
        if (!string.IsNullOrWhiteSpace(ghostscriptPath)
            && string.IsNullOrWhiteSpace(options.GhostscriptPath))
        {
            options.GhostscriptPath = ghostscriptPath.Trim();
        }

        if (TryParseTimeoutSeconds(timeoutSeconds, out var timeout)
            && ShouldApplyEnvironmentTimeout(options.ProcessTimeout))
        {
            options.ProcessTimeout = timeout;
        }

        return options;
    }

    /// <summary>
    /// Applies runtime override values to an existing options instance.
    /// </summary>
    public static XpsConversionOptions ApplyOverrides(
        XpsConversionOptions options,
        string? ghostscriptPath,
        string? timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(ghostscriptPath))
        {
            options.GhostscriptPath = ghostscriptPath.Trim();
        }

        if (TryParseTimeoutSeconds(timeoutSeconds, out var timeout))
        {
            options.ProcessTimeout = timeout;
        }

        return options;
    }

    /// <summary>
    /// Parses timeout in seconds using invariant culture.
    /// </summary>
    public static bool TryParseTimeoutSeconds(string? value, out TimeSpan timeout)
    {
        timeout = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d)
        {
            return false;
        }

        timeout = TimeSpan.FromSeconds(seconds);
        return timeout > TimeSpan.Zero;
    }

    private static XpsConversionOptions Clone(XpsConversionOptions source)
    {
        return new XpsConversionOptions
        {
            GhostscriptPath = source.GhostscriptPath,
            ProcessTimeout = source.ProcessTimeout,
            EnableNativeConversion = source.EnableNativeConversion,
            PreferNativeConversion = source.PreferNativeConversion,
            FallbackToGhostscript = source.FallbackToGhostscript
        };
    }

    private static bool ShouldApplyEnvironmentTimeout(TimeSpan currentTimeout)
    {
        var defaultTimeout = new XpsConversionOptions().ProcessTimeout;
        return currentTimeout <= TimeSpan.Zero || currentTimeout == defaultTimeout;
    }
}
