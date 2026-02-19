using Windows.Storage.Pickers;

namespace Vibe.WinUICompat.App.Services;

public sealed class UnoFlowDocumentFilePickerService : IFlowDocumentFilePickerService
{
    public async Task<string?> PickOpenPathAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".pdx");

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "flow-document" : suggestedFileName,
            };

            picker.FileTypeChoices.Add("Word Document", [".docx"]);
            picker.FileTypeChoices.Add("Markdown", [".md"]);
            picker.FileTypeChoices.Add("PDF", [".pdf"]);

            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        catch
        {
            return null;
        }
    }
}
