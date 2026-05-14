namespace ProEdit.Layout;

public readonly struct LineRange
{
    public int Start { get; }
    public int Count { get; }
    public int End => Start + Count;

    public LineRange(int start, int count)
    {
        Start = start;
        Count = count;
    }
}

public sealed class LineIndex
{
    private readonly int[] _linePageIndices;
    private readonly LineRange[] _pageLineRanges;
    private readonly float[] _sortedLineTops;
    private readonly float[] _sortedLineBottoms;
    private readonly int[] _sortedLineIndices;

    public LineIndex(IReadOnlyList<LayoutLine> lines, IReadOnlyList<int> linePageIndices, IReadOnlyList<PageLayout> pages)
    {
        if (lines.Count != linePageIndices.Count)
        {
            throw new ArgumentException("Line/page index count mismatch.", nameof(linePageIndices));
        }

        _linePageIndices = new int[lines.Count];
        _sortedLineIndices = new int[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            _linePageIndices[i] = linePageIndices[i];
            _sortedLineIndices[i] = i;
        }

        Array.Sort(_sortedLineIndices, (left, right) =>
        {
            var yCompare = lines[left].Y.CompareTo(lines[right].Y);
            if (yCompare != 0)
            {
                return yCompare;
            }

            var xCompare = lines[left].X.CompareTo(lines[right].X);
            if (xCompare != 0)
            {
                return xCompare;
            }

            return lines[left].LineHeight.CompareTo(lines[right].LineHeight);
        });

        _sortedLineTops = new float[lines.Count];
        _sortedLineBottoms = new float[lines.Count];
        for (var i = 0; i < _sortedLineIndices.Length; i++)
        {
            var line = lines[_sortedLineIndices[i]];
            _sortedLineTops[i] = line.Y;
            _sortedLineBottoms[i] = line.Y + line.LineHeight;
        }

        _pageLineRanges = BuildPageRanges(linePageIndices, pages.Count);
    }

    public int LineCount => _linePageIndices.Length;

    public int FindLineAtY(float y)
    {
        if (_sortedLineTops.Length == 0)
        {
            return -1;
        }

        var low = 0;
        var high = _sortedLineTops.Length - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (y < _sortedLineTops[mid])
            {
                high = mid - 1;
            }
            else if (y >= _sortedLineBottoms[mid])
            {
                low = mid + 1;
            }
            else
            {
                return _sortedLineIndices[mid];
            }
        }

        var clamped = Math.Clamp(high, 0, _sortedLineTops.Length - 1);
        return _sortedLineIndices[clamped];
    }

    public int GetPageForLine(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _linePageIndices.Length)
        {
            return -1;
        }

        return _linePageIndices[lineIndex];
    }

    public LineRange GetLineRangeForPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageLineRanges.Length)
        {
            return new LineRange(0, 0);
        }

        return _pageLineRanges[pageIndex];
    }

    private static LineRange[] BuildPageRanges(IReadOnlyList<int> linePageIndices, int pageCount)
    {
        if (pageCount <= 0)
        {
            return Array.Empty<LineRange>();
        }

        var ranges = new LineRange[pageCount];
        if (linePageIndices.Count == 0)
        {
            for (var i = 0; i < pageCount; i++)
            {
                ranges[i] = new LineRange(0, 0);
            }

            return ranges;
        }

        var currentPage = 0;
        var start = 0;

        for (var i = 0; i < linePageIndices.Count; i++)
        {
            var page = Math.Clamp(linePageIndices[i], 0, pageCount - 1);
            while (currentPage < page)
            {
                ranges[currentPage] = new LineRange(start, i - start);
                currentPage++;
                start = i;
            }
        }

        while (currentPage < pageCount)
        {
            ranges[currentPage] = new LineRange(start, linePageIndices.Count - start);
            currentPage++;
            start = linePageIndices.Count;
        }

        return ranges;
    }
}
