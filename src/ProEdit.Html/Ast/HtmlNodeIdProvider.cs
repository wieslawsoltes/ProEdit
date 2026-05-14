namespace ProEdit.Html.Ast;

public sealed class HtmlNodeIdProvider
{
    private long _nextId = 1;

    public HtmlNodeIdProvider()
    {
    }

    public HtmlNodeIdProvider(long nextId)
    {
        _nextId = Math.Max(1, nextId);
    }

    public HtmlNodeId NextId() => new(_nextId++);

    public void Reset() => _nextId = 1;
}
