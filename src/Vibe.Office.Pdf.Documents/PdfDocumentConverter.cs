using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vibe.Office.Documents;
using Vibe.Office.Pdf;
using Vibe.Office.Primitives;

namespace Vibe.Office.Pdf.Documents;

public static class PdfDocumentConverter
{
    public static Document FromPdf(PdfDocumentAst pdf, PdfImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var effectiveOptions = options ?? new PdfImportOptions();
        if (effectiveOptions.ShouldPreserveSource && pdf.OriginalBytes is null)
        {
            effectiveOptions.ParserOptions.PreserveSourceBytes = true;
        }

        var document = new Document();
        document.Blocks.Clear();
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);

        if (pdf.Pages.Count == 0)
        {
            return document;
        }

        ApplyPageSetup(document.SectionProperties, pdf.Pages[0]);

        switch (effectiveOptions.Mode)
        {
            case PdfImportMode.FixedLayout:
                BuildFixedLayout(document, pdf);
                break;
            default:
                BuildReflow(document, pdf);
                break;
        }

        PdfImportMetadataStore.Store(document, new PdfImportMetadata(
            effectiveOptions.Mode,
            pdf.ParserProviderId,
            pdf.Pages.Count));

        var diagnostics = BuildDiagnostics(pdf, effectiveOptions);
        if (diagnostics.Issues.Count > 0)
        {
            PdfImportDiagnosticsStore.Store(document, diagnostics);
        }
        else
        {
            PdfImportDiagnosticsStore.Clear(document);
        }

        if (effectiveOptions.PreservationMode != PdfPreservationMode.None && pdf.OriginalBytes is not null)
        {
            var manifest = BuildPreservationManifest(pdf, effectiveOptions, document);
            PdfPreservationStore.StoreOriginal(document, pdf.OriginalBytes, manifest);
        }

