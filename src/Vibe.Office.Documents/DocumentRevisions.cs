using System;
using System.Collections.Generic;

namespace Vibe.Office.Documents;

public enum RevisionKind
{
    Insert,
    Delete,
    MoveFrom,
    MoveTo
}

public sealed class RevisionInfo
{
    public int? Id { get; set; }
    public RevisionKind Kind { get; set; }
    public string? Author { get; set; }
    public DateTimeOffset? Date { get; set; }
    public string? Name { get; set; }

    public RevisionInfo Clone()
    {
        return new RevisionInfo
        {
            Id = Id,
            Kind = Kind,
            Author = Author,
            Date = Date,
            Name = Name
        };
    }
}

public sealed class DocumentRevisions
{
    private readonly Dictionary<RevisionKey, RevisionInfo> _byKey = new();
    public List<RevisionInfo> Timeline { get; } = new();

    public RevisionInfo AddOrUpdate(RevisionInfo info)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        if (info.Id.HasValue)
        {
            var key = new RevisionKey(info.Id.Value, info.Kind);
            if (_byKey.TryGetValue(key, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.Author))
                {
                    existing.Author = info.Author;
                }

                if (!existing.Date.HasValue)
                {
                    existing.Date = info.Date;
                }

                if (string.IsNullOrWhiteSpace(existing.Name))
                {
                    existing.Name = info.Name;
                }

                return existing;
            }

            var created = info.Clone();
            _byKey[key] = created;
            Timeline.Add(created);
            return created;
        }

        var fallback = info.Clone();
        Timeline.Add(fallback);
        return fallback;
    }

    public void Clear()
    {
        _byKey.Clear();
        Timeline.Clear();
    }

    private readonly record struct RevisionKey(int Id, RevisionKind Kind);
}

public sealed class RevisionStartInline : Inline
{
    public RevisionInfo Revision { get; }

    public RevisionStartInline(RevisionInfo revision)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
    }
}

public sealed class RevisionEndInline : Inline
{
    public RevisionKind Kind { get; }
    public int? Id { get; }

    public RevisionEndInline(RevisionKind kind, int? id)
    {
        Kind = kind;
        Id = id;
    }
}

public sealed class RevisionRangeStartInline : Inline
{
    public RevisionInfo Revision { get; }

    public RevisionRangeStartInline(RevisionInfo revision)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
    }
}

public sealed class RevisionRangeEndInline : Inline
{
    public RevisionKind Kind { get; }
    public int? Id { get; }

    public RevisionRangeEndInline(RevisionKind kind, int? id)
    {
        Kind = kind;
        Id = id;
    }
}

public sealed class RevisionStartBlock : Block
{
    public RevisionInfo Revision { get; }

    public RevisionStartBlock(RevisionInfo revision)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
    }
}

public sealed class RevisionEndBlock : Block
{
    public RevisionKind Kind { get; }
    public int? Id { get; }

    public RevisionEndBlock(RevisionKind kind, int? id)
    {
        Kind = kind;
        Id = id;
    }
}
