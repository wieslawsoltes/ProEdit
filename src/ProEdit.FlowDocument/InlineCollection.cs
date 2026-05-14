using System.Collections;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a collection of <see cref="Inline"/> elements.
/// </summary>
public sealed class InlineCollection : IList<Inline>
{
    private readonly FlowElementCollection<Inline> _items;

    internal InlineCollection(FlowElement owner)
    {
        _items = new FlowElementCollection<Inline>(owner);
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <summary>
    /// Gets the first inline in the collection.
    /// </summary>
    public Inline? FirstInline => _items.Count > 0 ? _items[0] : null;

    /// <summary>
    /// Gets the last inline in the collection.
    /// </summary>
    public Inline? LastInline => _items.Count > 0 ? _items[_items.Count - 1] : null;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public Inline this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <inheritdoc />
    public void Add(Inline item) => _items.Add(item);

    /// <summary>
    /// Adds a range of inlines to the collection.
    /// </summary>
    /// <param name="items">The inlines to add.</param>
    public void AddRange(IEnumerable<Inline> items) => _items.AddRange(items);

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(Inline item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(Inline[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<Inline> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(Inline item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, Inline item) => _items.Insert(index, item);

    /// <inheritdoc />
    public bool Remove(Inline item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);
}
