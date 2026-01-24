using System.Globalization;
using System.Text;
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
    private const float DefaultSignatureLineWidth = 220f;
    private const float DefaultSignatureLineHeight = 12f;
    private const int DefaultDropCapLines = 3;
    private const float DefaultTableBorderThickness = 1f;
    private const float DefaultTableCellPadding = 4f;
    private const string DefaultSymbol = "\u03A9";
    private const string DefaultHyperlink = "https://example.com";

    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private int _bookmarkCounter;
    private int _contentControlCounter;

    public EditorInsertCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _bookmarkCounter = FindNextBookmarkId(session.Document);
        _contentControlCounter = FindNextContentControlId(session.Document);
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
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Models3D, (_, payload) => InsertModel3D(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.SmartArt, (_, payload) => InsertSmartArt(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Chart, (_, payload) => InsertChart(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Illustrations.Screenshot, (_, payload) => InsertScreenshot(payload), (context, _) => HasParagraphs(context));
    }

    private void RegisterLinksCommands()
    {
        _router.RegisterAction(EditorInsertCommandIds.Links.Hyperlink, (_, payload) => InsertHyperlink(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Links.Bookmark, (_, payload) => InsertBookmark(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Links.CrossReference, (_, payload) => InsertCrossReference(payload), (context, _) => HasParagraphs(context));
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
        _router.RegisterAction(EditorInsertCommandIds.Text.SignatureLine, (_, __) => InsertSignatureLine(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.DateTime, (_, payload) => InsertDateTime(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorInsertCommandIds.Text.Object, (_, payload) => InsertEmbeddedObject(payload), (context, _) => HasParagraphs(context));
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
        _session.InsertBlock(new PageBreakBlock());
        InsertCoverPagePlaceholder("Title", label);
        InsertCoverPagePlaceholder("Subtitle", label);
        InsertCoverPagePlaceholder("Author", label);
        InsertDateField("MMMM d, yyyy");
        _session.InsertParagraphBreak();
        _session.InsertBlock(new PageBreakBlock());
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
            var image = CreateImageInline(request, DefaultImageWidth, DefaultImageHeight);
            _session.InsertInline(image);
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
        var svgData = BuildIconSvgData(label);
        var image = new ImageInline(svgData, DefaultIconSize, DefaultIconSize, "image/svg+xml");
        if (!string.IsNullOrWhiteSpace(label))
        {
            image.EmbeddedObject = new EmbeddedObjectInfo
            {
                ProgId = label,
                ContentType = image.ContentType
            };
        }

        _session.InsertInline(image);
    }

    private void InsertModel3D(object? payload)
    {
        var request = payload is EditorEmbeddedObjectInsertRequest provided
            ? provided
            : default;

        var image = CreateEmbeddedObjectInline(
            request,
            DefaultImageWidth,
            DefaultImageHeight,
            "3D Model",
            "model/3d");
        _session.InsertInline(image);
    }

    private void InsertSmartArt(object? payload)
    {
        var layoutName = payload as string;
        var image = new ImageInline(
            Array.Empty<byte>(),
            DefaultSmartArtWidth,
            DefaultSmartArtHeight,
            "application/vnd.openxmlformats-officedocument.drawingml.diagram");
        image.Diagram = BuildSmartArtDiagram(layoutName);
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

    private void InsertScreenshot(object? payload)
    {
        if (payload is EditorImageInsertRequest request)
        {
            var image = CreateImageInline(request, DefaultImageWidth, DefaultImageHeight);
            image.EmbeddedObject = new EmbeddedObjectInfo
            {
                ProgId = "Screenshot",
                ContentType = image.ContentType
            };
            _session.InsertInline(image);
            return;
        }

        var placeholder = new ImageInline(Array.Empty<byte>(), DefaultImageWidth, DefaultImageHeight, "image/png")
        {
            EmbeddedObject = new EmbeddedObjectInfo
            {
                ProgId = "Screenshot",
                ContentType = "image/png"
            }
        };
        _session.InsertInline(placeholder);
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

    private void InsertCrossReference(object? payload)
    {
        var request = payload is EditorCrossReferenceInsertRequest provided
            ? provided
            : new EditorCrossReferenceInsertRequest(payload as string);

        var bookmarkName = !string.IsNullOrWhiteSpace(request.BookmarkName)
            ? request.BookmarkName
            : FindFirstBookmarkName(_session.Document);
        if (string.IsNullOrWhiteSpace(bookmarkName))
        {
            InsertPlaceholderText("Cross-reference");
            return;
        }

        var displayText = ResolveBookmarkDisplayText(_session.Document, bookmarkName) ?? bookmarkName;
        var instruction = request.IncludePageNumber
            ? $"PAGEREF \"{bookmarkName}\""
            : $"REF \"{bookmarkName}\"";
        if (request.Hyperlink)
        {
            instruction += " \\h";
        }

        InsertField(instruction, displayText);
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
        target.IsDefined = true;
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
        if (string.Equals(label, "AutoText", StringComparison.OrdinalIgnoreCase))
        {
            InsertContentControlPlaceholder("AutoText");
            return;
        }

        if (string.Equals(label, "Field", StringComparison.OrdinalIgnoreCase))
        {
            InsertField("FIELD", "Field");
            return;
        }

        if (string.Equals(label, "DocProperty", StringComparison.OrdinalIgnoreCase))
        {
            InsertField("DOCPROPERTY \"Title\"", "Title");
            return;
        }

        InsertPlaceholderText(string.IsNullOrWhiteSpace(label) ? "Quick Parts" : $"Quick Parts - {label}");
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

    private void InsertSignatureLine()
    {
        _session.InsertText("Signature: ".AsSpan());
        var signatureLine = CreateSignatureLine(DefaultSignatureLineWidth);
        _session.InsertInline(signatureLine);
        _session.InsertParagraphBreak();

        _session.InsertText("Date: ".AsSpan());
        var dateLine = CreateSignatureLine(DefaultSignatureLineWidth * 0.7f);
        _session.InsertInline(dateLine);
    }

    private void InsertDateTime(object? payload)
    {
        var mode = payload as string;
        var format = string.Equals(mode, "long", StringComparison.OrdinalIgnoreCase)
            ? "dddd, MMMM d, yyyy"
            : "yyyy-MM-dd";
        InsertDateField(format);
    }

    private void InsertEmbeddedObject(object? payload)
    {
        var request = payload is EditorEmbeddedObjectInsertRequest provided
            ? provided
            : default;

        var image = CreateEmbeddedObjectInline(
            request,
            DefaultImageWidth,
            DefaultImageHeight,
            "Embedded Object",
            "application/octet-stream");
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

    private void InsertHeaderFooter(HeaderFooter target, string label, string? textOverride)
    {
        target.Blocks.Clear();
        target.IsDefined = true;

        if (!string.IsNullOrWhiteSpace(textOverride))
        {
            target.Blocks.Add(new ParagraphBlock(textOverride));
            return;
        }

        var paragraph = new ParagraphBlock();
        paragraph.Inlines.AddRange(BuildPlaceholderInlines(label, "HeaderFooter"));
        target.Blocks.Add(paragraph);
    }

    private void InsertCoverPagePlaceholder(string placeholder, string? template)
    {
        var tag = string.IsNullOrWhiteSpace(template) ? "CoverPage" : $"CoverPage:{template}";
        InsertContentControlPlaceholder(placeholder, tag);
        _session.InsertParagraphBreak();
    }

    private void InsertContentControlPlaceholder(string placeholderText, string? tag = null)
    {
        var inlines = BuildPlaceholderInlines(placeholderText, tag);
        _session.InsertInlines(inlines);
    }

    private void InsertDateField(string format)
    {
        var text = DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
        var instruction = $"DATE \\@ \"{format}\"";
        InsertField(instruction, text);
    }

    private void InsertField(string instruction, string resultText)
    {
        var inlines = BuildFieldInlines(instruction, resultText);
        _session.InsertInlines(inlines);
    }

    private Inline[] BuildPlaceholderInlines(string placeholderText, string? tag)
    {
        var properties = CreateContentControlProperties(placeholderText, tag);
        return new Inline[]
        {
            new ContentControlStartInline(properties),
            new ContentControlEndInline(properties.Id)
        };
    }

    private ContentControlProperties CreateContentControlProperties(string placeholderText, string? tag)
    {
        return new ContentControlProperties
        {
            Id = _contentControlCounter++,
            Kind = ContentControlKind.Run,
            Tag = tag,
            PlaceholderText = placeholderText,
            ShowingPlaceholder = true
        };
    }

    private static Inline[] BuildFieldInlines(string instruction, string resultText)
    {
        var start = new FieldStartInline(instruction)
        {
            Definition = FieldInstructionParser.Parse(instruction)
        };
        return new Inline[]
        {
            start,
            new FieldSeparatorInline(),
            new RunInline(resultText),
            new FieldEndInline()
        };
    }

    private static ShapeInline CreateSignatureLine(float width)
    {
        var properties = CreateDefaultShapeProperties("line");
        properties.FillColor = DocColor.Transparent;
        if (properties.Outline is not null)
        {
            properties.Outline.Thickness = 1.2f;
        }

        return new ShapeInline(width, DefaultSignatureLineHeight, properties, null, "Signature Line");
    }

    private static ImageInline CreateImageInline(EditorImageInsertRequest request, float defaultWidth, float defaultHeight)
    {
        var data = request.Data ?? Array.Empty<byte>();
        var width = request.Width > 0 ? request.Width : defaultWidth;
        var height = request.Height > 0 ? request.Height : defaultHeight;
        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "image/png" : request.ContentType;
        return new ImageInline(data, width, height, contentType!);
    }

    private static ImageInline CreateEmbeddedObjectInline(
        EditorEmbeddedObjectInsertRequest request,
        float width,
        float height,
        string defaultProgId,
        string defaultContentType)
    {
        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? defaultContentType : request.ContentType!;
        var image = new ImageInline(Array.Empty<byte>(), width, height, contentType);
        var embedded = new EmbeddedObjectInfo
        {
            ProgId = string.IsNullOrWhiteSpace(request.ProgId) ? defaultProgId : request.ProgId,
            ContentType = contentType
        };

        if (!string.IsNullOrWhiteSpace(request.TargetUri))
        {
            embedded.TargetUri = request.TargetUri;
        }

        if (request.Data is { Length: > 0 })
        {
            embedded.Data = request.Data;
        }

        if (request.IsLinked)
        {
            embedded.IsLinked = true;
        }

        image.EmbeddedObject = embedded;
        return image;
    }

    private static DiagramInfo BuildSmartArtDiagram(string? layoutName)
    {
        var normalized = ResolveSmartArtLayoutName(layoutName);
        var nodes = normalized switch
        {
            "Process" => new[] { "Step 1", "Step 2", "Step 3", "Step 4" },
            "Cycle" => new[] { "Phase 1", "Phase 2", "Phase 3", "Phase 4" },
            "Hierarchy" => new[] { "Root", "Child 1", "Child 2", "Child 3" },
            "Matrix" => new[] { "Item A", "Item B", "Item C", "Item D" },
            _ => new[] { "Item 1", "Item 2", "Item 3" }
        };

        var dataXml = BuildSmartArtData(nodes, normalized);
        var layoutXml = BuildSmartArtLayoutPart(normalized);
        return new DiagramInfo
        {
            DataPart = Encoding.UTF8.GetBytes(dataXml),
            LayoutPart = Encoding.UTF8.GetBytes(layoutXml)
        };
    }

    private static string ResolveSmartArtLayoutName(string? layoutName)
    {
        if (string.IsNullOrWhiteSpace(layoutName))
        {
            return "List";
        }

        return layoutName.Trim() switch
        {
            "Process" => "Process",
            "Cycle" => "Cycle",
            "Hierarchy" => "Hierarchy",
            "Matrix" => "Matrix",
            "Pyramid" => "Matrix",
            "Relationship" => "Cycle",
            "List" => "List",
            _ => "List"
        };
    }

    private static string BuildSmartArtData(IReadOnlyList<string> nodes, string layoutName)
    {
        var builder = new StringBuilder();
        builder.Append("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" ");
        builder.Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">");

        for (var i = 0; i < nodes.Count; i++)
        {
            var id = $"n{i + 1}";
            builder.Append("<dgm:pt modelId=\"").Append(id).Append("\">");
            builder.Append("<dgm:t><a:t>");
            builder.Append(EscapeXml(nodes[i]));
            builder.Append("</a:t></dgm:t></dgm:pt>");
        }

        if (nodes.Count > 1)
        {
            if (layoutName == "Hierarchy")
            {
                for (var i = 1; i < nodes.Count; i++)
                {
                    builder.Append("<dgm:cxn srcId=\"n1\" destId=\"n").Append(i + 1).Append("\"/>");
                }
            }
            else
            {
                for (var i = 1; i < nodes.Count; i++)
                {
                    builder.Append("<dgm:cxn srcId=\"n").Append(i).Append("\" destId=\"n").Append(i + 1).Append("\"/>");
                }

                if (layoutName == "Cycle" && nodes.Count > 2)
                {
                    builder.Append("<dgm:cxn srcId=\"n").Append(nodes.Count).Append("\" destId=\"n1\"/>");
                }
            }
        }

        builder.Append("</dgm:dataModel>");
        return builder.ToString();
    }

    private static string BuildSmartArtLayoutPart(string layoutName)
    {
        return $"<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" name=\"{layoutName.ToLowerInvariant()}\"/>";
    }

    private static byte[] BuildIconSvgData(string? label)
    {
        var glyph = ResolveIconGlyph(label);
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\" viewBox=\"0 0 64 64\">" +
                  "<rect x=\"2\" y=\"2\" width=\"60\" height=\"60\" rx=\"12\" ry=\"12\" fill=\"#F2F2F2\" stroke=\"#3A3A3A\" stroke-width=\"2\"/>" +
                  $"<text x=\"32\" y=\"40\" font-size=\"24\" text-anchor=\"middle\" fill=\"#3A3A3A\" font-family=\"Arial\">{EscapeXml(glyph)}</text>" +
                  "</svg>";
        return Encoding.UTF8.GetBytes(svg);
    }

    private static string ResolveIconGlyph(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "I";
        }

        var trimmed = label.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "INFO" => "i",
            "ALERT" => "!",
            "CHECK" => "OK",
            "STAR" => "*",
            "CALENDAR" => "C",
            "SEARCH" => "Q",
            "LOCK" => "L",
            "GLOBE" => "G",
            "USER" => "U",
            "SETTINGS" => "S",
            _ => trimmed.Length > 1 ? trimmed.AsSpan(0, 1).ToString().ToUpperInvariant() : trimmed.ToUpperInvariant()
        };
    }

    private static string? FindFirstBookmarkName(Document document)
    {
        foreach (var name in EnumerateBookmarkNames(document))
        {
            return name;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateBookmarkNames(Document document)
    {
        foreach (var block in document.Blocks)
        {
            foreach (var name in EnumerateBookmarkNames(block))
            {
                yield return name;
            }
        }

        foreach (var name in EnumerateBookmarkNames(document.Header))
        {
            yield return name;
        }

        foreach (var name in EnumerateBookmarkNames(document.Footer))
        {
            yield return name;
        }
    }

    private static IEnumerable<string> EnumerateBookmarkNames(Block block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                foreach (var name in EnumerateBookmarkNames(paragraph))
                {
                    yield return name;
                }

                break;
            case TableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var paragraph in cell.Paragraphs)
                        {
                            foreach (var name in EnumerateBookmarkNames(paragraph))
                            {
                                yield return name;
                            }
                        }
                    }
                }

                break;
        }
    }

    private static IEnumerable<string> EnumerateBookmarkNames(ParagraphBlock paragraph)
    {
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is BookmarkStartInline bookmarkStart && !string.IsNullOrWhiteSpace(bookmarkStart.Name))
            {
                yield return bookmarkStart.Name;
            }
        }
    }

    private static IEnumerable<string> EnumerateBookmarkNames(HeaderFooter headerFooter)
    {
        foreach (var block in headerFooter.Blocks)
        {
            foreach (var name in EnumerateBookmarkNames(block))
            {
                yield return name;
            }
        }
    }

    private static string? ResolveBookmarkDisplayText(Document document, string bookmarkName)
    {
        foreach (var block in document.Blocks)
        {
            var text = ResolveBookmarkDisplayText(block, bookmarkName);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var headerText = ResolveBookmarkDisplayText(document.Header, bookmarkName);
        if (!string.IsNullOrWhiteSpace(headerText))
        {
            return headerText;
        }

        var footerText = ResolveBookmarkDisplayText(document.Footer, bookmarkName);
        if (!string.IsNullOrWhiteSpace(footerText))
        {
            return footerText;
        }

        return null;
    }

    private static string? ResolveBookmarkDisplayText(Block block, string bookmarkName)
    {
        return block switch
        {
            ParagraphBlock paragraph => ResolveBookmarkDisplayText(paragraph, bookmarkName),
            TableBlock table => ResolveBookmarkDisplayText(table, bookmarkName),
            _ => null
        };
    }

    private static string? ResolveBookmarkDisplayText(TableBlock table, string bookmarkName)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var paragraph in cell.Paragraphs)
                {
                    var text = ResolveBookmarkDisplayText(paragraph, bookmarkName);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        return null;
    }

    private static string? ResolveBookmarkDisplayText(HeaderFooter headerFooter, string bookmarkName)
    {
        foreach (var block in headerFooter.Blocks)
        {
            var text = ResolveBookmarkDisplayText(block, bookmarkName);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? ResolveBookmarkDisplayText(ParagraphBlock paragraph, string bookmarkName)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            if (paragraph.Inlines[i] is not BookmarkStartInline start || !string.Equals(start.Name, bookmarkName, StringComparison.Ordinal))
            {
                continue;
            }

            var builder = new StringBuilder();
            for (var j = i + 1; j < paragraph.Inlines.Count; j++)
            {
                var inline = paragraph.Inlines[j];
                if (inline is BookmarkEndInline end && end.Id == start.Id)
                {
                    return builder.Length > 0 ? builder.ToString() : null;
                }

                AppendInlineText(builder, inline);
            }

            return builder.Length > 0 ? builder.ToString() : null;
        }

        return null;
    }

    private static void AppendInlineText(StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case RunInline run:
                builder.Append(run.Text.GetText());
                break;
            case ImageInline:
            case ShapeInline:
            case ChartInline:
            case EquationInline:
            case PageNumberInline:
            case TotalPagesInline:
                builder.Append(DocumentConstants.ObjectReplacementChar);
                break;
        }
    }

    private static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => ch.ToString()
            });
        }

        return builder.ToString();
    }

    private static int FindNextContentControlId(Document document)
    {
        var maxId = 0;
        ScanContentControls(document.Blocks, ref maxId);
        ScanContentControls(document.Header.Blocks, ref maxId);
        ScanContentControls(document.Footer.Blocks, ref maxId);
        ScanContentControls(document.FirstHeader.Blocks, ref maxId);
        ScanContentControls(document.FirstFooter.Blocks, ref maxId);
        ScanContentControls(document.EvenHeader.Blocks, ref maxId);
        ScanContentControls(document.EvenFooter.Blocks, ref maxId);
        return maxId + 1;
    }

    private static void ScanContentControls(IEnumerable<Block> blocks, ref int maxId)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ContentControlStartBlock contentStart when contentStart.Properties.Id.HasValue:
                    maxId = Math.Max(maxId, contentStart.Properties.Id.Value);
                    break;
                case ParagraphBlock paragraph:
                    ScanContentControls(paragraph, ref maxId);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            if (cell.ContentControl?.Id is int cellId)
                            {
                                maxId = Math.Max(maxId, cellId);
                            }

                            foreach (var paragraph in cell.Paragraphs)
                            {
                                ScanContentControls(paragraph, ref maxId);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static void ScanContentControls(ParagraphBlock paragraph, ref int maxId)
    {
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is ContentControlStartInline start && start.Properties.Id.HasValue)
            {
                maxId = Math.Max(maxId, start.Properties.Id.Value);
            }
        }
    }

    private static int FindNextBookmarkId(Document document)
    {
        var maxId = 0;
        ScanBookmarks(document.Blocks, ref maxId);
        ScanBookmarks(document.Header.Blocks, ref maxId);
        ScanBookmarks(document.Footer.Blocks, ref maxId);
        ScanBookmarks(document.FirstHeader.Blocks, ref maxId);
        ScanBookmarks(document.FirstFooter.Blocks, ref maxId);
        ScanBookmarks(document.EvenHeader.Blocks, ref maxId);
        ScanBookmarks(document.EvenFooter.Blocks, ref maxId);

        return maxId + 1;
    }

    private static void ScanBookmarks(IEnumerable<Block> blocks, ref int maxId)
    {
        foreach (var block in blocks)
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
