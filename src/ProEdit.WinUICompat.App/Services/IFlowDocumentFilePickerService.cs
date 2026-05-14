namespace ProEdit.WinUICompat.App.Services;

public interface IFlowDocumentFilePickerService
{
    Task<string?> PickOpenPathAsync();

    Task<string?> PickSavePathAsync(string suggestedFileName);
}
