using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using ProEdit.Layout;
using ProEdit.OpenXml;
using ProEdit.Primitives;
using ProEdit.Rendering;
using ProEdit.Rendering.Skia;
using Xunit;
using Xunit.Sdk;

namespace ProEdit.OpenXml.Tests;

[CollectionDefinition("DocxAssets", DisableParallelization = true)]
public sealed class DocxAssetsCollection
{
}

[Collection("DocxAssets")]
public sealed class DocxAssetHarnessTests
{
    [Fact]
    public void AssetHarness_ImportsLayoutsAndRenders()
    {
        var options = DocxAssetHarnessOptions.FromEnvironment();
        if (!options.Enabled)
        {
            Console.WriteLine("Skipping DOCX asset harness. Set DOCX_ASSETS_ENABLE=1 to run.");
            return;
        }

        if (!Directory.Exists(options.AssetsRoot))
        {
            Console.WriteLine($"DOCX assets not found at '{options.AssetsRoot}'. Set DOCX_ASSETS_ROOT or PROEDIT_DOCX_ASSETS_ROOT.");
            return;
        }

        var docxPaths = DocxAssetHarness.EnumerateDocxAssets(options.AssetsRoot, options.MaxDocuments).ToList();
        if (docxPaths.Count == 0)
        {
            Console.WriteLine($"No .docx files found under '{options.AssetsRoot}'.");
            return;
        }

        var report = DocxAssetHarness.Run(docxPaths, options);
        if (options.EmitReports)
        {
            DocxAssetHarnessReporter.WriteReports(report, options);
        }

        if (options.EnforceBaseline)
        {
            DocxAssetHarnessReporter.EnforceBaseline(report.FeatureCoverage, options);
        }

        if (options.FailOnImportError)
        {
            Assert.True(report.ImportFailures == 0, $"Import failures: {report.ImportFailures}");
        }

        if (options.FailOnLayoutError)
        {
            Assert.True(report.LayoutFailures == 0, $"Layout failures: {report.LayoutFailures}");
        }

        if (options.FailOnRenderError)
        {
            Assert.True(report.RenderFailures == 0, $"Render failures: {report.RenderFailures}");
        }

        Assert.True(report.ProcessedDocuments > 0, "No documents processed.");
    }
}

