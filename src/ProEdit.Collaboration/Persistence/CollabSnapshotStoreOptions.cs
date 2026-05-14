namespace ProEdit.Collaboration.Persistence;

public sealed record CollabSnapshotStoreOptions(
    int OpCountThreshold = 50000,
    long LogSizeThresholdBytes = 32 * 1024 * 1024,
    TimeSpan SnapshotInterval = default,
    int TailOpCount = 256)
{
    public TimeSpan SnapshotIntervalOrDefault => SnapshotInterval == default ? TimeSpan.FromMinutes(10) : SnapshotInterval;
}