        return document;
    }

    private static PdfPreservationManifest BuildPreservationManifest(PdfDocumentAst pdf, PdfImportOptions options, Document document)
    {
        var manifest = new PdfPreservationManifest
        {
            ParserProviderId = pdf.ParserProviderId,
            WriterProviderId = PdfProviderIds.PdfSharp,
            ImportMode = options.Mode,
            PreservationMode = options.PreservationMode,
            ContentHash = PdfDocumentHash.Compute(document),
            PageCount = pdf.Pages.Count
        };

        BuildObjectMap(pdf, manifest.ObjectMap);
        return manifest;
    }

    private static PdfImportDiagnostics BuildDiagnostics(PdfDocumentAst pdf, PdfImportOptions options)
    {
        var issues = new List<string>();

        if (pdf.Pages.Count == 0)
        {
            issues.Add("No pages were detected in the PDF document.");
        }

        if (options.Mode == PdfImportMode.FixedLayout
            && pdf.Pages.Any(page => ((page.Rotation % 360) + 360) % 360 != 0))
        {
            issues.Add("Page rotations are not fully applied in fixed-layout mode.");
        }

        var usedFonts = new List<string>();
        var usedFontSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pdf.Pages)
        {
            foreach (var run in page.TextRuns)
            {
                var name = run.Font?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (usedFontSet.Add(name))
                {
                    usedFonts.Add(name);
                }
            }

            foreach (var glyph in page.Glyphs)
            {
                var name = glyph.Font?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (usedFontSet.Add(name))
                {
                    usedFonts.Add(name);
                }
            }
        }

        var embeddedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (usedFonts.Count > 0)
        {
            foreach (var embedded in pdf.EmbeddedFonts)
            {
                AddEmbeddedKey(embedded.FamilyName);
                AddEmbeddedKey(embedded.PostScriptName);
            }

            var missingFonts = usedFonts
                .Where(font => !embeddedFamilies.Contains(NormalizeFontKey(font)))
                .ToList();

            if (missingFonts.Count > 0)
            {
                var fontList = string.Join(", ", missingFonts.Take(5));
                var suffix = missingFonts.Count > 5 ? "…" : string.Empty;
                issues.Add($"Some fonts are not embedded and will use fallbacks: {fontList}{suffix}");
            }
        }

        var missingImageMime = pdf.Pages
            .SelectMany(page => page.Images)
            .Any(image => string.IsNullOrWhiteSpace(image.MimeType));
        if (missingImageMime)
        {
            issues.Add("Some images have unknown formats and may not render correctly.");
        }

        return new PdfImportDiagnostics(issues);

        void AddEmbeddedKey(string? name)
        {
            var key = NormalizeFontKey(name);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            embeddedFamilies.Add(key);
        }
    }

    private static void BuildObjectMap(PdfDocumentAst pdf, PdfObjectMap map)
    {
        for (var pageIndex = 0; pageIndex < pdf.Pages.Count; pageIndex++)
        {
            var page = pdf.Pages[pageIndex];
            var pageMap = new PdfPageObjectMap { PageIndex = pageIndex };
            var objectIndex = 0;

            foreach (var run in page.TextRuns)
            {
                pageMap.Objects.Add(new PdfMappedObject
                {
                    ObjectId = $"p{pageIndex}-t{objectIndex++}",
                    Kind = PdfMappedObjectKind.Text,
                    Bounds = run.Bounds
                });
            }

            objectIndex = 0;
            foreach (var image in page.Images)
            {
                pageMap.Objects.Add(new PdfMappedObject
                {
                    ObjectId = $"p{pageIndex}-i{objectIndex++}",
                    Kind = PdfMappedObjectKind.Image,
                    Bounds = image.Bounds
                });
            }

            if (pageMap.Objects.Count > 0)
            {
                map.Pages.Add(pageMap);
            }
        }
    }

    private static void ApplyPageSetup(SectionProperties properties, PdfPageAst page)
    {
        properties.PageWidth = PdfUnits.PointsToDip(page.Width);
        properties.PageHeight = PdfUnits.PointsToDip(page.Height);
        properties.MarginLeft ??= 0f;
        properties.MarginRight ??= 0f;
        properties.MarginTop ??= 0f;
        properties.MarginBottom ??= 0f;
    }

    private static void BuildReflow(Document document, PdfDocumentAst pdf)
    {
        var headerFooter = DetectHeaderFooter(pdf);
        ApplyHeaderFooterBlocks(document, headerFooter);
        var footnoteState = new PdfFootnoteState(document);

        for (var pageIndex = 0; pageIndex < pdf.Pages.Count; pageIndex++)
        {
            var page = pdf.Pages[pageIndex];
            if (page.Glyphs.Count > 0)
            {
                RegisterFonts(document, page.Glyphs, pdf.EmbeddedFonts);
                var blocks = BuildReflowFromGlyphs(document, page, headerFooter, footnoteState);
                if (blocks.Count > 0)
                {
                    document.Blocks.AddRange(blocks);
                }
                else if (page.TextRuns.Count > 0)
                {
                    RegisterFonts(document, page.TextRuns, pdf.EmbeddedFonts);
                    BuildReflowFromRuns(document, page);
                }
            }
            else if (page.TextRuns.Count > 0)
            {
                RegisterFonts(document, page.TextRuns, pdf.EmbeddedFonts);
                BuildReflowFromRuns(document, page);
            }
            else
            {
                var paragraphs = BuildParagraphs(page.ExtractedText ?? string.Empty);
                document.Blocks.AddRange(paragraphs);
            }

            if (pageIndex < pdf.Pages.Count - 1)
            {
                document.Blocks.Add(new PageBreakBlock());
            }
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }
    }

    private static void BuildReflowFromRuns(Document document, PdfPageAst page)
    {
        var lineGroups = GroupRunsIntoLines(page.TextRuns);
        if (lineGroups.Count == 0)
        {
            var paragraphs = BuildParagraphs(page.ExtractedText ?? string.Empty);
            document.Blocks.AddRange(paragraphs);
            return;
        }

        var orderedLines = OrderLinesForReflow(lineGroups);
        var paragraphGroups = GroupLinesIntoReflowParagraphs(orderedLines);
        var baselineFontSize = ResolveBaselineFontSize(page.TextRuns);
        var listState = new ListInferenceState();

        foreach (var paragraphGroup in paragraphGroups)
        {
            if (paragraphGroup.Lines.Count == 0)
            {
                continue;
            }

            var paragraph = new ParagraphBlock();
            for (var index = 0; index < paragraphGroup.Lines.Count; index++)
            {
                if (index > 0)
                {
                    paragraph.Inlines.Add(new RunInline(" "));
                }

                AppendLineInlines(paragraph, paragraphGroup.Lines[index], addLineBreak: false);
            }

            paragraph.Text = string.Empty;
            ApplyHeadingHeuristics(paragraph, paragraphGroup, baselineFontSize);
            ApplyListHeuristics(document, paragraph, paragraphGroup, listState, baselineFontSize);
            if (paragraph.ListInfo is not null)
            {
                paragraph.StyleId = null;
            }
            document.Blocks.Add(paragraph);
        }
    }

    private static List<Block> BuildReflowFromGlyphs(
        Document document,
        PdfPageAst page,
        PdfHeaderFooterInfo headerFooter,
        PdfFootnoteState footnoteState)
    {
        var blocks = new List<Block>();
        if (page.Glyphs.Count == 0)
        {
            return blocks;
        }

        if (page.Glyphs.Any(glyph => !IsGlyphOrientationSupported(glyph.Orientation)))
        {
            return blocks;
        }

        var lines = GroupGlyphsIntoLines(page.Glyphs);
        if (lines.Count == 0)
        {
            return blocks;
        }

        if (IsGlyphLineQualityPoor(lines) && page.TextRuns.Count > 0)
        {
            return blocks;
        }

        AssignGlyphLineAlignment(lines);
        var filtered = FilterHeaderFooterLines(lines, page, headerFooter);
        var footnoteInfo = DetectFootnotesForPage(document, footnoteState, page, filtered);
        var bodyLines = RemoveFootnoteLines(filtered, footnoteInfo);
        var tables = DetectRuledTables(page, bodyLines);
        var tableLines = CollectTableLines(tables);
        var remainingLines = bodyLines.Where(line => !tableLines.Contains(line)).ToList();

        var orderedLines = OrderGlyphLinesForReflow(remainingLines);
        var paragraphGroups = GroupGlyphLinesIntoParagraphs(orderedLines);
        var baselineFontSize = ResolveBaselineFontSize(page.Glyphs);
        var listState = new ListInferenceState();

        var flowBlocks = new List<FlowBlock>();
        foreach (var paragraphGroup in paragraphGroups)
        {
            if (paragraphGroup.Lines.Count == 0)
            {
                continue;
            }

            var paragraph = new ParagraphBlock();
            AppendParagraphLines(paragraph, paragraphGroup, footnoteInfo);
            paragraph.Text = string.Empty;
            ApplyHeadingHeuristics(paragraph, paragraphGroup, baselineFontSize);
            ApplyListHeuristics(document, paragraph, paragraphGroup, listState, baselineFontSize);
            if (paragraph.ListInfo is not null)
            {
                paragraph.StyleId = null;
            }

            if (paragraphGroup.Alignment.HasValue)
            {
                paragraph.Properties.Alignment = paragraphGroup.Alignment;
            }

            flowBlocks.Add(new FlowBlock(paragraph, paragraphGroup.Bounds));
        }

        if (tables.Count > 0)
        {
            foreach (var table in tables)
            {
                var tableBlock = BuildTableBlock(table);
                flowBlocks.Add(new FlowBlock(tableBlock, table.Bounds));
            }
        }

        var orderedBlocks = flowBlocks;
        if (flowBlocks.Count > 1)
        {
            var averageHeight = ResolveAverageLineHeight(remainingLines);
            orderedBlocks = OrderFlowBlocksForReflow(flowBlocks, averageHeight);
        }

        foreach (var block in orderedBlocks)
        {
            blocks.Add(block.Block);
        }

        return blocks;
    }

    private static void BuildFixedLayout(Document document, PdfDocumentAst pdf)
    {
        for (var pageIndex = 0; pageIndex < pdf.Pages.Count; pageIndex++)
        {
            var page = pdf.Pages[pageIndex];
            var hostParagraph = new ParagraphBlock();

            if (page.TextRuns.Count == 0 && page.Images.Count == 0 && page.Paths.Count == 0 && page.Glyphs.Count == 0)
            {
                AddFullPageTextBox(hostParagraph, page);
            }
            else
            {
                if (page.TextRuns.Count > 0)
                {
                    RegisterFonts(document, page.TextRuns, pdf.EmbeddedFonts);
                }
                AddFixedLayoutObjects(hostParagraph, page);
            }

            document.Blocks.Add(hostParagraph);

            if (pageIndex < pdf.Pages.Count - 1)
            {
                document.Blocks.Add(new PageBreakBlock());
            }
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }
    }

    private static void AddFullPageTextBox(ParagraphBlock hostParagraph, PdfPageAst page)
    {
        var pageWidth = PdfUnits.PointsToDip(page.Width);
        var pageHeight = PdfUnits.PointsToDip(page.Height);
        var textBox = new ShapeTextBox();
        var blocks = BuildParagraphs(page.ExtractedText ?? string.Empty);
        textBox.Blocks.AddRange(blocks);

        var shape = new ShapeInline(pageWidth, pageHeight)
        {
            TextBox = textBox
        };

        var floating = CreatePageAnchoredObject(shape, page, new PdfRect(0, 0, page.Width, page.Height), behindText: true);
        hostParagraph.FloatingObjects.Add(floating);
    }

    private static void AddFixedLayoutObjects(ParagraphBlock hostParagraph, PdfPageAst page)
    {
        var textLayout = BuildTextRunBoxes(page);
        var pathObjects = BuildPathObjects(page);
        var imageObjects = BuildImageObjects(page);

        if (page.ContentOrder.Count == 0)
        {
            uint zOrder = 0;
            AddObjects(hostParagraph, textLayout.Objects, ref zOrder);
            AddObjects(hostParagraph, pathObjects, ref zOrder);
            AddObjects(hostParagraph, imageObjects, ref zOrder);
            return;
        }

        var added = new HashSet<FloatingObject>();
        uint order = 0;
        foreach (var item in page.ContentOrder)
        {
            switch (item.Kind)
            {
                case PdfContentItemKind.TextRun:
                {
                    if (textLayout.RunMap.TryGetValue(item.Index, out var textObject)
                        && added.Add(textObject))
                    {
                        AddObject(hostParagraph, textObject, ref order);
                    }
                    break;
                }
                case PdfContentItemKind.Path:
                {
                    if (item.Index >= 0 && item.Index < pathObjects.Count)
                    {
                        var pathObject = pathObjects[item.Index];
                        if (added.Add(pathObject))
                        {
                            AddObject(hostParagraph, pathObject, ref order);
                        }
                    }
                    break;
                }
                case PdfContentItemKind.Image:
                {
                    if (item.Index >= 0 && item.Index < imageObjects.Count)
                    {
                        var imageObject = imageObjects[item.Index];
                        if (added.Add(imageObject))
                        {
                            AddObject(hostParagraph, imageObject, ref order);
                        }
                    }
                    break;
                }
            }
        }

        AddRemaining(hostParagraph, textLayout.Objects, added, ref order);
        AddRemaining(hostParagraph, pathObjects, added, ref order);
        AddRemaining(hostParagraph, imageObjects, added, ref order);
    }

    private static TextRunLayout BuildTextRunBoxes(PdfPageAst page)
    {
        if (page.Glyphs.Count > 0)
        {
            var glyphLayout = BuildGlyphLineBoxes(page);
            if (glyphLayout.Objects.Count > 0)
            {
                return glyphLayout;
            }
        }

        var layout = new TextRunLayout();
        if (page.TextRuns.Count == 0)
        {
            return layout;
        }

        var orderedRuns = page.TextRuns
            .Where(run => !string.IsNullOrWhiteSpace(run.Text))
            .OrderByDescending(run => ResolveRunBaseline(run))
            .ThenBy(run => run.Bounds.X)
            .ToList();

        foreach (var run in orderedRuns)
        {
            var bounds = ResolveFixedRunBounds(run);
            var width = PdfUnits.PointsToDip(bounds.Width);
            var height = PdfUnits.PointsToDip(bounds.Height);
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            var paragraph = new ParagraphBlock();
            var text = NormalizeFixedRunText(run.Text);
            if (!string.IsNullOrEmpty(text))
            {
                paragraph.Inlines.Add(new RunInline(text, BuildRunStyle(run)));
            }

            paragraph.Text = string.Empty;
            ApplyFixedLineSpacing(paragraph, height);

            var textBox = new ShapeTextBox();
            textBox.Properties.AutoFit = ShapeTextAutoFit.TextToFitShape;
            textBox.Blocks.Add(paragraph);

            var shape = new ShapeInline(width, height)
            {
                TextBox = textBox
            };

            var floating = CreatePageAnchoredObject(shape, page, bounds, behindText: false);
            layout.Objects.Add(floating);

            if (run.Index >= 0)
            {
                layout.RunMap[run.Index] = floating;
            }
        }

        return layout;
    }

    private static TextRunLayout BuildGlyphLineBoxes(PdfPageAst page)
    {
        var layout = new TextRunLayout();
        var glyphs = page.Glyphs
            .Where(glyph => !string.IsNullOrWhiteSpace(glyph.Text) && IsGlyphOrientationSupported(glyph.Orientation))
            .ToList();
        if (glyphs.Count == 0)
        {
            return layout;
        }

        var lines = GroupGlyphsIntoLines(glyphs);
        if (lines.Count == 0)
        {
            return layout;
        }

        var orderedLines = lines
            .OrderByDescending(line => line.BaselineY)
            .ThenBy(line => line.Bounds.X)
            .ToList();

        var lineLayouts = new List<GlyphLineLayout>(orderedLines.Count);
        foreach (var line in orderedLines)
        {
            if (line.Glyphs.Count == 0)
            {
                continue;
            }

            var bounds = ResolveFixedGlyphLineBounds(line);
            var width = PdfUnits.PointsToDip(bounds.Width);
            var height = PdfUnits.PointsToDip(bounds.Height);
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            var paragraph = new ParagraphBlock();
            AppendLineSpans(paragraph, line.Spans, useNonBreakingSpaces: true);
            paragraph.Text = string.Empty;
            ApplyFixedLineSpacing(paragraph, height);

            var textBox = new ShapeTextBox();
            textBox.Properties.AutoFit = ShapeTextAutoFit.TextToFitShape;
            textBox.Blocks.Add(paragraph);

            var shape = new ShapeInline(width, height)
            {
                TextBox = textBox
            };

            var floating = CreatePageAnchoredObject(shape, page, bounds, behindText: false);
            layout.Objects.Add(floating);
            lineLayouts.Add(new GlyphLineLayout(line, bounds, floating));
        }

        MapRunsToGlyphLines(layout, page.TextRuns, lineLayouts);
        return layout;
    }

    private static string NormalizeFixedRunText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.IndexOf(' ') >= 0
            ? text.Replace(' ', '\u00A0')
            : text;
    }

    private static string NormalizeFontKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static void MapRunsToGlyphLines(
        TextRunLayout layout,
        IReadOnlyList<PdfTextRun> runs,
        IReadOnlyList<GlyphLineLayout> lineLayouts)
    {
        if (runs.Count == 0 || lineLayouts.Count == 0)
        {
            return;
        }

        var tolerance = ResolveLineTolerance(runs);
        var pad = Math.Max(tolerance * 0.6, 2.0);

        foreach (var run in runs)
        {
            if (run.Index < 0)
            {
                continue;
            }

            var baseline = ResolveRunBaseline(run);
            var best = -1;
            var bestDelta = double.MaxValue;
            for (var i = 0; i < lineLayouts.Count; i++)
            {
                var line = lineLayouts[i].Line;
                var delta = Math.Abs(line.BaselineY - baseline);
                if (delta > tolerance)
                {
                    continue;
                }

                var bounds = lineLayouts[i].Bounds;
                if (run.Bounds.Right < bounds.X - pad || run.Bounds.X > bounds.Right + pad)
                {
                    continue;
                }

                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
            }

            if (best >= 0)
            {
                layout.RunMap[run.Index] = lineLayouts[best].Object;
            }
        }
    }

    private static void AddObjects(ParagraphBlock hostParagraph, IReadOnlyList<FloatingObject> objects, ref uint order)
    {
        foreach (var obj in objects)
        {
            AddObject(hostParagraph, obj, ref order);
        }
    }

    private static void AddRemaining(
        ParagraphBlock hostParagraph,
        IReadOnlyList<FloatingObject> objects,
        HashSet<FloatingObject> added,
        ref uint order)
    {
        foreach (var obj in objects)
        {
            if (added.Add(obj))
            {
                AddObject(hostParagraph, obj, ref order);
            }
        }
    }

    private static void AddObject(ParagraphBlock hostParagraph, FloatingObject obj, ref uint order)
    {
        obj.Anchor.ZOrder = order++;
        hostParagraph.FloatingObjects.Add(obj);
    }

    private static void ApplyFixedLineSpacing(ParagraphBlock paragraph, double lineHeightDip)
    {
        if (lineHeightDip <= 0)
        {
            return;
        }

        paragraph.Properties.LineSpacingRule = DocLineSpacingRule.Exactly;
        paragraph.Properties.LineSpacing = DipToTwips(lineHeightDip);
        paragraph.Properties.SpacingBefore = 0;
        paragraph.Properties.SpacingAfter = 0;
        paragraph.Properties.AutoSpacingBefore = false;
        paragraph.Properties.AutoSpacingAfter = false;
        paragraph.Properties.ContextualSpacing = false;
    }

    private static int DipToTwips(double dip)
    {
        return (int)Math.Max(1, Math.Round(dip * 15.0));
    }

    private static void AppendLineInlines(
        ParagraphBlock paragraph,
        PdfLineGroup line,
        bool addLineBreak,
        bool useNonBreakingSpaces = false)
    {
        var spaceThreshold = ResolveSpaceThreshold(line.Runs);
        PdfTextRun? previous = null;
        RunInline? lastRunInline = null;
        TextStyleProperties? lastStyle = null;
        var spaceText = useNonBreakingSpaces ? "\u00A0" : " ";

        if (addLineBreak)
        {
            paragraph.Inlines.Add(new RunInline("\n"));
            lastRunInline = null;
            lastStyle = null;
        }

        foreach (var run in line.Runs)
        {
            if (string.IsNullOrWhiteSpace(run.Text))
            {
                continue;
            }

            if (previous is not null)
            {
                var gap = run.Bounds.X - previous.Bounds.Right;
                if (gap > spaceThreshold)
                {
                    AppendText(spaceText, lastStyle ?? BuildRunStyle(previous));
                }
            }

            var style = BuildRunStyle(run);
            AppendText(run.Text, style);
            previous = run;
        }

        void AppendText(string text, TextStyleProperties? style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (useNonBreakingSpaces && text.Contains(' '))
            {
                text = text.Replace(' ', '\u00A0');
            }

            if (lastRunInline is not null && AreStylesEquivalent(lastStyle, style))
            {
                lastRunInline.Text.Insert(lastRunInline.Text.Length, text);
                return;
            }

            var inline = new RunInline(text, style);
            paragraph.Inlines.Add(inline);
            lastRunInline = inline;
            lastStyle = style;
        }
    }

    private static TextStyleProperties? BuildRunStyle(PdfTextRun run)
    {
        if (run.Font is null && run.FontSize <= 0)
        {
            return null;
        }

        var style = new TextStyleProperties();
        if (run.Font is not null)
        {
            style.FontFamily = run.Font.Name;
            style.FontFamilyAscii = run.Font.Name;
            style.FontFamilyHighAnsi = run.Font.Name;
            style.FontWeight = run.Font.IsBold ? DocFontWeight.Bold : null;
            style.FontStyle = run.Font.IsItalic ? DocFontStyle.Italic : null;
        }

        if (run.FontSize > 0)
        {
            style.FontSize = PdfUnits.PointsToDip(run.FontSize);
        }

        if (run.Color is { } color)
        {
            style.Color = new DocColor(color.R, color.G, color.B, color.A);
        }

        if (run.AverageLetterGap > 0.1)
        {
            style.LetterSpacing = PdfUnits.PointsToDip(run.AverageLetterGap);
        }

        return style.HasValues ? style : null;
    }

    private static bool AreStylesEquivalent(TextStyleProperties? left, TextStyleProperties? right)
    {
        if (left is null || !left.HasValues)
        {
            return right is null || !right.HasValues;
        }

        return left.IsEquivalentTo(right);
    }

    private static double ResolveSpaceThreshold(IReadOnlyList<PdfTextRun> runs)
    {
        if (runs.Count == 0)
        {
            return 1.5;
        }

        var average = runs.Average(run => run.FontSize > 0 ? run.FontSize : run.Bounds.Height);
        return Math.Max(average * 0.25, 1.5);
    }

    private static List<PdfLineGroup> GroupRunsIntoLines(IReadOnlyList<PdfTextRun> runs)
    {
        if (runs.Count == 0)
        {
            return new List<PdfLineGroup>();
        }

        var ordered = runs
            .Where(run => !string.IsNullOrWhiteSpace(run.Text))
            .OrderByDescending(run => ResolveRunBaseline(run))
            .ThenBy(run => run.Bounds.X)
            .ToList();

        var tolerance = ResolveLineTolerance(ordered);
        var lines = new List<PdfLineGroup>();
        PdfLineGroup? current = null;

        foreach (var run in ordered)
        {
            var baseline = ResolveRunBaseline(run);
            if (current is null || Math.Abs(current.CenterY - baseline) > tolerance)
            {
                current = new PdfLineGroup(baseline);
                lines.Add(current);
            }

            current.Add(run);
        }

        foreach (var line in lines)
        {
            line.Sort();
        }

        return lines;
    }

    private static List<PdfColumnGroup> SplitLinesIntoRegions(IReadOnlyList<PdfLineGroup> lines)
    {
        if (lines.Count == 0)
        {
            return new List<PdfColumnGroup>();
        }

        var minX = lines.Min(line => line.Bounds.X);
        var maxRight = lines.Max(line => line.Bounds.Right);
        var pageWidth = Math.Max(maxRight - minX, 1);

        var spanningLines = new List<PdfLineGroup>();
        var columnLines = new List<PdfLineGroup>();
        foreach (var line in lines)
        {
            if (IsSpanningLine(line, minX, maxRight, pageWidth))
            {
                spanningLines.Add(line);
            }
            else
            {
                columnLines.Add(line);
            }
        }

        var columns = GroupLinesIntoColumns(columnLines);
        var columnRegions = new List<PdfColumnGroup>();
        foreach (var column in columns)
        {
            columnRegions.AddRange(SplitColumnIntoRegions(column));
        }

        var spans = BuildSpanningRegions(spanningLines);

        var regions = new List<PdfColumnGroup>();
        regions.AddRange(spans);
        regions.AddRange(columnRegions);

        SortRegions(regions, lines);
        return regions;
    }

    private static bool IsSpanningLine(PdfLineGroup line, double minX, double maxRight, double pageWidth)
    {
        var widthRatio = line.Bounds.Width / pageWidth;
        var leftGap = line.Bounds.X - minX;
        var rightGap = maxRight - line.Bounds.Right;
        return widthRatio >= 0.75 || (leftGap <= pageWidth * 0.05 && rightGap <= pageWidth * 0.05);
    }

    private static List<PdfColumnGroup> BuildSpanningRegions(IReadOnlyList<PdfLineGroup> lines)
    {
        if (lines.Count == 0)
        {
            return new List<PdfColumnGroup>();
        }

        var ordered = lines.OrderByDescending(line => line.CenterY).ToList();
        var averageHeight = ordered.Average(ResolveLineHeight);
        var gapThreshold = Math.Max(averageHeight * 1.6, 6);

        var regions = new List<PdfColumnGroup>();
        PdfColumnGroup? current = null;
        PdfLineGroup? previous = null;

        foreach (var line in ordered)
        {
            if (current is null || (previous is not null && (previous.CenterY - line.CenterY) > gapThreshold))
            {
                current = new PdfColumnGroup(line, isSpanning: true);
                regions.Add(current);
            }
            else
            {
                current.Add(line);
            }

            previous = line;
        }

        foreach (var region in regions)
        {
            region.Sort();
        }

        return regions;
    }

    private static void SortRegions(List<PdfColumnGroup> regions, IReadOnlyList<PdfLineGroup> allLines)
    {
        if (regions.Count <= 1)
        {
            return;
        }

        var averageHeight = allLines.Count > 0 ? allLines.Average(ResolveLineHeight) : 12;
        var topThreshold = Math.Max(averageHeight * 1.2, 6);

        regions.Sort((a, b) =>
        {
            var topDelta = b.TopY - a.TopY;
            if (Math.Abs(topDelta) > topThreshold)
            {
                return topDelta > 0 ? 1 : -1;
            }

            if (a.IsSpanning != b.IsSpanning)
            {
                return a.IsSpanning ? -1 : 1;
            }

            var columnCompare = a.ColumnIndex.CompareTo(b.ColumnIndex);
            if (columnCompare != 0)
            {
                return columnCompare;
            }

            return a.Bounds.X.CompareTo(b.Bounds.X);
        });
    }

    private static List<PdfColumnGroup> GroupLinesIntoColumns(IReadOnlyList<PdfLineGroup> lines)
    {
        var ordered = lines.OrderBy(line => line.Bounds.X).ToList();
        var tolerance = ResolveColumnTolerance(lines);
        var columns = new List<PdfColumnGroup>();

        foreach (var line in ordered)
        {
            var centerX = (line.Bounds.X + line.Bounds.Right) / 2;
            var target = columns
                .Select(column => new
                {
                    Column = column,
                    Distance = Math.Abs(column.CenterX - centerX),
                    Overlaps = centerX >= column.Bounds.X - tolerance && centerX <= column.Bounds.Right + tolerance
                })
                .Where(entry => entry.Overlaps)
                .OrderBy(entry => entry.Distance)
                .Select(entry => entry.Column)
                .FirstOrDefault();
            if (target is null)
            {
                target = new PdfColumnGroup(line, isSpanning: false);
                columns.Add(target);
            }
            else
            {
                target.Add(line);
            }
        }

        foreach (var column in columns)
        {
            column.Sort();
        }

        columns.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        for (var index = 0; index < columns.Count; index++)
        {
            columns[index].ColumnIndex = index;
        }
        return columns;
    }

    private static List<PdfLineGroup> OrderLinesForReflow(IReadOnlyList<PdfLineGroup> lines)
    {
        var regions = SplitLinesIntoRegions(lines);
        var ordered = new List<PdfLineGroup>();
        foreach (var region in regions)
        {
            ordered.AddRange(region.Lines);
        }

        return ordered;
    }

    private static List<PdfGlyphRegion> SplitGlyphLinesIntoRegions(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return new List<PdfGlyphRegion>();
        }

        var minX = lines.Min(line => line.Bounds.X);
        var maxRight = lines.Max(line => line.Bounds.Right);
        var pageWidth = Math.Max(maxRight - minX, 1);

        var spanningLines = new List<PdfGlyphLine>();
        var columnLines = new List<PdfGlyphLine>();
        foreach (var line in lines)
        {
            if (IsGlyphSpanningLine(line, minX, maxRight, pageWidth))
            {
                spanningLines.Add(line);
            }
            else
            {
                columnLines.Add(line);
            }
        }

        var columns = GroupGlyphLinesIntoColumns(columnLines);
        var columnRegions = new List<PdfGlyphRegion>();
        foreach (var column in columns)
        {
            columnRegions.AddRange(SplitGlyphColumnIntoRegions(column));
        }

        var spans = BuildGlyphSpanningRegions(spanningLines);

        var regions = new List<PdfGlyphRegion>();
        regions.AddRange(spans);
        regions.AddRange(columnRegions);

        SortGlyphRegions(regions, lines);
        return regions;
    }

    private static bool IsGlyphSpanningLine(PdfGlyphLine line, double minX, double maxRight, double pageWidth)
    {
        var widthRatio = line.Bounds.Width / pageWidth;
        var leftGap = line.Bounds.X - minX;
        var rightGap = maxRight - line.Bounds.Right;
        return widthRatio >= 0.75 || (leftGap <= pageWidth * 0.05 && rightGap <= pageWidth * 0.05);
    }

    private static List<PdfGlyphRegion> BuildGlyphSpanningRegions(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return new List<PdfGlyphRegion>();
        }

        var ordered = lines.OrderByDescending(line => line.BaselineY).ToList();
        var averageHeight = ordered.Average(line => line.LineHeight);
        var gapThreshold = Math.Max(averageHeight * 1.6, 6);

        var regions = new List<PdfGlyphRegion>();
        PdfGlyphRegion? current = null;
        PdfGlyphLine? previous = null;

        foreach (var line in ordered)
        {
            if (current is null || (previous is not null && (previous.BaselineY - line.BaselineY) > gapThreshold))
            {
                current = new PdfGlyphRegion(line, isSpanning: true);
                regions.Add(current);
            }
            else
            {
                current.Add(line);
            }

            previous = line;
        }

        foreach (var region in regions)
        {
            region.Sort();
        }

        return regions;
    }

    private static void SortGlyphRegions(List<PdfGlyphRegion> regions, IReadOnlyList<PdfGlyphLine> allLines)
    {
        if (regions.Count <= 1)
        {
            return;
        }

        var averageHeight = allLines.Count > 0 ? allLines.Average(line => line.LineHeight) : 12;
        var topThreshold = Math.Max(averageHeight * 1.2, 6);

        regions.Sort((a, b) =>
        {
            var topDelta = b.TopY - a.TopY;
            if (Math.Abs(topDelta) > topThreshold)
            {
                return topDelta > 0 ? 1 : -1;
            }

            if (a.IsSpanning != b.IsSpanning)
            {
                return a.IsSpanning ? -1 : 1;
            }

            var columnCompare = a.ColumnIndex.CompareTo(b.ColumnIndex);
            if (columnCompare != 0)
            {
                return columnCompare;
            }

            return a.Bounds.X.CompareTo(b.Bounds.X);
        });
    }

    private static List<PdfGlyphRegion> GroupGlyphLinesIntoColumns(IReadOnlyList<PdfGlyphLine> lines)
    {
        var ordered = lines.OrderBy(line => line.Bounds.X).ToList();
        var tolerance = ResolveGlyphColumnTolerance(lines);
        var columns = new List<PdfGlyphRegion>();

        foreach (var line in ordered)
        {
            var centerX = (line.Bounds.X + line.Bounds.Right) / 2;
            PdfGlyphRegion? target = null;
            var bestDistance = double.MaxValue;

            foreach (var column in columns)
            {
                var overlaps = centerX >= column.Bounds.X - tolerance && centerX <= column.Bounds.Right + tolerance;
                if (!overlaps)
                {
                    continue;
                }

                var distance = Math.Abs(column.CenterX - centerX);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    target = column;
                }
            }

            if (target is null)
            {
                target = new PdfGlyphRegion(line, isSpanning: false);
                columns.Add(target);
            }
            else
            {
                target.Add(line);
            }
        }

        foreach (var column in columns)
        {
            column.Sort();
        }

        columns.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        for (var index = 0; index < columns.Count; index++)
        {
            columns[index].ColumnIndex = index;
        }

        return columns;
    }

    private static List<PdfGlyphRegion> SplitGlyphColumnIntoRegions(PdfGlyphRegion column)
    {
        var lines = column.Lines;
        if (lines.Count == 0)
        {
            return new List<PdfGlyphRegion>();
        }

        var ordered = lines.OrderByDescending(line => line.BaselineY).ToList();
        var averageHeight = ordered.Average(line => line.LineHeight);
        var gapThreshold = Math.Max(averageHeight * 1.8, 8);

        var regions = new List<PdfGlyphRegion>();
        PdfGlyphRegion? current = null;
        PdfGlyphLine? previous = null;

        foreach (var line in ordered)
        {
            if (current is null || (previous is not null && (previous.BaselineY - line.BaselineY) > gapThreshold))
            {
                current = new PdfGlyphRegion(line, isSpanning: false)
                {
                    ColumnIndex = column.ColumnIndex
                };
                regions.Add(current);
            }
            else
            {
                current.Add(line);
            }

            previous = line;
        }

        foreach (var region in regions)
        {
            region.Sort();
        }

        return regions;
    }

    private static double ResolveGlyphColumnTolerance(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return 12;
        }

        var averageHeight = lines.Average(line => line.LineHeight > 0 ? line.LineHeight : line.Bounds.Height);
        return Math.Max(averageHeight * 2.5, 12);
    }

    private static List<PdfParagraphGroup> GroupLinesIntoReflowParagraphs(IReadOnlyList<PdfLineGroup> lines)
    {
        if (lines.Count == 0)
        {
            return new List<PdfParagraphGroup>();
        }

        var averageHeight = lines.Average(ResolveLineHeight);
        var lineGapThreshold = Math.Max(averageHeight * 1.4, 4);
        var indentThreshold = Math.Max(averageHeight * 1.5, 6);

        var paragraphs = new List<PdfParagraphGroup>();
        PdfParagraphGroup? current = null;
        PdfLineGroup? previous = null;
        var paragraphIndent = 0.0;

        foreach (var line in lines)
        {
            if (current is null)
            {
                current = new PdfParagraphGroup();
                current.Add(line);
                paragraphs.Add(current);
                previous = line;
                paragraphIndent = line.Bounds.X;
                continue;
            }

            var baselineGap = previous is null ? 0 : previous.CenterY - line.CenterY;
            var indentGap = Math.Abs(line.Bounds.X - paragraphIndent);

            if (baselineGap > lineGapThreshold || indentGap > indentThreshold)
            {
                current = new PdfParagraphGroup();
                current.Add(line);
                paragraphs.Add(current);
                paragraphIndent = line.Bounds.X;
            }
            else
            {
                current.Add(line);
            }

            previous = line;
        }

        return paragraphs;
    }

    private static double ResolveBaselineFontSize(IReadOnlyList<PdfTextRun> runs)
    {
        var sizes = runs
            .Where(run => run.FontSize > 0)
            .Select(run => run.FontSize)
            .OrderBy(value => value)
            .ToList();

        if (sizes.Count == 0)
        {
            return 10;
        }

        var mid = sizes.Count / 2;
        return sizes.Count % 2 == 0 ? (sizes[mid - 1] + sizes[mid]) / 2 : sizes[mid];
    }

    private static double ResolveBaselineFontSize(IReadOnlyList<PdfTextGlyph> glyphs)
    {
        var sizes = glyphs
            .Where(glyph => glyph.FontSize > 0)
            .Select(glyph => glyph.FontSize)
            .OrderBy(value => value)
            .ToList();

        if (sizes.Count == 0)
        {
            return 10;
        }

        var mid = sizes.Count / 2;
        return sizes.Count % 2 == 0 ? (sizes[mid - 1] + sizes[mid]) / 2 : sizes[mid];
    }

    private static bool IsGlyphOrientationSupported(PdfTextOrientation orientation)
        => orientation is PdfTextOrientation.Horizontal or PdfTextOrientation.Rotate180;

    private static bool IsGlyphLineQualityPoor(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count < 12)
        {
            return false;
        }

        var shortLines = 0;
        var singleGlyphLines = 0;
        var narrowLines = 0;
        var totalChars = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var text = line.Text?.Trim() ?? string.Empty;
            totalChars += text.Length;

            if (text.Length <= 2)
            {
                shortLines++;
            }

            if (line.Glyphs.Count <= 2)
            {
                singleGlyphLines++;
            }

            var lineHeight = line.LineHeight > 0 ? line.LineHeight : line.Bounds.Height;
            if (lineHeight > 0 && line.Bounds.Width <= lineHeight * 1.6)
            {
                narrowLines++;
            }
        }

        var avgChars = totalChars / (double)Math.Max(1, lines.Count);
        var shortRatio = shortLines / (double)lines.Count;
        var singleRatio = singleGlyphLines / (double)lines.Count;
        var narrowRatio = narrowLines / (double)lines.Count;

        if (avgChars <= 3.5 && shortRatio > 0.55 && singleRatio > 0.35)
        {
            return true;
        }

        return avgChars <= 4.0 && shortRatio > 0.5 && narrowRatio > 0.6;
    }

    private static List<PdfGlyphLine> GroupGlyphsIntoLines(IReadOnlyList<PdfTextGlyph> glyphs)
    {
        if (glyphs.Count == 0)
        {
            return new List<PdfGlyphLine>();
        }

        var ordered = glyphs
            .Where(glyph => !string.IsNullOrWhiteSpace(glyph.Text))
            .OrderByDescending(glyph => ResolveGlyphBaseline(glyph))
            .ThenBy(glyph => glyph.Bounds.X)
            .ToList();

        if (ordered.Count == 0)
        {
            return new List<PdfGlyphLine>();
        }

        var tolerance = ResolveGlyphLineTolerance(ordered);
        var lines = new List<PdfGlyphLine>();
        PdfGlyphLine? current = null;

        foreach (var glyph in ordered)
        {
            var baseline = ResolveGlyphBaseline(glyph);
            if (current is null || Math.Abs(current.BaselineY - baseline) > tolerance)
            {
                current = new PdfGlyphLine(baseline);
                lines.Add(current);
            }

            current.Add(glyph);
        }

        foreach (var line in lines)
        {
            line.Sort();
        }

        var finalLines = new List<PdfGlyphLine>(lines.Count);
        foreach (var line in lines)
        {
            var splits = SplitGlyphLineByLargeGap(line);
            for (var i = 0; i < splits.Count; i++)
            {
                splits[i].Sort();
                BuildGlyphLineSpans(splits[i]);
                finalLines.Add(splits[i]);
            }
        }

        return MergeSuperscriptLines(finalLines);
    }

    private static List<PdfGlyphLine> MergeSuperscriptLines(List<PdfGlyphLine> lines)
    {
        if (lines.Count < 2)
        {
            return lines;
        }

        var averageHeight = ResolveAverageLineHeight(lines);
        if (averageHeight <= 0)
        {
            return lines;
        }

        var consumed = new HashSet<PdfGlyphLine>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!IsSuperscriptMarkerLine(line, averageHeight))
            {
                continue;
            }

            var target = FindSuperscriptTarget(line, lines);
            if (target is null)
            {
                continue;
            }

            target.MergeSuperscript(line);
            target.Sort();
            BuildGlyphLineSpans(target);
            consumed.Add(line);
        }

        if (consumed.Count == 0)
        {
            return lines;
        }

        var result = new List<PdfGlyphLine>(lines.Count - consumed.Count);
        foreach (var line in lines)
        {
            if (!consumed.Contains(line))
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static bool IsSuperscriptMarkerLine(PdfGlyphLine line, double averageHeight)
    {
        if (string.IsNullOrWhiteSpace(line.Text))
        {
            return false;
        }

        var text = line.Text.Trim();
        if (text.Length == 0 || text.Length > 3)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
            {
                return false;
            }
        }

        var height = line.LineHeight > 0 ? line.LineHeight : line.Bounds.Height;
        return height <= averageHeight * 0.85;
    }

    private static PdfGlyphLine? FindSuperscriptTarget(PdfGlyphLine marker, IReadOnlyList<PdfGlyphLine> lines)
    {
        PdfGlyphLine? best = null;
        var bestDelta = double.MaxValue;
        for (var index = 0; index < lines.Count; index++)
        {
            var candidate = lines[index];
            if (ReferenceEquals(candidate, marker))
            {
                continue;
            }

            if (candidate.BaselineY >= marker.BaselineY)
            {
                continue;
            }

            var lineHeight = candidate.LineHeight > 0 ? candidate.LineHeight : candidate.Bounds.Height;
            var delta = marker.BaselineY - candidate.BaselineY;
            if (delta < lineHeight * 0.15 || delta > lineHeight * 1.6)
            {
                continue;
            }

            if (!IsSuperscriptAligned(marker, candidate, lineHeight))
            {
                continue;
            }

            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        return best;
    }

    private static bool IsSuperscriptAligned(PdfGlyphLine marker, PdfGlyphLine line, double lineHeight)
    {
        var minX = line.Bounds.Right - lineHeight * 1.6;
        var maxX = line.Bounds.Right + lineHeight * 1.6;
        var markerX = marker.Bounds.X;
        if (markerX < minX || markerX > maxX)
        {
            return false;
        }

        var markerWidth = marker.Bounds.Width;
        if (markerWidth > lineHeight * 1.2)
        {
            return false;
        }

        return true;
    }

    private static double ResolveGlyphLineTolerance(IReadOnlyList<PdfTextGlyph> glyphs)
    {
        if (glyphs.Count == 0)
        {
            return 2.0;
        }

        var averageSize = glyphs.Average(glyph => glyph.FontSize > 0 ? glyph.FontSize : glyph.Bounds.Height);
        return Math.Max(averageSize * 0.6, 2.0);
    }

    private static double ResolveGlyphBaseline(PdfTextGlyph glyph)
    {
        if (double.IsFinite(glyph.BaselineY) && glyph.BaselineY > 0)
        {
            return glyph.BaselineY;
        }

        return glyph.Bounds.Y + glyph.Bounds.Height / 2;
    }

    private static void BuildGlyphLineSpans(PdfGlyphLine line)
    {
        line.Spans.Clear();
        if (line.Glyphs.Count == 0)
        {
            line.Text = string.Empty;
            return;
        }

        var spaceThreshold = ResolveGlyphSpaceThreshold(line.Glyphs);
        PdfTextGlyph? previous = null;
        TextStyleProperties? lastStyle = null;
        var builder = new StringBuilder();
        var lineText = new StringBuilder();
        PdfRect? spanBounds = null;

        void FlushSpan()
        {
            if (builder.Length == 0)
            {
                return;
            }

            var text = builder.ToString();
            var bounds = spanBounds ?? line.Bounds;
            var style = lastStyle;
            line.Spans.Add(new PdfTextSpan(text, style, bounds));
            lineText.Append(text);
            builder.Clear();
            spanBounds = null;
        }

        void AppendText(string text, TextStyleProperties? style, PdfRect bounds)
        {
            if (builder.Length == 0 || !AreStylesEquivalent(lastStyle, style))
            {
                FlushSpan();
                lastStyle = style;
            }

            builder.Append(text);
            spanBounds = spanBounds.HasValue ? UnionRect(spanBounds.Value, bounds) : bounds;
        }

        void AppendSpace()
        {
            if (builder.Length == 0)
            {
                return;
            }

            builder.Append(' ');
        }

        foreach (var glyph in line.Glyphs)
        {
            var text = glyph.Text;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (char.IsWhiteSpace(text[0]))
            {
                AppendSpace();
                previous = glyph;
                continue;
            }

            if (previous is not null)
            {
                var gap = glyph.Bounds.X - previous.Bounds.Right;
                if (gap > spaceThreshold)
                {
                    AppendSpace();
                }
            }

            var style = BuildGlyphStyle(glyph);
            AppendText(text, style, glyph.Bounds);
            previous = glyph;
        }

        FlushSpan();
        line.Text = lineText.ToString();
        line.LineHeight = ResolveGlyphLineHeight(line);
        line.TrailingFootnoteMarker = DetectTrailingFootnoteMarker(line);
    }

    private static List<PdfGlyphLine> SplitGlyphLineByLargeGap(PdfGlyphLine line)
    {
        if (line.Glyphs.Count < 2)
        {
            return new List<PdfGlyphLine> { line };
        }

        var gaps = new List<double>(line.Glyphs.Count);
        var heights = new List<double>(line.Glyphs.Count);
        var widths = new List<double>(line.Glyphs.Count);
        for (var i = 0; i < line.Glyphs.Count; i++)
        {
            var glyph = line.Glyphs[i];
            var size = glyph.FontSize > 0 ? glyph.FontSize : glyph.Bounds.Height;
            if (size > 0)
            {
                heights.Add(size);
            }

            if (glyph.Bounds.Width > 0)
            {
                widths.Add(glyph.Bounds.Width);
            }

            if (i > 0)
            {
                var gap = glyph.Bounds.X - line.Glyphs[i - 1].Bounds.Right;
                if (gap > 0)
                {
                    gaps.Add(gap);
                }
            }
        }

        var medianGap = gaps.Count > 0 ? Median(gaps) : 0.0;
        var medianHeight = heights.Count > 0 ? Median(heights) : line.Bounds.Height;
        var medianWidth = widths.Count > 0 ? Median(widths) : 0.0;
        var spaceThreshold = ResolveGlyphSpaceThreshold(line.Glyphs);
        var gapThreshold = Math.Max(spaceThreshold * 3.5, Math.Max(medianHeight * 1.5, medianWidth * 1.5));
        if (medianGap > 0)
        {
            gapThreshold = Math.Max(gapThreshold, medianGap * 4.0);
        }

        gapThreshold = Math.Max(gapThreshold, 6.0);

        var segments = new List<PdfGlyphLine>();
        var current = new PdfGlyphLine(line.BaselineY);
        current.Add(line.Glyphs[0]);
        for (var i = 1; i < line.Glyphs.Count; i++)
        {
            var gap = line.Glyphs[i].Bounds.X - line.Glyphs[i - 1].Bounds.Right;
            if (gap > gapThreshold && current.Glyphs.Count > 0)
            {
                segments.Add(current);
                current = new PdfGlyphLine(line.BaselineY);
            }

            current.Add(line.Glyphs[i]);
        }

        if (current.Glyphs.Count > 0)
        {
            segments.Add(current);
        }

        return segments.Count == 0 ? new List<PdfGlyphLine> { line } : segments;
    }

    private static double ResolveGlyphSpaceThreshold(IReadOnlyList<PdfTextGlyph> glyphs)
    {
        if (glyphs.Count < 2)
        {
            return 1.5;
        }

        var gaps = new List<double>(glyphs.Count);
        var widths = new List<double>(glyphs.Count);
        var sizes = new List<double>(glyphs.Count);
        for (var i = 0; i < glyphs.Count; i++)
        {
            var glyph = glyphs[i];
            widths.Add(glyph.Bounds.Width);
            if (glyph.FontSize > 0)
            {
                sizes.Add(glyph.FontSize);
            }

            if (i > 0)
            {
                var gap = glyph.Bounds.X - glyphs[i - 1].Bounds.Right;
                if (gap > 0)
                {
                    gaps.Add(gap);
                }
            }
        }

        var medianGap = gaps.Count > 0 ? Median(gaps) : 0.0;
        var medianWidth = widths.Count > 0 ? Median(widths) : 0.0;
        var medianSize = sizes.Count > 0 ? Median(sizes) : 0.0;
        var threshold = Math.Max(medianGap * 1.6, Math.Max(medianWidth * 0.5, medianSize * 0.25));
        return Math.Max(threshold, 1.5);
    }

    private static double ResolveGlyphLineHeight(PdfGlyphLine line)
    {
        if (line.Glyphs.Count == 0)
        {
            return line.Bounds.Height;
        }

        var average = line.Glyphs.Average(glyph => glyph.FontSize > 0 ? glyph.FontSize : glyph.Bounds.Height);
        return Math.Max(average, line.Bounds.Height);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
    }

    private static PdfRect UnionRect(PdfRect left, PdfRect right)
    {
        var minX = Math.Min(left.X, right.X);
        var minY = Math.Min(left.Y, right.Y);
        var maxX = Math.Max(left.Right, right.Right);
        var maxY = Math.Max(left.Bottom, right.Bottom);
        return new PdfRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static void AssignGlyphLineAlignment(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var minX = lines.Min(line => line.Bounds.X);
        var maxRight = lines.Max(line => line.Bounds.Right);
        var contentWidth = Math.Max(maxRight - minX, 1);

        foreach (var line in lines)
        {
            var leftGap = line.Bounds.X - minX;
            var rightGap = maxRight - line.Bounds.Right;
            var lineHeight = line.LineHeight > 0 ? line.LineHeight : line.Bounds.Height;
            var tolerance = Math.Max(lineHeight * 0.6, 4);

            if (Math.Abs(leftGap - rightGap) <= tolerance)
            {
                line.Alignment = ParagraphAlignment.Center;
                continue;
            }

            if (rightGap <= tolerance && leftGap > tolerance)
            {
                line.Alignment = ParagraphAlignment.Right;
                continue;
            }

            if (leftGap <= tolerance && rightGap <= tolerance && line.Bounds.Width >= contentWidth * 0.9)
            {
                line.Alignment = ParagraphAlignment.Justify;
                continue;
            }

            line.Alignment = ParagraphAlignment.Left;
        }
    }

    private static List<PdfGlyphLine> OrderGlyphLinesForReflow(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return new List<PdfGlyphLine>();
        }

        var regions = SplitGlyphLinesIntoRegions(lines);
        if (regions.Count > 1)
        {
            var orderedRegions = new List<PdfGlyphLine>(lines.Count);
            foreach (var region in regions)
            {
                orderedRegions.AddRange(region.Lines);
            }

            return orderedRegions;
        }

        var ordered = new List<PdfGlyphLine>(lines.Count);
        OrderGlyphLinesXYCut(lines.ToList(), ordered, 0);
        return ordered;
    }

    private static void OrderGlyphLinesXYCut(List<PdfGlyphLine> lines, List<PdfGlyphLine> output, int depth)
    {
        if (lines.Count == 0)
        {
            return;
        }

        if (depth > 6 || lines.Count <= 3)
        {
            lines.Sort(static (a, b) =>
            {
                var yCompare = b.BaselineY.CompareTo(a.BaselineY);
                return yCompare != 0 ? yCompare : a.Bounds.X.CompareTo(b.Bounds.X);
            });
            output.AddRange(lines);
            return;
        }

        var bounds = GetBounds(lines);
        var averageHeight = ResolveAverageLineHeight(lines);
        var minGap = Math.Max(averageHeight * 2.0, bounds.Width * 0.06);

        if (TryFindVerticalGapByCenters(lines, bounds, minGap, out var verticalGap)
            || TryFindVerticalWhitespaceGap(lines, bounds, minGap, out verticalGap))
        {
            var gapMid = (verticalGap.Start + verticalGap.End) / 2;
            var left = new List<PdfGlyphLine>();
            var right = new List<PdfGlyphLine>();
            foreach (var line in lines)
            {
                var centerX = (line.Bounds.X + line.Bounds.Right) / 2;
                if (centerX < gapMid)
                {
                    left.Add(line);
                }
                else
                {
                    right.Add(line);
                }
            }

            OrderGlyphLinesXYCut(left, output, depth + 1);
            OrderGlyphLinesXYCut(right, output, depth + 1);
            return;
        }

        if (TryFindHorizontalWhitespaceGap(lines, bounds, minGap, out var horizontalGap))
        {
            var gapMid = (horizontalGap.Start + horizontalGap.End) / 2;
            var top = new List<PdfGlyphLine>();
            var bottom = new List<PdfGlyphLine>();
            foreach (var line in lines)
            {
                var centerY = (line.Bounds.Y + line.Bounds.Bottom) / 2;
                if (centerY > gapMid)
                {
                    top.Add(line);
                }
                else
                {
                    bottom.Add(line);
                }
            }

            OrderGlyphLinesXYCut(top, output, depth + 1);
            OrderGlyphLinesXYCut(bottom, output, depth + 1);
            return;
        }

        lines.Sort(static (a, b) =>
        {
            var yCompare = b.BaselineY.CompareTo(a.BaselineY);
            return yCompare != 0 ? yCompare : a.Bounds.X.CompareTo(b.Bounds.X);
        });
        output.AddRange(lines);
    }

    private static List<FlowBlock> OrderFlowBlocksForReflow(IReadOnlyList<FlowBlock> blocks, double averageLineHeight)
    {
        if (blocks.Count == 0)
        {
            return new List<FlowBlock>();
        }

        var ordered = new List<FlowBlock>(blocks.Count);
        OrderFlowBlocksXYCut(blocks.ToList(), ordered, 0, averageLineHeight);
        return ordered;
    }

    private static void OrderFlowBlocksXYCut(
        List<FlowBlock> blocks,
        List<FlowBlock> output,
        int depth,
        double averageLineHeight)
    {
        if (blocks.Count == 0)
        {
            return;
        }

        if (depth > 6 || blocks.Count <= 2)
        {
            blocks.Sort(static (a, b) =>
            {
                var yCompare = b.Bounds.Bottom.CompareTo(a.Bounds.Bottom);
                return yCompare != 0 ? yCompare : a.Bounds.X.CompareTo(b.Bounds.X);
            });
            output.AddRange(blocks);
            return;
        }

        var bounds = GetBounds(blocks);
        var minGap = Math.Max(averageLineHeight * 2.0, bounds.Width * 0.06);

        if (TryFindVerticalGapByCenters(blocks, bounds, minGap, out var verticalGap)
            || TryFindVerticalWhitespaceGap(blocks, bounds, minGap, out verticalGap))
        {
            var gapMid = (verticalGap.Start + verticalGap.End) / 2;
            var left = new List<FlowBlock>();
            var right = new List<FlowBlock>();
            foreach (var block in blocks)
            {
                var centerX = (block.Bounds.X + block.Bounds.Right) / 2;
                if (centerX < gapMid)
                {
                    left.Add(block);
                }
                else
                {
                    right.Add(block);
                }
            }

            OrderFlowBlocksXYCut(left, output, depth + 1, averageLineHeight);
            OrderFlowBlocksXYCut(right, output, depth + 1, averageLineHeight);
            return;
        }

        if (TryFindHorizontalWhitespaceGap(blocks, bounds, minGap, out var horizontalGap))
        {
            var gapMid = (horizontalGap.Start + horizontalGap.End) / 2;
            var top = new List<FlowBlock>();
            var bottom = new List<FlowBlock>();
            foreach (var block in blocks)
            {
                var centerY = (block.Bounds.Y + block.Bounds.Bottom) / 2;
                if (centerY > gapMid)
                {
                    top.Add(block);
                }
                else
                {
                    bottom.Add(block);
                }
            }

            OrderFlowBlocksXYCut(top, output, depth + 1, averageLineHeight);
            OrderFlowBlocksXYCut(bottom, output, depth + 1, averageLineHeight);
            return;
        }

        blocks.Sort(static (a, b) =>
        {
            var yCompare = b.Bounds.Bottom.CompareTo(a.Bounds.Bottom);
            return yCompare != 0 ? yCompare : a.Bounds.X.CompareTo(b.Bounds.X);
        });
        output.AddRange(blocks);
    }

    private static PdfRect GetBounds(IReadOnlyList<PdfGlyphLine> lines)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        for (var i = 0; i < lines.Count; i++)
        {
            var bounds = lines[i].Bounds;
            if (bounds.X < minX)
            {
                minX = bounds.X;
            }

            if (bounds.Y < minY)
            {
                minY = bounds.Y;
            }

            if (bounds.Right > maxX)
            {
                maxX = bounds.Right;
            }

            if (bounds.Bottom > maxY)
            {
                maxY = bounds.Bottom;
            }
        }

        if (minX == double.MaxValue)
        {
            return new PdfRect(0, 0, 0, 0);
        }

        return new PdfRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static PdfRect GetBounds(IReadOnlyList<FlowBlock> blocks)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        for (var i = 0; i < blocks.Count; i++)
        {
            var bounds = blocks[i].Bounds;
            if (bounds.X < minX)
            {
                minX = bounds.X;
            }

            if (bounds.Y < minY)
            {
                minY = bounds.Y;
            }

            if (bounds.Right > maxX)
            {
                maxX = bounds.Right;
            }

            if (bounds.Bottom > maxY)
            {
                maxY = bounds.Bottom;
            }
        }

        if (minX == double.MaxValue)
        {
            return new PdfRect(0, 0, 0, 0);
        }

        return new PdfRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static double ResolveAverageLineHeight(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        double total = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var lineHeight = lines[i].LineHeight > 0 ? lines[i].LineHeight : lines[i].Bounds.Height;
            total += lineHeight;
        }

        return total / lines.Count;
    }

    private static bool TryFindVerticalWhitespaceGap(
        IReadOnlyList<PdfGlyphLine> lines,
        PdfRect bounds,
        double minGap,
        out PdfGap gap)
    {
        gap = default;
        if (bounds.Width <= 0)
        {
            return false;
        }

        const int bins = 64;
        var coverage = new bool[bins];
        var binWidth = bounds.Width / bins;
        if (binWidth <= 0)
        {
            return false;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var start = (int)Math.Clamp(Math.Floor((line.Bounds.X - bounds.X) / binWidth), 0, bins - 1);
            var end = (int)Math.Clamp(Math.Floor((line.Bounds.Right - bounds.X) / binWidth), 0, bins - 1);
            for (var bin = start; bin <= end; bin++)
            {
                coverage[bin] = true;
            }
        }

        var bestStart = -1;
        var bestLength = 0;
        var currentStart = -1;
        var currentLength = 0;
        for (var i = 0; i < bins; i++)
        {
            if (!coverage[i])
            {
                if (currentStart < 0)
                {
                    currentStart = i;
                    currentLength = 1;
                }
                else
                {
                    currentLength++;
                }
            }
            else if (currentStart >= 0)
            {
                if (currentLength > bestLength)
                {
                    bestStart = currentStart;
                    bestLength = currentLength;
                }

                currentStart = -1;
                currentLength = 0;
            }
        }

        if (currentStart >= 0 && currentLength > bestLength)
        {
            bestStart = currentStart;
            bestLength = currentLength;
        }

        if (bestStart < 0)
        {
            return TryFindVerticalGapByCenters(lines, bounds, minGap, out gap);
        }

        var gapStart = bounds.X + bestStart * binWidth;
        var gapEnd = bounds.X + (bestStart + bestLength) * binWidth;
        var gapWidth = gapEnd - gapStart;
        if (gapWidth < minGap)
        {
            return TryFindVerticalGapByCenters(lines, bounds, minGap, out gap);
        }

        if (gapStart <= bounds.X + bounds.Width * 0.05 || gapEnd >= bounds.Right - bounds.Width * 0.05)
        {
            return TryFindVerticalGapByCenters(lines, bounds, minGap, out gap);
        }

        if (LineCrossesGap(lines, gapStart, gapEnd, vertical: true))
        {
            return TryFindVerticalGapByCenters(lines, bounds, minGap, out gap);
        }

        gap = new PdfGap(gapStart, gapEnd);
        return true;
    }

    private static bool TryFindVerticalWhitespaceGap(
        IReadOnlyList<FlowBlock> blocks,
        PdfRect bounds,
        double minGap,
        out PdfGap gap)
    {
        gap = default;
        if (bounds.Width <= 0)
        {
            return false;
        }

        const int bins = 64;
        var coverage = new bool[bins];
        var binWidth = bounds.Width / bins;
        if (binWidth <= 0)
        {
            return false;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var boundsItem = blocks[i].Bounds;
            var start = (int)Math.Clamp(Math.Floor((boundsItem.X - bounds.X) / binWidth), 0, bins - 1);
            var end = (int)Math.Clamp(Math.Floor((boundsItem.Right - bounds.X) / binWidth), 0, bins - 1);
            for (var bin = start; bin <= end; bin++)
            {
                coverage[bin] = true;
            }
        }

        var bestStart = -1;
        var bestLength = 0;
        var currentStart = -1;
        var currentLength = 0;
        for (var i = 0; i < bins; i++)
        {
            if (!coverage[i])
            {
                if (currentStart < 0)
                {
                    currentStart = i;
                    currentLength = 1;
                }
                else
                {
                    currentLength++;
                }
            }
            else if (currentStart >= 0)
            {
                if (currentLength > bestLength)
                {
                    bestStart = currentStart;
                    bestLength = currentLength;
                }

                currentStart = -1;
                currentLength = 0;
            }
        }

        if (currentStart >= 0 && currentLength > bestLength)
        {
            bestStart = currentStart;
            bestLength = currentLength;
        }

        if (bestStart < 0)
        {
            return TryFindVerticalGapByCenters(blocks, bounds, minGap, out gap);
        }

        var gapStart = bounds.X + bestStart * binWidth;
        var gapEnd = bounds.X + (bestStart + bestLength) * binWidth;
        var gapWidth = gapEnd - gapStart;
        if (gapWidth < minGap)
        {
            return TryFindVerticalGapByCenters(blocks, bounds, minGap, out gap);
        }

        if (gapStart <= bounds.X + bounds.Width * 0.05 || gapEnd >= bounds.Right - bounds.Width * 0.05)
        {
            return TryFindVerticalGapByCenters(blocks, bounds, minGap, out gap);
        }

        if (BlockCrossesGap(blocks, gapStart, gapEnd, vertical: true))
        {
            return TryFindVerticalGapByCenters(blocks, bounds, minGap, out gap);
        }

        gap = new PdfGap(gapStart, gapEnd);
        return true;
    }

    private static bool TryFindVerticalGapByCenters(
        IReadOnlyList<PdfGlyphLine> lines,
        PdfRect bounds,
        double minGap,
        out PdfGap gap)
    {
        gap = default;
        if (lines.Count < 2)
        {
            return false;
        }

        var centers = new double[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            centers[i] = (line.Bounds.X + line.Bounds.Right) / 2;
        }

        Array.Sort(centers);
        var bestGap = 0.0;
        var gapStart = 0.0;
        var gapEnd = 0.0;
        for (var i = 1; i < centers.Length; i++)
        {
            var currentGap = centers[i] - centers[i - 1];
            if (currentGap > bestGap)
            {
                bestGap = currentGap;
                gapStart = centers[i - 1];
                gapEnd = centers[i];
            }
        }

        if (bestGap < minGap)
        {
            return false;
        }

        if (gapStart <= bounds.X + bounds.Width * 0.05 || gapEnd >= bounds.Right - bounds.Width * 0.05)
        {
            return false;
        }

        if (LineCrossesGap(lines, gapStart, gapEnd, vertical: true))
        {
            return false;
        }

        gap = new PdfGap(gapStart, gapEnd);
        return true;
    }

    private static bool TryFindVerticalGapByCenters(
        IReadOnlyList<FlowBlock> blocks,
        PdfRect bounds,
        double minGap,
        out PdfGap gap)
    {
        gap = default;
        if (blocks.Count < 2)
        {
            return false;
        }

        var centers = new double[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            var boundsItem = blocks[i].Bounds;
            centers[i] = (boundsItem.X + boundsItem.Right) / 2;
        }

        Array.Sort(centers);
        var bestGap = 0.0;
        var gapStart = 0.0;
        var gapEnd = 0.0;
        for (var i = 1; i < centers.Length; i++)
        {
            var currentGap = centers[i] - centers[i - 1];
            if (currentGap > bestGap)
            {
                bestGap = currentGap;
                gapStart = centers[i - 1];
                gapEnd = centers[i];
            }
        }

        if (bestGap < minGap)
        {
            return false;
        }

        if (gapStart <= bounds.X + bounds.Width * 0.05 || gapEnd >= bounds.Right - bounds.Width * 0.05)
        {
            return false;
        }

        if (BlockCrossesGap(blocks, gapStart, gapEnd, vertical: true))
        {
            return false;
        }

        gap = new PdfGap(gapStart, gapEnd);
        return true;
    }

    private static bool TryFindHorizontalWhitespaceGap(
        IReadOnlyList<PdfGlyphLine> lines,
        PdfRect bounds,
        double minGap,
        out PdfGap gap)
    {
        gap = default;
        if (bounds.Height <= 0)
        {
            return false;
        }

        const int bins = 64;
        var coverage = new bool[bins];
        var binHeight = bounds.Height / bins;
        if (binHeight <= 0)
        {
            return false;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var start = (int)Math.Clamp(Math.Floor((line.Bounds.Y - bounds.Y) / binHeight), 0, bins - 1);
            var end = (int)Math.Clamp(Math.Floor((line.Bounds.Bottom - bounds.Y) / binHeight), 0, bins - 1);
            for (var bin = start; bin <= end; bin++)
            {
                coverage[bin] = true;
            }
        }

        var bestStart = -1;
        var bestLength = 0;
        var currentStart = -1;
        var currentLength = 0;
        for (var i = 0; i < bins; i++)
        {
            if (!coverage[i])
            {
                if (currentStart < 0)
                {
                    currentStart = i;
                    currentLength = 1;
                }
                else
                {
                    currentLength++;
                }
            }
            else if (currentStart >= 0)
            {
                if (currentLength > bestLength)
                {
                    bestStart = currentStart;
                    bestLength = currentLength;
                }

                currentStart = -1;
                currentLength = 0;
            }
        }

        if (currentStart >= 0 && currentLength > bestLength)
        {
            bestStart = currentStart;
            bestLength = currentLength;
        }

        if (bestStart < 0)
        {
            return false;
        }

        var gapStart = bounds.Y + bestStart * binHeight;
        var gapEnd = bounds.Y + (bestStart + bestLength) * binHeight;
        var gapHeight = gapEnd - gapStart;
        if (gapHeight < minGap)
        {
            return false;
        }

        if (gapStart <= bounds.Y + bounds.Height * 0.05 || gapEnd >= bounds.Bottom - bounds.Height * 0.05)
        {
            return false;
        }

        if (LineCrossesGap(lines, gapStart, gapEnd, vertical: false))
        {
            return false;
        }

        gap = new PdfGap(gapStart, gapEnd);
        return true;
    }

    private static bool TryFindHorizontalWhitespaceGap(
        IReadOnlyList<FlowBlock> blocks,
        PdfRect bounds,
        double minGap,
        out PdfGap gap)
    {
        gap = default;
        if (bounds.Height <= 0)
        {
            return false;
        }

        const int bins = 64;
        var coverage = new bool[bins];
        var binHeight = bounds.Height / bins;
        if (binHeight <= 0)
        {
            return false;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var boundsItem = blocks[i].Bounds;
            var start = (int)Math.Clamp(Math.Floor((boundsItem.Y - bounds.Y) / binHeight), 0, bins - 1);
            var end = (int)Math.Clamp(Math.Floor((boundsItem.Bottom - bounds.Y) / binHeight), 0, bins - 1);
            for (var bin = start; bin <= end; bin++)
            {
                coverage[bin] = true;
            }
        }

        var bestStart = -1;
        var bestLength = 0;
        var currentStart = -1;
        var currentLength = 0;
        for (var i = 0; i < bins; i++)
        {
            if (!coverage[i])
            {
                if (currentStart < 0)
                {
                    currentStart = i;
                    currentLength = 1;
                }
                else
                {
                    currentLength++;
                }
            }
            else if (currentStart >= 0)
            {
                if (currentLength > bestLength)
                {
                    bestStart = currentStart;
                    bestLength = currentLength;
                }

                currentStart = -1;
                currentLength = 0;
            }
        }

        if (currentStart >= 0 && currentLength > bestLength)
        {
            bestStart = currentStart;
            bestLength = currentLength;
        }

        if (bestStart < 0)
        {
            return false;
        }

        var gapStart = bounds.Y + bestStart * binHeight;
        var gapEnd = bounds.Y + (bestStart + bestLength) * binHeight;
        var gapHeight = gapEnd - gapStart;
        if (gapHeight < minGap)
        {
            return false;
        }

        if (gapStart <= bounds.Y + bounds.Height * 0.05 || gapEnd >= bounds.Bottom - bounds.Height * 0.05)
        {
            return false;
        }

        if (BlockCrossesGap(blocks, gapStart, gapEnd, vertical: false))
        {
            return false;
        }

        gap = new PdfGap(gapStart, gapEnd);
        return true;
    }

    private static bool LineCrossesGap(IReadOnlyList<PdfGlyphLine> lines, double gapStart, double gapEnd, bool vertical)
    {
        var gapMid = (gapStart + gapEnd) / 2;
        var gapSize = gapEnd - gapStart;
        var tolerance = Math.Max(gapSize * 0.1, 2);
        for (var i = 0; i < lines.Count; i++)
        {
            var bounds = lines[i].Bounds;
            if (vertical)
            {
                if (bounds.X < gapMid - tolerance && bounds.Right > gapMid + tolerance)
                {
                    return true;
                }
            }
            else
            {
                if (bounds.Y < gapMid - tolerance && bounds.Bottom > gapMid + tolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool BlockCrossesGap(IReadOnlyList<FlowBlock> blocks, double gapStart, double gapEnd, bool vertical)
    {
        var gapMid = (gapStart + gapEnd) / 2;
        var gapSize = gapEnd - gapStart;
        var tolerance = Math.Max(gapSize * 0.1, 2);
        for (var i = 0; i < blocks.Count; i++)
        {
            var bounds = blocks[i].Bounds;
            if (vertical)
            {
                if (bounds.X < gapMid - tolerance && bounds.Right > gapMid + tolerance)
                {
                    return true;
                }
            }
            else
            {
                if (bounds.Y < gapMid - tolerance && bounds.Bottom > gapMid + tolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<PdfGlyphParagraphGroup> GroupGlyphLinesIntoParagraphs(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return new List<PdfGlyphParagraphGroup>();
        }

        var averageHeight = lines.Average(line => line.LineHeight > 0 ? line.LineHeight : line.Bounds.Height);
        var lineGapThreshold = Math.Max(averageHeight * 1.4, 4);
        var indentThreshold = Math.Max(averageHeight * 1.5, 6);

        var paragraphs = new List<PdfGlyphParagraphGroup>();
        PdfGlyphParagraphGroup? current = null;
        PdfGlyphLine? previous = null;
        var paragraphIndent = 0.0;
        ParagraphAlignment? paragraphAlignment = null;

        foreach (var line in lines)
        {
            if (current is null)
            {
                current = new PdfGlyphParagraphGroup();
                current.Add(line);
                paragraphs.Add(current);
                previous = line;
                paragraphIndent = line.Bounds.X;
                paragraphAlignment = line.Alignment;
                continue;
            }

            var baselineGap = previous is null ? 0 : previous.BaselineY - line.BaselineY;
            var indentGap = Math.Abs(line.Bounds.X - paragraphIndent);
            var alignmentChanged = paragraphAlignment.HasValue
                                   && line.Alignment.HasValue
                                   && paragraphAlignment != line.Alignment
                                   && (paragraphAlignment == ParagraphAlignment.Center
                                       || paragraphAlignment == ParagraphAlignment.Right
                                       || line.Alignment == ParagraphAlignment.Center
                                       || line.Alignment == ParagraphAlignment.Right);

            var hyphenContinuation = previous is not null
                                     && indentGap <= indentThreshold
                                     && LineEndsWithHyphen(previous)
                                     && LineStartsWithLowercase(line);

            if ((baselineGap > lineGapThreshold || indentGap > indentThreshold || alignmentChanged) && !hyphenContinuation)
            {
                current = new PdfGlyphParagraphGroup();
                current.Add(line);
                paragraphs.Add(current);
                paragraphIndent = line.Bounds.X;
                paragraphAlignment = line.Alignment;
            }
            else
            {
                current.Add(line);
            }

            previous = line;
        }

        return paragraphs;
    }

    private static void AppendParagraphLines(
        ParagraphBlock paragraph,
        PdfGlyphParagraphGroup group,
        PdfFootnotePageInfo? footnoteInfo)
    {
        if (group.Lines.Count == 0)
        {
            return;
        }

        var averageHeight = group.Lines.Average(line => line.LineHeight);
        var indentThreshold = Math.Max(averageHeight * 1.2, 4);

        for (var index = 0; index < group.Lines.Count; index++)
        {
            var line = group.Lines[index];
            if (index > 0)
            {
                var previous = group.Lines[index - 1];
                var indentGap = Math.Abs(line.Bounds.X - previous.Bounds.X);
                var mergeHyphen = indentGap <= indentThreshold
                                  && LineEndsWithHyphen(previous)
                                  && LineStartsWithLowercase(line);

                if (mergeHyphen)
                {
                    TryTrimParagraphEndHyphen(paragraph);
                }
                else
                {
                    paragraph.Inlines.Add(new RunInline(" "));
                }
            }

            string? marker = null;
            if (footnoteInfo is not null && footnoteInfo.MarkerToId.Count > 0)
            {
                if (!string.IsNullOrEmpty(line.TrailingFootnoteMarker)
                    && footnoteInfo.MarkerToId.ContainsKey(line.TrailingFootnoteMarker!))
                {
                    marker = line.TrailingFootnoteMarker;
                }
                else
                {
                    marker = FindMarkerSuffix(line.Text, footnoteInfo.MarkerToId.Keys);
                }
            }

            if (marker is not null)
            {
                TrimTrailingMarkerFromLine(line, marker);
            }

            AppendLineSpans(paragraph, line.Spans);

            if (marker is not null && footnoteInfo is not null && footnoteInfo.MarkerToId.TryGetValue(marker, out var footnoteId))
            {
                paragraph.Inlines.Add(new FootnoteReferenceInline(footnoteId));
            }
        }
    }

    private static void AppendLineSpans(ParagraphBlock paragraph, IReadOnlyList<PdfTextSpan> spans, bool useNonBreakingSpaces = false)
    {
        RunInline? lastRunInline = null;
        TextStyleProperties? lastStyle = null;

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            var text = useNonBreakingSpaces && span.Text.IndexOf(' ') >= 0
                ? span.Text.Replace(' ', '\u00A0')
                : span.Text;

            if (lastRunInline is not null && AreStylesEquivalent(lastStyle, span.Style))
            {
                lastRunInline.Text.Insert(lastRunInline.Text.Length, text);
                continue;
            }

            var inline = new RunInline(text, span.Style);
            paragraph.Inlines.Add(inline);
            lastRunInline = inline;
            lastStyle = span.Style;
        }
    }

    private static string? DetectTrailingFootnoteMarker(PdfGlyphLine line)
    {
        if (line.Glyphs.Count == 0)
        {
            return null;
        }

        var lineHeight = line.LineHeight > 0 ? line.LineHeight : line.Bounds.Height;
        var baseY = line.BaselineY;
        var digits = new List<char>();
        var index = line.Glyphs.Count - 1;
        while (index >= 0 && string.IsNullOrEmpty(line.Glyphs[index].Text))
        {
            index--;
        }

        while (index >= 0)
        {
            var glyph = line.Glyphs[index];
            if (string.IsNullOrEmpty(glyph.Text))
            {
                index--;
                continue;
            }

            var ch = glyph.Text[0];
            if (!char.IsDigit(ch))
            {
                break;
            }

            var glyphHeight = glyph.FontSize > 0 ? glyph.FontSize : glyph.Bounds.Height;
            var sizeOk = glyphHeight <= lineHeight * 0.8;
            var raised = glyph.BaselineY > baseY + lineHeight * 0.1;
            if (!sizeOk && !raised)
            {
                break;
            }

            digits.Add(ch);
            index--;
        }

        if (digits.Count == 0)
        {
            return null;
        }

        digits.Reverse();
        return new string(digits.ToArray());
    }

    private static string? FindMarkerSuffix(string text, IEnumerable<string> markers)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
        {
            return null;
        }

        string? best = null;
        foreach (var marker in markers)
        {
            if (string.IsNullOrEmpty(marker))
            {
                continue;
            }

            if (!trimmed.EndsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            if (best is null || marker.Length > best.Length)
            {
                best = marker;
            }
        }

        return best;
    }

    private static void TrimTrailingMarkerFromLine(PdfGlyphLine line, string marker)
    {
        if (string.IsNullOrEmpty(marker) || line.Spans.Count == 0)
        {
            return;
        }

        if (!line.Text.EndsWith(marker, StringComparison.Ordinal))
        {
            return;
        }

        var remaining = marker.Length;
        for (var i = line.Spans.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var span = line.Spans[i];
            var text = span.Text;
            if (text.Length <= remaining)
            {
                remaining -= text.Length;
                line.Spans.RemoveAt(i);
                continue;
            }

            var trimmed = text.Substring(0, text.Length - remaining).TrimEnd();
            line.Spans[i] = new PdfTextSpan(trimmed, span.Style, span.Bounds);
            remaining = 0;
        }

        if (line.Spans.Count == 0)
        {
            line.Text = string.Empty;
        }
        else
        {
            var builder = new StringBuilder();
            for (var i = 0; i < line.Spans.Count; i++)
            {
                builder.Append(line.Spans[i].Text);
            }

            line.Text = builder.ToString();
        }
    }

    private static bool LineEndsWithHyphen(PdfGlyphLine line)
    {
        var text = line.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return IsHyphenChar(trimmed[^1]);
    }

    private static bool LineStartsWithLowercase(PdfGlyphLine line)
    {
        var text = line.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return char.IsLower(trimmed[0]);
    }

    private static bool TryTrimParagraphEndHyphen(ParagraphBlock paragraph)
    {
        for (var index = paragraph.Inlines.Count - 1; index >= 0; index--)
        {
            if (paragraph.Inlines[index] is not RunInline run)
            {
                continue;
            }

            var text = run.GetText();
            var trimmed = text.TrimEnd();
            if (trimmed.Length == 0)
            {
                paragraph.Inlines.RemoveAt(index);
                continue;
            }

            if (!IsHyphenChar(trimmed[^1]))
            {
                return false;
            }

            var updated = trimmed[..^1];
            updated = updated.TrimEnd();
            if (updated.Length == 0)
            {
                paragraph.Inlines.RemoveAt(index);
            }
            else
            {
                paragraph.Inlines[index] = new RunInline(updated, run.Style)
                {
                    Hyperlink = run.Hyperlink
                };
            }

            return true;
        }

        return false;
    }

    private static bool IsHyphenChar(char value)
        => value == '-'
           || value == '‐'
           || value == '‑'
           || value == '–';

    private static TextStyleProperties? BuildGlyphStyle(PdfTextGlyph glyph)
    {
        if (glyph.Font is null && glyph.FontSize <= 0)
        {
            return null;
        }

        var style = new TextStyleProperties();
        if (glyph.Font is not null)
        {
            style.FontFamily = glyph.Font.Name;
            style.FontFamilyAscii = glyph.Font.Name;
            style.FontFamilyHighAnsi = glyph.Font.Name;
            style.FontWeight = glyph.Font.IsBold ? DocFontWeight.Bold : null;
            style.FontStyle = glyph.Font.IsItalic ? DocFontStyle.Italic : null;
        }

        if (glyph.FontSize > 0)
        {
            style.FontSize = PdfUnits.PointsToDip(glyph.FontSize);
        }

        if (glyph.Color is { } color)
        {
            style.Color = new DocColor(color.R, color.G, color.B, color.A);
        }

        return style.HasValues ? style : null;
    }

    private static void ApplyHeadingHeuristics(ParagraphBlock paragraph, PdfGlyphParagraphGroup group, double baselineFontSize)
    {
        if (baselineFontSize <= 0 || group.Lines.Count == 0)
        {
            return;
        }

        var glyphs = group.Lines.SelectMany(line => line.Glyphs).ToList();
        if (glyphs.Count == 0)
        {
            return;
        }

        var averageSize = glyphs.Average(glyph => glyph.FontSize > 0 ? glyph.FontSize : baselineFontSize);
        var boldRatio = glyphs.Count == 0 ? 0 : glyphs.Count(glyph => glyph.Font?.IsBold == true) / (double)glyphs.Count;

        if (averageSize >= baselineFontSize * 1.6)
        {
            paragraph.StyleId = "Heading1";
        }
        else if (averageSize >= baselineFontSize * 1.35 || (boldRatio > 0.6 && averageSize >= baselineFontSize * 1.15))
        {
            paragraph.StyleId = "Heading2";
        }
        else if (averageSize >= baselineFontSize * 1.2 || (boldRatio > 0.5 && averageSize >= baselineFontSize * 1.05))
        {
            paragraph.StyleId = "Heading3";
        }
    }

    private static void ApplyListHeuristics(
        Document document,
        ParagraphBlock paragraph,
        PdfGlyphParagraphGroup group,
        ListInferenceState state,
        double baselineFontSize)
    {
        var text = ExtractParagraphText(paragraph);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!TryDetectListPrefix(text, out var kind, out var prefixLength, out var prefixLevel))
        {
            state.Reset();
            return;
        }

        var indent = group.Bounds.X;
        var indentThreshold = ResolveListIndentThreshold(group, baselineFontSize);
        var level = state.ResolveLevel(kind, indent, indentThreshold, prefixLevel);

        EnsureListDefinition(document, kind, level > 0 || prefixLevel > 1, out var listId);
        paragraph.ListInfo = new ListInfo(kind, level, listId);
        StripPrefixFromParagraph(paragraph, prefixLength);
    }

    private static PdfHeaderFooterInfo DetectHeaderFooter(PdfDocumentAst pdf)
    {
        var info = new PdfHeaderFooterInfo();
        if (pdf.Pages.Count < 2)
        {
            return info;
        }

        var minPages = Math.Max(2, (int)Math.Ceiling(pdf.Pages.Count * 0.5));
        var headerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var footerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var heightSamples = new List<double>();
        var topMargins = new List<double>();
        var bottomMargins = new List<double>();
        var pageLines = new List<(PdfPageAst Page, List<PdfGlyphLine> Lines)>();

        foreach (var page in pdf.Pages)
        {
            if (page.Glyphs.Count == 0)
            {
                continue;
            }

            var lines = GroupGlyphsIntoLines(page.Glyphs);
            if (lines.Count == 0)
            {
                continue;
            }

            AssignGlyphLineAlignment(lines);
            pageLines.Add((page, lines));
            var maxBottom = lines.Max(line => line.Bounds.Bottom);
            var minTop = lines.Min(line => line.Bounds.Y);
            var topMargin = Math.Max(0, page.Height - maxBottom);
            var bottomMargin = Math.Max(0, minTop);
            if (topMargin > 0)
            {
                topMargins.Add(topMargin);
            }

            if (bottomMargin > 0)
            {
                bottomMargins.Add(bottomMargin);
            }
            for (var index = 0; index < lines.Count; index++)
            {
                var lineHeight = lines[index].LineHeight > 0 ? lines[index].LineHeight : lines[index].Bounds.Height;
                if (lineHeight > 0)
                {
                    heightSamples.Add(lineHeight);
                }
            }
        }

        var medianHeight = heightSamples.Count > 0 ? Median(heightSamples) : 10;
        info.MedianLineHeight = medianHeight;
        info.MedianTopMargin = topMargins.Count > 0 ? Median(topMargins) : 0;
        info.MedianBottomMargin = bottomMargins.Count > 0 ? Median(bottomMargins) : 0;
        var anchorTolerance = ResolveHeaderFooterAnchorTolerance(medianHeight);

        foreach (var (page, lines) in pageLines)
        {
            var headerBand = ResolveHeaderFooterBand(page.Height, medianHeight, info.MedianTopMargin);
            var footerBand = ResolveHeaderFooterBand(page.Height, medianHeight, info.MedianBottomMargin);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                var key = BuildHeaderFooterKey(line, anchorTolerance);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (line.Bounds.Bottom >= page.Height - headerBand)
                {
                    IncrementCount(headerCounts, key);
                    info.HeaderSamples.TryAdd(key, line);
                }

                if (line.Bounds.Y <= footerBand)
                {
                    IncrementCount(footerCounts, key);
                    info.FooterSamples.TryAdd(key, line);
                }
            }
        }

        foreach (var entry in headerCounts)
        {
            if (entry.Value >= minPages)
            {
                info.HeaderKeys.Add(entry.Key);
            }
        }

        foreach (var entry in footerCounts)
        {
            if (entry.Value >= minPages)
            {
                info.FooterKeys.Add(entry.Key);
            }
        }

        return info;
    }

    private static void ApplyHeaderFooterBlocks(Document document, PdfHeaderFooterInfo info)
    {
        if (info.HasHeaders)
        {
            document.Header.Blocks.Clear();
            var headerLines = info.HeaderSamples.Values
                .OrderByDescending(line => line.BaselineY)
                .ThenBy(line => line.Bounds.X)
                .ToList();
            foreach (var line in headerLines)
            {
                var paragraph = new ParagraphBlock();
                AppendLineSpans(paragraph, line.Spans);
                paragraph.Text = string.Empty;
                document.Header.Blocks.Add(paragraph);
            }
        }

        if (info.HasFooters)
        {
            document.Footer.Blocks.Clear();
            var footerLines = info.FooterSamples.Values
                .OrderByDescending(line => line.BaselineY)
                .ThenBy(line => line.Bounds.X)
                .ToList();
            foreach (var line in footerLines)
            {
                var paragraph = new ParagraphBlock();
                AppendLineSpans(paragraph, line.Spans);
                paragraph.Text = string.Empty;
                document.Footer.Blocks.Add(paragraph);
            }
        }
    }

    private static List<PdfGlyphLine> FilterHeaderFooterLines(
        IReadOnlyList<PdfGlyphLine> lines,
        PdfPageAst page,
        PdfHeaderFooterInfo info)
    {
        if (!info.HasHeaders && !info.HasFooters)
        {
            return lines.ToList();
        }

        var result = new List<PdfGlyphLine>(lines.Count);
        var medianHeight = info.MedianLineHeight > 0 ? info.MedianLineHeight : ResolveMedianLineHeight(lines);
        var headerBand = ResolveHeaderFooterBand(page.Height, medianHeight, info.MedianTopMargin);
        var footerBand = ResolveHeaderFooterBand(page.Height, medianHeight, info.MedianBottomMargin);
        var anchorTolerance = ResolveHeaderFooterAnchorTolerance(medianHeight);
        foreach (var line in lines)
        {
            var key = BuildHeaderFooterKey(line, anchorTolerance);
            var isHeader = info.HasHeaders
                           && line.Bounds.Bottom >= page.Height - headerBand
                           && info.HeaderKeys.Contains(key);
            var isFooter = info.HasFooters
                           && line.Bounds.Y <= footerBand
                           && info.FooterKeys.Contains(key);
            if (isHeader || isFooter)
            {
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    private static double ResolveHeaderFooterBand(double pageHeight, double medianLineHeight, double? margin)
    {
        var minBand = Math.Max(medianLineHeight * 2.5, pageHeight * 0.03);
        var targetBand = Math.Max(medianLineHeight * 5.0, pageHeight * 0.04);
        var maxBand = pageHeight * 0.12;
        var band = Math.Clamp(targetBand, minBand, maxBand);
        if (margin.HasValue && margin.Value > 0)
        {
            var marginBand = margin.Value + medianLineHeight * 0.5;
            band = Math.Min(band, Math.Max(minBand, marginBand));
        }

        return band;
    }

    private static double ResolveHeaderFooterAnchorTolerance(double medianLineHeight)
        => Math.Max(medianLineHeight * 0.75, 4);

    private static double ResolveMedianLineHeight(IReadOnlyList<PdfGlyphLine> lines)
    {
        if (lines.Count == 0)
        {
            return 10;
        }

        var heights = new List<double>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineHeight = line.LineHeight > 0 ? line.LineHeight : line.Bounds.Height;
            if (lineHeight > 0)
            {
                heights.Add(lineHeight);
            }
        }

        return heights.Count > 0 ? Median(heights) : 10;
    }

    private static string BuildHeaderFooterKey(PdfGlyphLine line, double anchorTolerance)
    {
        var normalized = NormalizeHeaderFooterText(line.Text);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        var alignment = line.Alignment?.ToString() ?? "Unknown";
        var anchorX = line.Alignment switch
        {
            ParagraphAlignment.Right => line.Bounds.Right,
            ParagraphAlignment.Center => (line.Bounds.X + line.Bounds.Right) * 0.5,
            _ => line.Bounds.X
        };

        var bucket = (int)Math.Round(anchorX / Math.Max(anchorTolerance, 1), MidpointRounding.AwayFromZero);
        return $"{alignment}:{bucket}:{normalized}";
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key)
    {
        if (counts.TryGetValue(key, out var count))
        {
            counts[key] = count + 1;
        }
        else
        {
            counts[key] = 1;
        }
    }

    private static string NormalizeHeaderFooterText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var wasSpace = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsDigit(ch))
            {
                if (!wasSpace)
                {
                    builder.Append(' ');
                    wasSpace = true;
                }
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!wasSpace)
                {
                    builder.Append(' ');
                    wasSpace = true;
                }
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            wasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static PdfFootnotePageInfo? DetectFootnotesForPage(
        Document document,
        PdfFootnoteState footnoteState,
        PdfPageAst page,
        IReadOnlyList<PdfGlyphLine> lines)
    {
        if (!TryFindFootnoteSeparator(page, out var separatorY))
        {
            return null;
        }

        var footnoteLines = new List<PdfGlyphLine>();
        foreach (var line in lines)
        {
            if (line.Bounds.Bottom <= separatorY)
            {
                footnoteLines.Add(line);
            }
        }

        if (footnoteLines.Count == 0)
        {
            return null;
        }

        var info = new PdfFootnotePageInfo { SeparatorY = separatorY };
        info.Lines.AddRange(footnoteLines);

        var ordered = footnoteLines
            .OrderByDescending(line => line.BaselineY)
            .ThenBy(line => line.Bounds.X)
            .ToList();
        var groups = GroupGlyphLinesIntoParagraphs(ordered);
        foreach (var group in groups)
        {
            if (group.Lines.Count == 0)
            {
                continue;
            }

            var paragraph = new ParagraphBlock();
            AppendParagraphLines(paragraph, group, null);
            paragraph.Text = string.Empty;

            var marker = TryExtractFootnotePrefix(paragraph, out var prefixLength)
                ? ExtractMarkerText(paragraph, prefixLength)
                : null;
            if (!string.IsNullOrEmpty(marker))
            {
                StripPrefixFromParagraph(paragraph, prefixLength);
            }

            var footnoteId = footnoteState.NextId++;
            if (!string.IsNullOrEmpty(marker))
            {
                info.MarkerToId[marker] = footnoteId;
            }

            var definition = new FootnoteDefinition(footnoteId);
            definition.Blocks.Add(paragraph);
            document.Footnotes[footnoteId] = definition;
        }

        if (document.SectionProperties.Footnotes is null)
        {
            document.SectionProperties.Footnotes = new FootnoteSettings
            {
                Position = FootnotePosition.PageBottom
            };
        }

        return info;
    }

    private static List<PdfGlyphLine> RemoveFootnoteLines(IReadOnlyList<PdfGlyphLine> lines, PdfFootnotePageInfo? footnoteInfo)
    {
        if (footnoteInfo is null || footnoteInfo.Lines.Count == 0)
        {
            return lines.ToList();
        }

        var footnoteSet = new HashSet<PdfGlyphLine>(footnoteInfo.Lines);
        var result = new List<PdfGlyphLine>(lines.Count);
        foreach (var line in lines)
        {
            if (!footnoteSet.Contains(line))
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static bool TryFindFootnoteSeparator(PdfPageAst page, out double separatorY)
    {
        separatorY = 0;
        if (page.Paths.Count == 0)
        {
            return false;
        }

        var segments = ExtractLineSegments(page.Paths);
        var minWidth = page.Width * 0.2;
        var maxY = page.Height * 0.35;
        var candidates = new List<double>();
        foreach (var segment in segments)
        {
            if (!segment.IsHorizontal || segment.Length < minWidth)
            {
                continue;
            }

            var y = (segment.Y1 + segment.Y2) / 2;
            if (y <= maxY)
            {
                candidates.Add(y);
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        separatorY = candidates.Max();
        return true;
    }

    private static bool TryExtractFootnotePrefix(ParagraphBlock paragraph, out int prefixLength)
    {
        prefixLength = 0;
        var text = ExtractParagraphText(paragraph);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Regex.Match(text, @"^\s*(?<marker>\d+)[\.\)]?\s+");
        if (!match.Success)
        {
            return false;
        }

        prefixLength = match.Length;
        return true;
    }

    private static string? ExtractMarkerText(ParagraphBlock paragraph, int prefixLength)
    {
        if (prefixLength <= 0)
        {
            return null;
        }

        var text = ExtractParagraphText(paragraph);
        if (text.Length < prefixLength)
        {
            return null;
        }

        var prefix = text.Substring(0, prefixLength);
        var digits = new string(prefix.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? null : digits;
    }

    private static List<PdfTableRegion> DetectRuledTables(PdfPageAst page, IReadOnlyList<PdfGlyphLine> lines)
    {
        if (page.Paths.Count == 0 || lines.Count == 0)
        {
            return new List<PdfTableRegion>();
        }

        var segments = ExtractLineSegments(page.Paths);
        if (segments.Count == 0)
        {
            return new List<PdfTableRegion>();
        }

        var clusters = ClusterSegments(segments, page.Width * 0.02);
        var tables = new List<PdfTableRegion>();
        foreach (var cluster in clusters)
        {
            var vertical = new List<double>();
            var horizontal = new List<double>();
            foreach (var segment in cluster.Segments)
            {
                if (segment.IsVertical)
                {
                    vertical.Add(segment.X1);
                }
                else if (segment.IsHorizontal)
                {
                    horizontal.Add(segment.Y1);
                }
            }

            var columnX = MergePositions(vertical, 1.5);
            var rowY = MergePositions(horizontal, 1.5);
            if (columnX.Count < 2 || rowY.Count < 2)
            {
                continue;
            }

            var bounds = new PdfRect(columnX.Min(), rowY.Min(), columnX.Max() - columnX.Min(), rowY.Max() - rowY.Min());
            var region = new PdfTableRegion(bounds, columnX, rowY);

            AssignLinesToTable(region, lines);
            if (region.CellLines.Count == 0)
            {
                continue;
            }

            tables.Add(region);
        }

        if (tables.Count == 0 && TryBuildTableRegion(segments, lines, out var fallback))
        {
            tables.Add(fallback);
        }

        return tables;
    }

    private static HashSet<PdfGlyphLine> CollectTableLines(IReadOnlyList<PdfTableRegion> tables)
    {
        var set = new HashSet<PdfGlyphLine>();
        foreach (var table in tables)
        {
            foreach (var cell in table.CellLines.Values)
            {
                foreach (var line in cell)
                {
                    set.Add(line);
                }
            }
        }

        return set;
    }

    private static TableBlock BuildTableBlock(PdfTableRegion region)
    {
        var columns = region.ColumnX.OrderBy(value => value).ToList();
        var rows = region.RowY.OrderByDescending(value => value).ToList();
        var table = new TableBlock();

        for (var col = 0; col < columns.Count - 1; col++)
        {
            var width = PdfUnits.PointsToDip(columns[col + 1] - columns[col]);
            table.Properties.ColumnWidths.Add((float)Math.Max(1, width));
        }

        for (var rowIndex = 0; rowIndex < rows.Count - 1; rowIndex++)
        {
            var row = new TableRow();
            for (var colIndex = 0; colIndex < columns.Count - 1; colIndex++)
            {
                var cell = new TableCell();
                if (region.CellLines.TryGetValue((rowIndex, colIndex), out var cellLines) && cellLines.Count > 0)
                {
                    var ordered = cellLines
                        .OrderByDescending(line => line.BaselineY)
                        .ThenBy(line => line.Bounds.X)
                        .ToList();
                    var groups = GroupGlyphLinesIntoParagraphs(ordered);
                    foreach (var group in groups)
                    {
                        if (group.Lines.Count == 0)
                        {
                            continue;
                        }

                        var paragraph = new ParagraphBlock();
                        AppendParagraphLines(paragraph, group, null);
                        paragraph.Text = string.Empty;
                        cell.Blocks.Add(paragraph);
                    }
                }

                if (cell.Blocks.Count == 0)
                {
                    cell.Blocks.Add(new ParagraphBlock());
                }

                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        table.Properties.LayoutMode = TableLayoutMode.Fixed;
        return table;
    }

    private static bool TryBuildTableRegion(
        IReadOnlyList<PdfLineSegment> segments,
        IReadOnlyList<PdfGlyphLine> lines,
        out PdfTableRegion region)
    {
        region = null!;
        var vertical = new List<double>();
        var horizontal = new List<double>();
        foreach (var segment in segments)
        {
            if (segment.IsVertical)
            {
                vertical.Add(segment.X1);
            }
            else if (segment.IsHorizontal)
            {
                horizontal.Add(segment.Y1);
            }
        }

        var columnX = MergePositions(vertical, 1.5);
        var rowY = MergePositions(horizontal, 1.5);
        if (columnX.Count < 2 || rowY.Count < 2)
        {
            return false;
        }

        var bounds = new PdfRect(columnX.Min(), rowY.Min(), columnX.Max() - columnX.Min(), rowY.Max() - rowY.Min());
        region = new PdfTableRegion(bounds, columnX, rowY);
        AssignLinesToTable(region, lines);
        return region.CellLines.Count > 0;
    }

    private static void AssignLinesToTable(PdfTableRegion region, IReadOnlyList<PdfGlyphLine> lines)
    {
        var columns = region.ColumnX.OrderBy(value => value).ToList();
        var rows = region.RowY.OrderByDescending(value => value).ToList();
        if (columns.Count < 2 || rows.Count < 2)
        {
            return;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var centerX = (line.Bounds.X + line.Bounds.Right) / 2;
            var centerY = (line.Bounds.Y + line.Bounds.Bottom) / 2;
            var colIndex = FindInterval(columns, centerX);
            var rowIndex = FindInterval(rows, centerY, descending: true);
            if (colIndex < 0 || rowIndex < 0)
            {
                continue;
            }

            var key = (rowIndex, colIndex);
            if (!region.CellLines.TryGetValue(key, out var list))
            {
                list = new List<PdfGlyphLine>();
                region.CellLines[key] = list;
            }

            list.Add(line);
        }
    }

    private static int FindInterval(IReadOnlyList<double> positions, double value, bool descending = false)
    {
        if (positions.Count < 2)
        {
            return -1;
        }

        if (descending)
        {
            for (var i = 0; i < positions.Count - 1; i++)
            {
                var top = positions[i];
                var bottom = positions[i + 1];
                if (value <= top && value >= bottom)
                {
                    return i;
                }
            }

            return -1;
        }

        for (var i = 0; i < positions.Count - 1; i++)
        {
            var left = positions[i];
            var right = positions[i + 1];
            if (value >= left && value <= right)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<PdfLineSegment> ExtractLineSegments(IReadOnlyList<PdfPathObject> paths)
    {
        var segments = new List<PdfLineSegment>();
        const double tolerance = 1.0;
        foreach (var path in paths)
        {
            if (path.Segments.Count == 0)
            {
                continue;
            }

            var hasStart = false;
            var startX = 0.0;
            var startY = 0.0;
            var currentX = 0.0;
            var currentY = 0.0;
            foreach (var segment in path.Segments)
            {
                switch (segment.Kind)
                {
                    case PdfPathSegmentKind.MoveTo:
                        startX = segment.X1;
                        startY = segment.Y1;
                        currentX = startX;
                        currentY = startY;
                        hasStart = true;
                        break;
                    case PdfPathSegmentKind.LineTo:
                        if (!hasStart)
                        {
                            break;
                        }

                        AddSegment(currentX, currentY, segment.X1, segment.Y1);
                        currentX = segment.X1;
                        currentY = segment.Y1;
                        break;
                    case PdfPathSegmentKind.CubicTo:
                        if (!hasStart)
                        {
                            break;
                        }

                        currentX = segment.X3;
                        currentY = segment.Y3;
                        break;
                    case PdfPathSegmentKind.Close:
                        if (!hasStart)
                        {
                            break;
                        }

                        AddSegment(currentX, currentY, startX, startY);
                        currentX = startX;
                        currentY = startY;
                        break;
                }
            }
        }

        return segments;

        void AddSegment(double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.1)
            {
                return;
            }

            var horizontal = Math.Abs(dy) <= tolerance;
            var vertical = Math.Abs(dx) <= tolerance;
            if (!horizontal && !vertical)
            {
                return;
            }

            segments.Add(new PdfLineSegment(x1, y1, x2, y2, horizontal, vertical, length));
        }
    }

    private static List<PdfLineCluster> ClusterSegments(IReadOnlyList<PdfLineSegment> segments, double tolerance)
    {
        var clusters = new List<PdfLineCluster>();
        foreach (var segment in segments)
        {
            PdfLineCluster? target = null;
            foreach (var cluster in clusters)
            {
                var expanded = ExpandRect(cluster.Bounds, tolerance);
                if (Intersects(expanded, segment.Bounds))
                {
                    target = cluster;
                    break;
                }
            }

            if (target is null)
            {
                target = new PdfLineCluster();
                clusters.Add(target);
            }

            target.Add(segment);
        }

        return clusters;
    }

    private static PdfRect ExpandRect(PdfRect rect, double amount)
    {
        return new PdfRect(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);
    }

    private static bool Intersects(PdfRect a, PdfRect b)
    {
        return a.X <= b.Right && a.Right >= b.X && a.Y <= b.Bottom && a.Bottom >= b.Y;
    }

    private static List<double> MergePositions(IReadOnlyList<double> positions, double tolerance)
    {
        if (positions.Count == 0)
        {
            return new List<double>();
        }

        var sorted = positions.OrderBy(value => value).ToList();
        var merged = new List<double> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var value = sorted[i];
            var last = merged[^1];
            if (Math.Abs(value - last) <= tolerance)
            {
                merged[^1] = (last + value) / 2;
            }
            else
            {
                merged.Add(value);
            }
        }

        return merged;
    }

    private static void ApplyHeadingHeuristics(ParagraphBlock paragraph, PdfParagraphGroup group, double baselineFontSize)
    {
        if (baselineFontSize <= 0 || group.Lines.Count == 0)
        {
            return;
        }

        var runs = group.Lines.SelectMany(line => line.Runs).ToList();
        if (runs.Count == 0)
        {
            return;
        }

        var averageSize = runs.Average(run => run.FontSize > 0 ? run.FontSize : baselineFontSize);
        var boldRatio = runs.Count == 0 ? 0 : runs.Count(run => run.Font?.IsBold == true) / (double)runs.Count;

        if (averageSize >= baselineFontSize * 1.6)
        {
            paragraph.StyleId = "Heading1";
        }
        else if (averageSize >= baselineFontSize * 1.35 || (boldRatio > 0.6 && averageSize >= baselineFontSize * 1.15))
        {
            paragraph.StyleId = "Heading2";
        }
        else if (averageSize >= baselineFontSize * 1.2 || (boldRatio > 0.5 && averageSize >= baselineFontSize * 1.05))
        {
            paragraph.StyleId = "Heading3";
        }
    }

    private static void ApplyListHeuristics(
        Document document,
        ParagraphBlock paragraph,
        PdfParagraphGroup group,
        ListInferenceState state,
        double baselineFontSize)
    {
        var text = ExtractParagraphText(paragraph);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!TryDetectListPrefix(text, out var kind, out var prefixLength, out var prefixLevel))
        {
            state.Reset();
            return;
        }

        var indent = group.Bounds.X;
        var indentThreshold = ResolveListIndentThreshold(group, baselineFontSize);
        var level = state.ResolveLevel(kind, indent, indentThreshold, prefixLevel);

        EnsureListDefinition(document, kind, level > 0 || prefixLevel > 1, out var listId);
        paragraph.ListInfo = new ListInfo(kind, level, listId);
        StripPrefixFromParagraph(paragraph, prefixLength);
    }

    private static string ExtractParagraphText(ParagraphBlock paragraph)
    {
        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                builder.Append(run.GetText());
            }
        }

        return builder.ToString();
    }

    private static bool TryDetectListPrefix(string text, out ListKind kind, out int prefixLength, out int level)
    {
        kind = ListKind.Bullet;
        prefixLength = 0;
        level = 0;
        var span = text.AsSpan();
        var index = 0;
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }

        if (index >= span.Length)
        {
            return false;
        }

        var bulletChars = new[] { '•', '‣', '∙', '▪', '-', '*', '–', '—', '·', 'o' };
        if (bulletChars.Contains(span[index]))
        {
            var start = index;
            index++;
            if (index < span.Length && char.IsWhiteSpace(span[index]))
            {
                while (index < span.Length && char.IsWhiteSpace(span[index]))
                {
                    index++;
                }

                kind = ListKind.Bullet;
                prefixLength = index;
                level = 1;
                return true;
            }
        }

        var numberMatch = Regex.Match(text, @"^\s*(?<prefix>(?<seg>\d+|[A-Za-z]|[ivxlcdmIVXLCDM]+)(?:\.(?<seg>\d+|[A-Za-z]|[ivxlcdmIVXLCDM]+))*)(?<suffix>[\.\)])?\s+");
        if (numberMatch.Success)
        {
            var segmentCount = numberMatch.Groups["seg"].Captures.Count;
            var suffix = numberMatch.Groups["suffix"].Value;
            if (segmentCount == 1 && string.IsNullOrWhiteSpace(suffix))
            {
                return false;
            }

            kind = ListKind.Numbered;
            prefixLength = numberMatch.Length;
            level = Math.Max(1, segmentCount);
            return true;
        }

        return false;
    }

    private static void EnsureListDefinition(Document document, ListKind kind, bool multilevel, out int listId)
    {
        listId = kind == ListKind.Bullet ? 1 : 2;
        if (document.ListDefinitions.ContainsKey(listId))
        {
            return;
        }

        var definition = kind == ListKind.Bullet
            ? ListDefinitionDefaults.CreateBulleted(listId)
            : ListDefinitionDefaults.CreateNumbered(listId, multilevel);
        document.ListDefinitions[listId] = definition;
    }

    private static double ResolveListIndentThreshold(PdfParagraphGroup group, double baselineFontSize)
    {
        if (group.Lines.Count == 0)
        {
            return Math.Max(baselineFontSize * 0.8, 6);
        }

        var averageHeight = group.Lines.Average(ResolveLineHeight);
        return Math.Max(averageHeight * 0.6, 6);
    }

    private static double ResolveListIndentThreshold(PdfGlyphParagraphGroup group, double baselineFontSize)
    {
        if (group.Lines.Count == 0)
        {
            return Math.Max(baselineFontSize * 0.8, 6);
        }

        var averageHeight = group.Lines.Average(line => line.LineHeight);
        return Math.Max(averageHeight * 0.6, 6);
    }

    private sealed class ListInferenceState
    {
        private readonly List<double> _indentLevels = new();
        private ListKind? _currentKind;

        public void Reset()
        {
            _indentLevels.Clear();
            _currentKind = null;
        }

        public int ResolveLevel(ListKind kind, double indent, double indentThreshold, int prefixLevel)
        {
            if (_currentKind != kind || _indentLevels.Count == 0)
            {
                _indentLevels.Clear();
                _indentLevels.Add(indent);
                _currentKind = kind;
                return Math.Max(0, prefixLevel - 1);
            }

            if (prefixLevel > 0)
            {
                var level = Math.Max(0, prefixLevel - 1);
                EnsureIndentLevels(level, indent);
                return level;
            }

            var currentLevel = _indentLevels.Count - 1;
            if (indent > _indentLevels[currentLevel] + indentThreshold)
            {
                _indentLevels.Add(indent);
                return _indentLevels.Count - 1;
            }

            while (currentLevel > 0 && indent < _indentLevels[currentLevel] - indentThreshold)
            {
                currentLevel--;
            }

            if (currentLevel == 0 && Math.Abs(indent - _indentLevels[0]) > indentThreshold)
            {
                _indentLevels[0] = indent;
                return 0;
            }

            _indentLevels[currentLevel] = (_indentLevels[currentLevel] + indent) / 2;
            if (currentLevel < _indentLevels.Count - 1)
            {
                _indentLevels.RemoveRange(currentLevel + 1, _indentLevels.Count - currentLevel - 1);
            }

            return currentLevel;
        }

        private void EnsureIndentLevels(int level, double indent)
        {
            if (level < 0)
            {
                return;
            }

            while (_indentLevels.Count <= level)
            {
                _indentLevels.Add(indent);
            }

            _indentLevels[level] = (_indentLevels[level] + indent) / 2;
            if (level < _indentLevels.Count - 1)
            {
                _indentLevels.RemoveRange(level + 1, _indentLevels.Count - level - 1);
            }
        }
    }

    private static void StripPrefixFromParagraph(ParagraphBlock paragraph, int prefixLength)
    {
        if (prefixLength <= 0)
        {
            return;
        }

        var remaining = prefixLength;
        for (var index = 0; index < paragraph.Inlines.Count && remaining > 0;)
        {
            if (paragraph.Inlines[index] is not RunInline run)
            {
                index++;
                continue;
            }

            var runText = run.GetText();
            if (remaining >= runText.Length)
            {
                remaining -= runText.Length;
                paragraph.Inlines.RemoveAt(index);
                continue;
            }

            var trimmed = runText.Substring(remaining);
            var replacement = new RunInline(trimmed, run.Style)
            {
                Hyperlink = run.Hyperlink
            };
            paragraph.Inlines[index] = replacement;
            remaining = 0;
            break;
        }
    }

    private static List<PdfColumnGroup> SplitColumnIntoRegions(PdfColumnGroup column)
    {
        var lines = column.Lines;
        if (lines.Count == 0)
        {
            return new List<PdfColumnGroup>();
        }

        var ordered = lines.OrderByDescending(line => line.CenterY).ToList();
        var averageHeight = ordered.Average(ResolveLineHeight);
        var gapThreshold = Math.Max(averageHeight * 1.8, 8);

        var regions = new List<PdfColumnGroup>();
        PdfColumnGroup? current = null;
        PdfLineGroup? previous = null;

        foreach (var line in ordered)
        {
            if (current is null || (previous is not null && (previous.CenterY - line.CenterY) > gapThreshold))
            {
                current = new PdfColumnGroup(line, isSpanning: false)
                {
                    ColumnIndex = column.ColumnIndex
                };
                regions.Add(current);
            }
            else
            {
                current.Add(line);
            }

            previous = line;
        }

        foreach (var region in regions)
        {
            region.Sort();
        }

        return regions;
    }

    private static double ResolveColumnTolerance(IReadOnlyList<PdfLineGroup> lines)
    {
        if (lines.Count == 0)
        {
            return 12;
        }

        var averageHeight = lines.Average(ResolveLineHeight);
        return Math.Max(averageHeight * 2.5, 12);
    }

    private static List<PdfParagraphGroup> GroupColumnIntoParagraphs(PdfColumnGroup column)
    {
        var lines = column.Lines;
        if (lines.Count == 0)
        {
            return new List<PdfParagraphGroup>();
        }

        var averageHeight = lines.Average(ResolveLineHeight);
        var lineGapThreshold = Math.Max(averageHeight * 1.35, 4);
        var indentThreshold = Math.Max(averageHeight * 1.5, 6);

        var paragraphs = new List<PdfParagraphGroup>();
        PdfParagraphGroup? current = null;
        PdfLineGroup? previous = null;
        var paragraphIndent = 0.0;

        foreach (var line in lines)
        {
            if (current is null)
            {
                current = new PdfParagraphGroup();
                current.Add(line);
                paragraphs.Add(current);
                previous = line;
                paragraphIndent = line.Bounds.X;
                continue;
            }

            var baselineGap = previous is null ? 0 : previous.CenterY - line.CenterY;
            var indentGap = Math.Abs(line.Bounds.X - paragraphIndent);

            if (baselineGap > lineGapThreshold || indentGap > indentThreshold)
            {
                current = new PdfParagraphGroup();
                current.Add(line);
                paragraphs.Add(current);
                paragraphIndent = line.Bounds.X;
            }
            else
            {
                current.Add(line);
            }

            previous = line;
        }

        return paragraphs;
    }

    private static double ResolveLineTolerance(IReadOnlyList<PdfTextRun> runs)
    {
        if (runs.Count == 0)
        {
            return 2.0;
        }

        var averageHeight = runs.Average(run => run.FontSize > 0 ? run.FontSize : run.Bounds.Height);
        return Math.Max(averageHeight * 0.6, 2.0);
    }

    private static double ResolveRunBaseline(PdfTextRun run)
    {
        if (double.IsFinite(run.BaselineY) && run.BaselineY > 0)
        {
            return run.BaselineY;
        }

        return run.Bounds.Y + run.Bounds.Height / 2;
    }

    private sealed class TextRunLayout
    {
        public List<FloatingObject> Objects { get; } = new();
        public Dictionary<int, FloatingObject> RunMap { get; } = new();
    }

    private readonly record struct GlyphLineLayout(PdfGlyphLine Line, PdfRect Bounds, FloatingObject Object);

    private sealed class PdfLineGroup
    {
        public PdfLineGroup(double centerY)
        {
            CenterY = centerY;
        }

        public double CenterY { get; private set; }
        public List<PdfTextRun> Runs { get; } = new();
        public PdfRect Bounds { get; private set; }

        public void Add(PdfTextRun run)
        {
            Runs.Add(run);
            var baseline = ResolveRunBaseline(run);
            CenterY = (CenterY * (Runs.Count - 1) + baseline) / Runs.Count;

            if (Runs.Count == 1)
            {
                Bounds = run.Bounds;
                return;
            }

            var minX = Math.Min(Bounds.X, run.Bounds.X);
            var minY = Math.Min(Bounds.Y, run.Bounds.Y);
            var maxX = Math.Max(Bounds.Right, run.Bounds.Right);
            var maxY = Math.Max(Bounds.Bottom, run.Bounds.Bottom);
            Bounds = new PdfRect(minX, minY, maxX - minX, maxY - minY);
        }

        public void Sort()
        {
            Runs.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        }
    }

    private sealed class PdfColumnGroup
    {
        public PdfColumnGroup(PdfLineGroup line, bool isSpanning)
        {
            Lines.Add(line);
            Bounds = line.Bounds;
            Left = line.Bounds.X;
            CenterX = (line.Bounds.X + line.Bounds.Right) / 2;
            TopY = line.Bounds.Bottom;
            IsSpanning = isSpanning;
        }

        public List<PdfLineGroup> Lines { get; } = new();
        public PdfRect Bounds { get; private set; }
        public double Left { get; private set; }
        public double CenterX { get; private set; }
        public double TopY { get; private set; }
        public bool IsSpanning { get; }
        public int ColumnIndex { get; set; } = -1;

        public void Add(PdfLineGroup line)
        {
            Lines.Add(line);
            var weight = Lines.Count;
            Left = (Left * (weight - 1) + line.Bounds.X) / weight;
            CenterX = (CenterX * (weight - 1) + (line.Bounds.X + line.Bounds.Right) / 2) / weight;
            TopY = Math.Max(TopY, line.Bounds.Bottom);

            var minX = Math.Min(Bounds.X, line.Bounds.X);
            var minY = Math.Min(Bounds.Y, line.Bounds.Y);
            var maxX = Math.Max(Bounds.Right, line.Bounds.Right);
            var maxY = Math.Max(Bounds.Bottom, line.Bounds.Bottom);
            Bounds = new PdfRect(minX, minY, maxX - minX, maxY - minY);
        }

        public void Sort()
        {
            Lines.Sort((a, b) => b.CenterY.CompareTo(a.CenterY));
        }
    }

    private sealed class PdfParagraphGroup
    {
        public List<PdfLineGroup> Lines { get; } = new();
        public PdfRect Bounds { get; private set; }

        public void Add(PdfLineGroup line)
        {
            Lines.Add(line);
            if (Lines.Count == 1)
            {
                Bounds = line.Bounds;
                return;
            }

            var minX = Math.Min(Bounds.X, line.Bounds.X);
            var minY = Math.Min(Bounds.Y, line.Bounds.Y);
            var maxX = Math.Max(Bounds.Right, line.Bounds.Right);
            var maxY = Math.Max(Bounds.Bottom, line.Bounds.Bottom);
            Bounds = new PdfRect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    private sealed class PdfTextSpan
    {
        public string Text { get; }
        public TextStyleProperties? Style { get; }
        public PdfRect Bounds { get; }

        public PdfTextSpan(string text, TextStyleProperties? style, PdfRect bounds)
        {
            Text = text;
            Style = style;
            Bounds = bounds;
        }
    }

    private sealed class PdfGlyphLine
    {
        public PdfGlyphLine(double baselineY)
        {
            BaselineY = baselineY;
        }

        public double BaselineY { get; private set; }
        public List<PdfTextGlyph> Glyphs { get; } = new();
        public List<PdfTextSpan> Spans { get; } = new();
        public PdfRect Bounds { get; private set; }
        public double LineHeight { get; set; }
        public string Text { get; set; } = string.Empty;
        public ParagraphAlignment? Alignment { get; set; }
        public string? TrailingFootnoteMarker { get; set; }

        public void Add(PdfTextGlyph glyph)
        {
            Glyphs.Add(glyph);
            var baseline = ResolveGlyphBaseline(glyph);
            BaselineY = (BaselineY * (Glyphs.Count - 1) + baseline) / Glyphs.Count;
            var glyphHeight = glyph.FontSize > 0 ? glyph.FontSize : glyph.Bounds.Height;
            if (glyphHeight > LineHeight)
            {
                LineHeight = glyphHeight;
            }

            if (Glyphs.Count == 1)
            {
                Bounds = glyph.Bounds;
                return;
            }

            var minX = Math.Min(Bounds.X, glyph.Bounds.X);
            var minY = Math.Min(Bounds.Y, glyph.Bounds.Y);
            var maxX = Math.Max(Bounds.Right, glyph.Bounds.Right);
            var maxY = Math.Max(Bounds.Bottom, glyph.Bounds.Bottom);
            Bounds = new PdfRect(minX, minY, maxX - minX, maxY - minY);
        }

        public void MergeSuperscript(PdfGlyphLine superscript)
        {
            if (superscript.Glyphs.Count == 0)
            {
                return;
            }

            var baseline = BaselineY;
            foreach (var glyph in superscript.Glyphs)
            {
                Add(glyph);
            }

            BaselineY = baseline;
        }

        public void Sort()
        {
            Glyphs.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        }
    }

    private sealed class PdfGlyphRegion
    {
        public PdfGlyphRegion(PdfGlyphLine line, bool isSpanning)
        {
            Lines.Add(line);
            Bounds = line.Bounds;
            Left = line.Bounds.X;
            CenterX = (line.Bounds.X + line.Bounds.Right) / 2;
            TopY = line.Bounds.Bottom;
            IsSpanning = isSpanning;
        }

        public List<PdfGlyphLine> Lines { get; } = new();
        public PdfRect Bounds { get; private set; }
        public double Left { get; private set; }
        public double CenterX { get; private set; }
        public double TopY { get; private set; }
        public bool IsSpanning { get; }
        public int ColumnIndex { get; set; } = -1;

        public void Add(PdfGlyphLine line)
        {
            Lines.Add(line);
            var weight = Lines.Count;
            Left = (Left * (weight - 1) + line.Bounds.X) / weight;
            CenterX = (CenterX * (weight - 1) + (line.Bounds.X + line.Bounds.Right) / 2) / weight;
            TopY = Math.Max(TopY, line.Bounds.Bottom);

            var minX = Math.Min(Bounds.X, line.Bounds.X);
            var minY = Math.Min(Bounds.Y, line.Bounds.Y);
            var maxX = Math.Max(Bounds.Right, line.Bounds.Right);
            var maxY = Math.Max(Bounds.Bottom, line.Bounds.Bottom);
            Bounds = new PdfRect(minX, minY, maxX - minX, maxY - minY);
        }

        public void Sort()
        {
            Lines.Sort((a, b) => b.BaselineY.CompareTo(a.BaselineY));
        }
    }

    private sealed class PdfGlyphParagraphGroup
    {
        private readonly Dictionary<ParagraphAlignment, int> _alignmentCounts = new();

        public List<PdfGlyphLine> Lines { get; } = new();
        public PdfRect Bounds { get; private set; }
        public ParagraphAlignment? Alignment { get; private set; }

        public void Add(PdfGlyphLine line)
        {
            Lines.Add(line);
            if (Lines.Count == 1)
            {
                Bounds = line.Bounds;
            }
            else
            {
                var minX = Math.Min(Bounds.X, line.Bounds.X);
                var minY = Math.Min(Bounds.Y, line.Bounds.Y);
                var maxX = Math.Max(Bounds.Right, line.Bounds.Right);
                var maxY = Math.Max(Bounds.Bottom, line.Bounds.Bottom);
                Bounds = new PdfRect(minX, minY, maxX - minX, maxY - minY);
            }

            if (line.Alignment.HasValue)
            {
                var alignment = line.Alignment.Value;
                if (_alignmentCounts.TryGetValue(alignment, out var count))
                {
                    _alignmentCounts[alignment] = count + 1;
                }
                else
                {
                    _alignmentCounts[alignment] = 1;
                }

                ParagraphAlignment? topAlignment = null;
                var topCount = -1;
                foreach (var pair in _alignmentCounts)
                {
                    if (pair.Value > topCount)
                    {
                        topAlignment = pair.Key;
                        topCount = pair.Value;
                    }
                }

                Alignment = topAlignment;
            }
        }
    }

    private readonly record struct PdfGap(double Start, double End);

    private sealed class FlowBlock
    {
        public Block Block { get; }
        public PdfRect Bounds { get; }

        public FlowBlock(Block block, PdfRect bounds)
        {
            Block = block;
            Bounds = bounds;
        }
    }

    private sealed class PdfHeaderFooterInfo
    {
        public HashSet<string> HeaderKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> FooterKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, PdfGlyphLine> HeaderSamples { get; } = new Dictionary<string, PdfGlyphLine>(StringComparer.Ordinal);
        public Dictionary<string, PdfGlyphLine> FooterSamples { get; } = new Dictionary<string, PdfGlyphLine>(StringComparer.Ordinal);
        public double MedianLineHeight { get; set; }
        public double MedianTopMargin { get; set; }
        public double MedianBottomMargin { get; set; }

        public bool HasHeaders => HeaderKeys.Count > 0;
        public bool HasFooters => FooterKeys.Count > 0;
    }

    private sealed class PdfFootnoteState
    {
        public Document Document { get; }
        public int NextId { get; set; } = 1;

        public PdfFootnoteState(Document document)
        {
            Document = document;
        }
    }

    private sealed class PdfFootnotePageInfo
    {
        public List<PdfGlyphLine> Lines { get; } = new();
        public Dictionary<string, int> MarkerToId { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public double SeparatorY { get; set; }
    }

    private sealed class PdfLineSegment
    {
        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public bool IsHorizontal { get; }
        public bool IsVertical { get; }
        public double Length { get; }

        public PdfLineSegment(double x1, double y1, double x2, double y2, bool horizontal, bool vertical, double length)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            IsHorizontal = horizontal;
            IsVertical = vertical;
            Length = length;
        }

        public PdfRect Bounds
        {
            get
            {
                var minX = Math.Min(X1, X2);
                var minY = Math.Min(Y1, Y2);
                var maxX = Math.Max(X1, X2);
                var maxY = Math.Max(Y1, Y2);
                return new PdfRect(minX, minY, maxX - minX, maxY - minY);
            }
        }
    }

    private sealed class PdfLineCluster
    {
        public List<PdfLineSegment> Segments { get; } = new();
        public PdfRect Bounds { get; private set; }

        public void Add(PdfLineSegment segment)
        {
            Segments.Add(segment);
            var segBounds = segment.Bounds;
            if (Segments.Count == 1)
            {
                Bounds = segBounds;
                return;
            }

            var minX = Math.Min(Bounds.X, segBounds.X);
            var minY = Math.Min(Bounds.Y, segBounds.Y);
            var maxX = Math.Max(Bounds.Right, segBounds.Right);
            var maxY = Math.Max(Bounds.Bottom, segBounds.Bottom);
            Bounds = new PdfRect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    private sealed class PdfTableRegion
    {
        public PdfRect Bounds { get; }
        public IReadOnlyList<double> ColumnX { get; }
        public IReadOnlyList<double> RowY { get; }
        public Dictionary<(int Row, int Col), List<PdfGlyphLine>> CellLines { get; } = new();

        public PdfTableRegion(PdfRect bounds, IReadOnlyList<double> columnX, IReadOnlyList<double> rowY)
        {
            Bounds = bounds;
            ColumnX = columnX;
            RowY = rowY;
        }
    }

    private static double ResolveLineHeight(PdfLineGroup line)
    {
        if (line.Runs.Count == 0)
        {
            return line.Bounds.Height;
        }

        var average = line.Runs.Average(run => run.FontSize > 0 ? run.FontSize : run.Bounds.Height);
        return Math.Max(average, line.Bounds.Height);
    }

    private static PdfRect ResolveFixedLineBounds(PdfLineGroup line)
    {
        var bounds = line.Bounds;
        if (line.Runs.Count == 0)
        {
            return bounds;
        }

        var baseline = line.CenterY;
        if (!double.IsFinite(baseline))
        {
            return bounds;
        }

        var lineHeight = ResolveLineHeight(line);
        if (lineHeight <= 0)
        {
            return bounds;
        }

        var topGap = Math.Max(0, bounds.Bottom - baseline);
        var bottomGap = Math.Max(0, baseline - bounds.Y);
        var ascent = Math.Max(topGap, lineHeight * 0.7);
        var descent = Math.Max(bottomGap, lineHeight * 0.25);
        var height = ascent + descent;
        if (height <= 0 || bounds.Width <= 0)
        {
            return bounds;
        }

        var bottom = baseline - descent;
        return new PdfRect(bounds.X, bottom, bounds.Width, height);
    }

    private static PdfRect ResolveFixedRunBounds(PdfTextRun run)
    {
        var bounds = run.Bounds;
        var lineHeight = run.FontSize > 0 ? run.FontSize : bounds.Height;
        if (lineHeight <= 0)
        {
            return bounds;
        }
        if (bounds.Width <= 0)
        {
            return bounds;
        }

        var height = Math.Max(bounds.Height, lineHeight);
        var top = bounds.Bottom;
        var bottom = top - height;
        return new PdfRect(bounds.X, bottom, bounds.Width, height);
    }

    private static PdfRect ResolveFixedGlyphLineBounds(PdfGlyphLine line)
    {
        if (line.Glyphs.Count == 0)
        {
            return line.Bounds;
        }

        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;

        foreach (var glyph in line.Glyphs)
        {
            var bounds = glyph.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            if (bounds.X < minX)
            {
                minX = bounds.X;
            }

            if (bounds.Right > maxX)
            {
                maxX = bounds.Right;
            }

            if (bounds.Y < minY)
            {
                minY = bounds.Y;
            }

            if (bounds.Bottom > maxY)
            {
                maxY = bounds.Bottom;
            }
        }

        if (minX == double.MaxValue || maxX == double.MinValue || minY == double.MaxValue || maxY == double.MinValue)
        {
            return line.Bounds;
        }

        var width = Math.Max(0.1, maxX - minX);
        var height = Math.Max(0.1, maxY - minY);
        var baseline = line.BaselineY;
        if (!double.IsFinite(baseline) || baseline < minY || baseline > maxY)
        {
            return new PdfRect(minX, minY, width, height);
        }

        var ascent = maxY - baseline;
        var descent = baseline - minY;
        var lineHeight = Math.Max(height, ascent + descent);
        var bottom = baseline - descent;
        return new PdfRect(minX, bottom, width, lineHeight);
    }

    private static List<FloatingObject> BuildImageObjects(PdfPageAst page)
    {
        var objects = new List<FloatingObject>();
        foreach (var image in page.Images)
        {
            if (image.Data.Length == 0)
            {
                continue;
            }

            var width = PdfUnits.PointsToDip(image.Bounds.Width);
            var height = PdfUnits.PointsToDip(image.Bounds.Height);
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            var inline = new ImageInline(image.Data, width, height, image.MimeType ?? "image/png");
            var floating = CreatePageAnchoredObject(inline, page, image.Bounds, behindText: image.IsBackground);
            objects.Add(floating);
        }

        return objects;
    }

    private static List<FloatingObject> BuildPathObjects(PdfPageAst page)
    {
        var objects = new List<FloatingObject>();
        foreach (var path in page.Paths)
        {
            if (path.Segments.Count == 0)
            {
                continue;
            }

            if (!path.Style.IsFilled && !path.Style.IsStroked)
            {
                continue;
            }

            var bounds = ResolvePathBounds(path);
            var width = PdfUnits.PointsToDip(bounds.Width);
            var height = PdfUnits.PointsToDip(bounds.Height);
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            var geometry = BuildShapeGeometryFromPath(path, bounds);
            if (geometry.Paths.Count == 0)
            {
                continue;
            }

            var shape = new ShapeInline(width, height)
            {
                TextBox = null
            };

            shape.Properties.CustomGeometry = geometry;
            ApplyShapeStyle(shape.Properties, path.Style);

            var floating = CreatePageAnchoredObject(shape, page, bounds, behindText: false);
            objects.Add(floating);
        }

        return objects;
    }

    private static ShapeGeometry BuildShapeGeometryFromPath(PdfPathObject path, PdfRect bounds)
    {
        var geometry = new ShapeGeometry();
        ShapePath? current = null;
        foreach (var segment in path.Segments)
        {
            if (segment.Kind == PdfPathSegmentKind.MoveTo)
            {
                if (current is null || current.Commands.Count > 0)
                {
                    current = CreateShapePath(path.Style);
                    geometry.Paths.Add(current);
                }

                var (x, y) = ToLocalPoint(bounds, segment.X1, segment.Y1);
                current.Commands.Add(new ShapeMoveToCommand(new ShapeAdjustPoint(
                    x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture))));
                continue;
            }

            if (current is null)
            {
                current = CreateShapePath(path.Style);
                geometry.Paths.Add(current);
            }

            switch (segment.Kind)
            {
                case PdfPathSegmentKind.LineTo:
                {
                    var (x, y) = ToLocalPoint(bounds, segment.X1, segment.Y1);
                    current.Commands.Add(new ShapeLineToCommand(new ShapeAdjustPoint(
                        x.ToString(CultureInfo.InvariantCulture),
                        y.ToString(CultureInfo.InvariantCulture))));
                    break;
                }
                case PdfPathSegmentKind.CubicTo:
                {
                    var (c1x, c1y) = ToLocalPoint(bounds, segment.X1, segment.Y1);
                    var (c2x, c2y) = ToLocalPoint(bounds, segment.X2, segment.Y2);
                    var (x, y) = ToLocalPoint(bounds, segment.X3, segment.Y3);
                    current.Commands.Add(new ShapeCubicBezierToCommand(
                        new ShapeAdjustPoint(c1x.ToString(CultureInfo.InvariantCulture), c1y.ToString(CultureInfo.InvariantCulture)),
                        new ShapeAdjustPoint(c2x.ToString(CultureInfo.InvariantCulture), c2y.ToString(CultureInfo.InvariantCulture)),
                        new ShapeAdjustPoint(x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture))));
                    break;
                }
                case PdfPathSegmentKind.Close:
                    current.Commands.Add(new ShapeClosePathCommand());
                    break;
            }
        }

        return geometry;
    }

    private static PdfRect ResolvePathBounds(PdfPathObject path)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        void Include(double x, double y)
        {
            if (x < minX)
            {
                minX = x;
            }

            if (x > maxX)
            {
                maxX = x;
            }

            if (y < minY)
            {
                minY = y;
            }

            if (y > maxY)
            {
                maxY = y;
            }
        }

        foreach (var segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case PdfPathSegmentKind.MoveTo:
                case PdfPathSegmentKind.LineTo:
                    Include(segment.X1, segment.Y1);
                    break;
                case PdfPathSegmentKind.CubicTo:
                    Include(segment.X1, segment.Y1);
                    Include(segment.X2, segment.Y2);
                    Include(segment.X3, segment.Y3);
                    break;
            }
        }

        if (minX == double.MaxValue || minY == double.MaxValue || maxX == double.MinValue || maxY == double.MinValue)
        {
            return path.Bounds;
        }

        var stroke = path.Style.IsStroked ? path.Style.LineWidth : 0;
        var pad = Math.Max(stroke, 0.5);
        var half = pad * 0.5;
        minX -= half;
        minY -= half;
        maxX += half;
        maxY += half;

        var width = maxX - minX;
        var height = maxY - minY;
        if (width < pad)
        {
            var center = (minX + maxX) * 0.5;
            minX = center - pad * 0.5;
            width = pad;
        }

        if (height < pad)
        {
            var center = (minY + maxY) * 0.5;
            minY = center - pad * 0.5;
            height = pad;
        }

        return new PdfRect(minX, minY, width, height);
    }

    private static ShapePath CreateShapePath(PdfPathStyle style)
    {
        return new ShapePath
        {
            IsStroked = style.IsStroked,
            FillMode = style.IsFilled ? ShapePathFillMode.Normal : ShapePathFillMode.None,
            FillRule = style.FillRule == PdfFillRule.EvenOdd ? ShapePathFillRule.EvenOdd : ShapePathFillRule.NonZero
        };
    }

    private static void ApplyShapeStyle(ShapeProperties properties, PdfPathStyle style)
    {
        if (style.IsFilled)
        {
            var fill = style.FillColor ?? new PdfColor(0, 0, 0, 255);
            properties.FillColor = new DocColor(fill.R, fill.G, fill.B, fill.A);
        }

        if (style.IsStroked)
        {
            var stroke = style.StrokeColor ?? new PdfColor(0, 0, 0, 255);
            var thickness = (float)PdfUnits.PointsToDip(style.LineWidth);
            if (thickness <= 0f)
            {
                thickness = 0.25f;
            }

            properties.Outline = new BorderLine
            {
                Style = DocBorderStyle.Single,
                Thickness = thickness,
                Color = new DocColor(stroke.R, stroke.G, stroke.B, stroke.A),
                LineCap = style.LineCap switch
                {
                    PdfLineCap.Round => DocLineCap.Round,
                    PdfLineCap.Square => DocLineCap.Square,
                    _ => DocLineCap.Flat
                },
                LineJoin = style.LineJoin switch
                {
                    PdfLineJoin.Round => DocLineJoin.Round,
                    PdfLineJoin.Bevel => DocLineJoin.Bevel,
                    _ => DocLineJoin.Miter
                },
                MiterLimit = style.MiterLimit.HasValue ? (float)style.MiterLimit.Value : null
            };

            if (style.DashArray is { Count: > 0 })
            {
                var dash = new float[style.DashArray.Count];
                for (var i = 0; i < style.DashArray.Count; i++)
                {
                    dash[i] = (float)PdfUnits.PointsToDip(style.DashArray[i]);
                }

                properties.Outline.DashArray = dash;
                properties.Outline.DashPhase = (float)PdfUnits.PointsToDip(style.DashPhase);
            }
        }
    }

    private static (double X, double Y) ToLocalPoint(PdfRect bounds, double x, double y)
    {
        var localX = x - bounds.X;
        var localY = (bounds.Y + bounds.Height) - y;
        return (localX, localY);
    }

    private static void RegisterFonts(Document document, IReadOnlyList<PdfTextRun> runs, IReadOnlyList<PdfEmbeddedFont> embeddedFonts)
    {
        var fonts = document.Fonts;
        foreach (var run in runs)
        {
            if (run.Font is null || string.IsNullOrWhiteSpace(run.Font.Name))
            {
                continue;
            }

            EnsureFontDefinition(fonts, run.Font.Name, null);
        }

        RegisterEmbeddedFonts(fonts, embeddedFonts);
    }

    private static void RegisterFonts(Document document, IReadOnlyList<PdfTextGlyph> glyphs, IReadOnlyList<PdfEmbeddedFont> embeddedFonts)
    {
        var fonts = document.Fonts;
        foreach (var glyph in glyphs)
        {
            if (glyph.Font is null || string.IsNullOrWhiteSpace(glyph.Font.Name))
            {
                continue;
            }

            EnsureFontDefinition(fonts, glyph.Font.Name, null);
        }

        RegisterEmbeddedFonts(fonts, embeddedFonts);
    }

    private static void RegisterEmbeddedFonts(DocumentFonts fonts, IReadOnlyList<PdfEmbeddedFont> embeddedFonts)
    {
        if (embeddedFonts.Count == 0)
        {
            return;
        }

        var normalizedMap = BuildNormalizedFontMap(fonts);

        foreach (var embedded in embeddedFonts)
        {
            if (string.IsNullOrWhiteSpace(embedded.FamilyName) || embedded.Data.Length == 0)
            {
                continue;
            }

            var fontData = new EmbeddedFontData(embedded.Data, embedded.ContentType, embedded.PostScriptName);
            var familyDefinition = EnsureFontDefinition(fonts, embedded.FamilyName, normalizedMap);
            ApplyEmbeddedFont(familyDefinition, fontData, embedded.IsBold, embedded.IsItalic);

            if (!string.IsNullOrWhiteSpace(embedded.PostScriptName)
                && !string.Equals(embedded.PostScriptName, embedded.FamilyName, StringComparison.OrdinalIgnoreCase))
            {
                var postScriptDefinition = EnsureFontDefinition(fonts, embedded.PostScriptName, normalizedMap);
                ApplyEmbeddedFont(postScriptDefinition, fontData, embedded.IsBold, embedded.IsItalic);

                familyDefinition.AltName ??= embedded.PostScriptName;
                postScriptDefinition.AltName ??= embedded.FamilyName;
            }

            ApplyEmbeddedToMatchingKeys(normalizedMap, embedded.FamilyName, fontData, embedded.IsBold, embedded.IsItalic);
            if (!string.IsNullOrWhiteSpace(embedded.PostScriptName))
            {
                ApplyEmbeddedToMatchingKeys(normalizedMap, embedded.PostScriptName, fontData, embedded.IsBold, embedded.IsItalic);
            }
        }
    }

    private static Dictionary<string, HashSet<DocumentFontDefinition>> BuildNormalizedFontMap(DocumentFonts fonts)
    {
        var map = new Dictionary<string, HashSet<DocumentFontDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in fonts.FontTable.Values)
        {
            AddDefinitionToMap(map, definition);
        }

        return map;
    }

    private static DocumentFontDefinition EnsureFontDefinition(
        DocumentFonts fonts,
        string name,
        Dictionary<string, HashSet<DocumentFontDefinition>>? normalizedMap)
    {
        if (!fonts.FontTable.TryGetValue(name, out var definition))
        {
            definition = new DocumentFontDefinition(name);
            fonts.FontTable[name] = definition;
            if (normalizedMap is not null)
            {
                AddDefinitionToMap(normalizedMap, definition);
            }
        }

        return definition;
    }

    private static void AddDefinitionToMap(
        Dictionary<string, HashSet<DocumentFontDefinition>> normalizedMap,
        DocumentFontDefinition definition)
    {
        var key = NormalizeFontKey(definition.Name);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!normalizedMap.TryGetValue(key, out var list))
        {
            list = new HashSet<DocumentFontDefinition>();
            normalizedMap[key] = list;
        }

        list.Add(definition);
    }

    private static void ApplyEmbeddedToMatchingKeys(
        Dictionary<string, HashSet<DocumentFontDefinition>> normalizedMap,
        string name,
        EmbeddedFontData fontData,
        bool isBold,
        bool isItalic)
    {
        var key = NormalizeFontKey(name);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!normalizedMap.TryGetValue(key, out var matches))
        {
            return;
        }

        foreach (var definition in matches)
        {
            ApplyEmbeddedFont(definition, fontData, isBold, isItalic);
        }
    }

    private static void ApplyEmbeddedFont(
        DocumentFontDefinition definition,
        EmbeddedFontData fontData,
        bool isBold,
        bool isItalic)
    {
        if (isBold && isItalic)
        {
            definition.BoldItalic ??= fontData;
        }
        else if (isBold)
        {
            definition.Bold ??= fontData;
        }
        else if (isItalic)
        {
            definition.Italic ??= fontData;
        }
        else
        {
            definition.Regular ??= fontData;
        }
    }

    private static FloatingObject CreatePageAnchoredObject(Inline content, PdfPageAst page, PdfRect bounds, bool behindText)
    {
        var floating = new FloatingObject(content);
        floating.Anchor.HorizontalReference = FloatingHorizontalReference.Page;
        floating.Anchor.VerticalReference = FloatingVerticalReference.Page;
        floating.Anchor.OffsetX = PdfUnits.PointsToDip(bounds.X);
        floating.Anchor.OffsetY = PdfUnits.PointsToDip(page.Height - bounds.Y - bounds.Height);
        floating.Anchor.WrapStyle = FloatingWrapStyle.None;
        floating.Anchor.BehindText = behindText;
        return floating;
    }

    private static List<Block> BuildParagraphs(string text)
    {
        var blocks = new List<Block>();
        if (string.IsNullOrEmpty(text))
        {
            blocks.Add(new ParagraphBlock());
            return blocks;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var paragraph = new ParagraphBlock(line);
            if (!string.IsNullOrEmpty(line))
            {
                paragraph.Inlines.Add(new RunInline(line));
            }

            blocks.Add(paragraph);
        }

        return blocks;
    }
}
