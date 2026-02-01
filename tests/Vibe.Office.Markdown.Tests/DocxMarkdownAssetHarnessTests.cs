using System.Text.Json;
using Vibe.Office.Markdown;
using Vibe.Office.OpenXml;
using Xunit;

namespace Vibe.Office.Markdown.Tests;

public sealed class DocxMarkdownAssetHarnessTests
{
    [Fact]
    public void DocxAssetHarness_ExportsMarkdown()
    {
        var options = DocxMarkdownAssetHarnessOptions.FromEnvironment();
        if (!options.RunEnabled)
        {
            Console.WriteLine("DOCX Markdown asset harness disabled. Set DOCX_MD_ASSETS_RUN=1 to enable.");
            return;
        }

        if (!Directory.Exists(options.AssetsRoot))
        {
            Console.WriteLine($"DOCX assets not found at '{options.AssetsRoot}'. Set DOCX_MD_ASSETS_ROOT.");
            return;
        }

        var docxPaths = DocxMarkdownAssetHarness.EnumerateDocxAssets(options.AssetsRoot, options.MaxDocuments).ToList();
        if (docxPaths.Count == 0)
        {
            Console.WriteLine($"No .docx files found under '{options.AssetsRoot}'.");
            return;
        }

        var report = DocxMarkdownAssetHarness.Run(docxPaths, options);
        if (options.EmitReports)
        {
            DocxMarkdownAssetHarnessReporter.WriteReports(report, options);
        }

        Assert.True(report.ProcessedDocuments > 0, "No documents processed.");
    }
}

internal sealed class DocxMarkdownAssetHarnessOptions
{
    public string AssetsRoot { get; private set; } = string.Empty;
    public int MaxDocuments { get; private set; } = 10;
    public bool EmitReports { get; private set; } = true;
    public bool RunEnabled { get; private set; }
    public string ReportDirectory { get; private set; } = Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
    public string ReportPath { get; private set; } = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "docx-markdown-report.json");

    public static DocxMarkdownAssetHarnessOptions FromEnvironment()
    {
        var options = new DocxMarkdownAssetHarnessOptions
        {
            AssetsRoot = GetEnv("DOCX_MD_ASSETS_ROOT") ?? string.Empty,
            MaxDocuments = GetIntEnv("DOCX_MD_ASSETS_MAX_DOCS", 10),
            EmitReports = GetBoolEnv("DOCX_MD_ASSETS_REPORT", true),
            RunEnabled = GetBoolEnv("DOCX_MD_ASSETS_RUN", false)
        };

        var reportDir = GetEnv("DOCX_MD_ASSETS_REPORT_DIR");
        if (!string.IsNullOrWhiteSpace(reportDir))
        {
            options.ReportDirectory = reportDir;
        }

        var reportPath = GetEnv("DOCX_MD_ASSETS_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            options.ReportPath = reportPath;
        }

        return options;
    }

    private static string? GetEnv(string name) => Environment.GetEnvironmentVariable(name);

    private static int GetIntEnv(string name, int fallback)
    {
        var value = GetEnv(name);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static bool GetBoolEnv(string name, bool fallback)
    {
        var value = GetEnv(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Equals("1", StringComparison.Ordinal)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DocxMarkdownAssetHarness
{
    public static IEnumerable<string> EnumerateDocxAssets(string assetsRoot, int maxDocuments)
    {
        var candidates = Directory.EnumerateFiles(assetsRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal));

        if (maxDocuments > 0)
        {
            return TakeFirst(candidates, maxDocuments);
        }

        return candidates.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> TakeFirst(IEnumerable<string> candidates, int maxDocuments)
    {
        var count = 0;
        foreach (var path in candidates)
        {
            yield return path;
            count++;
            if (count >= maxDocuments)
            {
                yield break;
            }
        }
    }

    public static DocxMarkdownAssetHarnessReport Run(IReadOnlyList<string> docxPaths, DocxMarkdownAssetHarnessOptions options)
    {
        var report = new DocxMarkdownAssetHarnessReport();
        foreach (var docxPath in docxPaths)
        {
            var result = new DocxMarkdownAssetResult
            {
                RelativePath = Path.GetRelativePath(options.AssetsRoot, docxPath)
            };

            Vibe.Office.Documents.Document document;
            try
            {
                var importer = new DocxImporter();
                document = importer.Load(docxPath);
            }
            catch (Exception ex)
            {
                result.ImportError = ex.ToString();
                report.ImportFailures++;
                report.Results.Add(result);
                continue;
            }

            var conversionReport = new MarkdownConversionReport();
            var markdown = MarkdownDocumentConverter.ToMarkdown(document, new MarkdownOptions(), conversionReport);
            result.MarkdownLength = markdown.Length;

            report.ProcessedDocuments++;
            report.TotalMarkdownChars += markdown.Length;
            report.Results.Add(result);
            Accumulate(report.ConversionCounts, conversionReport);
        }

        return report;
    }

    private static void Accumulate(Dictionary<string, int> totals, MarkdownConversionReport report)
    {
        foreach (var entry in report.Counts)
        {
            var key = $"{entry.Key.Feature}:{entry.Key.Action}";
            totals.TryGetValue(key, out var existing);
            totals[key] = existing + entry.Value;
        }
    }
}

internal sealed class DocxMarkdownAssetHarnessReport
{
    public int ProcessedDocuments { get; set; }
    public int ImportFailures { get; set; }
    public int TotalMarkdownChars { get; set; }
    public Dictionary<string, int> ConversionCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DocxMarkdownAssetResult> Results { get; } = new();
}

internal sealed class DocxMarkdownAssetResult
{
    public string RelativePath { get; set; } = string.Empty;
    public int MarkdownLength { get; set; }
    public string? ImportError { get; set; }
}

internal static class DocxMarkdownAssetHarnessReporter
{
    public static void WriteReports(DocxMarkdownAssetHarnessReport report, DocxMarkdownAssetHarnessOptions options)
    {
        Directory.CreateDirectory(options.ReportDirectory);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(options.ReportPath, json);
        Console.WriteLine($"Markdown export report written to '{options.ReportPath}'.");
    }
}
