using System.Collections;

namespace ProEdit.FlowDocument;

/// <summary>
/// Represents a generic collection of <see cref="TextElement"/> elements.
/// </summary>
public sealed class TextElementCollection : IList<TextElement>
{
    private readonly List<TextElement> _items = new();

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public TextElement this[int index]
    {
        get => _items[index];
        set => _items[index] = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public void Add(TextElement item)
    {
        _items.Add(item ?? throw new ArgumentNullException(nameof(item)));
    }

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(TextElement item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(TextElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<TextElement> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(TextElement item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, TextElement item)
    {
        _items.Insert(index, item ?? throw new ArgumentNullException(nameof(item)));
    }

    /// <inheritdoc />
    public bool Remove(TextElement item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);
}
