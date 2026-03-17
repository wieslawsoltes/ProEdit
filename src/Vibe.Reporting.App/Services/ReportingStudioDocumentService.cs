using Vibe.Office.Reporting;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Rdl;
using Vibe.Office.Reporting.Serialization;

namespace Vibe.Reporting.App.Services;

internal enum ReportingStudioDocumentKind
{
    Sample,
    NativeTemplate,
    ImportedRdl
}

internal sealed class ReportingStudioWorkspace(
    ReportViewerSource source,
    ReportingStudioDocumentKind documentKind,
    string? path,
    IReadOnlyList<ReportDiagnostic> diagnostics)
{
    public ReportViewerSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    public ReportingStudioDocumentKind DocumentKind { get; } = documentKind;

    public string? Path { get; } = path;

    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; } = diagnostics ?? Array.Empty<ReportDiagnostic>();

    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

internal sealed class ReportingStudioWorkspaceLoadResult(
    ReportingStudioWorkspace? workspace,
    IReadOnlyList<ReportDiagnostic> diagnostics)
{
    public ReportingStudioWorkspace? Workspace { get; } = workspace;

    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; } = diagnostics ?? Array.Empty<ReportDiagnostic>();

    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

internal sealed class ReportingStudioDocumentWriteResult(
    string path,
    IReadOnlyList<ReportDiagnostic> diagnostics)
{
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; } = diagnostics ?? Array.Empty<ReportDiagnostic>();

    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

internal sealed class ReportingStudioDocumentService
{
    private static readonly string[] NativeReportFileNames = [".vreport.json", ".json"];
    private static readonly string[] RdlFileNames = [".rdl", ".rdlx", ".xml"];

    private readonly IReportTemplateSerializer _templateSerializer;
    private readonly IReportRdlSerializer _rdlSerializer;
    private readonly ReportingStudioWorkspaceFactory _workspaceFactory;

    public ReportingStudioDocumentService(
        IReportTemplateSerializer templateSerializer,
        IReportRdlSerializer rdlSerializer,
        ReportingStudioWorkspaceFactory workspaceFactory)
    {
        _templateSerializer = templateSerializer ?? throw new ArgumentNullException(nameof(templateSerializer));
        _rdlSerializer = rdlSerializer ?? throw new ArgumentNullException(nameof(rdlSerializer));
        _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
    }

    public ReportDataConnectorCatalog ConnectorCatalog => _workspaceFactory.ConnectorCatalog;

    public ReportingStudioWorkspace CreateSampleWorkspace()
    {
        return _workspaceFactory.CreateSampleWorkspace();
    }

    public async ValueTask<ReportingStudioWorkspaceLoadResult> OpenNativeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var diagnostics = new List<ReportDiagnostic>();
        var readResult = await ReadNativeAsync(path, cancellationToken);
        diagnostics.AddRange(readResult.Diagnostics);
        if (readResult.ReportDefinition is null)
        {
            return new ReportingStudioWorkspaceLoadResult(null, diagnostics);
        }

        var referencedReports = new Dictionary<string, ReportDefinition>(StringComparer.OrdinalIgnoreCase);
        await LoadSiblingReferencedReportsAsync(
            Path.GetDirectoryName(path),
            readResult.ReportDefinition,
            referencedReports,
            diagnostics,
            cancellationToken);

        var workspace = new ReportingStudioWorkspace(
            _workspaceFactory.CreateSource(readResult.ReportDefinition, referencedReports),
            ReportingStudioDocumentKind.NativeTemplate,
            path,
            diagnostics.ToArray());

