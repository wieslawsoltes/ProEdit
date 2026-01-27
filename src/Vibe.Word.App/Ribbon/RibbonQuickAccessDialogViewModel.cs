using System.Collections.ObjectModel;

namespace Vibe.Word.App;

public sealed class RibbonQuickAccessDialogViewModel
{
    public RibbonQuickAccessDialogViewModel(IEnumerable<RibbonQuickAccessCandidate> candidates)
    {
        Candidates = new ObservableCollection<RibbonQuickAccessCandidate>(candidates ?? Array.Empty<RibbonQuickAccessCandidate>());
    }

    public ObservableCollection<RibbonQuickAccessCandidate> Candidates { get; }
}
