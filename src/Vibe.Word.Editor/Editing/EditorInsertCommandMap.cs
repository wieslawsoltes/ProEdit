using System.Globalization;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorInsertCommandMap
{
    private const int MaxTableRows = 12;
    private const int MaxTableColumns = 12;
    private const float DefaultImageWidth = 240f;
    private const float DefaultImageHeight = 160f;
    private const float DefaultIconSize = 96f;
    private const float DefaultShapeWidth = 160f;
    private const float DefaultShapeHeight = 120f;
    private const float DefaultChartWidth = 260f;
    private const float DefaultChartHeight = 180f;
    private const float DefaultSmartArtWidth = 260f;
    private const float DefaultSmartArtHeight = 180f;
    private const float DefaultTextBoxWidth = 200f;
    private const float DefaultTextBoxHeight = 120f;
    private const int DefaultDropCapLines = 3;
    private const float DefaultTableBorderThickness = 1f;
    private const float DefaultTableCellPadding = 4f;
    private const string DefaultSymbol = "\u03A9";
    private const string DefaultHyperlink = "https://example.com";

    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private int _bookmarkCounter;

    public EditorInsertCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _bookmarkCounter = FindNextBookmarkId(session.Document);
    }

    public void Register()
    {
        RegisterPagesCommands();
        RegisterTablesCommands();
        RegisterIllustrationsCommands();
        RegisterLinksCommands();
        RegisterHeaderFooterCommands();
        RegisterTextCommands();
        RegisterSymbolsCommands();
    }

    private void RegisterPagesCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.Pages.CoverPage, (_, payload) => InsertCoverPage(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Pages.BlankPage, (_, __) => InsertBlankPage(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Pages.PageBreak, (_, __) => InsertPageBreak(), (context, _) => HasParagraphs(context));
    }

    private void RegisterTablesCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.Tables.InsertTable, (_, payload) => InsertTable(payload), (context, _) => HasParagraphs(context));
    }

    private void RegisterIllustrationsCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Pictures, (_, payload) => InsertPicture(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Shapes, (_, payload) => InsertShape(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Icons, (_, payload) => InsertIcon(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Models3D, (_, __) => InsertModel3D(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.SmartArt, (_, payload) => InsertSmartArt(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Chart, (_, payload) => InsertChart(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Screenshot, (_, __) => InsertScreenshot(), (context, _) => HasParagraphs(context));
    }

    private void RegisterLinksCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.Links.Hyperlink, (_, payload) => InsertHyperlink(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Links.Bookmark, (_, payload) => InsertBookmark(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Links.CrossReference, (_, __) => InsertCrossReference(), (context, _) => HasParagraphs(context));
    }

    private void RegisterHeaderFooterCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.HeaderFooter.Header, (_, payload) => InsertHeader(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.HeaderFooter.Footer, (_, payload) => InsertFooter(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.HeaderFooter.PageNumber, (_, payload) => InsertPageNumber(payload), (context, _) => HasParagraphs(context));
    }

    private void RegisterTextCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.Text.TextBox, (_, payload) => InsertTextBox(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.QuickParts, (_, payload) => InsertQuickParts(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.WordArt, (_, payload) => InsertWordArt(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.DropCap, (_, payload) => InsertDropCap(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.SignatureLine, (_, __) => InsertPlaceholderText("Signature Line"), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.DateTime, (_, payload) => InsertDateTime(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.Object, (_, __) => InsertEmbeddedObject(), (context, _) => HasParagraphs(context));
    }

    private void RegisterSymbolsCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.Symbols.Equation, (_, __) => InsertEquation(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Symbols.Symbol, (_, payload) => InsertSymbol(payload), (context, _) => HasParagraphs(context));
    }

    private bool HasParagraphs(RibbonContextSnapshot? context)
    {
        if (context.HasValue && context.Value.Selection.Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

        return _session.Document.ParagraphCount > 0;
    }

    private void InsertCoverPage(object? payload)
    {
        var label = payload as string;
        var title = string.IsNullOrWhiteSpace(label) ? "Cover Page" : $"Cover Page - {label}";
        _session.InsertBlock(new PageBreakBlock());
        _session.InsertText(title.AsSpan());
        _session.InsertParagraphBreak();
    }

    private void InsertBlankPage()
    {
        _session.InsertBlock(new PageBreakBlock());
    }

    private void InsertPageBreak()
    {
        _session.InsertBlock(new PageBreakBlock());
    }

    private void InsertTable(object? payload)
    {
        var request = payload is EditorTableInsertRequest provided
            ? provided
            : new EditorTableInsertRequest(2, 2);
        var rows = Math.Clamp(request.Rows, 1, MaxTableRows);
        var columns = Math.Clamp(request.Columns, 1, MaxTableColumns);
        var table = BuildTable(rows, columns);
        _session.InsertBlock(table);
    }

    private void InsertPicture(object? payload)
    {
        if (payload is EditorImageInsertRequest request)
        {
            var data = request.Data ?? Array.Empty<byte>();
            var width = request.Width > 0 ? request.Width : DefaultImageWidth;
            var height = request.Height > 0 ? request.Height : DefaultImageHeight;
            var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "image/png" : request.ContentType;
            _session.InsertInline(new ImageInline(data, width, height, contentType));
            return;
        }

        if (payload is string label && !string.IsNullOrWhiteSpace(label))
        {
            var image = new ImageInline(Array.Empty<byte>(), DefaultImageWidth, DefaultImageHeight, "image/png")
            {
                EmbeddedObject = new EmbeddedObjectInfo
                {
                    ProgId = label,
                    ContentType = "image/png"
                }
            };
            _session.InsertInline(image);
            return;
        }

        _session.InsertInline(new ImageInline(Array.Empty<byte>(), DefaultImageWidth, DefaultImageHeight, "image/png"));
    }

    private void InsertShape(object? payload)
    {
        var preset = payload as string;
        _session.InsertInline(CreateDefaultShape("Shape", preset));
    }

    private void InsertIcon(object? payload)
    {
        var label = payload as string;
        var image = new ImageInline(Array.Empty<byte>(), DefaultIconSize, DefaultIconSize, "image/svg+xml");
        if (!string.IsNullOrWhiteSpace(label))
        {
            image.EmbeddedObject = new EmbeddedObjectInfo
            {
                ProgId = label,
                ContentType = "image/svg+xml"
            };
        }

        _session.InsertInline(image);
    }

    private void InsertModel3D()
    {
        var image = new ImageInline(Array.Empty<byte>(), DefaultImageWidth, DefaultImageHeight, "model/3d");
        image.EmbeddedObject = new EmbeddedObjectInfo
        {
            ProgId = "3D Model",
            ContentType = "model/3d"
        };
        _session.InsertInline(image);
    }

    private void InsertSmartArt(object? payload)
    {
        var image = new ImageInline(
            Array.Empty<byte>(),
            DefaultSmartArtWidth,
            DefaultSmartArtHeight,
            "application/vnd.openxmlformats-officedocument.drawingml.diagram");
        image.Diagram = new DiagramInfo
        {
            LayoutRelationshipId = payload as string
        };
        _session.InsertInline(image);
    }

    private void InsertChart(object? payload)
    {
        EditorChartInsertRequest request = payload is EditorChartInsertRequest provided
            ? provided
            : new EditorChartInsertRequest(ChartType.Bar);
        var model = BuildSampleChartModel(request);
        _session.InsertInline(new ChartInline(DefaultChartWidth, DefaultChartHeight, model, null));
    }

    private void InsertScreenshot()
    {
        _session.InsertInline(new ImageInline(Array.Empty<byte>(), DefaultImageWidth, DefaultImageHeight, "image/png"));
    }

    private void InsertHyperlink(object? payload)
    {
        EditorHyperlinkInsertRequest request = payload is EditorHyperlinkInsertRequest provided
            ? provided
            : new EditorHyperlinkInsertRequest(DefaultHyperlink, null, null, null);

        var displayText = string.IsNullOrWhiteSpace(request.DisplayText)
            ? (string.IsNullOrWhiteSpace(request.Uri) ? "Link" : request.Uri)
            : request.DisplayText;

        var hyperlinkStyle = new TextStyleProperties
        {
            Color = DocColor.FromArgb(255, 0, 102, 204),
            Underline = true,
            UnderlineStyle = DocUnderlineStyle.Single
        };
        var run = new RunInline(displayText, hyperlinkStyle)
        {
            Hyperlink = new HyperlinkInfo(request.Uri, request.Anchor, request.Tooltip)
        };
        _session.InsertInline(run);
    }

    private void InsertBookmark(object? payload)
    {
        var name = payload is EditorBookmarkInsertRequest provided && !string.IsNullOrWhiteSpace(provided.Name)
            ? provided.Name
            : $"Bookmark{_bookmarkCounter}";
        var id = _bookmarkCounter++;
        var inlines = new Inline[]
        {
            new BookmarkStartInline(id, name),
            new RunInline(name),
            new BookmarkEndInline(id)
        };
        _session.InsertInlines(inlines);
    }

    private void InsertCrossReference()
    {
        InsertPlaceholderText("Cross-reference");
    }

    private void InsertHeader(object? payload)
    {
        InsertHeaderFooter(_session.Document.Header, "Header", payload as string);
        _session.RefreshLayout();
    }

    private void InsertFooter(object? payload)
    {
        InsertHeaderFooter(_session.Document.Footer, "Footer", payload as string);
        _session.RefreshLayout();
    }

    private void InsertPageNumber(object? payload)
    {
        var request = payload is EditorPageNumberInsertRequest provided
            ? provided
            : new EditorPageNumberInsertRequest(true, false);
        var target = request.InFooter ? _session.Document.Footer : _session.Document.Header;
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new PageNumberInline());
        if (request.IncludeTotalPages)
        {
            paragraph.Inlines.Add(new RunInline(" / "));
            paragraph.Inlines.Add(new TotalPagesInline());
        }

        target.Blocks.Add(paragraph);
        _session.RefreshLayout();
    }

    private void InsertTextBox(object? payload)
    {
        var label = payload as string;
        var text = string.IsNullOrWhiteSpace(label) ? "Text Box" : $"{label} Text Box";
        var textBox = new ShapeTextBox();
        textBox.Blocks.Add(new ParagraphBlock(text));
        var shape = new ShapeInline(DefaultTextBoxWidth, DefaultTextBoxHeight, CreateDefaultShapeProperties("roundrect"), textBox, "Text Box");
        _session.InsertInline(shape);
    }

    private void InsertQuickParts(object? payload)
    {
        var label = payload as string;
        var text = string.IsNullOrWhiteSpace(label) ? "Quick Parts" : $"Quick Parts - {label}";
        InsertPlaceholderText(text);
    }

    private void InsertWordArt(object? payload)
    {
        var label = payload as string;
        var text = string.IsNullOrWhiteSpace(label) ? "WordArt" : $"WordArt - {label}";
        var textBox = new ShapeTextBox();
        textBox.Blocks.Add(new ParagraphBlock(text));
        var properties = CreateDefaultShapeProperties("roundrect");
        if (string.Equals(label, "Outline", StringComparison.OrdinalIgnoreCase))
        {
            properties.FillColor = DocColor.Transparent;
            properties.Outline = new BorderLine
            {
                Style = DocBorderStyle.Single,
                Thickness = 1.5f,
                Color = new DocColor(0, 120, 212)
            };
        }
        else
        {
            properties.FillColor = new DocColor(242, 242, 242);
        }

        var shape = new ShapeInline(DefaultTextBoxWidth, DefaultTextBoxHeight, properties, textBox, "WordArt");
        _session.InsertInline(shape);
    }

    private void InsertDropCap(object? payload)
    {
        var mode = payload as string;
        var kind = string.Equals(mode, "margin", StringComparison.OrdinalIgnoreCase)
            ? DropCapKind.Margin
            : DropCapKind.Drop;
        var paragraph = _session.Document.GetParagraph(_session.Caret.ParagraphIndex);
        paragraph.Properties.DropCap = new DropCapSettings
        {
            Kind = kind,
            Lines = DefaultDropCapLines
        };
        _session.RefreshLayout();
    }

    private void InsertDateTime(object? payload)
    {
        var mode = payload as string;
        var format = string.Equals(mode, "long", StringComparison.OrdinalIgnoreCase)
            ? "dddd, MMMM d, yyyy"
            : "yyyy-MM-dd";
        var text = DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
        _session.InsertText(text.AsSpan());
    }

    private void InsertEmbeddedObject()
    {
        var image = new ImageInline(Array.Empty<byte>(), DefaultImageWidth, DefaultImageHeight, "application/octet-stream")
        {
            EmbeddedObject = new EmbeddedObjectInfo
            {
                ProgId = "Embedded Object",
                ContentType = "application/octet-stream"
            }
        };
        _session.InsertInline(image);
    }

    private void InsertEquation()
    {
        var row = new MathRow();
        row.Elements.Add(new MathRun { Text = "x = 1" });
        _session.InsertEquation(row);
    }

    private void InsertSymbol(object? payload)
    {
        var symbol = payload as string;
        var value = string.IsNullOrWhiteSpace(symbol) ? DefaultSymbol : symbol!;
        _session.InsertText(value.AsSpan());
    }

    private void InsertPlaceholderText(string text)
    {
        _session.InsertText(text.AsSpan());
    }

    private static TableBlock BuildTable(int rows, int columns)
    {
        var table = new TableBlock();
        table.Properties.CellPadding = DocThickness.Uniform(DefaultTableCellPadding);
        var borderLine = new BorderLine
        {
            Style = DocBorderStyle.Single,
            Thickness = DefaultTableBorderThickness,
            Color = DocColor.Black
        };
        table.Properties.Borders.Top = borderLine.Clone();
        table.Properties.Borders.Bottom = borderLine.Clone();
        table.Properties.Borders.Left = borderLine.Clone();
        table.Properties.Borders.Right = borderLine.Clone();
        table.Properties.Borders.InsideHorizontal = borderLine.Clone();
        table.Properties.Borders.InsideVertical = borderLine.Clone();
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var row = new TableRow();
            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                var cell = new TableCell();
                cell.Paragraphs.Add(new ParagraphBlock());
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static ShapeInline CreateDefaultShape(string name, string? preset)
    {
        return new ShapeInline(DefaultShapeWidth, DefaultShapeHeight, CreateDefaultShapeProperties(preset), null, name);
    }

    private static ShapeProperties CreateDefaultShapeProperties(string? preset)
    {
        return new ShapeProperties
        {
            PresetGeometry = string.IsNullOrWhiteSpace(preset) ? "rect" : preset,
            FillColor = DocColor.White,
            Outline = new BorderLine
            {
                Style = DocBorderStyle.Single,
                Thickness = 1f,
                Color = DocColor.Black
            }
        };
    }

    private static void InsertHeaderFooter(HeaderFooter target, string label, string? textOverride)
    {
        if (textOverride is not null)
        {
            target.Blocks.Clear();
            target.Blocks.Add(new ParagraphBlock(textOverride));
            return;
        }

        target.Blocks.Add(new ParagraphBlock(label));
    }

    private static int FindNextBookmarkId(Document document)
    {
        var maxId = 0;
        foreach (var block in document.Blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                maxId = Math.Max(maxId, FindBookmarkMax(paragraph));
                continue;
            }

            if (block is TableBlock table)
            {
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var paragraphInCell in cell.Paragraphs)
                        {
                            maxId = Math.Max(maxId, FindBookmarkMax(paragraphInCell));
                        }
                    }
                }
            }
        }

        return maxId + 1;
    }

    private static int FindBookmarkMax(ParagraphBlock paragraph)
    {
        var max = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is BookmarkStartInline bookmarkStart)
            {
                max = Math.Max(max, bookmarkStart.Id);
            }
        }

        return max;
    }

    private static ChartModel BuildSampleChartModel(EditorChartInsertRequest request)
    {
        var type = request.Type == ChartType.Unknown ? ChartType.Bar : request.Type;
        var title = request.Title ?? $"{type} Chart";
        var model = new ChartModel
        {
            Type = type,
            Title = title,
            BarDirection = request.BarDirection ?? ChartBarDirection.Column,
            Stacking = request.Stacking ?? ChartStacking.None
        };

        var seriesDefault = type switch
        {
            ChartType.Pie or ChartType.Doughnut => 1,
            ChartType.Scatter or ChartType.Bubble => 2,
            ChartType.Radar => 2,
            _ => 2
        };
        var categoryDefault = type switch
        {
            ChartType.Pie or ChartType.Doughnut => 4,
            ChartType.Scatter or ChartType.Bubble => 6,
            _ => 5
        };
        var seriesCount = ClampCount(request.SeriesCount, seriesDefault, 6);
        var categoryCount = ClampCount(request.CategoryCount, categoryDefault, 8);

        switch (type)
        {
            case ChartType.Pie:
            case ChartType.Doughnut:
            {
                seriesCount = 1;
                var series = new ChartSeries { Name = "Series 1" };
                for (var i = 0; i < categoryCount; i++)
                {
                    series.Points.Add(new ChartPoint
                    {
                        Category = CategoryLabel(i, categoryCount),
                        Value = GenerateValue(0, i)
                    });
                }

                model.Series.Add(series);
                break;
            }
            case ChartType.Scatter:
            {
                for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
                {
                    var series = new ChartSeries { Name = $"Series {seriesIndex + 1}" };
                    for (var i = 0; i < categoryCount; i++)
                    {
                        series.Points.Add(new ChartPoint
                        {
                            XValue = i + 1,
                            Value = GenerateValue(seriesIndex, i)
                        });
                    }

                    model.Series.Add(series);
                }

                break;
            }
            case ChartType.Bubble:
            {
                for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
                {
                    var series = new ChartSeries { Name = $"Series {seriesIndex + 1}" };
                    for (var i = 0; i < categoryCount; i++)
                    {
                        series.Points.Add(new ChartPoint
                        {
                            XValue = i + 1,
                            Value = GenerateValue(seriesIndex, i),
                            Size = 8 + (seriesIndex + i) * 2
                        });
                    }

                    model.Series.Add(series);
                }

                break;
            }
            case ChartType.Line:
            case ChartType.Area:
            case ChartType.Radar:
            case ChartType.Bar:
            default:
            {
                for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
                {
                    var series = new ChartSeries { Name = $"Series {seriesIndex + 1}" };
                    for (var i = 0; i < categoryCount; i++)
                    {
                        series.Points.Add(new ChartPoint
                        {
                            Category = CategoryLabel(i, categoryCount),
                            Value = GenerateValue(seriesIndex, i)
                        });
                    }

                    model.Series.Add(series);
                }

                break;
            }
        }

        return model;

        static int ClampCount(int? value, int fallback, int max)
        {
            var resolved = value.GetValueOrDefault(fallback);
            return Math.Clamp(resolved, 1, max);
        }

        static string CategoryLabel(int index, int total)
        {
            if (total <= 4)
            {
                return $"Q{index + 1}";
            }

            if (total <= 6)
            {
                return $"Cat {index + 1}";
            }

            return $"Item {index + 1}";
        }

        static double GenerateValue(int seriesIndex, int categoryIndex)
        {
            var baseValue = 12 + seriesIndex * 6;
            var variation = categoryIndex * 3 + ((seriesIndex + categoryIndex) % 2 == 0 ? 2 : -1);
            return baseValue + variation;
        }
    }
}
