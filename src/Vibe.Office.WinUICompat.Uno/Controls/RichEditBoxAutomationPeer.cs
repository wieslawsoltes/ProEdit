using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Vibe.Office.WinUICompat.Controls;

internal sealed class RichEditBoxAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    private readonly RichEditBox _owner;

    public RichEditBoxAutomationPeer(RichEditBox owner)
        : base(owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Document;
    }

    protected override string GetClassNameCore()
    {
        return nameof(RichEditBox);
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    bool IValueProvider.IsReadOnly => _owner.IsReadOnly;

    string IValueProvider.Value => _owner.GetAutomationTextValueForTests();

    void IValueProvider.SetValue(string value)
    {
        if (_owner.IsReadOnly)
        {
            throw new InvalidOperationException("RichEditBox is read-only.");
        }

        _owner.ReplaceText(value ?? string.Empty);
    }
}
