using System.Collections;

namespace Vibe.Office.FlowDocument;

internal sealed class FlowElementCollection<TElement> : IList<TElement>
    where TElement : FlowElement
{
    private readonly FlowElement _owner;
    private readonly List<TElement> _items = new List<TElement>();

    public FlowElementCollection(FlowElement owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public TElement this[int index]
    {
        get => _items[index];
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var existing = _items[index];
            if (ReferenceEquals(existing, value))
            {
                return;
            }

            EnsureCanAssign(value);
            existing.Parent = null;
            value.Parent = _owner;
            _items[index] = value;
            _owner.NotifyChanged();
        }
    }

    public void Add(TElement item)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        EnsureCanAssign(item);
        item.Parent = _owner;
        _items.Add(item);
        _owner.NotifyChanged();
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        foreach (var item in _items)
        {
            item.Parent = null;
        }

        _items.Clear();
        _owner.NotifyChanged();
    }

    public bool Contains(TElement item) => _items.Contains(item);

    public void CopyTo(TElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<TElement> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int IndexOf(TElement item) => _items.IndexOf(item);

    public void Insert(int index, TElement item)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        EnsureCanAssign(item);
        item.Parent = _owner;
        _items.Insert(index, item);
        _owner.NotifyChanged();
    }

    public bool Remove(TElement item)
    {
        if (item is null)
        {
            return false;
        }

        var removed = _items.Remove(item);
        if (removed)
        {
            item.Parent = null;
            _owner.NotifyChanged();
        }

        return removed;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        item.Parent = null;
        _owner.NotifyChanged();
    }

    public void AddRange(IEnumerable<TElement> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        foreach (var item in items)
        {
            Add(item);
        }
    }

    private void EnsureCanAssign(TElement element)
    {
        if (element.Parent is not null)
        {
            throw new InvalidOperationException("FlowDocument elements cannot belong to multiple parents.");
        }
    }
}
