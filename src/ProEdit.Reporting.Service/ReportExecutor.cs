using System.Diagnostics;
using ProEdit.Reporting.DocumentComposition;
using ProEdit.Reporting.Expressions;
using ProEdit.Reporting.Materialization;

namespace ProEdit.Reporting.Service;

/// <summary>
/// Default host-side implementation of <see cref="IReportExecutor" />.
/// </summary>
public sealed class ReportExecutor : IReportExecutor
{
    private readonly IReportDocumentComposer _documentComposer;
    private readonly ReportExecutionEnvironment _environment;
    private readonly IReportMaterializer _materializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportExecutor" /> class.
    /// </summary>
    /// <param name="environment">The execution environment.</param>
    /// <param name="expressionCompiler">The optional expression compiler used to create default runtime services.</param>
    /// <param name="materializer">The optional materializer.</param>
    /// <param name="documentComposer">The optional document composer.</param>
    public ReportExecutor(
        ReportExecutionEnvironment environment,
        IReportExpressionCompiler? expressionCompiler = null,
        IReportMaterializer? materializer = null,
        IReportDocumentComposer? documentComposer = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        var compiler = expressionCompiler ?? new ReportExpressionCompiler();
        _materializer = materializer ?? new ReportMaterializer(compiler);
        _documentComposer = documentComposer ?? new ReportDocumentComposer();
    }

    /// <inheritdoc />
    public async ValueTask<ReportExecutionResult> ExecuteAsync(
        ReportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var result = new ReportExecutionResult();

        var materializationRequest = new ReportMaterializationRequest
        {
            ReportDefinition = request.ReportDefinition,
            ProviderRegistry = _environment.ProviderRegistry,
            HostDataRegistry = _environment.HostDataRegistry,
            Culture = request.Culture,
            UiCulture = request.UiCulture,
            TimeZone = request.TimeZone
        };

        ReportServiceModelCloner.CopyParameters(request.ParameterValues, materializationRequest.ParameterValues);
        CopyGlobals(_environment.Globals, materializationRequest.Globals);
        CopyReferencedReports(_environment.ReferencedReports, materializationRequest.ReferencedReports);

        var materialization = await _materializer.MaterializeAsync(materializationRequest, cancellationToken);
        ReportServiceModelCloner.AddDiagnostics(materialization.Diagnostics, result.Diagnostics);
        ReportServiceModelCloner.CopyParameters(materialization.ResolvedParameters, result.ResolvedParameters);
        result.MaterializedReport = materialization.MaterializedReport;

        if (materialization.MaterializedReport is not null)
        {
            result.Metrics.DataRowCount = CountDataRows(materialization.MaterializedReport);

            var composition = await _documentComposer.ComposeAsync(
                new ReportDocumentCompositionRequest
                {
                    MaterializedReport = materialization.MaterializedReport
                },
                cancellationToken);
            ReportServiceModelCloner.AddDiagnostics(composition.Diagnostics, result.Diagnostics);
            result.Document = composition.Document;
        }

        stopwatch.Stop();
        result.Metrics.Duration = stopwatch.Elapsed;
        return result;
    }

    private static void CopyGlobals(
        IReadOnlyDictionary<string, object?> source,
        Dictionary<string, object?> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void CopyReferencedReports(
        IReadOnlyDictionary<string, ReportDefinition> source,
        Dictionary<string, ReportDefinition> target)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static int CountDataRows(MaterializedReport materializedReport)
    {
        var rowCount = 0;
        for (var index = 0; index < materializedReport.DataSets.Count; index++)
        {
            rowCount += materializedReport.DataSets[index].Rows.Count;
        }

        return rowCount;
    }
}
