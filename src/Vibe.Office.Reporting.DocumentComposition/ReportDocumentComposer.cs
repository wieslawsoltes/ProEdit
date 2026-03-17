using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Html;
using Vibe.Office.Markdown;
using Vibe.Office.OpenXml;
using Vibe.Office.Primitives;

namespace Vibe.Office.Reporting.DocumentComposition;

/// <summary>
/// Default implementation of <see cref="IReportDocumentComposer" />.
/// </summary>
public sealed class ReportDocumentComposer : IReportDocumentComposer
{
    /// <inheritdoc />
    public ValueTask<ReportDocumentCompositionResult> ComposeAsync(
        ReportDocumentCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ReportDocumentCompositionResult();
        try
        {
            var document = CreateEmptyDocument();
            ComposeSections(request.MaterializedReport, document, result.Diagnostics, cancellationToken);
            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new ParagraphBlock());
            }

            result.Document = document;
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DocumentCompositionFailed,
                ex.Message,
                "$"));
        }

        return ValueTask.FromResult(result);
    }

    private static void ComposeSections(
        MaterializedReport report,
        Document document,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        for (var sectionIndex = 0; sectionIndex < report.Sections.Count; sectionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = report.Sections[sectionIndex];
            DocumentSection targetSection;

            if (sectionIndex == 0)
            {
                targetSection = document.Sections[0];
                ApplyPageSettings(section.PageSettings, targetSection.Properties);
                ComposeHeaderFooter(section.HeaderItems, document, targetSection.Header, diagnostics, cancellationToken);
                ComposeHeaderFooter(section.FooterItems, document, targetSection.Footer, diagnostics, cancellationToken);
                if (!string.IsNullOrWhiteSpace(section.Bookmark))
                {
                    document.Blocks.Add(CreateBookmarkParagraph(section.Bookmark));
                }

                ComposeItems(section.BodyItems, document, document.Blocks, diagnostics, cancellationToken);
                continue;
            }

            var sectionProperties = new SectionProperties();
            ApplyPageSettings(section.PageSettings, sectionProperties);
            targetSection = new DocumentSection(
                sectionProperties,
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter(),
                new HeaderFooter());

            document.Sections.Add(targetSection);
            document.Blocks.Add(new SectionBreakBlock
            {
                BreakType = SectionBreakType.NextPage,
                SectionIndex = document.Sections.Count - 1,
                Properties = sectionProperties.Clone()
            });

            ComposeHeaderFooter(section.HeaderItems, document, targetSection.Header, diagnostics, cancellationToken);
            ComposeHeaderFooter(section.FooterItems, document, targetSection.Footer, diagnostics, cancellationToken);
            if (!string.IsNullOrWhiteSpace(section.Bookmark))
            {
                document.Blocks.Add(CreateBookmarkParagraph(section.Bookmark));
            }

            ComposeItems(section.BodyItems, document, document.Blocks, diagnostics, cancellationToken);
        }
    }

    private static void ComposeHeaderFooter(
        IReadOnlyList<MaterializedReportItem> items,
        Document document,
        HeaderFooter headerFooter,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        headerFooter.Blocks.Clear();
        ComposeItems(items, document, headerFooter.Blocks, diagnostics, cancellationToken);
        headerFooter.IsDefined = headerFooter.Blocks.Count > 0;
    }

    private static void ComposeItems(
        IReadOnlyList<MaterializedReportItem> items,
        Document document,
        List<Block> targetBlocks,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        float offsetX = 0f,
        float offsetY = 0f)
    {
        ParagraphBlock? floatingAnchor = null;

        static ParagraphBlock EnsureFloatingAnchor(List<Block> blocks, ref ParagraphBlock? anchor)
        {
            anchor ??= new ParagraphBlock();
            if (!blocks.Contains(anchor))
            {
                blocks.Add(anchor);
            }

            return anchor;
        }

        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[itemIndex];
            var localBounds = TranslateBounds(item.Bounds, offsetX, offsetY);
            switch (item)
            {
                case MaterializedTextReportItem textItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateTextShape(textItem),
                        localBounds);
                    break;
                case MaterializedImageReportItem imageItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateImageInline(imageItem, diagnostics),
                        localBounds);
                    break;
                case MaterializedLineReportItem lineItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateLineInline(lineItem),
                        localBounds);
                    break;
                case MaterializedShapeReportItem shapeItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateShapeInline(shapeItem),
                        localBounds);
                    break;
                case MaterializedContainerReportItem containerItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateContainerInline(containerItem, document, diagnostics, cancellationToken),
                        localBounds);
                    break;
                case MaterializedChartReportItem chartItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateChartInline(chartItem),
                        localBounds);
                    break;
                case MaterializedGaugeReportItem gaugeItem:
                    AddFloatingObject(
                        EnsureFloatingAnchor(targetBlocks, ref floatingAnchor),
                        CreateGaugeInline(gaugeItem),
                        localBounds);
                    break;
                case MaterializedTablixReportItem tablixItem:
                    if (!string.IsNullOrWhiteSpace(tablixItem.Bookmark))
                    {
                        targetBlocks.Add(CreateBookmarkParagraph(tablixItem.Bookmark));
                    }

                    EnsureFloatingAnchor(targetBlocks, ref floatingAnchor);
                    var table = CreateTable(tablixItem, diagnostics);
                    table.Properties.FloatingAnchor = CreateFloatingAnchor(localBounds);
                    targetBlocks.Add(table);
                    break;
                case MaterializedSubreportReportItem subreportItem:
                    ComposeSubreport(subreportItem, document, targetBlocks, diagnostics, cancellationToken);
                    break;
                case MaterializedDocumentTemplateReportItem templateItem:
                    ComposeDocumentTemplate(templateItem, document, targetBlocks, diagnostics);
                    break;
            }
        }
    }

    private static ParagraphBlock CreateTextParagraph(MaterializedTextReportItem item)
    {
        var paragraph = new ParagraphBlock(item.Text);
        AddBookmark(paragraph, item.Bookmark);
        if (item.Style?.TextAlign.HasValue == true)
        {
            paragraph.Properties.Alignment = item.Style.TextAlign.Value;
        }

        switch (item.ValueKind)
        {
            case MaterializedTextValueKind.PageNumber:
                paragraph.Inlines.Add(new PageNumberInline(CreateInlineStyle(item.Style)));
                break;
            case MaterializedTextValueKind.TotalPages:
                paragraph.Inlines.Add(new TotalPagesInline(CreateInlineStyle(item.Style)));
                break;
            default:
                paragraph.Inlines.Add(CreateStyledRun(item.Text, item.Style));
                break;
        }

        return paragraph;
    }

    private static Inline CreateImageInline(
        MaterializedImageReportItem item,
        List<ReportDiagnostic> diagnostics)
    {
        if (item.Data is null || item.Data.Length == 0)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DocumentCompositionFailed,
                $"Image item '{item.SourceItemId}' does not contain data.",
                "$.sections"));
            return CreateEmptyTextShapeInline(item.Bounds.Width, item.Bounds.Height);
        }

        return new ImageInline(
            item.Data,
            Math.Max(1f, item.Bounds.Width),
            Math.Max(1f, item.Bounds.Height),
            item.ContentType);
    }

    private static Inline CreateChartInline(MaterializedChartReportItem item)
    {
        return new ChartInline(
            Math.Max(1f, item.Bounds.Width),
            Math.Max(1f, item.Bounds.Height),
            item.Model,
            partData: null)
        {
            Name = item.Name
        };
    }

    private static Inline CreateLineInline(MaterializedLineReportItem item)
    {
        var width = Math.Max(1f, Math.Abs(item.X2 - item.Bounds.X));
        var height = Math.Max(1f, Math.Abs(item.Y2 - item.Bounds.Y));
        return new ShapeInline(
            width,
            height,
            new ShapeProperties
            {
                PresetGeometry = "line",
                Fill = new ShapeNoFill(),
                Outline = new BorderLine()
            },
            name: item.Name);
    }

    private static Inline CreateShapeInline(MaterializedShapeReportItem item)
    {
        var properties = new ShapeProperties
        {
            PresetGeometry = item.Shape switch
            {
                ReportShapeKind.RoundedRectangle => "roundRect",
                ReportShapeKind.Ellipse => "ellipse",
                _ => "rect"
            },
            Outline = new BorderLine()
        };

        if (ReportDocumentColorParser.TryParse(item.Style?.Background, out var fillColor))
        {
            properties.Fill = new ShapeSolidFill(fillColor);
        }
        else
        {
            properties.Fill = new ShapeNoFill();
        }

        return new ShapeInline(
            Math.Max(1f, item.Bounds.Width),
            Math.Max(1f, item.Bounds.Height),
            properties,
            name: item.Name);
    }

    private static TableBlock CreateTable(
        MaterializedTablixReportItem item,
        List<ReportDiagnostic> diagnostics)
    {
        var table = new TableBlock();
        table.Properties.LayoutMode = TableLayoutMode.Fixed;

        var totalWidth = 0f;
        for (var columnIndex = 0; columnIndex < item.Columns.Count; columnIndex++)
        {
            var width = Math.Max(1f, item.Columns[columnIndex].Width);
            table.Properties.ColumnWidths.Add(width);
            totalWidth += width;
        }

        if (totalWidth > 0f)
        {
            table.Properties.Width = totalWidth;
            table.Properties.WidthUnit = TableWidthUnit.Dxa;
        }

        for (var rowIndex = 0; rowIndex < item.Rows.Count; rowIndex++)
        {
            var sourceRow = item.Rows[rowIndex];
            var row = new TableRow();
            if (sourceRow.Height > 0f)
            {
                row.Properties.Height = sourceRow.Height;
                row.Properties.HeightRule = TableRowHeightRule.Exact;
            }

            if (item.RepeatHeaderRows && sourceRow.IsHeader)
            {
                row.Properties.RepeatOnEachPage = true;
            }

            for (var cellIndex = 0; cellIndex < sourceRow.Cells.Count; cellIndex++)
            {
                var sourceCell = sourceRow.Cells[cellIndex];
                if (sourceCell.RowSpan > 1)
                {
                    diagnostics.Add(new ReportDiagnostic(
                        ReportDiagnosticSeverity.Warning,
                        ReportDiagnosticCodes.UnsupportedFeature,
                        $"Row spans are not yet composed into the document model for tablix '{item.SourceItemId}'.",
                        "$.sections"));
                }

                var cell = new TableCell
                {
                    ColumnSpan = Math.Max(1, sourceCell.ColumnSpan)
                };
                ApplyCellStyle(sourceCell, cell.Properties);
                cell.Paragraphs.Add(CreateCellParagraph(sourceCell, diagnostics));
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static ParagraphBlock CreateCellParagraph(
        MaterializedTablixCell cell,
        List<ReportDiagnostic> diagnostics)
    {
        if (cell.Content is not null)
        {
            return CreateCellContentParagraph(cell, diagnostics);
        }

        var paragraph = new ParagraphBlock(cell.Text);
        if (cell.Style?.TextAlign.HasValue == true)
        {
            paragraph.Properties.Alignment = cell.Style.TextAlign.Value;
        }

        paragraph.Inlines.Add(CreateStyledRun(cell.Text, cell.Style));
        return paragraph;
    }

    private static ParagraphBlock CreateCellContentParagraph(
        MaterializedTablixCell cell,
        List<ReportDiagnostic> diagnostics)
    {
        return cell.Content switch
        {
            MaterializedTextReportItem textItem => CreateTextParagraph(textItem),
            MaterializedImageReportItem imageItem => CreateInlineParagraph(CreateImageInline(imageItem, new List<ReportDiagnostic>())),
            MaterializedChartReportItem chartItem => CreateInlineParagraph(CreateChartInline(chartItem)),
            MaterializedGaugeReportItem gaugeItem => CreateInlineParagraph(CreateGaugeInline(gaugeItem)),
            MaterializedShapeReportItem shapeItem => CreateInlineParagraph(CreateShapeInline(shapeItem)),
            MaterializedContainerReportItem containerItem => CreateContainerCellParagraph(containerItem, diagnostics),
            _ => new ParagraphBlock(cell.Text)
        };
    }

    private static void ApplyCellStyle(
        MaterializedTablixCell source,
        TableCellProperties target)
    {
        if (ReportDocumentColorParser.TryParse(source.Style?.Background, out var background))
        {
            target.ShadingColor = background;
        }

        target.Padding = DocThickness.Uniform(4f);
    }

    private static void ComposeSubreport(
        MaterializedSubreportReportItem item,
        Document document,
        List<Block> targetBlocks,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (item.Report is null)
        {
            return;
        }

        for (var sectionIndex = 0; sectionIndex < item.Report.Sections.Count; sectionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = item.Report.Sections[sectionIndex];
            if (sectionIndex > 0)
            {
                targetBlocks.Add(new PageBreakBlock());
            }

            ComposeItems(section.BodyItems, document, targetBlocks, diagnostics, cancellationToken);
        }
    }

    private static void ComposeDocumentTemplate(
        MaterializedDocumentTemplateReportItem item,
        Document document,
        List<Block> targetBlocks,
        List<ReportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(item.Content))
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Warning,
                ReportDiagnosticCodes.DocumentTemplateLoadFailed,
                $"Template item '{item.SourceItemId}' does not contain content.",
                "$.sections"));
            return;
        }

        try
        {
            var templateDocument = LoadTemplateDocument(item);
            ReportTemplateDocumentBinder.Bind(
                templateDocument,
                item.Bindings,
                $"vibeoffice-report-bindings-{Guid.NewGuid():N}");
            if (!string.IsNullOrWhiteSpace(item.Bookmark))
            {
                targetBlocks.Add(CreateBookmarkParagraph(item.Bookmark));
            }

            ReportDocumentFragmentImporter.ImportBlocks(templateDocument, document, targetBlocks);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DocumentTemplateLoadFailed,
                ex.Message,
                "$.sections"));
        }
    }

    private static Document LoadTemplateDocument(MaterializedDocumentTemplateReportItem item)
    {
        var content = item.Content ?? string.Empty;
        return item.TemplateFormat switch
        {
            ReportDocumentTemplateFormat.Html => HtmlDocumentConverter.FromHtml(content.AsSpan()),
            ReportDocumentTemplateFormat.Markdown => MarkdownDocumentConverter.FromMarkdown(content.AsSpan()),
            ReportDocumentTemplateFormat.Docx => new DocxImporter().Load(new MemoryStream(Convert.FromBase64String(content))),
            _ => DocumentPlainTextParser.FromPlainText(content)
        };
    }

    private static ParagraphBlock CreateBookmarkParagraph(string bookmark)
    {
        var paragraph = new ParagraphBlock();
        AddBookmark(paragraph, bookmark);
        return paragraph;
    }

    private static void AddBookmark(
        ParagraphBlock paragraph,
        string? bookmark)
    {
        if (string.IsNullOrWhiteSpace(bookmark))
        {
            return;
        }

        var bookmarkId = Math.Abs(bookmark.GetHashCode(StringComparison.Ordinal)) + 1;
        paragraph.Inlines.Add(new BookmarkStartInline(bookmarkId, bookmark));
        paragraph.Inlines.Add(new BookmarkEndInline(bookmarkId));
    }

    private static RunInline CreateStyledRun(
        string text,
        MaterializedReportStyle? style)
    {
        var runStyle = new TextStyleProperties();
        if (style is not null)
        {
            if (!string.IsNullOrWhiteSpace(style.FontFamily))
            {
                runStyle.FontFamily = style.FontFamily;
            }

            if (style.FontSize.HasValue)
            {
                runStyle.FontSize = style.FontSize.Value;
            }

            if (style.Bold == true)
            {
                runStyle.FontWeight = DocFontWeight.Bold;
            }

            if (style.Italic == true)
            {
                runStyle.FontStyle = DocFontStyle.Italic;
            }

            if (ReportDocumentColorParser.TryParse(style.Foreground, out var foreground))
            {
                runStyle.Color = foreground;
            }

            if (ReportDocumentColorParser.TryParse(style.Background, out var highlight))
            {
                runStyle.HighlightColor = highlight;
            }
        }

        return runStyle.HasValues
            ? new RunInline(text, runStyle)
            : new RunInline(text);
    }

    private static void AddFloatingObject(
        ParagraphBlock anchorParagraph,
        Inline content,
        ReportItemBounds bounds)
    {
        var floating = new FloatingObject(content);
        CopyAnchor(CreateFloatingAnchor(bounds), floating.Anchor);
        anchorParagraph.FloatingObjects.Add(floating);
    }

    private static ShapeInline CreateTextShape(MaterializedTextReportItem item)
    {
        var shape = CreateEmptyTextShapeInline(item.Bounds.Width, item.Bounds.Height);
        ApplyShapeStyle(shape.Properties, item.Style, showOutline: false);
        shape.Name = item.Name;
        shape.TextBox!.Blocks.Add(CreateTextParagraph(item));
        return shape;
    }

    private static ShapeInline CreateGaugeInline(MaterializedGaugeReportItem item)
    {
        var shape = CreateEmptyTextShapeInline(item.Bounds.Width, item.Bounds.Height);
        shape.Name = item.Name;
        ApplyGaugeStyle(shape.Properties, item);

        var text = item.Label;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = item.GaugeKind == ReportGaugeKind.StateIndicator
                ? FormatGaugeState(item.Value)
                : FormatGaugeValue(item);
        }
        else if (item.GaugeKind != ReportGaugeKind.StateIndicator)
        {
            text = string.Concat(text, "\n", FormatGaugeValue(item));
        }

        var paragraph = new ParagraphBlock(text ?? string.Empty);
        paragraph.Inlines.Add(CreateStyledRun(text ?? string.Empty, item.Style));
        shape.TextBox!.Blocks.Add(paragraph);
        return shape;
    }

    private static ShapeInline CreateContainerInline(
        MaterializedContainerReportItem item,
        Document document,
        List<ReportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var shape = CreateEmptyTextShapeInline(item.Bounds.Width, item.Bounds.Height);
        shape.Name = item.Name;
        shape.TextBox!.Properties.Padding = DocThickness.Uniform(0f);
        ApplyShapeStyle(shape.Properties, item.Style, showOutline: false);
        ComposeItems(
            item.Items,
            document,
            shape.TextBox.Blocks,
            diagnostics,
            cancellationToken,
            item.Bounds.X,
            item.Bounds.Y);
        return shape;
    }

    private static ParagraphBlock CreateContainerCellParagraph(
        MaterializedContainerReportItem item,
        List<ReportDiagnostic> diagnostics)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(CreateContainerInline(item, CreateEmptyDocument(), diagnostics, CancellationToken.None));
        return paragraph;
    }

    private static ReportItemBounds TranslateBounds(ReportItemBounds bounds, float offsetX, float offsetY)
    {
        if (offsetX == 0f && offsetY == 0f)
        {
            return bounds;
        }

        return bounds with
        {
            X = bounds.X - offsetX,
            Y = bounds.Y - offsetY
        };
    }

    private static ParagraphBlock CreateInlineParagraph(Inline inline)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(inline);
        return paragraph;
    }

    private static ShapeInline CreateEmptyTextShapeInline(float width, float height)
    {
        var textBox = new ShapeTextBox();
        textBox.Properties.Padding = DocThickness.Uniform(4f);
        return new ShapeInline(
            Math.Max(1f, width),
            Math.Max(1f, height),
            new ShapeProperties
            {
                PresetGeometry = "rect",
                Fill = new ShapeNoFill(),
                Outline = new BorderLine
                {
                    Style = DocBorderStyle.None,
                    Thickness = 0f
                }
            },
            textBox);
    }

    private static void ApplyShapeStyle(
        ShapeProperties properties,
        MaterializedReportStyle? style,
        bool showOutline)
    {
        if (ReportDocumentColorParser.TryParse(style?.Background, out var fillColor))
        {
            properties.Fill = new ShapeSolidFill(fillColor);
        }
        else
        {
            properties.Fill = new ShapeNoFill();
        }

        properties.Outline = showOutline
            ? new BorderLine()
            : new BorderLine
            {
                Style = DocBorderStyle.None,
                Thickness = 0f
            };
    }

    private static void ApplyGaugeStyle(
        ShapeProperties properties,
        MaterializedGaugeReportItem item)
    {
        var fillColor = item.GaugeKind switch
        {
            ReportGaugeKind.StateIndicator => ResolveGaugeStateColor(item.Value),
            _ when item.TargetValue.HasValue && item.Value.HasValue && item.Value.Value >= item.TargetValue.Value
                => new DocColor(98, 162, 69),
            _ when item.TargetValue.HasValue && item.Value.HasValue
                => new DocColor(192, 67, 58),
            _ => new DocColor(68, 67, 67)
        };

        properties.Fill = new ShapeSolidFill(fillColor);
        properties.Outline = new BorderLine
        {
            Color = fillColor
        };
    }

    private static string FormatGaugeState(double? value)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        return value.Value switch
        {
            >= 1d => "OK",
            >= 0d => "Warning",
            _ => "Alert"
        };
    }

    private static string FormatGaugeValue(MaterializedGaugeReportItem item)
    {
        if (!item.Value.HasValue)
        {
            return string.Empty;
        }

        if (item.Maximum.HasValue)
        {
            return $"{item.Value.Value:0.##} / {item.Maximum.Value:0.##}";
        }

        if (item.TargetValue.HasValue)
        {
            return $"{item.Value.Value:0.##} ({item.TargetValue.Value:0.##})";
        }

        return item.Value.Value.ToString("0.##");
    }

    private static DocColor ResolveGaugeStateColor(double? value)
    {
        if (!value.HasValue)
        {
            return new DocColor(120, 120, 120);
        }

        return value.Value switch
        {
            >= 1d => new DocColor(98, 162, 69),
            >= 0d => new DocColor(232, 214, 46),
            _ => new DocColor(192, 67, 58)
        };
    }

    private static FloatingAnchor CreateFloatingAnchor(ReportItemBounds bounds)
    {
        return new FloatingAnchor
        {
            HorizontalReference = FloatingHorizontalReference.Margin,
            VerticalReference = FloatingVerticalReference.Margin,
            OffsetX = bounds.X,
            OffsetY = bounds.Y,
            WrapStyle = FloatingWrapStyle.None,
            WrapSide = FloatingWrapSide.Both,
            AllowOverlap = true
        };
    }

    private static void CopyAnchor(FloatingAnchor source, FloatingAnchor target)
    {
        target.HorizontalReference = source.HorizontalReference;
        target.VerticalReference = source.VerticalReference;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;
        target.OffsetX = source.OffsetX;
        target.OffsetY = source.OffsetY;
        target.WrapStyle = source.WrapStyle;
        target.WrapSide = source.WrapSide;
        target.BehindText = source.BehindText;
        target.AllowOverlap = source.AllowOverlap;
        target.ZOrder = source.ZOrder;
        target.Distance = source.Distance;
        target.AnchorOffset = source.AnchorOffset;
    }

    private static TextStyle? CreateInlineStyle(MaterializedReportStyle? style)
    {
        if (style is null)
        {
            return null;
        }

        var textStyle = new TextStyle();
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            textStyle.FontFamily = style.FontFamily;
        }

        if (style.FontSize.HasValue)
        {
            textStyle.FontSize = style.FontSize.Value;
        }

        if (style.Bold == true)
        {
            textStyle.FontWeight = DocFontWeight.Bold;
        }

        if (style.Italic == true)
        {
            textStyle.FontStyle = DocFontStyle.Italic;
        }

        if (ReportDocumentColorParser.TryParse(style.Foreground, out var foreground))
        {
            textStyle.Color = foreground;
        }

        if (ReportDocumentColorParser.TryParse(style.Background, out var highlight))
        {
            textStyle.HighlightColor = highlight;
        }

        return textStyle;
    }

    private static void ApplyPageSettings(
        ReportPageSettings source,
        SectionProperties target)
    {
        target.PageWidth = source.Width;
        target.PageHeight = source.Height;
        target.Orientation = source.Orientation == ReportPageOrientation.Landscape
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
        target.MarginLeft = source.MarginLeft;
        target.MarginTop = source.MarginTop;
        target.MarginRight = source.MarginRight;
        target.MarginBottom = source.MarginBottom;
        target.ColumnCount = source.ColumnCount;
        target.ColumnGap = source.ColumnGap;
        target.ColumnEqualWidth = true;
    }

    private static Document CreateEmptyDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(
            document.SectionProperties,
            document.Header,
            document.Footer,
            document.FirstHeader,
            document.FirstFooter,
            document.EvenHeader,
            document.EvenFooter));
        return document;
    }
}
