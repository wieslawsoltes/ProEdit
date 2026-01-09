using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal struct EditorValueAccumulator<T>
{
    private bool _hasValue;
    private bool _isMixed;
    private T? _value;

    public void Add(T value)
    {
        if (!_hasValue)
        {
            _value = value;
            _hasValue = true;
            return;
        }

        if (!EqualityComparer<T>.Default.Equals(_value, value))
        {
            _isMixed = true;
        }
    }

    public EditorValue<T> Build()
    {
        if (_isMixed)
        {
            return EditorValue<T>.Mixed();
        }

        if (!_hasValue)
        {
            return EditorValue<T>.Missing();
        }

        return EditorValue<T>.FromValue(_value!);
    }
}

internal struct OptionalEditorValueAccumulator<T> where T : class
{
    private bool _hasValue;
    private bool _hasMissing;
    private bool _isMixed;
    private T? _value;

    public void Add(T? value)
    {
        if (value is null)
        {
            _hasMissing = true;
            return;
        }

        if (!_hasValue)
        {
            _value = value;
            _hasValue = true;
            return;
        }

        if (!EqualityComparer<T>.Default.Equals(_value, value))
        {
            _isMixed = true;
        }
    }

    public EditorValue<T> Build()
    {
        if (_isMixed || (_hasValue && _hasMissing))
        {
            return EditorValue<T>.Mixed();
        }

        if (!_hasValue)
        {
            return EditorValue<T>.Missing();
        }

        return EditorValue<T>.FromValue(_value!);
    }
}

internal struct NullableEditorValueAccumulator<T> where T : struct
{
    private bool _hasValue;
    private bool _hasMissing;
    private bool _isMixed;
    private T _value;

    public void Add(T? value)
    {
        if (!value.HasValue)
        {
            _hasMissing = true;
            return;
        }

        if (!_hasValue)
        {
            _value = value.Value;
            _hasValue = true;
            return;
        }

        if (!EqualityComparer<T>.Default.Equals(_value, value.Value))
        {
            _isMixed = true;
        }
    }

    public EditorValue<T> Build()
    {
        if (_isMixed || (_hasValue && _hasMissing))
        {
            return EditorValue<T>.Mixed();
        }

        if (!_hasValue)
        {
            return EditorValue<T>.Missing();
        }

        return EditorValue<T>.FromValue(_value);
    }
}
