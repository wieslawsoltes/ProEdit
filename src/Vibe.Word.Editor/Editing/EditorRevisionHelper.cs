using System;
using Vibe.Office.Documents;

namespace Vibe.Word.Editor.Editing;

internal static class EditorRevisionHelper
{
    public static RevisionInfo? CreateRevision(Document document, RevisionKind kind)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.TrackChangesEnabled)
        {
            return null;
        }

        var author = string.IsNullOrWhiteSpace(document.RevisionAuthorOverride)
            ? Environment.UserName
            : document.RevisionAuthorOverride;

        var info = new RevisionInfo
        {
            Kind = kind,
            Id = GetNextRevisionId(document.Revisions),
            Author = author,
            Date = DateTimeOffset.UtcNow
        };

        return document.Revisions.AddOrUpdate(info);
    }

    private static int GetNextRevisionId(DocumentRevisions revisions)
    {
        var max = 0;
        foreach (var revision in revisions.Timeline)
        {
            if (revision.Id.HasValue && revision.Id.Value > max)
            {
                max = revision.Id.Value;
            }
        }

        return max + 1;
    }
}
