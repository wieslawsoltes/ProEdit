using System.Collections;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a collection of <see cref="TableColumn"/> elements.
/// </summary>
public sealed class TableColumnCollection : IList<TableColumn>
{
    private readonly FlowElementCollection<TableColumn> _items;
    private readonly object _syncRoot = new();
    private int _capacity;

    internal TableColumnCollection(FlowElement owner)
    {
        _items = new FlowElementCollection<TableColumn>(owner);
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
    bool ICollection<TableColumn>.IsReadOnly => false;

    /// <inheritdoc />
    public TableColumn this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <inheritdoc />
    public void Add(TableColumn item) => _items.Add(item);

    /// <summary>
    /// Adds a range of columns to the collection.
    /// </summary>
    /// <param name="items">The columns to add.</param>
    public void AddRange(IEnumerable<TableColumn> items) => _items.AddRange(items);

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(TableColumn item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(TableColumn[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<TableColumn> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(TableColumn item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, TableColumn item) => _items.Insert(index, item);

    /// <inheritdoc />
    public bool Remove(TableColumn item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);
}
