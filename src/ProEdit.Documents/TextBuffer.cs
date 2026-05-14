namespace ProEdit.Documents;

public sealed class TextBuffer
{
    private readonly Storage _storage;
    private readonly List<Piece> _pieces;
    private int _length;
    private string? _cachedText;

    public TextBuffer(string text)
    {
        var value = text ?? string.Empty;
        _storage = new Storage(value);
        _pieces = new List<Piece>();
        if (value.Length > 0)
        {
            _pieces.Add(new Piece(0, 0, value.Length));
        }

        _length = value.Length;
    }

    private TextBuffer(Storage storage, List<Piece> pieces, int length)
    {
        _storage = storage;
        _pieces = pieces;
        _length = length;
    }

    public int Length => _length;

    public bool IsEmpty => _length == 0;

    public string GetText()
    {
        if (_cachedText is not null)
        {
            return _cachedText;
        }

        if (_length == 0)
        {
            return string.Empty;
        }

        var chars = new char[_length];
        var position = 0;
        foreach (var piece in _pieces)
        {
            var buffer = _storage.GetBuffer(piece.BufferIndex);
            buffer.AsSpan(piece.Start, piece.Length).CopyTo(chars.AsSpan(position));
            position += piece.Length;
        }

        _cachedText = new string(chars);
        return _cachedText;
    }

    public string GetSlice(int start, int length)
    {
        if (_length == 0)
        {
            return string.Empty;
        }

        start = Math.Clamp(start, 0, _length);
        length = Math.Clamp(length, 0, _length - start);
        if (length == 0)
        {
            return string.Empty;
        }

        var chars = new char[length];
        var remaining = length;
        var position = 0;
        var global = 0;

        foreach (var piece in _pieces)
        {
            if (remaining == 0)
            {
                break;
            }

            var pieceEnd = global + piece.Length;
            if (pieceEnd <= start)
            {
                global = pieceEnd;
                continue;
            }

            var overlapStart = Math.Max(start, global);
            var overlapEnd = Math.Min(pieceEnd, start + length);
            var overlapLength = overlapEnd - overlapStart;
            if (overlapLength > 0)
            {
                var buffer = _storage.GetBuffer(piece.BufferIndex);
                var bufferStart = piece.Start + (overlapStart - global);
                buffer.AsSpan(bufferStart, overlapLength).CopyTo(chars.AsSpan(position));
                position += overlapLength;
                remaining -= overlapLength;
            }

            global = pieceEnd;
        }

        return position == length ? new string(chars) : new string(chars, 0, position);
    }

    public TextBuffer SliceBuffer(int start, int length)
    {
        if (_length == 0)
        {
            return new TextBuffer(_storage, new List<Piece>(), 0);
        }

        start = Math.Clamp(start, 0, _length);
        length = Math.Clamp(length, 0, _length - start);
        if (length == 0)
        {
            return new TextBuffer(_storage, new List<Piece>(), 0);
        }

        var pieces = new List<Piece>();
        var global = 0;
        var end = start + length;

        foreach (var piece in _pieces)
        {
            var pieceEnd = global + piece.Length;
            if (pieceEnd <= start)
            {
                global = pieceEnd;
                continue;
            }

            if (global >= end)
            {
                break;
            }

            var overlapStart = Math.Max(start, global);
            var overlapEnd = Math.Min(end, pieceEnd);
            var overlapLength = overlapEnd - overlapStart;
            if (overlapLength > 0)
            {
                var bufferStart = piece.Start + (overlapStart - global);
                pieces.Add(new Piece(piece.BufferIndex, bufferStart, overlapLength));
            }

            global = pieceEnd;
        }

        return new TextBuffer(_storage, pieces, length);
    }

