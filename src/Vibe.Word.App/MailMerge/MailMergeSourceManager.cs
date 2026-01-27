using Avalonia.Controls;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public sealed class MailMergeSourceManager : IMailMergeSourceManager
{
    private readonly Window _owner;

    public MailMergeSourceManager(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public async ValueTask<MailMergeData?> EditRecipientsAsync(MailMergeData? currentData)
    {
        var dialog = new MailMergeRecipientsDialog(currentData);
        return await dialog.ShowDialog<MailMergeData?>(_owner);
    }
}
