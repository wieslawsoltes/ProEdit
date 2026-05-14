using Avalonia.Controls;
using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

public sealed class MailMergeSourceManager : IMailMergeSourceManager
{
    private readonly Func<Window?> _ownerProvider;

    public MailMergeSourceManager(Func<Window?> ownerProvider)
    {
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
    }

    public async ValueTask<MailMergeData?> EditRecipientsAsync(MailMergeData? currentData)
    {
        var dialog = new MailMergeRecipientsDialog(currentData);
        var owner = _ownerProvider();
        if (owner is null)
        {
            dialog.Show();
            return null;
        }

        return await dialog.ShowDialog<MailMergeData?>(owner);
    }
}
