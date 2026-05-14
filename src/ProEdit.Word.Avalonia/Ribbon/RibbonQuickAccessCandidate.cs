using System.ComponentModel;
using ProEdit.Ribbon;

namespace ProEdit.Word.Avalonia;

public sealed class RibbonQuickAccessCandidate : INotifyPropertyChanged
{
    private bool _isSelected;

    public RibbonQuickAccessCandidate(IRibbonControl control, string tabHeader, string groupHeader, bool isSelected)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
        TabHeader = tabHeader ?? throw new ArgumentNullException(nameof(tabHeader));
        GroupHeader = groupHeader ?? throw new ArgumentNullException(nameof(groupHeader));
        _isSelected = isSelected;
    }

    public IRibbonControl Control { get; }
    public string TabHeader { get; }
    public string GroupHeader { get; }
    public string Id => Control.Id;
    public string Label => Control.Label;
    public string GroupPath => string.IsNullOrWhiteSpace(GroupHeader)
        ? TabHeader
        : $"{TabHeader} - {GroupHeader}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
