using System;
using System.Threading.Tasks;

namespace ProEdit.Ribbon;

public sealed class RibbonTextBox : RibbonControlBase
{
    private string? _text;
    private bool _suppressTextChanged;
    private readonly Func<string?>? _textEvaluator;
    private readonly Func<string?, ValueTask>? _textChangedHandler;
    private readonly Func<string?, ValueTask>? _submitHandler;

    public RibbonTextBox(
        string id,
        string label,
        string? placeholder = null,
        string? text = null,
        Func<string?>? textEvaluator = null,
        Func<string?, ValueTask>? textChangedHandler = null,
        Func<string?, ValueTask>? submitHandler = null,
        string? keyTip = null,
        string? iconKey = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? canExecute = null,
        Func<bool>? isVisibleEvaluator = null,
        RibbonControlSize size = RibbonControlSize.Medium,
        string? toolTipDescription = null,
        bool isEditable = true,
        string? compactLabel = null,
        RibbonLabelMode labelMode = RibbonLabelMode.Auto)
        : base(
            id,
            label,
            keyTip,
            iconKey,
            size,
            isEnabled,
            isVisible,
            canExecute,
            isVisibleEvaluator,
            toolTipDescription,
            compactLabel,
            labelMode)
    {
        Placeholder = placeholder;
        IsEditable = isEditable;
        _textEvaluator = textEvaluator;
        _textChangedHandler = textChangedHandler;
        _submitHandler = submitHandler;

        if (_textEvaluator is not null)
        {
            UpdateText(_textEvaluator());
        }
        else
        {
            _text = text;
        }
    }

    public string? Placeholder { get; }
    public bool IsEditable { get; }

    public string? Text
    {
        get => _text;
        set
        {
            if (!SetField(ref _text, value, nameof(Text)))
            {
                return;
            }

            if (_suppressTextChanged)
            {
                return;
            }

            _ = HandleTextChangedAsync(value);
        }
    }

    public override void RefreshState()
    {
        base.RefreshState();

        if (_textEvaluator is not null)
        {
            UpdateText(_textEvaluator());
        }
    }

    public async ValueTask SubmitAsync()
    {
        if (!IsEnabled || !IsVisible)
        {
            return;
        }

        if (_submitHandler is null)
        {
            return;
        }

        try
        {
            await _submitHandler(Text);
        }
        catch
        {
        }
    }

    private void UpdateText(string? value)
    {
        _suppressTextChanged = true;
        Text = value;
        _suppressTextChanged = false;
    }

    private async Task HandleTextChangedAsync(string? value)
    {
        if (!IsEnabled || !IsVisible || !IsEditable)
        {
            return;
        }

        if (_textChangedHandler is null)
        {
            return;
        }

        try
        {
            await _textChangedHandler(value);
        }
        catch
        {
        }
    }
}
