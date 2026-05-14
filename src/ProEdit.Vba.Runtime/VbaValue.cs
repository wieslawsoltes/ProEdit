namespace ProEdit.Vba.Runtime;

public readonly record struct VbaValue(VbaValueKind Kind, object? Value)
{
    public static readonly VbaValue Empty = new(VbaValueKind.Empty, null);

    public static VbaValue FromDouble(double value) => new(VbaValueKind.Double, value);
    public static VbaValue FromString(string value) => new(VbaValueKind.String, value ?? string.Empty);
    public static VbaValue FromBoolean(bool value) => new(VbaValueKind.Boolean, value);
    public static VbaValue FromObjectPath(string path) => new(VbaValueKind.Object, new VbaObjectReference(path));
    public static VbaValue FromArray(VbaArray array) => new(VbaValueKind.Array, array);

    public double AsDouble()
    {
        if (Kind == VbaValueKind.Double && Value is double number)
        {
            return number;
        }

        if (Kind == VbaValueKind.Boolean && Value is bool boolean)
        {
            return boolean ? 1d : 0d;
        }

        if (Kind == VbaValueKind.String && Value is string text
            && double.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return 0d;
    }

    public bool AsBoolean()
    {
        if (Kind == VbaValueKind.Boolean && Value is bool boolean)
        {
            return boolean;
        }

        if (Kind == VbaValueKind.Double && Value is double number)
        {
            return Math.Abs(number) > double.Epsilon;
        }

        if (Kind == VbaValueKind.String && Value is string text)
        {
            return !string.IsNullOrEmpty(text);
        }

        return false;
    }

    public string AsString()
    {
        return Value?.ToString() ?? string.Empty;
    }

    public string? AsObjectPath()
    {
        return Value is VbaObjectReference reference ? reference.Path : null;
    }

    public VbaArray? AsArray()
    {
        return Value as VbaArray;
    }
}

public enum VbaValueKind
{
    Empty,
    Double,
    String,
    Boolean,
    Object,
    Array
}

public sealed record VbaObjectReference(string Path);

public sealed class VbaArray
{
    private readonly int[] _lowerBounds;
    private readonly int[] _upperBounds;
    private readonly int[] _strides;
    private readonly VbaValue[] _data;

    public VbaArray(IReadOnlyList<int> lowerBounds, IReadOnlyList<int> upperBounds)
    {
        if (lowerBounds.Count != upperBounds.Count)
        {
            throw new ArgumentException("Array bounds must have matching rank.");
        }

        _lowerBounds = lowerBounds.ToArray();
        _upperBounds = upperBounds.ToArray();
        _strides = new int[_lowerBounds.Length];

        var length = 1;
        for (var i = _lowerBounds.Length - 1; i >= 0; i--)
        {
            _strides[i] = length;
            var count = _upperBounds[i] - _lowerBounds[i] + 1;
            if (count < 0)
            {
                count = 0;
            }

            length *= count;
        }

        _data = new VbaValue[length];
    }

    public int Rank => _lowerBounds.Length;
    public IReadOnlyList<int> LowerBounds => _lowerBounds;
    public IReadOnlyList<int> UpperBounds => _upperBounds;

    public VbaValue Get(ReadOnlySpan<int> indices)
    {
        var index = ComputeIndex(indices);
        if (index < 0 || index >= _data.Length)
        {
            return VbaValue.Empty;
        }

        return _data[index];
    }

    public void Set(ReadOnlySpan<int> indices, VbaValue value)
    {
        var index = ComputeIndex(indices);
        if (index < 0 || index >= _data.Length)
        {
            return;
        }

        _data[index] = value;
    }

    private int ComputeIndex(ReadOnlySpan<int> indices)
    {
        if (indices.Length != _lowerBounds.Length)
        {
            return -1;
        }

        var index = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            var normalized = indices[i] - _lowerBounds[i];
            if (normalized < 0)
            {
                return -1;
            }

            index += normalized * _strides[i];
        }

        return index;
    }
}