internal sealed class DocxAssetHarnessOptions
{
    private const string DefaultAssetsRoot = "/Users/wieslawsoltes/GitHub/Open-XML-SDK/test/DocumentFormat.OpenXml.Tests.Assets/assets";
    public bool Enabled { get; private set; }
    public string AssetsRoot { get; private set; } = DefaultAssetsRoot;
    public int MaxDocuments { get; private set; }
    public int MaxRenderPixels { get; private set; } = 30_000_000;
    public bool RenderEnabled { get; private set; } = true;
    public bool RenderTableSnapshots { get; private set; } = true;
    public bool RenderFootnoteSnapshots { get; private set; } = true;
    public bool EmitReports { get; private set; } = true;
    public bool FailOnImportError { get; private set; }
    public bool FailOnLayoutError { get; private set; }
    public bool FailOnRenderError { get; private set; }
    public bool EnforceBaseline { get; private set; }
    public bool WriteBaseline { get; private set; }
    public string ReportDirectory { get; private set; } = Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
    public string BaselinePath { get; private set; } = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "docx-assets-feature-baseline.json");

    public static DocxAssetHarnessOptions FromEnvironment()
    {
        var options = new DocxAssetHarnessOptions
        {
            Enabled = GetBoolEnv("DOCX_ASSETS_ENABLE", false) || GetBoolEnv("PROEDIT_DOCX_ASSETS_ENABLE", false),
            AssetsRoot = GetEnv("DOCX_ASSETS_ROOT")
                ?? GetEnv("PROEDIT_DOCX_ASSETS_ROOT")
                ?? DefaultAssetsRoot,
            MaxDocuments = GetIntEnv("DOCX_ASSETS_MAX_DOCS", 0),
            MaxRenderPixels = GetIntEnv("DOCX_ASSETS_MAX_RENDER_PIXELS", 30_000_000),
            RenderEnabled = GetBoolEnv("DOCX_ASSETS_RENDER", true),
            RenderTableSnapshots = GetBoolEnv("DOCX_ASSETS_RENDER_TABLES", true),
            RenderFootnoteSnapshots = GetBoolEnv("DOCX_ASSETS_RENDER_FOOTNOTES", true),
            EmitReports = GetBoolEnv("DOCX_ASSETS_REPORT", true),
            FailOnImportError = GetBoolEnv("DOCX_ASSETS_FAIL_ON_IMPORT", false),
            FailOnLayoutError = GetBoolEnv("DOCX_ASSETS_FAIL_ON_LAYOUT", false),
            FailOnRenderError = GetBoolEnv("DOCX_ASSETS_FAIL_ON_RENDER", false),
            EnforceBaseline = GetBoolEnv("DOCX_ASSETS_ENFORCE_BASELINE", false),
            WriteBaseline = GetBoolEnv("DOCX_ASSETS_WRITE_BASELINE", false)
        };

        var reportDir = GetEnv("DOCX_ASSETS_REPORT_DIR");
        if (!string.IsNullOrWhiteSpace(reportDir))
        {
            options.ReportDirectory = reportDir;
        }

        var baselinePath = GetEnv("DOCX_ASSETS_BASELINE_PATH");
        if (!string.IsNullOrWhiteSpace(baselinePath))
        {
            options.BaselinePath = baselinePath;
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

internal static class DocxAssetHarness
{
    public static IEnumerable<string> EnumerateDocxAssets(string assetsRoot, int maxDocuments)
    {
        var candidates = Directory.EnumerateFiles(assetsRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);

        if (maxDocuments > 0)
        {
            return candidates.Take(maxDocuments);
        }

        return candidates;
    }

    public static DocxAssetHarnessReport Run(IReadOnlyList<string> docxPaths, DocxAssetHarnessOptions options)
    {
        var results = new List<DocxAssetResult>(docxPaths.Count);
        var importFailures = 0;
        var layoutFailures = 0;
        var renderFailures = 0;
        var renderSkipped = 0;

        foreach (var docxPath in docxPaths)
        {
            var result = new DocxAssetResult
            {
                RelativePath = Path.GetRelativePath(options.AssetsRoot, docxPath),
                FileSizeBytes = SafeGetFileSize(docxPath)
            };

            var detection = DocxFeatureDetector.Detect(docxPath);
            result.Features = detection.Features;
            result.UnsupportedFeatures = detection.UnsupportedFeatures;
            result.FeatureDetectionError = detection.Error;

            ProEdit.Documents.Document document;
            try
            {
                var importer = new DocxImporter();
                document = importer.Load(docxPath);
            }
            catch (Exception ex)
            {
                result.ImportError = ex.ToString();
                importFailures++;
                results.Add(result);
                continue;
            }

            DocumentLayout layout;
            using var fontResolver = new SkiaDocumentFontResolver(document.Fonts);
            try
            {
                var textMeasurer = new SkiaTextMeasurer { TypefaceResolver = fontResolver };
                var layouter = new DocumentLayouter();
                layout = layouter.Layout(document, new LayoutSettings(), textMeasurer);
                result.PageCount = layout.Pages.Count;
                result.ContentHeight = layout.ContentHeight;
            }
            catch (Exception ex)
            {
                result.LayoutError = ex.ToString();
                layoutFailures++;
                results.Add(result);
                continue;
            }

            if (options.RenderEnabled)
            {
                TryRender(document, layout, fontResolver, options, result, ref renderFailures, ref renderSkipped);
            }
            else
            {
                result.RenderSkipped = true;
                result.RenderSkipReason = "Rendering disabled via options.";
                renderSkipped++;
            }

            results.Add(result);
        }

        var featureCoverage = DocxAssetHarnessReporter.BuildFeatureCoverage(results, options.AssetsRoot);
        return new DocxAssetHarnessReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            AssetsRoot = options.AssetsRoot,
            TotalDocuments = docxPaths.Count,
            ProcessedDocuments = results.Count,
            ImportFailures = importFailures,
            LayoutFailures = layoutFailures,
            RenderFailures = renderFailures,
            RenderSkipped = renderSkipped,
            FeatureCoverage = featureCoverage,
            FeatureCounts = featureCoverage.FeatureCounts,
            UnsupportedFeatureCounts = featureCoverage.UnsupportedFeatureCounts,
            Documents = results
        };
    }

    private static long SafeGetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static void TryRender(
        ProEdit.Documents.Document document,
        DocumentLayout layout,
        SkiaDocumentFontResolver fontResolver,
        DocxAssetHarnessOptions options,
        DocxAssetResult result,
        ref int renderFailures,
        ref int renderSkipped)
    {
        var renderWidth = GetRenderWidth(layout);
        var renderHeight = GetRenderHeight(layout);
        result.RenderWidth = renderWidth;
        result.RenderHeight = renderHeight;

        if (renderWidth <= 0 || renderHeight <= 0)
        {
            result.RenderSkipped = true;
            result.RenderSkipReason = "Invalid render surface size.";
            renderSkipped++;
            return;
        }

        var pixelCount = (long)renderWidth * renderHeight;
        if (options.MaxRenderPixels > 0 && pixelCount > options.MaxRenderPixels)
        {
            result.RenderSkipped = true;
            result.RenderSkipReason = $"Render surface too large ({pixelCount} pixels).";
            renderSkipped++;
            return;
        }

        try
        {
            var info = new SKImageInfo(renderWidth, renderHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var renderer = new SkiaDocumentRenderer { TypefaceResolver = fontResolver };
            var renderOptions = new RenderOptions
            {
                UsePictureCache = false
            };
            renderer.Render(surface.Canvas, document, layout, renderOptions);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null)
            {
                result.RenderError = "Encoded render output was empty.";
                renderFailures++;
            }
            else
            {
                result.RenderHash = ComputeSha256Hex(data);
                if (options.RenderTableSnapshots)
                {
                    var tableRegions = CollectTableRegions(layout);
                    result.TableRenderHash = ComputeRegionHash(image, tableRegions);
                }

                if (options.RenderFootnoteSnapshots)
                {
                    var footnoteRegions = CollectFootnoteRegions(layout);
                    result.FootnoteRenderHash = ComputeRegionHash(image, footnoteRegions);
                }
            }
        }
        catch (Exception ex)
        {
            result.RenderError = ex.ToString();
            renderFailures++;
        }
    }

    private static int GetRenderWidth(DocumentLayout layout)
    {
        if (layout.Pages.Count == 0)
        {
            return 1;
        }

        var maxRight = 0f;
        foreach (var page in layout.Pages)
        {
            if (page.Bounds.Right > maxRight)
            {
                maxRight = page.Bounds.Right;
            }
        }

        return Math.Max(1, (int)MathF.Ceiling(maxRight));
    }

    private static int GetRenderHeight(DocumentLayout layout)
    {
        if (layout.Pages.Count == 0)
        {
            return 1;
        }

        var maxBottom = 0f;
        foreach (var page in layout.Pages)
        {
            if (page.Bounds.Bottom > maxBottom)
            {
                maxBottom = page.Bounds.Bottom;
            }
        }

        return Math.Max(1, (int)MathF.Ceiling(maxBottom));
    }

    private static IReadOnlyList<DocRect> CollectTableRegions(DocumentLayout layout)
    {
        if (layout.Tables.Count == 0 && layout.HeaderFooters.Count == 0 && layout.Footnotes.Count == 0)
        {
            return Array.Empty<DocRect>();
        }

        var regions = new List<DocRect>();
        AddTables(layout.Tables, regions);
        foreach (var headerFooter in layout.HeaderFooters)
        {
            AddTables(headerFooter.HeaderTables, regions);
            AddTables(headerFooter.FooterTables, regions);
        }

        foreach (var footnote in layout.Footnotes)
        {
            AddTables(footnote.Tables, regions);
        }

        return regions.Count == 0 ? Array.Empty<DocRect>() : regions;
    }

    private static IReadOnlyList<DocRect> CollectFootnoteRegions(DocumentLayout layout)
    {
        if (layout.Footnotes.Count == 0)
        {
            return Array.Empty<DocRect>();
        }

        var regions = new List<DocRect>(layout.Footnotes.Count);
        foreach (var footnote in layout.Footnotes)
        {
            if (TryBuildFootnoteBounds(footnote, out var bounds))
            {
                regions.Add(bounds);
            }
        }

        return regions.Count == 0 ? Array.Empty<DocRect>() : regions;
    }

    private static void AddTables(IReadOnlyList<TableLayout> tables, List<DocRect> regions)
    {
        foreach (var table in tables)
        {
            if (IsValidBounds(table.Bounds))
            {
                regions.Add(table.Bounds);
            }
        }
    }

    private static bool TryBuildFootnoteBounds(FootnoteLayout footnote, out DocRect bounds)
    {
        var left = float.MaxValue;
        var right = float.MinValue;
        var top = float.MaxValue;
        var bottom = float.MinValue;
        var hasBounds = false;

        if (IsValidBounds(footnote.SeparatorBounds))
        {
            var separator = footnote.SeparatorBounds;
            left = MathF.Min(left, separator.X);
            right = MathF.Max(right, separator.Right);
            top = MathF.Min(top, separator.Y);
            bottom = MathF.Max(bottom, separator.Bottom);
            hasBounds = true;
        }

        foreach (var table in footnote.Tables)
        {
            var boundsTable = table.Bounds;
            if (!IsValidBounds(boundsTable))
            {
                continue;
            }

            left = MathF.Min(left, boundsTable.X);
            right = MathF.Max(right, boundsTable.Right);
            top = MathF.Min(top, boundsTable.Y);
            bottom = MathF.Max(bottom, boundsTable.Bottom);
            hasBounds = true;
        }

        foreach (var line in footnote.Lines)
        {
            var lineLeft = line.X - (line.Prefix is null ? 0f : line.PrefixWidth);
            var lineRight = line.X + line.Width;
            left = MathF.Min(left, lineLeft);
            right = MathF.Max(right, lineRight);
            top = MathF.Min(top, line.Y);
            bottom = MathF.Max(bottom, line.Y + line.LineHeight);
            hasBounds = true;
        }

        if (!hasBounds || right <= left || bottom <= top)
        {
            bounds = default;
            return false;
        }

        bounds = new DocRect(left, top, right - left, bottom - top);
        return true;
    }

    private static bool IsValidBounds(DocRect bounds)
    {
        return bounds.Width > 0f
               && bounds.Height > 0f
               && !float.IsNaN(bounds.X)
               && !float.IsNaN(bounds.Y)
               && !float.IsNaN(bounds.Width)
               && !float.IsNaN(bounds.Height);
    }

    private static string? ComputeRegionHash(SKImage image, IReadOnlyList<DocRect> regions)
    {
        if (regions.Count == 0)
        {
            return null;
        }

        var rects = new List<SKRectI>(regions.Count);
        foreach (var region in regions)
        {
            var rect = NormalizeCrop(region, image.Width, image.Height);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            rects.Add(rect);
        }

        if (rects.Count == 0)
        {
            return null;
        }

        rects.Sort(static (left, right) =>
        {
            var compare = left.Top.CompareTo(right.Top);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.Left.CompareTo(right.Left);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.Bottom.CompareTo(right.Bottom);
            if (compare != 0)
            {
                return compare;
            }

            return left.Right.CompareTo(right.Right);
        });

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> rectBytes = stackalloc byte[16];

        foreach (var rect in rects)
        {
            BinaryPrimitives.WriteInt32LittleEndian(rectBytes[..4], rect.Left);
            BinaryPrimitives.WriteInt32LittleEndian(rectBytes[4..8], rect.Top);
            BinaryPrimitives.WriteInt32LittleEndian(rectBytes[8..12], rect.Right);
            BinaryPrimitives.WriteInt32LittleEndian(rectBytes[12..16], rect.Bottom);
            hasher.AppendData(rectBytes);

            var info = new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface is null)
            {
                continue;
            }

            surface.Canvas.Clear(SKColors.Transparent);
            var src = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var dest = new SKRect(0, 0, rect.Width, rect.Height);
            surface.Canvas.DrawImage(image, src, dest);
            using var subset = surface.Snapshot();
            using var data = subset.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null)
            {
                continue;
            }

            hasher.AppendData(data.ToArray());
        }

        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static SKRectI NormalizeCrop(DocRect bounds, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp((int)MathF.Floor(bounds.X), 0, imageWidth);
        var top = Math.Clamp((int)MathF.Floor(bounds.Y), 0, imageHeight);
        var right = Math.Clamp((int)MathF.Ceiling(bounds.Right), left, imageWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling(bounds.Bottom), top, imageHeight);
        return new SKRectI(left, top, right, bottom);
    }

    private static string ComputeSha256Hex(SKData data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data.ToArray());
        return Convert.ToHexString(hash);
    }
}

