namespace Vibe.Office.Collaboration.Transports.SharedFile;

public sealed record SharedFileTransportOptions(
    TimeSpan PollInterval,
    TimeSpan WriteLockTimeout)
{
    public static SharedFileTransportOptions Default => new(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));
}
