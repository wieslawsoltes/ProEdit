using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Vibe.Reporting.App.Services;

internal sealed class AvaloniaReportingStudioFilePickerService : IReportingStudioFilePickerService
{
    private static readonly FilePickerFileType NativeTemplateFileType = new("Vibe Report Template (*.vreport.json;*.json)")
    {
        Patterns = ["*.vreport.json", "*.json"],
        AppleUniformTypeIdentifiers = ["public.json"],
        MimeTypes = ["application/json", "text/json"]
    };

    private static readonly FilePickerFileType RdlFileType = new("Report Definition Language (*.rdl;*.rdlx;*.xml)")
    {
        Patterns = ["*.rdl", "*.rdlx", "*.xml"],
        AppleUniformTypeIdentifiers = ["public.xml"],
        MimeTypes = ["application/xml", "text/xml"]
    };

    private readonly Window _window;

    public AvaloniaReportingStudioFilePickerService(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public ValueTask<string?> PickOpenTemplatePathAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<string?>(InvokeOnUiThreadAsync(PickOpenTemplatePathCoreAsync));
    }

    public ValueTask<string?> PickSaveTemplatePathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<string?>(InvokeOnUiThreadAsync(() => PickSaveTemplatePathCoreAsync(suggestedFileName)));
    }

    public ValueTask<string?> PickImportRdlPathAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<string?>(InvokeOnUiThreadAsync(PickImportRdlPathCoreAsync));
    }

    public ValueTask<string?> PickExportRdlPathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<string?>(InvokeOnUiThreadAsync(() => PickExportRdlPathCoreAsync(suggestedFileName)));
    }

    private async Task<string?> PickOpenTemplatePathCoreAsync()
    {
        var result = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [NativeTemplateFileType]
        });

        return result.Count == 0 ? null : result[0].TryGetLocalPath();
    }

    private async Task<string?> PickSaveTemplatePathCoreAsync(string suggestedFileName)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "json",
            SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "report.vreport" : suggestedFileName,
            ShowOverwritePrompt = true,
            FileTypeChoices = [NativeTemplateFileType]
        });

        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickImportRdlPathCoreAsync()
    {
        var result = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [RdlFileType]
        });

        return result.Count == 0 ? null : result[0].TryGetLocalPath();
    }

    private async Task<string?> PickExportRdlPathCoreAsync(string suggestedFileName)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "rdl",
            SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "report" : suggestedFileName,
            ShowOverwritePrompt = true,
            FileTypeChoices = [RdlFileType]
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
