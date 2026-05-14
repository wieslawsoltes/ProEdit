namespace ProEdit.Reporting.Service;

/// <summary>
/// Provides time to the optional reporting service layer.
/// </summary>
public interface IReportClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Default clock implementation backed by the system clock.
/// </summary>
public sealed class SystemReportClock : IReportClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
