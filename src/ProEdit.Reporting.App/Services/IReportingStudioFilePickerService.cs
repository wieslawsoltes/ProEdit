namespace ProEdit.Reporting.App.Services;

internal interface IReportingStudioFilePickerService
{
    ValueTask<string?> PickOpenTemplatePathAsync(CancellationToken cancellationToken = default);

    ValueTask<string?> PickSaveTemplatePathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);

    ValueTask<string?> PickImportRdlPathAsync(CancellationToken cancellationToken = default);

    ValueTask<string?> PickExportRdlPathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
