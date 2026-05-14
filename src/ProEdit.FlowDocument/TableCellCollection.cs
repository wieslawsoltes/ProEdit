using System.Collections;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a collection of <see cref="TableCell"/> elements.
/// </summary>
public sealed class TableCellCollection : IList<TableCell>
{
    private readonly FlowElementCollection<TableCell> _items;
    private readonly object _syncRoot = new();
    private int _capacity;

    internal TableCellCollection(FlowElement owner)
    {
        _items = new FlowElementCollection<TableCell>(owner);
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
    public TableCell this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <inheritdoc />
    public void Add(TableCell item) => _items.Add(item);

    /// <summary>
    /// Adds a range of cells to the collection.
    /// </summary>
    /// <param name="items">The cells to add.</param>
    public void AddRange(IEnumerable<TableCell> items) => _items.AddRange(items);

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(TableCell item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(TableCell[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<TableCell> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(TableCell item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, TableCell item) => _items.Insert(index, item);

    /// <inheritdoc />
    public bool Remove(TableCell item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);
}
