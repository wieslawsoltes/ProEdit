using System.Collections;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Represents a collection of <see cref="ListItem"/> elements.
/// </summary>
public sealed class ListItemCollection : IList<ListItem>
{
    private readonly FlowElementCollection<ListItem> _items;

    internal ListItemCollection(FlowElement owner)
    {
        _items = new FlowElementCollection<ListItem>(owner);
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <summary>
    /// Gets the first list item in the collection.
    /// </summary>
    public ListItem? FirstListItem => _items.Count > 0 ? _items[0] : null;

    /// <summary>
    /// Gets the last list item in the collection.
    /// </summary>
    public ListItem? LastListItem => _items.Count > 0 ? _items[_items.Count - 1] : null;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public ListItem this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <inheritdoc />
    public void Add(ListItem item) => _items.Add(item);

    /// <summary>
    /// Adds a range of list items to the collection.
    /// </summary>
    /// <param name="items">The list items to add.</param>
    public void AddRange(IEnumerable<ListItem> items) => _items.AddRange(items);

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(ListItem item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(ListItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<ListItem> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(ListItem item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, ListItem item) => _items.Insert(index, item);

    /// <inheritdoc />
    public bool Remove(ListItem item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);
}
