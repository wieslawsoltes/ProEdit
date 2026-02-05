using System.Collections;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a collection of <see cref="Block"/> elements.
/// </summary>
public sealed class BlockCollection : IList<Block>
{
    private readonly FlowElementCollection<Block> _items;

    internal BlockCollection(FlowElement owner)
    {
        _items = new FlowElementCollection<Block>(owner);
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <summary>
    /// Gets the first block in the collection.
    /// </summary>
    public Block? FirstBlock => _items.Count > 0 ? _items[0] : null;

    /// <summary>
    /// Gets the last block in the collection.
    /// </summary>
    public Block? LastBlock => _items.Count > 0 ? _items[_items.Count - 1] : null;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public Block this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <inheritdoc />
    public void Add(Block item) => _items.Add(item);

    /// <summary>
    /// Adds a range of blocks to the collection.
    /// </summary>
    /// <param name="items">The blocks to add.</param>
    public void AddRange(IEnumerable<Block> items) => _items.AddRange(items);

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(Block item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(Block[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<Block> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(Block item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, Block item) => _items.Insert(index, item);

    /// <inheritdoc />
    public bool Remove(Block item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);
}
