using System.ComponentModel;

namespace ProEdit.Ribbon;

public interface IRibbonStateful
{
    void RefreshState();
}

public abstract class RibbonStateNode : IRibbonStateful, INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _isVisible;
    private readonly Func<bool>? _isEnabledEvaluator;
    private readonly Func<bool>? _isVisibleEvaluator;

    protected RibbonStateNode(
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
    {
        _isEnabled = isEnabled;
        _isVisible = isVisible;
        _isEnabledEvaluator = isEnabledEvaluator;
        _isVisibleEvaluator = isVisibleEvaluator;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        protected set => SetField(ref _isEnabled, value, nameof(IsEnabled));
    }

    public bool IsVisible
    {
        get => _isVisible;
        protected set => SetField(ref _isVisible, value, nameof(IsVisible));
    }

    public virtual void RefreshState()
    {
        IsEnabled = EvaluateIsEnabled();
        IsVisible = EvaluateIsVisible();
    }

    protected bool EvaluateIsEnabled()
    {
        return _isEnabledEvaluator?.Invoke() ?? _isEnabled;
    }

    protected bool EvaluateIsVisible()
    {
        return _isVisibleEvaluator?.Invoke() ?? _isVisible;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}
