namespace ProEdit.Documents;

public static class CommentThreading
{
    public static int ResolveThreadId(int commentId, IReadOnlyDictionary<int, CommentDefinition> comments)
    {
        if (comments.TryGetValue(commentId, out var comment))
        {
            return ResolveThreadId(comment, comments);
        }

        return commentId;
    }

    public static int ResolveThreadId(CommentDefinition comment, IReadOnlyDictionary<int, CommentDefinition> comments)
    {
        if (comment.ThreadId.HasValue)
        {
            return comment.ThreadId.Value;
        }

        var current = comment;
        for (var i = 0; i < comments.Count; i++)
        {
            if (!current.ParentId.HasValue)
            {
                return current.Id;
            }

            if (!comments.TryGetValue(current.ParentId.Value, out var parent))
            {
                return current.ParentId.Value;
            }

            if (parent.ThreadId.HasValue)
            {
                return parent.ThreadId.Value;
            }

            current = parent;
        }

        return comment.Id;
    }

    public static CommentDefinition? ResolveRootComment(int commentId, IReadOnlyDictionary<int, CommentDefinition> comments)
    {
        if (!comments.TryGetValue(commentId, out var comment))
        {
            return null;
        }

        return ResolveRootComment(comment, comments);
    }

    public static CommentDefinition ResolveRootComment(CommentDefinition comment, IReadOnlyDictionary<int, CommentDefinition> comments)
    {
        var threadId = ResolveThreadId(comment, comments);
        if (comments.TryGetValue(threadId, out var root))
        {
            return root;
        }

        return comment;
    }

    public static int ResolveDepth(CommentDefinition comment, IReadOnlyDictionary<int, CommentDefinition> comments)
    {
        var depth = 0;
        var current = comment;
        for (var i = 0; i < comments.Count; i++)
        {
            if (!current.ParentId.HasValue)
            {
                break;
            }

            if (!comments.TryGetValue(current.ParentId.Value, out var parent))
            {
                break;
            }

            depth++;
            current = parent;
        }

        return depth;
    }
}