internal sealed class DocxAssetResult
{
    public string RelativePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public IReadOnlyList<string> Features { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedFeatures { get; set; } = Array.Empty<string>();
    public string? FeatureDetectionError { get; set; }
    public string? ImportError { get; set; }
    public string? LayoutError { get; set; }
    public string? RenderError { get; set; }
    public bool RenderSkipped { get; set; }
    public string? RenderSkipReason { get; set; }
    public int PageCount { get; set; }
    public float ContentHeight { get; set; }
    public int RenderWidth { get; set; }
    public int RenderHeight { get; set; }
    public string? RenderHash { get; set; }
    public string? TableRenderHash { get; set; }
    public string? FootnoteRenderHash { get; set; }
}

internal sealed class DocxAssetHarnessReport
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string AssetsRoot { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public int ProcessedDocuments { get; set; }
    public int ImportFailures { get; set; }
    public int LayoutFailures { get; set; }
    public int RenderFailures { get; set; }
    public int RenderSkipped { get; set; }
    public IReadOnlyDictionary<string, int> FeatureCounts { get; set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> UnsupportedFeatureCounts { get; set; } = new Dictionary<string, int>();
    public DocxAssetFeatureCoverageReport FeatureCoverage { get; set; } = new DocxAssetFeatureCoverageReport();
    public IReadOnlyList<DocxAssetResult> Documents { get; set; } = Array.Empty<DocxAssetResult>();
}

internal sealed class DocxAssetFeatureCoverageReport
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string AssetsRoot { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public IReadOnlyDictionary<string, int> FeatureCounts { get; set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> UnsupportedFeatureCounts { get; set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, string> FeatureSupport { get; set; } = new Dictionary<string, string>();
}

internal static class DocxAssetHarnessReporter
{
    public static DocxAssetFeatureCoverageReport BuildFeatureCoverage(IReadOnlyList<DocxAssetResult> results, string assetsRoot)
    {
        var featureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var unsupportedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var supportMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var feature in DocxFeatureCatalog.Features)
        {
            featureCounts[feature.Id] = 0;
            unsupportedCounts[feature.Id] = 0;
            supportMap[feature.Id] = feature.Support.ToString();
        }

        foreach (var result in results)
        {
            foreach (var featureId in result.Features)
            {
                if (!featureCounts.TryGetValue(featureId, out var current))
                {
                    featureCounts[featureId] = 1;
                }
                else
                {
                    featureCounts[featureId] = current + 1;
                }
            }

            foreach (var featureId in result.UnsupportedFeatures)
            {
                if (!unsupportedCounts.TryGetValue(featureId, out var current))
                {
                    unsupportedCounts[featureId] = 1;
                }
                else
                {
                    unsupportedCounts[featureId] = current + 1;
                }
            }
        }

        return new DocxAssetFeatureCoverageReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            AssetsRoot = assetsRoot,
            TotalDocuments = results.Count,
            FeatureCounts = new SortedDictionary<string, int>(featureCounts, StringComparer.Ordinal),
            UnsupportedFeatureCounts = new SortedDictionary<string, int>(unsupportedCounts, StringComparer.Ordinal),
            FeatureSupport = new SortedDictionary<string, string>(supportMap, StringComparer.Ordinal)
        };
    }

    public static void WriteReports(DocxAssetHarnessReport report, DocxAssetHarnessOptions options)
    {
        Directory.CreateDirectory(options.ReportDirectory);
        var reportPath = Path.Combine(options.ReportDirectory, "docx-asset-harness-report.json");
        var featurePath = Path.Combine(options.ReportDirectory, "docx-asset-feature-coverage.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, jsonOptions));
        File.WriteAllText(featurePath, JsonSerializer.Serialize(report.FeatureCoverage, jsonOptions));

        if (options.WriteBaseline)
        {
            File.WriteAllText(options.BaselinePath, JsonSerializer.Serialize(report.FeatureCoverage, jsonOptions));
        }
    }

    public static void EnforceBaseline(DocxAssetFeatureCoverageReport current, DocxAssetHarnessOptions options)
    {
        if (!File.Exists(options.BaselinePath))
        {
            throw new XunitException($"Baseline not found at '{options.BaselinePath}'. Set DOCX_ASSETS_WRITE_BASELINE=1 to create one.");
        }

        var baselineJson = File.ReadAllText(options.BaselinePath);
        var baseline = JsonSerializer.Deserialize<DocxAssetFeatureCoverageReport>(baselineJson);
        if (baseline is null)
        {
            throw new XunitException($"Baseline at '{options.BaselinePath}' could not be read.");
        }

        var diffs = DocxAssetBaselineComparer.Compare(baseline, current);
        if (diffs.Count > 0)
        {
            var message = string.Join(Environment.NewLine, diffs);
            throw new XunitException($"DOCX asset feature baseline mismatch:{Environment.NewLine}{message}");
        }
    }
}

internal static class DocxAssetBaselineComparer
{
    public static IReadOnlyList<string> Compare(DocxAssetFeatureCoverageReport baseline, DocxAssetFeatureCoverageReport current)
    {
        var diffs = new List<string>();
        if (baseline.TotalDocuments != current.TotalDocuments)
        {
            diffs.Add($"TotalDocuments baseline={baseline.TotalDocuments} current={current.TotalDocuments}");
        }

        CompareDictionary("FeatureCounts", baseline.FeatureCounts, current.FeatureCounts, diffs);
        CompareDictionary("UnsupportedFeatureCounts", baseline.UnsupportedFeatureCounts, current.UnsupportedFeatureCounts, diffs);
        return diffs;
    }

    private static void CompareDictionary(
        string label,
        IReadOnlyDictionary<string, int> baseline,
        IReadOnlyDictionary<string, int> current,
        List<string> diffs)
    {
        foreach (var item in baseline)
        {
            if (!current.TryGetValue(item.Key, out var value))
            {
                diffs.Add($"{label} missing key '{item.Key}' in current.");
                continue;
            }

            if (value != item.Value)
            {
                diffs.Add($"{label} '{item.Key}' baseline={item.Value} current={value}");
            }
        }

        foreach (var item in current)
        {
            if (!baseline.ContainsKey(item.Key))
            {
                diffs.Add($"{label} new key '{item.Key}' in current (value={item.Value}).");
            }
        }
    }
}

internal sealed class DocxFeatureDetectionResult
{
    public IReadOnlyList<string> Features { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedFeatures { get; set; } = Array.Empty<string>();
    public string? Error { get; set; }
}

internal static class DocxFeatureDetector
{
    public static DocxFeatureDetectionResult Detect(string docxPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(docxPath);
            var parts = new List<DocxXmlPart>();
            var partNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries)
            {
                partNames.Add(entry.FullName);
                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var content = ReadEntry(entry);
                if (content.Length == 0)
                {
                    continue;
                }

                parts.Add(new DocxXmlPart(entry.FullName, content));
            }

            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            var unsupported = new HashSet<string>(StringComparer.Ordinal);

            foreach (var feature in DocxFeatureCatalog.Features)
            {
                if (feature.Matches(parts, partNames))
                {
                    featureIds.Add(feature.Id);
                    if (feature.Support != DocxFeatureSupport.Supported)
                    {
                        unsupported.Add(feature.Id);
                    }
                }
            }

            return new DocxFeatureDetectionResult
            {
                Features = featureIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
                UnsupportedFeatures = unsupported.OrderBy(static id => id, StringComparer.Ordinal).ToArray()
            };
        }
        catch (Exception ex)
        {
            return new DocxFeatureDetectionResult
            {
                Error = ex.ToString()
            };
        }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

internal sealed class DocxXmlPart
{
    public DocxXmlPart(string name, string content)
    {
        Name = name;
        Content = content;
    }

    public string Name { get; }
    public string Content { get; }

    public bool ContainsAny(string[] markers)
    {
        if (markers.Length == 0 || Content.Length == 0)
        {
            return false;
        }

        var span = Content.AsSpan();
        foreach (var marker in markers)
        {
            if (span.IndexOf(marker.AsSpan(), StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}

internal enum DocxFeatureSupport
{
    Supported,
    Partial,
    NotSupported
}

internal sealed class DocxFeatureDefinition
{
    public DocxFeatureDefinition(
        string id,
        string description,
        DocxFeatureSupport support,
        string[] xmlMarkers,
        Func<string, bool>? partNamePredicate = null)
    {
        Id = id;
        Description = description;
        Support = support;
        XmlMarkers = xmlMarkers;
        PartNamePredicate = partNamePredicate;
    }

    public string Id { get; }
    public string Description { get; }
    public DocxFeatureSupport Support { get; }
    public string[] XmlMarkers { get; }
    public Func<string, bool>? PartNamePredicate { get; }

    public bool Matches(IReadOnlyList<DocxXmlPart> parts, IReadOnlySet<string> partNames)
    {
        if (PartNamePredicate is not null && HasMatchingPart(partNames, PartNamePredicate))
        {
            return true;
        }

        if (XmlMarkers.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.ContainsAny(XmlMarkers))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMatchingPart(IReadOnlySet<string> partNames, Func<string, bool> predicate)
    {
        foreach (var partName in partNames)
        {
            if (predicate(partName))
            {
                return true;
            }
        }

        return false;
    }
}

internal static class DocxFeatureCatalog
{
    public static readonly IReadOnlyList<DocxFeatureDefinition> Features = new[]
    {
        new DocxFeatureDefinition(
            "track_changes",
            "Track changes",
            DocxFeatureSupport.Partial,
            new[] { "<w:ins", "<w:del", "<w:moveFrom", "<w:moveTo", "<w:moveFromRangeStart", "<w:moveToRangeStart" }),
        new DocxFeatureDefinition(
            "content_controls",
            "Content controls (SDT)",
            DocxFeatureSupport.Partial,
            new[] { "<w:sdt", "<w:sdtPr", "<w:sdtContent" }),
        new DocxFeatureDefinition(
            "data_bound_content_controls",
            "Data-bound content controls",
            DocxFeatureSupport.Partial,
            new[] { "<w:dataBinding" }),
        new DocxFeatureDefinition(
            "fields",
            "Fields (fldSimple/fldChar/instrText)",
            DocxFeatureSupport.Partial,
            new[] { "<w:fldSimple", "<w:fldChar", "<w:instrText" }),
        new DocxFeatureDefinition(
            "mail_merge_fields",
            "Mail merge fields",
            DocxFeatureSupport.Partial,
            new[] { "<w:mailMerge", "MERGEFIELD" }),
        new DocxFeatureDefinition(
            "custom_xml_markup",
            "Custom XML markup",
            DocxFeatureSupport.Partial,
            new[] { "<w:customXml", "<w:customXmlPr" }),
        new DocxFeatureDefinition(
            "smart_tags",
            "Smart tags",
            DocxFeatureSupport.Partial,
            new[] { "<w:smartTag", "<w:smartTagPr" }),
        new DocxFeatureDefinition(
            "comment_ex",
            "CommentEx (Word 2013+)",
            DocxFeatureSupport.Partial,
            new[] { "<w15:commentEx" },
            static part => part.EndsWith("word/commentsExtended.xml", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "smartart_diagrams",
            "SmartArt / diagrams",
            DocxFeatureSupport.Partial,
            new[] { "<dgm:" },
            static part => part.StartsWith("word/diagrams/", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "svg_images",
            "SVG images",
            DocxFeatureSupport.Partial,
            new[] { "<a:svgBlip" },
            static part => part.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)
                           && part.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "ole_objects",
            "OLE / embedded objects",
            DocxFeatureSupport.Partial,
            new[] { "<o:OLEObject", "<w:object" },
            static part => part.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "vml_shapes",
            "VML shapes",
            DocxFeatureSupport.Partial,
            new[] { "<v:shape", "<v:group", "<v:rect" },
            static part => part.StartsWith("word/vmlDrawing", StringComparison.OrdinalIgnoreCase)
                           || part.EndsWith(".vml", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "text_boxes",
            "Text boxes",
            DocxFeatureSupport.Partial,
            new[] { "<w:txbxContent", "<v:textbox", "<wps:txbx" }),
        new DocxFeatureDefinition(
            "floating_drawings",
            "Floating drawings (anchors/wrap)",
            DocxFeatureSupport.Partial,
            new[] { "<wp:anchor", "<wp:wrap" }),
        new DocxFeatureDefinition(
            "page_borders",
            "Page borders",
            DocxFeatureSupport.Partial,
            new[] { "<w:pgBorders" }),
        new DocxFeatureDefinition(
            "page_background_watermarks",
            "Page background / watermarks",
            DocxFeatureSupport.Partial,
            new[] { "<w:background", "<v:background", "watermark" }),
        new DocxFeatureDefinition(
            "line_numbering",
            "Line numbering",
            DocxFeatureSupport.Partial,
            new[] { "<w:lnNumType" }),
        new DocxFeatureDefinition(
            "text_direction_vertical",
            "Text direction / vertical text",
            DocxFeatureSupport.Partial,
            new[] { "<w:textDirection", "<w:vert", "<w:rtl", "<w:bidi" }),
        new DocxFeatureDefinition(
            "east_asian_layout",
            "East Asian layout / ruby / phonetics",
            DocxFeatureSupport.Partial,
            new[] { "<w:ruby", "<w:phonetic", "<w:rt" }),
        new DocxFeatureDefinition(
            "document_grid_frame_dropcap",
            "Document grid / frame / drop cap",
            DocxFeatureSupport.Partial,
            new[] { "<w:docGrid", "<w:framePr", "<w:dropCap" }),
        new DocxFeatureDefinition(
            "document_protection_forms",
            "Document protection / forms",
            DocxFeatureSupport.Partial,
            new[] { "<w:documentProtection", "<w:forms" }),
        new DocxFeatureDefinition(
            "text_effects",
            "Text effects (shadow/outline/emboss)",
            DocxFeatureSupport.Partial,
            new[] { "<w:shadow", "<w:outline", "<w:emboss", "<w:imprint" }),
        new DocxFeatureDefinition(
            "compat_settings",
            "Compatibility settings",
            DocxFeatureSupport.Partial,
            new[] { "<w:compat" }),
        new DocxFeatureDefinition(
            "sections_columns_layout",
            "Sections / columns / page layout variants",
            DocxFeatureSupport.Partial,
            new[] { "<w:sectPr", "<w:cols" }),
        new DocxFeatureDefinition(
            "tables_advanced",
            "Tables (advanced borders/merges/layout)",
            DocxFeatureSupport.Partial,
            new[] { "<w:tbl", "<w:vMerge", "<w:gridSpan", "<w:tblGrid", "<w:tblPrEx" }),
        new DocxFeatureDefinition(
            "numbering_formats",
            "Numbering formats / bullets",
            DocxFeatureSupport.Partial,
            new[] { "<w:numPr", "<w:numId", "<w:abstractNum" }),
        new DocxFeatureDefinition(
            "equations",
            "Equations (OMML)",
            DocxFeatureSupport.Partial,
            new[] { "<m:oMath", "<m:oMathPara" }),
        new DocxFeatureDefinition(
            "charts",
            "Charts (advanced types)",
            DocxFeatureSupport.Partial,
            new[] { "<c:chart" },
            static part => part.StartsWith("word/charts/", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "shapes_drawingml",
            "Shapes (DrawingML effects)",
            DocxFeatureSupport.Partial,
            new[] { "<a:prstGeom", "<a:effectLst", "<a:glow", "<a:outerShdw", "<a:innerShdw" }),
        new DocxFeatureDefinition(
            "headers_footers",
            "Headers / footers",
            DocxFeatureSupport.Partial,
            Array.Empty<string>(),
            static part => part.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
                           || part.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "footnotes_endnotes",
            "Footnotes / endnotes",
            DocxFeatureSupport.Partial,
            Array.Empty<string>(),
            static part => part.Equals("word/footnotes.xml", StringComparison.OrdinalIgnoreCase)
                           || part.Equals("word/endnotes.xml", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "comments_classic",
            "Comments (classic)",
            DocxFeatureSupport.Partial,
            Array.Empty<string>(),
            static part => part.Equals("word/comments.xml", StringComparison.OrdinalIgnoreCase)),
        new DocxFeatureDefinition(
            "hyperlinks_bookmarks",
            "Hyperlinks / bookmarks",
            DocxFeatureSupport.Partial,
            new[] { "<w:hyperlink", "<w:bookmarkStart", "<w:bookmarkEnd" }),
        new DocxFeatureDefinition(
            "page_column_breaks",
            "Page/column breaks",
            DocxFeatureSupport.Partial,
            new[] { "<w:lastRenderedPageBreak", "<w:columnBreak", "w:type=\"page\"", "w:type='page'", "w:type=\"column\"", "w:type='column'" })
    };
}
