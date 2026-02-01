using System.Threading;

namespace Vibe.Office.Markdown.Ast;

public sealed class MarkdownNodeIdProvider
{
    private long _next;

    public MarkdownNodeIdProvider(long seed = 1)
    {
        _next = seed - 1;
    }

    public MarkdownNodeId NextId()
    {
        var value = Interlocked.Increment(ref _next);
        return new MarkdownNodeId(value);
    }
}