        return new ReportingStudioWorkspaceLoadResult(workspace, diagnostics.ToArray());
    }

    public async ValueTask<ReportingStudioWorkspaceLoadResult> ImportRdlAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var diagnostics = new List<ReportDiagnostic>();
        var readResult = await ReadRdlAsync(path, cancellationToken);
        diagnostics.AddRange(readResult.Diagnostics);
        if (readResult.ReportDefinition is null)
        {
            return new ReportingStudioWorkspaceLoadResult(null, diagnostics);
        }

        var referencedReports = new Dictionary<string, ReportDefinition>(StringComparer.OrdinalIgnoreCase);
        await LoadSiblingReferencedReportsAsync(
            Path.GetDirectoryName(path),
            readResult.ReportDefinition,
            referencedReports,
            diagnostics,
            cancellationToken);

        var workspace = new ReportingStudioWorkspace(
            _workspaceFactory.CreateSource(readResult.ReportDefinition, referencedReports),
            ReportingStudioDocumentKind.ImportedRdl,
            path,
            diagnostics.ToArray());

        return new ReportingStudioWorkspaceLoadResult(workspace, diagnostics.ToArray());
    }

    public async ValueTask<ReportingStudioDocumentWriteResult> SaveNativeAsync(
        ReportDefinition reportDefinition,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        path = EnsureNativeTemplateExtension(path);
        CreateDirectoryIfNeeded(path);

        await using var stream = File.Create(path);
        var result = await _templateSerializer.WriteAsync(reportDefinition, stream, cancellationToken);
        return new ReportingStudioDocumentWriteResult(path, result.Diagnostics);
    }

    public async ValueTask<ReportingStudioDocumentWriteResult> ExportRdlAsync(
        ReportDefinition reportDefinition,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        path = EnsureRdlExtension(path);
        CreateDirectoryIfNeeded(path);

        await using var stream = File.Create(path);
        var result = await _rdlSerializer.WriteAsync(reportDefinition, stream, cancellationToken: cancellationToken);
        return new ReportingStudioDocumentWriteResult(path, result.Diagnostics);
    }

    public static bool IsNativeTemplatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return NativeReportFileNames.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsRdlPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return RdlFileNames.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static string EnsureNativeTemplateExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.EndsWith(".vreport.json", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, null) + ".vreport.json";
        }

        return path + ".vreport.json";
    }

    public static string EnsureRdlExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.EndsWith(".rdl", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.EndsWith(".rdlx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(path, ".rdl");
        }

        return path + ".rdl";
    }

    private async ValueTask<ReportTemplateReadResult> ReadNativeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await _templateSerializer.ReadAsync(stream, cancellationToken);
    }

    private async ValueTask<ReportRdlReadResult> ReadRdlAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await _rdlSerializer.ReadAsync(stream, cancellationToken);
    }

    private async ValueTask LoadSiblingReferencedReportsAsync(
        string? directory,
        ReportDefinition rootDefinition,
        Dictionary<string, ReportDefinition> referencedReports,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var pending = new Queue<string>(EnumerateReferencedReportIds(rootDefinition));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reportId = pending.Dequeue();
            if (!seen.Add(reportId) || referencedReports.ContainsKey(reportId))
            {
                continue;
            }

            if (!TryResolveReferencedReportPath(directory, reportId, out var path))
            {
                diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Warning,
                    ReportDiagnosticCodes.SubreportNotFound,
                    $"Referenced report '{reportId}' was not found next to the selected report.",
                    $"$references.{reportId}"));
                continue;
            }

            if (IsNativeTemplatePath(path))
            {
                var nativeResult = await ReadNativeAsync(path, cancellationToken);
                diagnostics.AddRange(nativeResult.Diagnostics);
                if (nativeResult.ReportDefinition is null)
                {
                    continue;
                }

                referencedReports[reportId] = nativeResult.ReportDefinition;
                EnqueueReferencedReportIds(nativeResult.ReportDefinition, pending, seen);
                continue;
            }

            var rdlResult = await ReadRdlAsync(path, cancellationToken);
            diagnostics.AddRange(rdlResult.Diagnostics);
            if (rdlResult.ReportDefinition is null)
            {
                continue;
            }

            referencedReports[reportId] = rdlResult.ReportDefinition;
            EnqueueReferencedReportIds(rdlResult.ReportDefinition, pending, seen);
        }
    }

    private static void EnqueueReferencedReportIds(
        ReportDefinition reportDefinition,
        Queue<string> pending,
        HashSet<string> seen)
    {
        foreach (var reportId in EnumerateReferencedReportIds(reportDefinition))
        {
            if (!seen.Contains(reportId))
            {
                pending.Enqueue(reportId);
            }
        }
    }

    private static IEnumerable<string> EnumerateReferencedReportIds(ReportDefinition reportDefinition)
    {
        ArgumentNullException.ThrowIfNull(reportDefinition);

        foreach (var section in reportDefinition.Sections)
        {
            foreach (var item in EnumerateSectionItems(section))
            {
                if (item is SubreportItem subreport && !string.IsNullOrWhiteSpace(subreport.ReportReferenceId))
                {
                    yield return subreport.ReportReferenceId;
                }

                if (!string.IsNullOrWhiteSpace(item.DrillthroughAction?.ReportReferenceId))
                {
                    yield return item.DrillthroughAction.ReportReferenceId;
                }
            }
        }
    }

    private static IEnumerable<ReportItem> EnumerateSectionItems(ReportSection section)
    {
        foreach (var item in section.HeaderItems)
        {
            yield return item;
        }

        foreach (var item in section.BodyItems)
        {
            yield return item;
        }

        foreach (var item in section.FooterItems)
        {
            yield return item;
        }
    }

    private static bool TryResolveReferencedReportPath(string directory, string reportId, out string path)
    {
        var candidates = new[]
        {
            Path.Combine(directory, reportId + ".vreport.json"),
            Path.Combine(directory, reportId + ".json"),
            Path.Combine(directory, reportId + ".rdl"),
            Path.Combine(directory, reportId + ".rdlx")
        };

        for (var index = 0; index < candidates.Length; index++)
        {
            if (File.Exists(candidates[index]))
            {
                path = candidates[index];
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static void CreateDirectoryIfNeeded(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