    public void Insert(int index, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        index = Math.Clamp(index, 0, _length);
        var bufferIndex = _storage.AddBuffer(text);
        var newPiece = new Piece(bufferIndex, 0, text.Length);

        if (_pieces.Count == 0)
        {
            _pieces.Add(newPiece);
            _length = text.Length;
            _cachedText = null;
            return;
        }

        LocatePiece(index, out var pieceIndex, out var offsetInPiece);
        if (pieceIndex >= _pieces.Count)
        {
            _pieces.Add(newPiece);
        }
        else
        {
            var piece = _pieces[pieceIndex];
            if (offsetInPiece <= 0)
            {
                _pieces.Insert(pieceIndex, newPiece);
            }
            else if (offsetInPiece >= piece.Length)
            {
                _pieces.Insert(pieceIndex + 1, newPiece);
            }
            else
            {
                var left = new Piece(piece.BufferIndex, piece.Start, offsetInPiece);
                var right = new Piece(piece.BufferIndex, piece.Start + offsetInPiece, piece.Length - offsetInPiece);
                _pieces[pieceIndex] = left;
                _pieces.Insert(pieceIndex + 1, newPiece);
                _pieces.Insert(pieceIndex + 2, right);
            }
        }

        _length += text.Length;
        _cachedText = null;
    }

    public void Delete(int start, int length)
    {
        if (_length == 0 || length <= 0)
        {
            return;
        }

        start = Math.Clamp(start, 0, _length);
        length = Math.Clamp(length, 0, _length - start);
        if (length <= 0)
        {
            return;
        }

        var end = start + length;
        var newPieces = new List<Piece>(_pieces.Count);
        var global = 0;

        foreach (var piece in _pieces)
        {
            var pieceEnd = global + piece.Length;
            if (pieceEnd <= start || global >= end)
            {
                newPieces.Add(piece);
            }
            else
            {
                var overlapStart = Math.Max(start, global);
                var overlapEnd = Math.Min(end, pieceEnd);
                var leftLength = overlapStart - global;
                if (leftLength > 0)
                {
                    newPieces.Add(new Piece(piece.BufferIndex, piece.Start, leftLength));
                }

                var rightLength = pieceEnd - overlapEnd;
                if (rightLength > 0)
                {
                    var rightStart = piece.Start + (piece.Length - rightLength);
                    newPieces.Add(new Piece(piece.BufferIndex, rightStart, rightLength));
                }
            }

            global = pieceEnd;
        }

        _pieces.Clear();
        _pieces.AddRange(newPieces);
        _length -= length;
        _cachedText = null;
    }

    public void Append(TextBuffer other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other._length == 0)
        {
            return;
        }

        if (_pieces.Count == 0)
        {
            CopyFrom(other);
            return;
        }

        if (ReferenceEquals(_storage, other._storage))
        {
            _pieces.AddRange(other._pieces);
        }
        else
        {
            var offset = _storage.AddBuffersFrom(other._storage);
            foreach (var piece in other._pieces)
            {
                _pieces.Add(new Piece(piece.BufferIndex + offset, piece.Start, piece.Length));
            }
        }

        _length += other._length;
        _cachedText = null;
    }

    private void CopyFrom(TextBuffer other)
    {
        if (ReferenceEquals(_storage, other._storage))
        {
            _pieces.AddRange(other._pieces);
        }
        else
        {
            var offset = _storage.AddBuffersFrom(other._storage);
            foreach (var piece in other._pieces)
            {
                _pieces.Add(new Piece(piece.BufferIndex + offset, piece.Start, piece.Length));
            }
        }

        _length = other._length;
        _cachedText = null;
    }

    private void LocatePiece(int index, out int pieceIndex, out int offsetInPiece)
    {
        var position = 0;
        for (var i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];
            var end = position + piece.Length;
            if (index <= end)
            {
                pieceIndex = i;
                offsetInPiece = Math.Clamp(index - position, 0, piece.Length);
                return;
            }

            position = end;
        }

        pieceIndex = _pieces.Count;
        offsetInPiece = 0;
    }

    private readonly struct Piece
    {
        public int BufferIndex { get; }
        public int Start { get; }
        public int Length { get; }

        public Piece(int bufferIndex, int start, int length)
        {
            BufferIndex = bufferIndex;
            Start = start;
            Length = length;
        }
    }

    private sealed class Storage
    {
        private readonly List<string> _buffers;

        public Storage(string text)
        {
            _buffers = new List<string> { text ?? string.Empty };
        }

        public int AddBuffer(string text)
        {
            _buffers.Add(text ?? string.Empty);
            return _buffers.Count - 1;
        }

        public int AddBuffersFrom(Storage other)
        {
            var offset = _buffers.Count;
            _buffers.AddRange(other._buffers);
            return offset;
        }

        public string GetBuffer(int index) => _buffers[index];
    }
}
