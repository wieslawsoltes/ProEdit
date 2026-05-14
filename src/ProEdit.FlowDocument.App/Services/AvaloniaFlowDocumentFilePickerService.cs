using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ProEdit.FlowDocument.App.Services;

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

    private static readonly FilePickerFileType RtfFileType = new("Rich Text Format (*.rtf)")
    {
        Patterns = ["*.rtf"],
        AppleUniformTypeIdentifiers = ["public.rtf"],
        MimeTypes = ["text/rtf", "application/rtf"]
    };

    private static readonly FilePickerFileType PdfFileType = new("Portable Document (*.pdf;*.pdx)")
    {
        Patterns = ["*.pdf", "*.pdx"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf"],
        MimeTypes = ["application/pdf"]
    };

    private static readonly FilePickerFileType PostScriptFileType = new("PostScript (*.ps;*.eps)")
    {
        Patterns = ["*.ps", "*.eps"],
        AppleUniformTypeIdentifiers = ["com.adobe.postscript", "com.adobe.encapsulated-postscript"],
        MimeTypes = ["application/postscript"]
    };

    private static readonly FilePickerFileType XpsFileType = new("XML Paper Specification (*.xps;*.oxps)")
    {
        Patterns = ["*.xps", "*.oxps"],
        AppleUniformTypeIdentifiers = ["org.openxps.xps-document"],
        MimeTypes = ["application/oxps", "application/vnd.ms-xpsdocument"]
    };

    private readonly Window _window;

    public AvaloniaFlowDocumentFilePickerService(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<string?> PickOpenPathAsync()
    {
        return await InvokeOnUiThreadAsync(PickOpenPathCoreAsync).ConfigureAwait(false);
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName)
    {
        return await InvokeOnUiThreadAsync(() => PickSavePathCoreAsync(suggestedFileName)).ConfigureAwait(false);
    }

    private async Task<string?> PickOpenPathCoreAsync()
    {
        var result = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [DocxFileType, MarkdownFileType, RtfFileType, PdfFileType, PostScriptFileType, XpsFileType]
        });

        if (result.Count == 0)
        {
            return null;
        }

        return result[0].TryGetLocalPath();
    }

    private async Task<string?> PickSavePathCoreAsync(string suggestedFileName)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "docx",
            SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "flow-document" : suggestedFileName,
            ShowOverwritePrompt = true,
            FileTypeChoices = [DocxFileType, MarkdownFileType, RtfFileType, PdfFileType, PostScriptFileType, XpsFileType]
        });

        return file?.TryGetLocalPath();
    }

    private static Task<T> InvokeOnUiThreadAsync<T>(Func<Task<T>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (Dispatcher.UIThread.CheckAccess())
        {
            return callback();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var result = await callback().ConfigureAwait(true);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }
}
