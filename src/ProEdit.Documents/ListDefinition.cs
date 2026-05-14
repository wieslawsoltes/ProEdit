namespace ProEdit.Documents;

public sealed class ListDefinition
{
    public int Id { get; }
    public Dictionary<int, ListLevelDefinition> Levels { get; } = new();

    public ListDefinition(int id)
    {
        Id = id;
    }

    public ListDefinition Clone()
    {
        var clone = new ListDefinition(Id);
        foreach (var pair in Levels)
        {
            clone.Levels[pair.Key] = pair.Value.Clone();
        }

        return clone;
    }
}
