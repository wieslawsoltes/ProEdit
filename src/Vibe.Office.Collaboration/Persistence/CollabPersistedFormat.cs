namespace Vibe.Office.Collaboration.Persistence;

public static class CollabPersistedFormat
{
    public const uint SnapshotMagic = 0x564F4253; // VOBS
    public const uint SnapshotIndexMagic = 0x564F4249; // VOBI
    public const uint OpLogMagic = 0x564F4F50; // VOOP
    public const int SnapshotVersion = 2;
    public const int SnapshotIndexVersion = 1;
    public const int OpLogVersion = 1;

    public const string SnapshotExtension = ".vibe.snapshot";
    public const string SnapshotIndexExtension = ".vibe.snapshot.idx";
    public const string OpLogExtension = ".vibe.ops";
    public const string PresenceExtension = ".vibe.presence";

    public static string NormalizeBasePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.EndsWith(OpLogExtension, StringComparison.OrdinalIgnoreCase))
        {
            return path[..^OpLogExtension.Length];
        }

        if (path.EndsWith(SnapshotExtension, StringComparison.OrdinalIgnoreCase))
        {
            return path[..^SnapshotExtension.Length];
        }

        if (path.EndsWith(SnapshotIndexExtension, StringComparison.OrdinalIgnoreCase))
        {
            return path[..^SnapshotIndexExtension.Length];
        }

        if (path.EndsWith(PresenceExtension, StringComparison.OrdinalIgnoreCase))
        {
            return path[..^PresenceExtension.Length];
        }

        return path;
    }
}
