using System.Collections;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a collection of <see cref="TableRowGroup"/> elements.
/// </summary>
public sealed class TableRowGroupCollection : IList<TableRowGroup>
{
    private readonly FlowElementCollection<TableRowGroup> _items;
    private readonly object _syncRoot = new();
    private int _capacity;

    internal TableRowGroupCollection(FlowElement owner)
    {
        _items = new FlowElementCollection<TableRowGroup>(owner);
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <summary>
    /// Gets or sets the capacity metadata for the collection.
    /// </summary>
    public int Capacity
    {
        get => Math.Max(_capacity, Count);
        set => _capacity = Math.Max(0, value);
    }

    /// <summary>
    /// Gets a value indicating whether this collection is synchronized.
    /// </summary>
    public bool IsSynchronized => false;

    /// <summary>
    /// Gets the synchronization root.
    /// </summary>
    public object SyncRoot => _syncRoot;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public TableRowGroup this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <inheritdoc />
    public void Add(TableRowGroup item) => _items.Add(item);

    /// <summary>
    /// Adds a range of row groups to the collection.
    /// </summary>
    /// <param name="items">The row groups to add.</param>
    public void AddRange(IEnumerable<TableRowGroup> items) => _items.AddRange(items);

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(TableRowGroup item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(TableRowGroup[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<TableRowGroup> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(TableRowGroup item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, TableRowGroup item) => _items.Insert(index, item);

    /// <inheritdoc />
    public bool Remove(TableRowGroup item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);
}
