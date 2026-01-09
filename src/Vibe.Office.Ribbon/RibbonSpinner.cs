namespace Vibe.Office.Ribbon;

public sealed class RibbonSpinner : RibbonControlBase
{
    private double? _value;
    private readonly Func<double?>? _valueEvaluator;
    private readonly Func<double?, ValueTask>? _valueChangedHandler;

    public RibbonSpinner(
        string id,
        string label,
        double? value = null,
        double step = 1d,
        double? minimum = null,
        double? maximum = null,
        bool isEditable = true,
        string? format = null,
        Func<double?>? valueEvaluator = null,
        Func<double?, ValueTask>? valueChangedHandler = null,
        string? keyTip = null,
        string? iconKey = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? canExecute = null,
        Func<bool>? isVisibleEvaluator = null,
        RibbonControlSize size = RibbonControlSize.Medium)
        : base(
            id,
            label,
            keyTip,
            iconKey,
            size,
            isEnabled,
            isVisible,
            canExecute,
            isVisibleEvaluator)
    {
        Step = step > 0d ? step : 1d;
        Minimum = minimum;
        Maximum = maximum;
        IsEditable = isEditable;
        Format = format;
        _valueEvaluator = valueEvaluator;
        _valueChangedHandler = valueChangedHandler;
        _value = Clamp(valueEvaluator?.Invoke() ?? value);
    }

    public double? Value
    {
        get => _value;
        private set => SetField(ref _value, value, nameof(Value));
    }

    public double Step { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public bool IsEditable { get; }
    public string? Format { get; }

    public ValueTask IncreaseAsync()
    {
        if (!IsEnabled || !IsVisible)
        {
            return ValueTask.CompletedTask;
        }

        var baseValue = Value ?? Minimum ?? 0d;
        return SetValueAsync(baseValue + Step);
    }

    public ValueTask DecreaseAsync()
    {
        if (!IsEnabled || !IsVisible)
        {
            return ValueTask.CompletedTask;
        }

        var baseValue = Value ?? Minimum ?? 0d;
        return SetValueAsync(baseValue - Step);
    }

    public async ValueTask SetValueAsync(double? value)
    {
        if (!IsEnabled || !IsVisible)
        {
            return;
        }

        var clamped = Clamp(value);
        if (_valueChangedHandler is not null)
        {
            await _valueChangedHandler(clamped);
        }

        Value = clamped;
    }

    public override void RefreshState()
    {
        base.RefreshState();
        if (_valueEvaluator is not null)
        {
            Value = Clamp(_valueEvaluator());
        }
    }

    private double? Clamp(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var result = value.Value;
        if (Minimum.HasValue && result < Minimum.Value)
        {
            result = Minimum.Value;
        }

        if (Maximum.HasValue && result > Maximum.Value)
        {
            result = Maximum.Value;
        }

        return result;
    }
}
