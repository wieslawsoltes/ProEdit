using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive;

namespace Vibe.Office.Reporting.Avalonia.Designer;

/// <summary>
/// One placeholder that can be inserted into template content.
/// </summary>
public sealed class ReportDesignerTemplatePlaceholderEntryViewModel
{
    public ReportDesignerTemplatePlaceholderEntryViewModel(
        string key,
        string token,
        string label,
        string expression,
        string category)
    {
        Key = key ?? string.Empty;
        Token = token ?? string.Empty;
        Label = label ?? string.Empty;
        Expression = expression ?? string.Empty;
        Category = category ?? string.Empty;
    }

    public string Key { get; }

    public string Token { get; }

    public string Label { get; }

    public string Expression { get; }

    public string Category { get; }
}

/// <summary>
/// One editable template binding row.
/// </summary>
public sealed class ReportDesignerTemplateBindingEntryViewModel : ReactiveObject
{
    private readonly Action<ReportDesignerTemplateBindingEntryViewModel> _apply;
    private string _expression;
    private string _key;

    internal ReportDesignerTemplateBindingEntryViewModel(
        string key,
        string expression,
        Action<ReportDesignerTemplateBindingEntryViewModel> apply,
        Action<ReportDesignerTemplateBindingEntryViewModel> remove)
    {
        _key = key ?? string.Empty;
        _expression = expression ?? string.Empty;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        ArgumentNullException.ThrowIfNull(remove);
        RemoveCommand = DesignerCommandFactory.Create(() => remove(this));
    }

    public string Key
    {
        get => _key;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_key, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _key, normalized);
            _apply(this);
        }
    }

    public string Expression
    {
        get => _expression;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_expression, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _expression, normalized);
            _apply(this);
        }
    }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
}
