using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.FlowDocument.IO;

internal static class XpsNativePackageConverter
{
    private const string XpsNamespaceUri = "http://schemas.microsoft.com/xps/2005/06";
    private const string OxpsNamespaceUri = "http://schemas.openxps.org/oxps/v1.0";
    private const string PackageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string FixedRepresentationRelationshipType = "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation";
    private const string FixedDocSeqContentType = "application/vnd.ms-package.xps-fixeddocumentsequence+xml";
    private const string FixedDocumentContentType = "application/vnd.ms-package.xps-fixeddocument+xml";
    private const string FixedPageContentType = "application/vnd.ms-package.xps-fixedpage+xml";

    public static Document Load(Stream source, XpsFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var archive = OpenReadArchive(source);
        var sequencePath = ResolveFixedDocumentSequencePath(archive)
                           ?? throw new InvalidDataException("XPS package does not contain a FixedDocumentSequence part.");

        var document = CreateEmptyDocument();
        document.Blocks.Clear();

        var sequence = LoadXml(archive, sequencePath);
        var documentReferences = sequence
            .Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "DocumentReference", StringComparison.Ordinal))
            .Select(static element => element.Attribute("Source")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var pageIndex = 0;
        foreach (var documentReference in documentReferences)
        {
            var fixedDocumentPath = ResolvePartPath(sequencePath, documentReference!);
            var fixedDocument = LoadXml(archive, fixedDocumentPath);
            var pageContentPaths = fixedDocument
                .Descendants()
                .Where(static element => string.Equals(element.Name.LocalName, "PageContent", StringComparison.Ordinal))
                .Select(static element => element.Attribute("Source")?.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            foreach (var pageSource in pageContentPaths)
            {
                var fixedPagePath = ResolvePartPath(fixedDocumentPath, pageSource!);
                var fixedPage = LoadXml(archive, fixedPagePath);
                AppendFixedPageContent(archive, fixedPagePath, fixedPage, document, pageIndex);
                pageIndex++;
            }
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        return document;
    }

    public static void Save(Document document, Stream target, XpsFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);

        using var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        var ns = XNamespace.Get(flavor == XpsFlavor.Oxps ? OxpsNamespaceUri : XpsNamespaceUri);

        var layoutPages = BuildLayoutPages(document);
        var pageFileNames = new List<string>(layoutPages.Count);
        var imagePartPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < layoutPages.Count; i++)
        {
            pageFileNames.Add($"/Documents/1/Pages/{i + 1}.fpage");
        }

        WritePackageRelationships(archive);
        WriteFixedDocumentSequence(archive, ns);
        WriteFixedDocument(archive, ns, pageFileNames);

        var imageCounter = 0;
        for (var i = 0; i < layoutPages.Count; i++)
        {
            WriteFixedPage(archive, layoutPages[i], pageFileNames[i], ns, imagePartPaths, ref imageCounter);
        }

        WriteContentTypes(archive, pageFileNames, imagePartPaths);
    }

    private static ZipArchive OpenReadArchive(Stream source)
    {
        if (source.CanSeek)
        {
            source.Position = 0;
            return new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }

        var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
    }

    private static string? ResolveFixedDocumentSequencePath(ZipArchive archive)
    {
        var rootRels = archive.GetEntry("_rels/.rels");
        if (rootRels is not null)
        {
            using var stream = rootRels.Open();
            var rels = XDocument.Load(stream);
            var relationships = rels.Root;
            if (relationships is not null)
            {
                var relationship = relationships
                    .Elements(XName.Get("Relationship", PackageRelationshipsNamespace))
                    .FirstOrDefault(static element =>
                        string.Equals(
                            (string?)element.Attribute("Type"),
                            FixedRepresentationRelationshipType,
                            StringComparison.OrdinalIgnoreCase));
                var target = relationship?.Attribute("Target")?.Value;
                if (!string.IsNullOrWhiteSpace(target))
                {
                    return NormalizePartPath(target!);
                }
            }
        }

        var fdseqEntry = archive.Entries.FirstOrDefault(static entry =>
            entry.FullName.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase));
        if (fdseqEntry is null)
        {
            return null;
        }

        return "/" + fdseqEntry.FullName.Replace('\\', '/');
    }

    private static XDocument LoadXml(ZipArchive archive, string partPath)
    {
        var entry = archive.GetEntry(TrimLeadingSlash(partPath))
                    ?? throw new InvalidDataException($"Missing XPS part '{partPath}'.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void AppendFixedPageContent(
        ZipArchive archive,
        string pagePath,
        XDocument fixedPage,
        Document target,
        int pageIndex)
    {
        var root = fixedPage.Root;
        if (root is null)
        {
            return;
        }

        if (pageIndex == 0)
        {
            if (TryParseFloat(root.Attribute("Width")?.Value, out var width) && width > 0f)
            {
                target.SectionProperties.PageWidth = width;
            }

            if (TryParseFloat(root.Attribute("Height")?.Value, out var height) && height > 0f)
            {
                target.SectionProperties.PageHeight = height;
            }
        }
        else
        {
            target.Blocks.Add(new PageBreakBlock());
        }

        var resources = BuildPageResources(archive, pagePath, root);
        var glyphRecords = ExtractGlyphRecords(root, resources);
        var imageRecords = ExtractImageRecords(archive, pagePath, root, resources);
        var shapeRecords = ExtractShapeRecords(pagePath, root, resources);

        var blocks = new List<PageBlockRecord>(glyphRecords.Count + imageRecords.Count + shapeRecords.Count);
        for (var i = 0; i < glyphRecords.Count; i++)
        {
            var record = glyphRecords[i];
            blocks.Add(new PageBlockRecord(record.OriginX, record.OriginY, CreateParagraphFromGlyphRecord(record)));
        }

        for (var i = 0; i < imageRecords.Count; i++)
        {
            var record = imageRecords[i];
            blocks.Add(new PageBlockRecord(record.X, record.Y, CreateImageParagraph(record)));
        }

        for (var i = 0; i < shapeRecords.Count; i++)
        {
            var record = shapeRecords[i];
            blocks.Add(new PageBlockRecord(record.X, record.Y, CreateShapeParagraph(record)));
        }

        blocks.Sort(static (left, right) =>
        {
            var compareY = left.Y.CompareTo(right.Y);
            if (compareY != 0)
            {
                return compareY;
            }

            return left.X.CompareTo(right.X);
        });

        for (var i = 0; i < blocks.Count; i++)
        {
            target.Blocks.Add(blocks[i].Block);
        }
    }

    private static List<GlyphRecord> ExtractGlyphRecords(XElement root, PageResourceSet resources)
    {
        var glyphs = root
            .Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "Glyphs", StringComparison.Ordinal))
            .Where(static glyph => !IsWithinResourceDictionary(glyph))
            .Select(glyph => CreateGlyphRecord(glyph, resources, GetEffectiveTransform(glyph)))
            .Where(static record => !string.IsNullOrWhiteSpace(record.Text))
            .OrderBy(static record => record.OriginY)
            .ThenBy(static record => record.OriginX)
            .ToList();

        return glyphs;
    }

    private static GlyphRecord CreateGlyphRecord(
        XElement glyph,
        PageResourceSet resources,
        AffineTransform effectiveTransform)
    {
        var text = glyph.Attribute("UnicodeString")?.Value ?? string.Empty;
        var fontUri = glyph.Attribute("FontUri")?.Value;
        var fill = glyph.Attribute("Fill")?.Value;
        var style = new TextStyleProperties();

        if (!string.IsNullOrWhiteSpace(fontUri))
        {
            style.FontFamily = InferFontFamily(fontUri!);
        }

        if (TryParseFloat(glyph.Attribute("FontRenderingEmSize")?.Value, out var fontSize) && fontSize > 0f)
        {
            style.FontSize = fontSize;
        }

        if (TryResolveColor(fill, resources, out var color))
        {
            style.Color = color;
        }

        _ = TryParseFloat(glyph.Attribute("OriginX")?.Value, out var originX);
        _ = TryParseFloat(glyph.Attribute("OriginY")?.Value, out var originY);
        if (!effectiveTransform.IsIdentity)
        {
            TransformPoint(effectiveTransform, originX, originY, out originX, out originY);
        }

        var hyperlink = ExtractHyperlinkInfo(glyph);
        return new GlyphRecord(text, originX, originY, style, hyperlink);
    }

    private static List<ImageRecord> ExtractImageRecords(
        ZipArchive archive,
        string pagePath,
        XElement root,
        PageResourceSet resources)
    {
        var paths = root
            .Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "Path", StringComparison.Ordinal))
            .Where(static path => !IsWithinResourceDictionary(path))
            .ToList();
        var records = new List<ImageRecord>(paths.Count);

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (!TryResolvePathImageBrush(path, pagePath, resources, out var brush))
            {
                continue;
            }

            var effectiveTransform = GetEffectiveTransform(path);

            var imageSource = brush.ImageSource;
            var imageBasePartPath = string.IsNullOrWhiteSpace(brush.BasePartPath) ? pagePath : brush.BasePartPath;
            var imagePath = ResolvePartPath(imageBasePartPath, imageSource!);
            var entry = archive.GetEntry(TrimLeadingSlash(imagePath));
            if (entry is null)
            {
                continue;
            }

            byte[] bytes;
            using (var imageStream = entry.Open())
            using (var buffer = new MemoryStream())
            {
                imageStream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            var dimensions = ParseViewport(brush.Viewport);
            if (!dimensions.HasValue)
            {
                var pathData = path.Attribute("Data")?.Value;
                dimensions = TryExtractPathBounds(pathData, out var x, out var y, out var pathWidth, out var pathHeight)
                    ? (x, y, pathWidth, pathHeight)
                    : (0f, 0f, 128f, 128f);
            }

            var (xOrigin, yOrigin, width, height) = dimensions.Value;
            if (!effectiveTransform.IsIdentity)
            {
                TransformRect(effectiveTransform, xOrigin, yOrigin, width, height, out xOrigin, out yOrigin, out width, out height);
            }

            var contentType = InferImageContentType(imagePath);
            var hyperlink = ExtractHyperlinkInfo(path);
            records.Add(new ImageRecord(bytes, xOrigin, yOrigin, Math.Max(1f, width), Math.Max(1f, height), contentType, hyperlink));
        }

        records.Sort(static (left, right) =>
        {
            var compareY = left.Y.CompareTo(right.Y);
            if (compareY != 0)
            {
                return compareY;
            }

            return left.X.CompareTo(right.X);
        });
        return records;
    }

    private static List<ShapeRecord> ExtractShapeRecords(string pagePath, XElement root, PageResourceSet resources)
    {
        var paths = root
            .Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "Path", StringComparison.Ordinal))
            .Where(static path => !IsWithinResourceDictionary(path))
            .ToList();
        var records = new List<ShapeRecord>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (TryResolvePathImageBrush(path, pagePath, resources, out _))
            {
                continue;
            }

            if (!TryCreateShapeFromPath(path, resources, GetEffectiveTransform(path), out var record))
            {
                continue;
            }

            records.Add(record);
        }

        records.Sort(static (left, right) =>
        {
            var compareY = left.Y.CompareTo(right.Y);
            if (compareY != 0)
            {
                return compareY;
            }

            return left.X.CompareTo(right.X);
        });
        return records;
    }

    private static ParagraphBlock CreateParagraphFromGlyphRecord(GlyphRecord record)
    {
        var paragraph = new ParagraphBlock(record.Text);
        paragraph.Inlines.Add(new RunInline(record.Text, record.Style.HasValues ? record.Style : null)
        {
            Hyperlink = record.Hyperlink
        });
        return paragraph;
    }

    private static ParagraphBlock CreateImageParagraph(ImageRecord record)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new ImageInline(record.Bytes, record.Width, record.Height, record.ContentType)
        {
            Hyperlink = record.Hyperlink
        });
        return paragraph;
    }

    private static ParagraphBlock CreateShapeParagraph(ShapeRecord record)
    {
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(record.Shape);
        return paragraph;
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
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);
        return document;
    }

    private static void WriteContentTypes(
        ZipArchive archive,
        IReadOnlyList<string> pageFileNames,
        IReadOnlyCollection<string> imagePartPaths)
    {
        var contentTypesNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        var types = new XDocument(
            new XElement(
                contentTypesNs + "Types",
                new XElement(contentTypesNs + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(contentTypesNs + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                new XElement(contentTypesNs + "Default", new XAttribute("Extension", "jpg"), new XAttribute("ContentType", "image/jpeg")),
                new XElement(contentTypesNs + "Default", new XAttribute("Extension", "jpeg"), new XAttribute("ContentType", "image/jpeg")),
                new XElement(contentTypesNs + "Default", new XAttribute("Extension", "gif"), new XAttribute("ContentType", "image/gif")),
                new XElement(contentTypesNs + "Override", new XAttribute("PartName", "/FixedDocSeq.fdseq"), new XAttribute("ContentType", FixedDocSeqContentType)),
                new XElement(contentTypesNs + "Override", new XAttribute("PartName", "/Documents/1/FixedDoc.fdoc"), new XAttribute("ContentType", FixedDocumentContentType))));

        var root = types.Root!;
        for (var i = 0; i < pageFileNames.Count; i++)
        {
            root.Add(new XElement(contentTypesNs + "Override", new XAttribute("PartName", pageFileNames[i]), new XAttribute("ContentType", FixedPageContentType)));
        }

        foreach (var imagePartPath in imagePartPaths)
        {
            var contentType = InferImageContentType(imagePartPath);
            root.Add(new XElement(contentTypesNs + "Override", new XAttribute("PartName", imagePartPath), new XAttribute("ContentType", contentType)));
        }

        WriteXmlEntry(archive, "[Content_Types].xml", types);
    }

    private static void WritePackageRelationships(ZipArchive archive)
    {
        var rels = new XDocument(
            new XElement(
                XName.Get("Relationships", PackageRelationshipsNamespace),
                new XElement(
                    XName.Get("Relationship", PackageRelationshipsNamespace),
                    new XAttribute("Id", "R1"),
                    new XAttribute("Type", FixedRepresentationRelationshipType),
                    new XAttribute("Target", "/FixedDocSeq.fdseq"))));

        WriteXmlEntry(archive, "_rels/.rels", rels);
    }

    private static void WriteFixedDocumentSequence(ZipArchive archive, XNamespace ns)
    {
        var document = new XDocument(
            new XElement(
                ns + "FixedDocumentSequence",
                new XElement(ns + "DocumentReference", new XAttribute("Source", "/Documents/1/FixedDoc.fdoc"))));
        WriteXmlEntry(archive, "FixedDocSeq.fdseq", document);
    }

    private static void WriteFixedDocument(ZipArchive archive, XNamespace ns, IReadOnlyList<string> pageFileNames)
    {
        var root = new XElement(ns + "FixedDocument");
        for (var i = 0; i < pageFileNames.Count; i++)
        {
            root.Add(new XElement(ns + "PageContent", new XAttribute("Source", pageFileNames[i])));
        }

        WriteXmlEntry(archive, "Documents/1/FixedDoc.fdoc", new XDocument(root));
    }

    private static void WriteFixedPage(
        ZipArchive archive,
        LayoutPage page,
        string pagePartPath,
        XNamespace ns,
        ISet<string> imagePartPaths,
        ref int imageCounter)
    {
        var root = new XElement(
            ns + "FixedPage",
            new XAttribute("Width", FormatFloat(page.Width)),
            new XAttribute("Height", FormatFloat(page.Height)),
            new XAttribute(XNamespace.Xml + "lang", "en-US"));

        for (var i = 0; i < page.Items.Count; i++)
        {
            switch (page.Items[i])
            {
                case TextItem textItem:
                    var glyph = new XElement(
                        ns + "Glyphs",
                        new XAttribute("FontUri", "/Resources/Fonts/" + SanitizeFontName(textItem.FontFamily) + ".odttf"),
                        new XAttribute("FontRenderingEmSize", FormatFloat(textItem.FontSize)),
                        new XAttribute("Fill", FormatColor(textItem.Color)),
                        new XAttribute("OriginX", FormatFloat(textItem.X)),
                        new XAttribute("OriginY", FormatFloat(textItem.Y)),
                        new XAttribute("UnicodeString", textItem.Text));
                    ApplyHyperlinkAttribute(glyph, textItem.Hyperlink);
                    root.Add(glyph);
                    break;
                case ImageItem imageItem:
                    imageCounter++;
                    var imagePartPath = "/Resources/Images/img" + imageCounter + InferImageExtension(imageItem.ContentType);
                    imagePartPaths.Add(imagePartPath);
                    WriteBinaryEntry(archive, TrimLeadingSlash(imagePartPath), imageItem.Bytes);

                    var x1 = imageItem.X;
                    var y1 = imageItem.Y;
                    var x2 = imageItem.X + imageItem.Width;
                    var y2 = imageItem.Y + imageItem.Height;
                    var imagePath = new XElement(
                        ns + "Path",
                        new XAttribute("Data", $"M {FormatFloat(x1)},{FormatFloat(y1)} L {FormatFloat(x2)},{FormatFloat(y1)} {FormatFloat(x2)},{FormatFloat(y2)} {FormatFloat(x1)},{FormatFloat(y2)} Z"),
                        new XElement(
                            ns + "Path.Fill",
                            new XElement(
                                ns + "ImageBrush",
                                new XAttribute("ImageSource", imagePartPath),
                                new XAttribute("Viewport", $"{FormatFloat(x1)},{FormatFloat(y1)},{FormatFloat(imageItem.Width)},{FormatFloat(imageItem.Height)}"),
                                new XAttribute("ViewportUnits", "Absolute"),
                                new XAttribute("Viewbox", "0,0,1,1"),
                                new XAttribute("ViewboxUnits", "RelativeToBoundingBox"),
                                new XAttribute("TileMode", "None"))));
                    ApplyHyperlinkAttribute(imagePath, imageItem.Hyperlink);
                    root.Add(imagePath);
                    break;
                case ShapeItem shapeItem:
                    var vectorPath = new XElement(
                        ns + "Path",
                        new XAttribute("Data", shapeItem.Data));
                    if (shapeItem.FillRule.HasValue)
                    {
                        vectorPath.SetAttributeValue(
                            "FillRule",
                            shapeItem.FillRule.Value == ShapePathFillRule.EvenOdd ? "EvenOdd" : "NonZero");
                    }

                    if (shapeItem.Fill.HasValue)
                    {
                        vectorPath.SetAttributeValue("Fill", FormatColor(shapeItem.Fill.Value));
                    }

                    if (shapeItem.Stroke.HasValue && shapeItem.StrokeThickness > 0f)
                    {
                        vectorPath.SetAttributeValue("Stroke", FormatColor(shapeItem.Stroke.Value));
                        vectorPath.SetAttributeValue("StrokeThickness", FormatFloat(shapeItem.StrokeThickness));
                    }

                    ApplyHyperlinkAttribute(vectorPath, shapeItem.Hyperlink);
                    root.Add(vectorPath);
                    break;
            }
        }

        WriteXmlEntry(archive, TrimLeadingSlash(pagePartPath), new XDocument(root));
    }

    private static List<LayoutPage> BuildLayoutPages(Document document)
    {
        var pageWidth = document.SectionProperties.PageWidth ?? DocumentDefaults.ResolvePageSetup().PageWidth;
        var pageHeight = document.SectionProperties.PageHeight ?? DocumentDefaults.ResolvePageSetup().PageHeight;
        var marginLeft = document.SectionProperties.MarginLeft ?? DocumentDefaults.ResolvePageSetup().MarginLeft;
        var marginRight = document.SectionProperties.MarginRight ?? DocumentDefaults.ResolvePageSetup().MarginRight;
        var marginTop = document.SectionProperties.MarginTop ?? DocumentDefaults.ResolvePageSetup().MarginTop;
        var marginBottom = document.SectionProperties.MarginBottom ?? DocumentDefaults.ResolvePageSetup().MarginBottom;
        var contentBottom = Math.Max(marginTop + 16f, pageHeight - marginBottom);

        var pages = new List<LayoutPage> { new LayoutPage(pageWidth, pageHeight) };
        var currentPage = pages[0];
        var cursorY = marginTop;

        void AdvancePage()
        {
            currentPage = new LayoutPage(pageWidth, pageHeight);
            pages.Add(currentPage);
            cursorY = marginTop;
        }

        void EnsureSpace(float requiredHeight)
        {
            if (cursorY + requiredHeight <= contentBottom)
            {
                return;
            }

            AdvancePage();
        }

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            switch (block)
            {
                case PageBreakBlock:
                    AdvancePage();
                    break;
                case ParagraphBlock paragraph:
                    WriteParagraphItems(paragraph, currentPage.Items, marginLeft, pageWidth - marginLeft - marginRight, ref cursorY, EnsureSpace);
                    cursorY += 6f;
                    break;
                case TableBlock table:
                    var rows = FlattenTableRows(table);
                    for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                    {
                        var rowParagraph = new ParagraphBlock(rows[rowIndex]);
                        rowParagraph.Inlines.Add(new RunInline(rows[rowIndex]));
                        WriteParagraphItems(rowParagraph, currentPage.Items, marginLeft, pageWidth - marginLeft - marginRight, ref cursorY, EnsureSpace);
                        cursorY += 4f;
                    }

                    cursorY += 8f;
                    break;
                default:
                    break;
            }
        }

        if (pages.Count == 0)
        {
            pages.Add(new LayoutPage(pageWidth, pageHeight));
        }

        return pages;
    }

    private static void WriteParagraphItems(
        ParagraphBlock paragraph,
        ICollection<PageItem> target,
        float marginLeft,
        float maxWidth,
        ref float cursorY,
        Action<float> ensureSpace)
    {
        var textRuns = new List<RunInline>();
        var imageRuns = new List<ImageInline>();
        var shapeRuns = new List<ShapeInline>();
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            switch (paragraph.Inlines[i])
            {
                case RunInline run when !string.IsNullOrEmpty(run.GetText()):
                    textRuns.Add(run);
                    break;
                case ImageInline image when image.Data.Length > 0:
                    imageRuns.Add(image);
                    break;
                case ShapeInline shape when shape.Width > 0f && shape.Height > 0f:
                    shapeRuns.Add(shape);
                    break;
            }
        }

        if (textRuns.Count == 0 && !string.IsNullOrWhiteSpace(paragraph.Text))
        {
            textRuns.Add(new RunInline(paragraph.Text));
        }

        for (var i = 0; i < textRuns.Count; i++)
        {
            var run = textRuns[i];
            var runText = run.GetText();
            if (string.IsNullOrWhiteSpace(runText))
            {
                continue;
            }

            var style = run.Style ?? new TextStyleProperties();
            var fontSize = style.FontSize.HasValue && style.FontSize.Value > 0f ? style.FontSize.Value : 14f;
            var lineHeight = Math.Max(12f, fontSize * 1.35f);
            ensureSpace(lineHeight);
            var x = marginLeft;
            _ = maxWidth;
            var y = cursorY + fontSize;
            var color = style.Color ?? DocColor.Black;
            var fontFamily = !string.IsNullOrWhiteSpace(style.FontFamily) ? style.FontFamily! : "Segoe UI";
            target.Add(new TextItem(runText, x, y, fontFamily, fontSize, color, run.Hyperlink));
            cursorY += lineHeight;
        }

        for (var i = 0; i < imageRuns.Count; i++)
        {
            var image = imageRuns[i];
            var width = image.Width > 0f ? image.Width : 96f;
            var height = image.Height > 0f ? image.Height : 96f;
            ensureSpace(height);
            target.Add(new ImageItem(image.Data, marginLeft, cursorY, width, height, image.ContentType, image.Hyperlink));
            cursorY += height + 4f;
        }

        for (var i = 0; i < shapeRuns.Count; i++)
        {
            var shape = shapeRuns[i];
            var width = shape.Width > 0f ? shape.Width : 96f;
            var height = shape.Height > 0f ? shape.Height : 96f;
            ensureSpace(height);
            var x = marginLeft;
            var y = cursorY;
            var data = BuildShapePathData(shape);
            var fill = ResolveShapeFillColor(shape);
            var stroke = ResolveShapeStrokeColor(shape, out var strokeThickness);
            var fillRule = ResolveShapeFillRule(shape);
            target.Add(new ShapeItem(data, x, y, width, height, fill, stroke, strokeThickness, fillRule, shape.Hyperlink));
            cursorY += height + 4f;
        }
    }

    private static List<string> FlattenTableRows(TableBlock table)
    {
        var rows = new List<string>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var cellTexts = new List<string>(row.Cells.Count);
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var text = ExtractTextFromBlocks(cell.Blocks);
                cellTexts.Add(text);
            }

            rows.Add(string.Join('\t', cellTexts));
        }

        return rows;
    }

    private static string ExtractTextFromBlocks(IReadOnlyList<Block> blocks)
    {
        var lines = new List<string>();
        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case ParagraphBlock paragraph:
                    lines.Add(ExtractParagraphText(paragraph));
                    break;
                case TableBlock table:
                    var nestedRows = FlattenTableRows(table);
                    for (var rowIndex = 0; rowIndex < nestedRows.Count; rowIndex++)
                    {
                        lines.Add(nestedRows[rowIndex]);
                    }

                    break;
            }
        }

        return string.Join(" ", lines.Where(static line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string ExtractParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return paragraph.Text ?? string.Empty;
        }

        var parts = new List<string>(paragraph.Inlines.Count);
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            switch (paragraph.Inlines[i])
            {
                case RunInline run:
                    parts.Add(run.GetText());
                    break;
                case ImageInline:
                    parts.Add("[Image]");
                    break;
            }
        }

        var combined = string.Concat(parts);
        if (!string.IsNullOrWhiteSpace(combined))
        {
            return combined;
        }

        return paragraph.Text ?? string.Empty;
    }

    private static string ResolvePartPath(string basePartPath, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return NormalizePartPath(basePartPath);
        }

        if (source.StartsWith("/", StringComparison.Ordinal))
        {
            return NormalizePartPath(source);
        }

        var baseDirectory = NormalizePartPath(basePartPath);
        var lastSlash = baseDirectory.LastIndexOf('/');
        var directory = lastSlash <= 0 ? "/" : baseDirectory[..(lastSlash + 1)];
        return NormalizePartPath(directory + source);
    }

    private static string NormalizePartPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count > 0)
                {
                    stack.Pop();
                }

                continue;
            }

            stack.Push(segment);
        }

        return "/" + string.Join("/", stack.Reverse());
    }

    private static string TrimLeadingSlash(string path)
    {
        return path.StartsWith("/", StringComparison.Ordinal) ? path[1..] : path;
    }

    private static (float X, float Y, float Width, float Height)? ParseViewport(string? viewport)
    {
        if (string.IsNullOrWhiteSpace(viewport))
        {
            return null;
        }

        var tokens = viewport
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 4)
        {
            return null;
        }

        if (!TryParseFloat(tokens[0], out var x)
            || !TryParseFloat(tokens[1], out var y)
            || !TryParseFloat(tokens[2], out var width)
            || !TryParseFloat(tokens[3], out var height))
        {
            return null;
        }

        return (x, y, width, height);
    }

    private static AffineTransform GetEffectiveTransform(XElement element)
    {
        var transform = AffineTransform.Identity;
        var current = element;
        while (current is not null)
        {
            if (TryGetOwnTransform(current, out var ownTransform))
            {
                transform = MultiplyTransforms(ownTransform, transform);
            }

            current = current.Parent;
        }

        return transform;
    }

    private static bool TryGetOwnTransform(XElement element, out AffineTransform transform)
    {
        transform = AffineTransform.Identity;
        var inlineValue = GetAttributeValue(element, "RenderTransform");
        if (TryParseMatrixTransform(inlineValue, out transform))
        {
            return true;
        }

        if (string.Equals(element.Name.LocalName, "MatrixTransform", StringComparison.Ordinal)
            && TryParseMatrixTransform(element, out transform))
        {
            return true;
        }

        var children = element.Elements().ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (string.Equals(child.Name.LocalName, "MatrixTransform", StringComparison.Ordinal)
                && TryParseMatrixTransform(child, out transform))
            {
                return true;
            }

            if (string.Equals(child.Name.LocalName, "RenderTransform", StringComparison.Ordinal)
                || child.Name.LocalName.EndsWith(".RenderTransform", StringComparison.Ordinal))
            {
                if (TryParseTransformContainer(child, out transform))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseTransformContainer(XElement container, out AffineTransform transform)
    {
        if (TryParseMatrixTransform(container, out transform))
        {
            return true;
        }

        var descendants = container.Descendants().ToList();
        for (var i = 0; i < descendants.Count; i++)
        {
            var descendant = descendants[i];
            if (string.Equals(descendant.Name.LocalName, "MatrixTransform", StringComparison.Ordinal)
                && TryParseMatrixTransform(descendant, out transform))
            {
                return true;
            }
        }

        transform = AffineTransform.Identity;
        return false;
    }

    private static bool TryParseMatrixTransform(XElement element, out AffineTransform transform)
    {
        if (TryParseMatrixTransform(GetAttributeValue(element, "Matrix"), out transform))
        {
            return true;
        }

        if (!TryParseFloat(GetAttributeValue(element, "M11"), out var m11)
            || !TryParseFloat(GetAttributeValue(element, "M12"), out var m12)
            || !TryParseFloat(GetAttributeValue(element, "M21"), out var m21)
            || !TryParseFloat(GetAttributeValue(element, "M22"), out var m22)
            || !TryParseFloat(GetAttributeValue(element, "OffsetX"), out var offsetX)
            || !TryParseFloat(GetAttributeValue(element, "OffsetY"), out var offsetY))
        {
            transform = AffineTransform.Identity;
            return false;
        }

        transform = new AffineTransform(m11, m12, m21, m22, offsetX, offsetY);
        return true;
    }

    private static bool TryParseMatrixTransform(string? value, out AffineTransform transform)
    {
        transform = AffineTransform.Identity;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 6)
        {
            return false;
        }

        if (!TryParseFloat(tokens[0], out var m11)
            || !TryParseFloat(tokens[1], out var m12)
            || !TryParseFloat(tokens[2], out var m21)
            || !TryParseFloat(tokens[3], out var m22)
            || !TryParseFloat(tokens[4], out var offsetX)
            || !TryParseFloat(tokens[5], out var offsetY))
        {
            return false;
        }

        transform = new AffineTransform(m11, m12, m21, m22, offsetX, offsetY);
        return true;
    }

    private static AffineTransform MultiplyTransforms(AffineTransform left, AffineTransform right)
    {
        return new AffineTransform(
            (left.M11 * right.M11) + (left.M21 * right.M12),
            (left.M12 * right.M11) + (left.M22 * right.M12),
            (left.M11 * right.M21) + (left.M21 * right.M22),
            (left.M12 * right.M21) + (left.M22 * right.M22),
            (left.M11 * right.OffsetX) + (left.M21 * right.OffsetY) + left.OffsetX,
            (left.M12 * right.OffsetX) + (left.M22 * right.OffsetY) + left.OffsetY);
    }

    private static void TransformPoint(
        AffineTransform transform,
        float x,
        float y,
        out float transformedX,
        out float transformedY)
    {
        transformedX = (float)((transform.M11 * x) + (transform.M21 * y) + transform.OffsetX);
        transformedY = (float)((transform.M12 * x) + (transform.M22 * y) + transform.OffsetY);
    }

    private static void TransformRect(
        AffineTransform transform,
        float x,
        float y,
        float width,
        float height,
        out float transformedX,
        out float transformedY,
        out float transformedWidth,
        out float transformedHeight)
    {
        TransformPoint(transform, x, y, out var x1, out var y1);
        TransformPoint(transform, x + width, y, out var x2, out var y2);
        TransformPoint(transform, x + width, y + height, out var x3, out var y3);
        TransformPoint(transform, x, y + height, out var x4, out var y4);

        var minX = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
        var minY = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
        var maxX = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
        var maxY = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));

        transformedX = minX;
        transformedY = minY;
        transformedWidth = Math.Max(1f, maxX - minX);
        transformedHeight = Math.Max(1f, maxY - minY);
    }

    private static bool TryParseFloat(string? value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static string InferFontFamily(string fontUri)
    {
        var normalized = fontUri.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Segoe UI";
        }

        return fileName.Replace("_", " ", StringComparison.Ordinal);
    }

    private static bool TryParseColor(string? value, out DocColor color)
    {
        color = DocColor.Black;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
        {
            return false;
        }

        var hex = trimmed[1..];
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = new DocColor(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF));
            return true;
        }

        if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            color = DocColor.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
            return true;
        }

        return false;
    }

    private static string FormatColor(DocColor color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void WriteXmlEntry(ZipArchive archive, string entryPath, XDocument document)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void WriteBinaryEntry(ZipArchive archive, string entryPath, byte[] bytes)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string SanitizeFontName(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return "SegoeUI";
        }

        var builder = new char[fontFamily.Length];
        var length = 0;
        for (var i = 0; i < fontFamily.Length; i++)
        {
            var ch = fontFamily[i];
            if (char.IsLetterOrDigit(ch))
            {
                builder[length++] = ch;
            }
        }

        if (length == 0)
        {
            return "SegoeUI";
        }

        return new string(builder, 0, length);
    }

    private static string InferImageExtension(string contentType)
    {
        if (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase))
        {
            return ".jpg";
        }

        if (contentType.Contains("gif", StringComparison.OrdinalIgnoreCase))
        {
            return ".gif";
        }

        return ".png";
    }

    private static string InferImageContentType(string partPath)
    {
        var extension = Path.GetExtension(partPath);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "image/jpeg";
        }

        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            return "image/gif";
        }

        return "image/png";
    }

    private static string BuildShapePathData(ShapeInline shape)
    {
        var width = Math.Max(1f, shape.Width);
        var height = Math.Max(1f, shape.Height);
        var evaluated = ShapeGeometryEvaluator.Evaluate(shape.Properties, width, height);
        if (evaluated.Count == 0)
        {
            return BuildRectanglePathData(width, height);
        }

        var builder = new StringBuilder();
        for (var pathIndex = 0; pathIndex < evaluated.Count; pathIndex++)
        {
            var path = evaluated[pathIndex];
            for (var segmentIndex = 0; segmentIndex < path.Segments.Count; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                switch (segment.Kind)
                {
                    case ShapePathSegmentKind.MoveTo:
                        builder.Append("M ")
                            .Append(FormatFloat(segment.X1))
                            .Append(',')
                            .Append(FormatFloat(segment.Y1))
                            .Append(' ');
                        break;
                    case ShapePathSegmentKind.LineTo:
                        builder.Append("L ")
                            .Append(FormatFloat(segment.X1))
                            .Append(',')
                            .Append(FormatFloat(segment.Y1))
                            .Append(' ');
                        break;
                    case ShapePathSegmentKind.QuadTo:
                        builder.Append("Q ")
                            .Append(FormatFloat(segment.X1))
                            .Append(',')
                            .Append(FormatFloat(segment.Y1))
                            .Append(' ')
                            .Append(FormatFloat(segment.X2))
                            .Append(',')
                            .Append(FormatFloat(segment.Y2))
                            .Append(' ');
                        break;
                    case ShapePathSegmentKind.CubicTo:
                        builder.Append("C ")
                            .Append(FormatFloat(segment.X1))
                            .Append(',')
                            .Append(FormatFloat(segment.Y1))
                            .Append(' ')
                            .Append(FormatFloat(segment.X2))
                            .Append(',')
                            .Append(FormatFloat(segment.Y2))
                            .Append(' ')
                            .Append(FormatFloat(segment.X3))
                            .Append(',')
                            .Append(FormatFloat(segment.Y3))
                            .Append(' ');
                        break;
                    case ShapePathSegmentKind.Close:
                        builder.Append("Z ");
                        break;
                    case ShapePathSegmentKind.ArcTo:
                        return BuildRectanglePathData(width, height);
                }
            }
        }

        if (builder.Length == 0)
        {
            return BuildRectanglePathData(width, height);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildRectanglePathData(float width, float height)
    {
        return "M 0,0 L " + FormatFloat(width) + ",0 " + FormatFloat(width) + "," + FormatFloat(height) + " 0," + FormatFloat(height) + " Z";
    }

    private static DocColor? ResolveShapeFillColor(ShapeInline shape)
    {
        if (shape.Properties.FillColor.HasValue)
        {
            return shape.Properties.FillColor.Value;
        }

        return shape.Properties.Fill switch
        {
            ShapeSolidFill solid => solid.Color,
            ShapeNoFill => null,
            _ => null
        };
    }

    private static DocColor? ResolveShapeStrokeColor(ShapeInline shape, out float strokeThickness)
    {
        strokeThickness = 0f;
        var outline = shape.Properties.Outline;
        if (outline is null || outline.Style == DocBorderStyle.None || outline.Thickness <= 0f)
        {
            return null;
        }

        strokeThickness = outline.Thickness;
        return outline.Color;
    }

    private static ShapePathFillRule? ResolveShapeFillRule(ShapeInline shape)
    {
        var geometry = ShapeGeometryEvaluator.ResolveGeometry(shape.Properties);
        if (geometry is null || geometry.Paths.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < geometry.Paths.Count; i++)
        {
            if (geometry.Paths[i].FillRule == ShapePathFillRule.EvenOdd)
            {
                return ShapePathFillRule.EvenOdd;
            }
        }

        return null;
    }

    private static void ApplyHyperlinkAttribute(XElement element, HyperlinkInfo? hyperlink)
    {
        if (hyperlink is null)
        {
            return;
        }

        var target = !string.IsNullOrWhiteSpace(hyperlink.Uri)
            ? hyperlink.Uri
            : !string.IsNullOrWhiteSpace(hyperlink.Anchor)
                ? "#" + hyperlink.Anchor
                : null;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        element.SetAttributeValue("FixedPage.NavigateUri", target);
    }

    private static HyperlinkInfo? ExtractHyperlinkInfo(XElement element)
    {
        var uri = GetNavigateUri(element);
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        return new HyperlinkInfo(uri, null, null);
    }

    private static string? GetNavigateUri(XElement? element)
    {
        var current = element;
        while (current is not null)
        {
            foreach (var attribute in current.Attributes())
            {
                if (string.Equals(attribute.Name.LocalName, "FixedPage.NavigateUri", StringComparison.Ordinal)
                    || string.Equals(attribute.Name.LocalName, "NavigateUri", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(attribute.Value))
                    {
                        return attribute.Value.Trim();
                    }
                }
            }

            current = current.Parent;
        }

        return null;
    }

    private static PageResourceSet BuildPageResources(ZipArchive archive, string pagePath, XElement fixedPageRoot)
    {
        var resources = new PageResourceSet();
        var visitedExternalDictionaries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dictionaries = fixedPageRoot
            .Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
            .ToList();

        for (var i = 0; i < dictionaries.Count; i++)
        {
            ParseResourceDictionary(archive, pagePath, dictionaries[i], resources, visitedExternalDictionaries);
        }

        return resources;
    }

    private static void ParseResourceDictionary(
        ZipArchive archive,
        string basePartPath,
        XElement dictionary,
        PageResourceSet target,
        ISet<string> visitedExternalDictionaries)
    {
        var source = GetAttributeValue(dictionary, "Source");
        if (!string.IsNullOrWhiteSpace(source))
        {
            var dictionaryPath = ResolvePartPath(basePartPath, source!);
            if (visitedExternalDictionaries.Add(dictionaryPath))
            {
                var document = LoadXml(archive, dictionaryPath);
                if (document.Root is { } externalRoot
                    && string.Equals(externalRoot.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
                {
                    ParseResourceDictionary(archive, dictionaryPath, externalRoot, target, visitedExternalDictionaries);
                }
            }
        }

        var elements = dictionary.Elements().ToList();
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if (string.Equals(element.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
            {
                ParseResourceDictionary(archive, basePartPath, element, target, visitedExternalDictionaries);
                continue;
            }

            if (string.Equals(element.Name.LocalName, "SolidColorBrush", StringComparison.Ordinal))
            {
                var key = GetResourceKey(element);
                var colorValue = GetAttributeValue(element, "Color");
                if (!string.IsNullOrWhiteSpace(key)
                    && TryResolveColor(colorValue, target, out var color))
                {
                    target.SolidColors[key] = color;
                }

                continue;
            }

            if (!string.Equals(element.Name.LocalName, "ImageBrush", StringComparison.Ordinal))
            {
                if (element.HasElements)
                {
                    var nestedDictionaries = element
                        .Elements()
                        .Where(static child => string.Equals(child.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
                        .ToList();
                    for (var dictionaryIndex = 0; dictionaryIndex < nestedDictionaries.Count; dictionaryIndex++)
                    {
                        ParseResourceDictionary(
                            archive,
                            basePartPath,
                            nestedDictionaries[dictionaryIndex],
                            target,
                            visitedExternalDictionaries);
                    }
                }

                continue;
            }

            var imageSource = GetAttributeValue(element, "ImageSource");
            var resourceKey = GetResourceKey(element);
            if (string.IsNullOrWhiteSpace(resourceKey) || string.IsNullOrWhiteSpace(imageSource))
            {
                continue;
            }

            var viewport = GetAttributeValue(element, "Viewport");
            target.ImageBrushes[resourceKey] = new ImageBrushResource(basePartPath, imageSource!, viewport);
        }
    }

    private static bool TryResolvePathImageBrush(
        XElement path,
        string pagePath,
        PageResourceSet resources,
        out ImageBrushResource brush)
    {
        var fillValue = GetAttributeValue(path, "Fill");
        if (TryResolveImageBrushReference(fillValue, resources, out brush))
        {
            return true;
        }

        var pathFill = path
            .Elements()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "Path.Fill", StringComparison.Ordinal));
        if (pathFill is null)
        {
            brush = default!;
            return false;
        }

        var staticResource = pathFill
            .Elements()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "StaticResource", StringComparison.Ordinal));
        if (staticResource is not null)
        {
            var key = GetAttributeValue(staticResource, "ResourceKey")
                      ?? GetAttributeValue(staticResource, "Key");
            if (TryResolveImageBrushReference(key, resources, out brush))
            {
                return true;
            }
        }

        var imageBrush = pathFill
            .Elements()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "ImageBrush", StringComparison.Ordinal));
        if (imageBrush is null)
        {
            brush = default!;
            return false;
        }

        var imageSource = GetAttributeValue(imageBrush, "ImageSource");
        if (string.IsNullOrWhiteSpace(imageSource))
        {
            brush = default!;
            return false;
        }

        brush = new ImageBrushResource(pagePath, imageSource!, GetAttributeValue(imageBrush, "Viewport"));
        return true;
    }

    private static bool TryResolveImageBrushReference(
        string? value,
        PageResourceSet resources,
        out ImageBrushResource brush)
    {
        brush = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = value.Trim();
        if (TryParseStaticResourceKey(key, out var parsedKey))
        {
            key = parsedKey;
        }
        else if (key.Length >= 2 && key[0] == '"' && key[^1] == '"')
        {
            key = key[1..^1];
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (resources.ImageBrushes.TryGetValue(key, out var resolved) && resolved is not null)
        {
            brush = resolved;
            return true;
        }

        return false;
    }

    private static bool TryResolveColor(string? value, PageResourceSet resources, out DocColor color)
    {
        if (TryParseColor(value, out color))
        {
            return true;
        }

        if (TryParseStaticResourceKey(value, out var key)
            && resources.SolidColors.TryGetValue(key, out color))
        {
            return true;
        }

        color = default;
        return false;
    }

    private static string? GetAttributeValue(XElement element, string localName)
    {
        var attribute = element
            .Attributes()
            .FirstOrDefault(value => string.Equals(value.Name.LocalName, localName, StringComparison.Ordinal));
        return attribute?.Value;
    }

    private static string? GetResourceKey(XElement element)
    {
        var keyAttribute = element
            .Attributes()
            .FirstOrDefault(static attribute =>
                string.Equals(attribute.Name.LocalName, "Key", StringComparison.Ordinal));
        return keyAttribute?.Value;
    }

    private static bool TryParseStaticResourceKey(string? value, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return false;
        }

        var expression = trimmed[1..^1].Trim();
        if (expression.StartsWith("StaticResource", StringComparison.OrdinalIgnoreCase))
        {
            key = expression["StaticResource".Length..].Trim();
        }
        else if (expression.StartsWith("DynamicResource", StringComparison.OrdinalIgnoreCase))
        {
            key = expression["DynamicResource".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.Length >= 2 && key[0] == '"' && key[^1] == '"')
        {
            key = key[1..^1];
        }

        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool IsWithinResourceDictionary(XElement element)
    {
        var current = element.Parent;
        while (current is not null)
        {
            if (string.Equals(current.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool TryCreateShapeFromPath(
        XElement path,
        PageResourceSet resources,
        AffineTransform effectiveTransform,
        out ShapeRecord record)
    {
        record = default!;
        var data = GetAttributeValue(path, "Data");
        if (!TryParsePathData(data, out var commands, out var dataFillRule, out _, out _, out _, out _))
        {
            return false;
        }

        var strokeScale = ResolveStrokeScale(effectiveTransform);
        if (!effectiveTransform.IsIdentity)
        {
            commands = TransformPathCommands(commands, effectiveTransform);
        }

        if (!TryComputePathBounds(commands, out var minX, out var minY, out var width, out var height))
        {
            return false;
        }

        var normalizedWidth = Math.Max(1f, width);
        var normalizedHeight = Math.Max(1f, height);
        var shape = new ShapeInline(normalizedWidth, normalizedHeight);
        var geometry = new ShapeGeometry();
        var shapePath = new ShapePath
        {
            Width = Math.Max(1, (long)Math.Ceiling(normalizedWidth)),
            Height = Math.Max(1, (long)Math.Ceiling(normalizedHeight))
        };
        var fillRule = GetAttributeValue(path, "FillRule");
        if (!string.IsNullOrWhiteSpace(fillRule))
        {
            shapePath.FillRule = string.Equals(fillRule, "EvenOdd", StringComparison.OrdinalIgnoreCase)
                ? ShapePathFillRule.EvenOdd
                : ShapePathFillRule.NonZero;
        }
        else if (dataFillRule.HasValue)
        {
            shapePath.FillRule = dataFillRule.Value;
        }

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    shapePath.Commands.Add(new ShapeMoveToCommand(new ShapeAdjustPoint(
                        FormatFloat(command.X1 - minX),
                        FormatFloat(command.Y1 - minY))));
                    break;
                case PathCommandKind.LineTo:
                    shapePath.Commands.Add(new ShapeLineToCommand(new ShapeAdjustPoint(
                        FormatFloat(command.X1 - minX),
                        FormatFloat(command.Y1 - minY))));
                    break;
                case PathCommandKind.QuadTo:
                    shapePath.Commands.Add(new ShapeQuadBezierToCommand(
                        new ShapeAdjustPoint(FormatFloat(command.X1 - minX), FormatFloat(command.Y1 - minY)),
                        new ShapeAdjustPoint(FormatFloat(command.X2 - minX), FormatFloat(command.Y2 - minY))));
                    break;
                case PathCommandKind.CubicTo:
                    shapePath.Commands.Add(new ShapeCubicBezierToCommand(
                        new ShapeAdjustPoint(FormatFloat(command.X1 - minX), FormatFloat(command.Y1 - minY)),
                        new ShapeAdjustPoint(FormatFloat(command.X2 - minX), FormatFloat(command.Y2 - minY)),
                        new ShapeAdjustPoint(FormatFloat(command.X3 - minX), FormatFloat(command.Y3 - minY))));
                    break;
                case PathCommandKind.Close:
                    shapePath.Commands.Add(new ShapeClosePathCommand());
                    break;
            }
        }

        var fillAttribute = GetAttributeValue(path, "Fill");
        var hasNoFill = string.Equals(fillAttribute, "None", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fillAttribute, "Transparent", StringComparison.OrdinalIgnoreCase);
        if (hasNoFill)
        {
            shapePath.FillMode = ShapePathFillMode.None;
        }
        else if (TryResolveColor(fillAttribute, resources, out var fillColor))
        {
            shape.Properties.FillColor = fillColor;
            shapePath.FillMode = ShapePathFillMode.Normal;
        }
        else if (TryResolvePathFillColor(path, resources, out var elementFillColor))
        {
            shape.Properties.FillColor = elementFillColor;
            shapePath.FillMode = ShapePathFillMode.Normal;
        }
        else
        {
            shapePath.FillMode = ShapePathFillMode.None;
        }

        var strokeAttribute = GetAttributeValue(path, "Stroke");
        var strokeColorResolved = false;
        var strokeColor = default(DocColor);
        if (TryResolveColor(strokeAttribute, resources, out var attributeStrokeColor))
        {
            strokeColor = attributeStrokeColor;
            strokeColorResolved = true;
        }
        else if (TryResolvePathStrokeColor(path, resources, out var elementStrokeColor))
        {
            strokeColor = elementStrokeColor;
            strokeColorResolved = true;
        }

        if ((!strokeColorResolved && string.IsNullOrWhiteSpace(strokeAttribute))
            || string.Equals(strokeAttribute, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(strokeAttribute, "Transparent", StringComparison.OrdinalIgnoreCase))
        {
            shapePath.IsStroked = false;
        }
        else if (strokeColorResolved)
        {
            shapePath.IsStroked = true;
            var strokeThickness = TryParseFloat(GetAttributeValue(path, "StrokeThickness"), out var parsedThickness)
                ? Math.Max(0.1f, parsedThickness)
                : 1f;
            strokeThickness = Math.Max(0.1f, strokeThickness * strokeScale);
            shape.Properties.Outline = new BorderLine
            {
                Style = DocBorderStyle.Single,
                Color = strokeColor,
                Thickness = strokeThickness
            };
        }
        else
        {
            shapePath.IsStroked = false;
        }

        geometry.Paths.Add(shapePath);
        geometry.TextRectangle = new ShapeTextRectangle("l", "t", "r", "b");
        shape.Properties.CustomGeometry = geometry;
        shape.Hyperlink = ExtractHyperlinkInfo(path);
        record = new ShapeRecord(minX, minY, shape);
        return true;
    }

    private static bool TryResolvePathStrokeColor(XElement path, PageResourceSet resources, out DocColor color)
    {
        var pathStroke = path
            .Elements()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "Path.Stroke", StringComparison.Ordinal));
        return TryResolvePathBrushColor(pathStroke, resources, out color);
    }

    private static bool TryResolvePathFillColor(XElement path, PageResourceSet resources, out DocColor color)
    {
        var pathFill = path
            .Elements()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "Path.Fill", StringComparison.Ordinal));
        return TryResolvePathBrushColor(pathFill, resources, out color);
    }

    private static bool TryResolvePathBrushColor(XElement? brushOwner, PageResourceSet resources, out DocColor color)
    {
        color = default;
        if (brushOwner is null)
        {
            return false;
        }

        if (TryResolveColorReferenceElement(brushOwner, resources, out color))
        {
            return true;
        }

        var solidBrush = brushOwner
            .Elements()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "SolidColorBrush", StringComparison.Ordinal));
        if (solidBrush is null)
        {
            return false;
        }

        return TryResolveColor(GetAttributeValue(solidBrush, "Color"), resources, out color);
    }

    private static bool TryResolveColorReferenceElement(
        XElement brushOwner,
        PageResourceSet resources,
        out DocColor color)
    {
        color = default;
        var reference = brushOwner
            .Elements()
            .FirstOrDefault(static element =>
                string.Equals(element.Name.LocalName, "StaticResource", StringComparison.Ordinal)
                || string.Equals(element.Name.LocalName, "DynamicResource", StringComparison.Ordinal));
        if (reference is null)
        {
            return false;
        }

        var key = GetAttributeValue(reference, "ResourceKey")
                  ?? GetAttributeValue(reference, "Key");
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (TryParseStaticResourceKey(key, out var parsedKey))
        {
            key = parsedKey;
        }

        return resources.SolidColors.TryGetValue(key, out color);
    }

    private static float ResolveStrokeScale(AffineTransform transform)
    {
        if (transform.IsIdentity)
        {
            return 1f;
        }

        var axisX = Math.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        var axisY = Math.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));
        var average = (axisX + axisY) * 0.5d;
        if (double.IsNaN(average) || double.IsInfinity(average) || average <= 0d)
        {
            return 1f;
        }

        return (float)average;
    }

    private static List<ParsedPathCommand> TransformPathCommands(
        IReadOnlyList<ParsedPathCommand> commands,
        AffineTransform transform)
    {
        var transformed = new List<ParsedPathCommand>(commands.Count);
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                case PathCommandKind.LineTo:
                {
                    TransformPoint(transform, command.X1, command.Y1, out var x1, out var y1);
                    transformed.Add(new ParsedPathCommand(command.Kind, x1, y1, 0f, 0f, 0f, 0f));
                    break;
                }
                case PathCommandKind.QuadTo:
                {
                    TransformPoint(transform, command.X1, command.Y1, out var cx, out var cy);
                    TransformPoint(transform, command.X2, command.Y2, out var x2, out var y2);
                    transformed.Add(new ParsedPathCommand(command.Kind, cx, cy, x2, y2, 0f, 0f));
                    break;
                }
                case PathCommandKind.CubicTo:
                {
                    TransformPoint(transform, command.X1, command.Y1, out var c1x, out var c1y);
                    TransformPoint(transform, command.X2, command.Y2, out var c2x, out var c2y);
                    TransformPoint(transform, command.X3, command.Y3, out var x3, out var y3);
                    transformed.Add(new ParsedPathCommand(command.Kind, c1x, c1y, c2x, c2y, x3, y3));
                    break;
                }
                case PathCommandKind.Close:
                    transformed.Add(command);
                    break;
            }
        }

        return transformed;
    }

    private static bool TryComputePathBounds(
        IReadOnlyList<ParsedPathCommand> commands,
        out float minX,
        out float minY,
        out float width,
        out float height)
    {
        var localMinX = 0f;
        var localMinY = 0f;
        width = 0f;
        height = 0f;
        var hasPoint = false;
        var maxX = 0f;
        var maxY = 0f;

        void Register(float x, float y)
        {
            if (!hasPoint)
            {
                localMinX = x;
                localMinY = y;
                maxX = x;
                maxY = y;
                hasPoint = true;
                return;
            }

            localMinX = Math.Min(localMinX, x);
            localMinY = Math.Min(localMinY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                case PathCommandKind.LineTo:
                    Register(command.X1, command.Y1);
                    break;
                case PathCommandKind.QuadTo:
                    Register(command.X1, command.Y1);
                    Register(command.X2, command.Y2);
                    break;
                case PathCommandKind.CubicTo:
                    Register(command.X1, command.Y1);
                    Register(command.X2, command.Y2);
                    Register(command.X3, command.Y3);
                    break;
                case PathCommandKind.Close:
                    break;
            }
        }

        if (!hasPoint)
        {
            minX = 0f;
            minY = 0f;
            return false;
        }

        minX = localMinX;
        minY = localMinY;
        width = Math.Max(1f, maxX - localMinX);
        height = Math.Max(1f, maxY - localMinY);
        return true;
    }

    private static bool TryExtractPathBounds(
        string? data,
        out float x,
        out float y,
        out float width,
        out float height)
    {
        return TryParsePathData(data, out _, out _, out x, out y, out width, out height);
    }

    private static bool TryParsePathData(
        string? data,
        out List<ParsedPathCommand> commands,
        out ShapePathFillRule? fillRule,
        out float minX,
        out float minY,
        out float width,
        out float height)
    {
        commands = new List<ParsedPathCommand>();
        fillRule = null;
        minX = 0f;
        minY = 0f;
        width = 0f;
        height = 0f;
        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var span = data.AsSpan();
        var index = 0;
        fillRule = ReadOptionalFillRulePrefix(span, ref index);
        var currentX = 0f;
        var currentY = 0f;
        var startX = 0f;
        var startY = 0f;
        var localMinX = 0f;
        var localMinY = 0f;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var hasPoint = false;
        char command = '\0';

        void RegisterPoint(float xPoint, float yPoint)
        {
            if (!hasPoint)
            {
                localMinX = xPoint;
                localMinY = yPoint;
                maxX = xPoint;
                maxY = yPoint;
                hasPoint = true;
                return;
            }

            localMinX = Math.Min(localMinX, xPoint);
            localMinY = Math.Min(localMinY, yPoint);
            maxX = Math.Max(maxX, xPoint);
            maxY = Math.Max(maxY, yPoint);
        }

        while (true)
        {
            SkipPathSeparators(span, ref index);
            if (index >= span.Length)
            {
                break;
            }

            var token = span[index];
            if (IsPathCommand(token))
            {
                command = token;
                index++;
            }
            else if (command == '\0')
            {
                return false;
            }

            var upperCommand = char.ToUpperInvariant(command);
            var relative = char.IsLower(command);
            switch (upperCommand)
            {
                case 'M':
                {
                    if (!TryReadPathPair(span, ref index, out var moveX, out var moveY))
                    {
                        return false;
                    }

                    if (relative)
                    {
                        moveX += currentX;
                        moveY += currentY;
                    }

                    commands.Add(new ParsedPathCommand(PathCommandKind.MoveTo, moveX, moveY, 0f, 0f, 0f, 0f));
                    currentX = moveX;
                    currentY = moveY;
                    startX = moveX;
                    startY = moveY;
                    RegisterPoint(moveX, moveY);

                    while (TryReadPathPair(span, ref index, out var lineX, out var lineY))
                    {
                        if (relative)
                        {
                            lineX += currentX;
                            lineY += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.LineTo, lineX, lineY, 0f, 0f, 0f, 0f));
                        currentX = lineX;
                        currentY = lineY;
                        RegisterPoint(lineX, lineY);
                    }

                    break;
                }
                case 'L':
                {
                    var parsedAny = false;
                    while (TryReadPathPair(span, ref index, out var lineX, out var lineY))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            lineX += currentX;
                            lineY += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.LineTo, lineX, lineY, 0f, 0f, 0f, 0f));
                        currentX = lineX;
                        currentY = lineY;
                        RegisterPoint(lineX, lineY);
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'H':
                {
                    var parsedAny = false;
                    while (TryReadPathNumber(span, ref index, out var horizontal))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            horizontal += currentX;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.LineTo, horizontal, currentY, 0f, 0f, 0f, 0f));
                        currentX = horizontal;
                        RegisterPoint(currentX, currentY);
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'V':
                {
                    var parsedAny = false;
                    while (TryReadPathNumber(span, ref index, out var vertical))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            vertical += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.LineTo, currentX, vertical, 0f, 0f, 0f, 0f));
                        currentY = vertical;
                        RegisterPoint(currentX, currentY);
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'C':
                {
                    var parsedAny = false;
                    while (TryReadPathPair(span, ref index, out var c1x, out var c1y)
                           && TryReadPathPair(span, ref index, out var c2x, out var c2y)
                           && TryReadPathPair(span, ref index, out var endX, out var endY))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            c1x += currentX;
                            c1y += currentY;
                            c2x += currentX;
                            c2y += currentY;
                            endX += currentX;
                            endY += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.CubicTo, c1x, c1y, c2x, c2y, endX, endY));
                        RegisterPoint(c1x, c1y);
                        RegisterPoint(c2x, c2y);
                        RegisterPoint(endX, endY);
                        currentX = endX;
                        currentY = endY;
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'S':
                {
                    var parsedAny = false;
                    while (TryReadPathPair(span, ref index, out var c2x, out var c2y)
                           && TryReadPathPair(span, ref index, out var endX, out var endY))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            c2x += currentX;
                            c2y += currentY;
                            endX += currentX;
                            endY += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.CubicTo, currentX, currentY, c2x, c2y, endX, endY));
                        RegisterPoint(c2x, c2y);
                        RegisterPoint(endX, endY);
                        currentX = endX;
                        currentY = endY;
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'Q':
                {
                    var parsedAny = false;
                    while (TryReadPathPair(span, ref index, out var qx, out var qy)
                           && TryReadPathPair(span, ref index, out var endX, out var endY))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            qx += currentX;
                            qy += currentY;
                            endX += currentX;
                            endY += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.QuadTo, qx, qy, endX, endY, 0f, 0f));
                        RegisterPoint(qx, qy);
                        RegisterPoint(endX, endY);
                        currentX = endX;
                        currentY = endY;
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'T':
                {
                    var parsedAny = false;
                    while (TryReadPathPair(span, ref index, out var endX, out var endY))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            endX += currentX;
                            endY += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.LineTo, endX, endY, 0f, 0f, 0f, 0f));
                        RegisterPoint(endX, endY);
                        currentX = endX;
                        currentY = endY;
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'A':
                {
                    var parsedAny = false;
                    while (TryReadPathNumber(span, ref index, out _)
                           && TryReadPathNumber(span, ref index, out _)
                           && TryReadPathNumber(span, ref index, out _)
                           && TryReadPathNumber(span, ref index, out _)
                           && TryReadPathNumber(span, ref index, out _)
                           && TryReadPathPair(span, ref index, out var endX, out var endY))
                    {
                        parsedAny = true;
                        if (relative)
                        {
                            endX += currentX;
                            endY += currentY;
                        }

                        commands.Add(new ParsedPathCommand(PathCommandKind.LineTo, endX, endY, 0f, 0f, 0f, 0f));
                        RegisterPoint(endX, endY);
                        currentX = endX;
                        currentY = endY;
                    }

                    if (!parsedAny)
                    {
                        return false;
                    }

                    break;
                }
                case 'Z':
                    commands.Add(new ParsedPathCommand(PathCommandKind.Close, 0f, 0f, 0f, 0f, 0f, 0f));
                    currentX = startX;
                    currentY = startY;
                    RegisterPoint(currentX, currentY);
                    break;
                default:
                    return false;
            }
        }

        if (!hasPoint || commands.Count == 0)
        {
            return false;
        }

        minX = localMinX;
        minY = localMinY;
        width = Math.Max(1f, maxX - localMinX);
        height = Math.Max(1f, maxY - localMinY);
        return true;
    }

    private static bool TryReadPathPair(ReadOnlySpan<char> span, ref int index, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        var start = index;
        if (!TryReadPathNumber(span, ref index, out x))
        {
            index = start;
            return false;
        }

        if (!TryReadPathNumber(span, ref index, out y))
        {
            index = start;
            return false;
        }

        return true;
    }

    private static bool TryReadPathNumber(ReadOnlySpan<char> span, ref int index, out float value)
    {
        value = 0f;
        SkipPathSeparators(span, ref index);
        if (index >= span.Length)
        {
            return false;
        }

        var start = index;
        if (span[index] == '+' || span[index] == '-')
        {
            index++;
        }

        var hasDigits = false;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            hasDigits = true;
            index++;
        }

        if (index < span.Length && span[index] == '.')
        {
            index++;
            while (index < span.Length && char.IsDigit(span[index]))
            {
                hasDigits = true;
                index++;
            }
        }

        if (!hasDigits)
        {
            index = start;
            return false;
        }

        if (index < span.Length && (span[index] == 'e' || span[index] == 'E'))
        {
            var exponent = index;
            index++;
            if (index < span.Length && (span[index] == '+' || span[index] == '-'))
            {
                index++;
            }

            var exponentDigits = false;
            while (index < span.Length && char.IsDigit(span[index]))
            {
                exponentDigits = true;
                index++;
            }

            if (!exponentDigits)
            {
                index = exponent;
            }
        }

        var token = span[start..index];
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static ShapePathFillRule? ReadOptionalFillRulePrefix(ReadOnlySpan<char> span, ref int index)
    {
        SkipPathSeparators(span, ref index);
        if (index >= span.Length)
        {
            return null;
        }

        if (span[index] is not ('F' or 'f'))
        {
            return null;
        }

        var originalIndex = index;
        index++;
        SkipPathSeparators(span, ref index);
        if (index >= span.Length || (span[index] is not ('0' or '1')))
        {
            index = originalIndex;
            return null;
        }

        var fillRuleCode = span[index];
        index++;
        SkipPathSeparators(span, ref index);
        return fillRuleCode == '0' ? ShapePathFillRule.EvenOdd : ShapePathFillRule.NonZero;
    }

    private static void SkipPathSeparators(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length)
        {
            var ch = span[index];
            if (char.IsWhiteSpace(ch) || ch == ',')
            {
                index++;
                continue;
            }

            break;
        }
    }

    private static bool IsPathCommand(char value)
    {
        return value is 'M' or 'm'
            or 'L' or 'l'
            or 'H' or 'h'
            or 'V' or 'v'
            or 'C' or 'c'
            or 'S' or 's'
            or 'Q' or 'q'
            or 'T' or 't'
            or 'A' or 'a'
            or 'Z' or 'z';
    }

    private abstract record PageItem;

    private sealed record TextItem(string Text, float X, float Y, string FontFamily, float FontSize, DocColor Color, HyperlinkInfo? Hyperlink) : PageItem;

    private sealed record ImageItem(byte[] Bytes, float X, float Y, float Width, float Height, string ContentType, HyperlinkInfo? Hyperlink) : PageItem;

    private sealed record ShapeItem(
        string Data,
        float X,
        float Y,
        float Width,
        float Height,
        DocColor? Fill,
        DocColor? Stroke,
        float StrokeThickness,
        ShapePathFillRule? FillRule,
        HyperlinkInfo? Hyperlink) : PageItem;

    private sealed record LayoutPage(float Width, float Height)
    {
        public List<PageItem> Items { get; } = new();
    }

    private sealed record GlyphRecord(string Text, float OriginX, float OriginY, TextStyleProperties Style, HyperlinkInfo? Hyperlink);

    private sealed record ImageRecord(byte[] Bytes, float X, float Y, float Width, float Height, string ContentType, HyperlinkInfo? Hyperlink);

    private sealed record ShapeRecord(float X, float Y, ShapeInline Shape);

    private sealed record PageBlockRecord(float X, float Y, Block Block);

    private sealed class PageResourceSet
    {
        public Dictionary<string, DocColor> SolidColors { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ImageBrushResource> ImageBrushes { get; } = new(StringComparer.Ordinal);
    }

    private sealed record ImageBrushResource(string BasePartPath, string ImageSource, string? Viewport);

    private readonly record struct AffineTransform(
        double M11,
        double M12,
        double M21,
        double M22,
        double OffsetX,
        double OffsetY)
    {
        public static readonly AffineTransform Identity = new(1d, 0d, 0d, 1d, 0d, 0d);

        public bool IsIdentity =>
            Math.Abs(M11 - 1d) < 0.0001d
            && Math.Abs(M12) < 0.0001d
            && Math.Abs(M21) < 0.0001d
            && Math.Abs(M22 - 1d) < 0.0001d
            && Math.Abs(OffsetX) < 0.0001d
            && Math.Abs(OffsetY) < 0.0001d;
    }

    private enum PathCommandKind
    {
        MoveTo,
        LineTo,
        QuadTo,
        CubicTo,
        Close
    }

    private readonly record struct ParsedPathCommand(
        PathCommandKind Kind,
        float X1,
        float Y1,
        float X2,
        float Y2,
        float X3,
        float Y3);
}
