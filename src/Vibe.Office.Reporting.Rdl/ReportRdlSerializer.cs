using System.Text;

namespace Vibe.Office.Reporting.Rdl;

/// <summary>
/// Default implementation of <see cref="IReportRdlSerializer" />.
/// </summary>
public sealed class ReportRdlSerializer : IReportRdlSerializer
{
    /// <inheritdoc />
    public ReportRdlReadResult Read(string xml)
    {
        return new ReportRdlImporter().Read(xml);
    }

    /// <inheritdoc />
    public async ValueTask<ReportRdlReadResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var xml = await reader.ReadToEndAsync(cancellationToken);
        return Read(xml);
    }

    /// <inheritdoc />
    public ReportRdlWriteResult Write(
        ReportDefinition reportDefinition,
        ReportRdlWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);

        return new ReportRdlExporter(reportDefinition, options ?? new ReportRdlWriteOptions()).Write();
    }

    /// <inheritdoc />
    public async ValueTask<ReportRdlWriteResult> WriteAsync(
        ReportDefinition reportDefinition,
        Stream stream,
        ReportRdlWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);
        ArgumentNullException.ThrowIfNull(stream);

        var result = Write(reportDefinition, options);
        if (!result.HasErrors)
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            await writer.WriteAsync(result.Xml.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        return result;
    }
}
