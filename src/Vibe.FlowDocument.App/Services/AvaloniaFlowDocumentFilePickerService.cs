using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Vibe.FlowDocument.App.Services;

public sealed class AvaloniaFlowDocumentFilePickerService : IFlowDocumentFilePickerService
{
    private static readonly FilePickerFileType DocxFileType = new("Word Document (*.docx)")
    {
        Patterns = ["*.docx"],
        AppleUniformTypeIdentifiers = ["org.openxmlformats.wordprocessingml.document"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]
    };

    private static readonly FilePickerFileType MarkdownFileType = new("Markdown (*.md;*.markdown)")
    {
        Patterns = ["*.md", "*.markdown"],
        AppleUniformTypeIdentifiers = ["net.daringfireball.markdown", "public.plain-text"],
        MimeTypes = ["text/markdown", "text/plain"]
    };

    private static readonly FilePickerFileType PdfFileType = new("Portable Document (*.pdf;*.pdx)")
    {
        Patterns = ["*.pdf", "*.pdx"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf"],
        MimeTypes = ["application/pdf"]
    };

    private readonly Window _window;

    public AvaloniaFlowDocumentFilePickerService(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<string?> PickOpenPathAsync()
    {
        var result = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [DocxFileType, MarkdownFileType, PdfFileType]
        });

        if (result.Count == 0)
        {
            return null;
        }

        return result[0].TryGetLocalPath();
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "docx",
            SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "flow-document" : suggestedFileName,
            ShowOverwritePrompt = true,
            FileTypeChoices = [DocxFileType, MarkdownFileType, PdfFileType]
        });

        return file?.TryGetLocalPath();
    }
}
