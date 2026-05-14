using System.Collections.ObjectModel;

namespace ProEdit.Word.Avalonia;

public sealed class RibbonQuickAccessDialogViewModel
{
    public RibbonQuickAccessDialogViewModel(IEnumerable<RibbonQuickAccessCandidate> candidates)
    {
        Candidates = new ObservableCollection<RibbonQuickAccessCandidate>(candidates ?? Array.Empty<RibbonQuickAccessCandidate>());
    }

    public ObservableCollection<RibbonQuickAccessCandidate> Candidates { get; }
}
