using System.Collections;

namespace ProEdit.Documents;

public sealed class TableCellParagraphCollection : IList<ParagraphBlock>, IReadOnlyList<ParagraphBlock>
{
    private readonly List<Block> _blocks;

    public TableCellParagraphCollection(List<Block> blocks)
    {
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
    }

    public int Count => CountParagraphs();

    public bool IsReadOnly => false;

    public ParagraphBlock this[int index]
    {
        get
        {
            var (blockIndex, paragraph) = GetParagraphAt(index);
            _ = blockIndex;
            return paragraph;
        }
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var blockIndex = FindBlockIndexForParagraphIndex(index, allowEnd: false);
            _blocks[blockIndex] = value;
        }
    }

    public void Add(ParagraphBlock item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _blocks.Add(item);
    }

    public void AddRange(IEnumerable<ParagraphBlock> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        foreach (var paragraph in paragraphs)
        {
            Add(paragraph);
        }
    }

    public void Clear()
    {
        for (var i = _blocks.Count - 1; i >= 0; i--)
        {
            if (_blocks[i] is ParagraphBlock)
            {
                _blocks.RemoveAt(i);
            }
        }
    }

    public bool Contains(ParagraphBlock item)
    {
        return IndexOf(item) >= 0;
    }

    public void CopyTo(ParagraphBlock[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        var index = arrayIndex;
        foreach (var paragraph in this)
        {
            if ((uint)index >= (uint)array.Length)
            {
                throw new ArgumentException("Target array is too small.", nameof(array));
            }

            array[index++] = paragraph;
        }
    }

    public IEnumerator<ParagraphBlock> GetEnumerator()
    {
        foreach (var block in _blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                yield return paragraph;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int IndexOf(ParagraphBlock item)
    {
        var paragraphIndex = 0;
        var comparer = EqualityComparer<ParagraphBlock>.Default;
        foreach (var block in _blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                if (comparer.Equals(paragraph, item))
                {
                    return paragraphIndex;
                }

                paragraphIndex++;
            }
        }

        return -1;
    }

    public void Insert(int index, ParagraphBlock item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (index < 0 || index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var blockIndex = FindBlockIndexForParagraphIndex(index, allowEnd: true);
        _blocks.Insert(blockIndex, item);
    }

    public bool Remove(ParagraphBlock item)
    {
        var comparer = EqualityComparer<ParagraphBlock>.Default;
        for (var i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i] is ParagraphBlock paragraph && comparer.Equals(paragraph, item))
            {
                _blocks.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public void RemoveAt(int index)
    {
        var blockIndex = FindBlockIndexForParagraphIndex(index, allowEnd: false);
        _blocks.RemoveAt(blockIndex);
    }

    private int CountParagraphs()
    {
        var count = 0;
        foreach (var block in _blocks)
        {
            if (block is ParagraphBlock)
            {
                count++;
            }
        }

        return count;
    }

    private (int BlockIndex, ParagraphBlock Paragraph) GetParagraphAt(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var paragraphIndex = 0;
        for (var i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i] is ParagraphBlock paragraph)
            {
                if (paragraphIndex == index)
                {
                    return (i, paragraph);
                }

                paragraphIndex++;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private int FindBlockIndexForParagraphIndex(int paragraphIndex, bool allowEnd)
    {
        if (paragraphIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
        }

        var current = 0;
        for (var i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i] is ParagraphBlock)
            {
                if (current == paragraphIndex)
                {
                    return i;
                }

                current++;
            }
        }

        if (allowEnd && current == paragraphIndex)
        {
            return _blocks.Count;
        }

        throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
    }
}
