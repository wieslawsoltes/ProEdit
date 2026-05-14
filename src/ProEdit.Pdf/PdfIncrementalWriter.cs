using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProEdit.Pdf;

public static class PdfIncrementalWriter
{
    private static readonly Regex SizeRegex = new(@"/Size\s+(\d+)", RegexOptions.Compiled);
    private static readonly Regex RootRegex = new(@"/Root\s+(\d+\s+\d+\s+R)", RegexOptions.Compiled);
    private static readonly Regex InfoRegex = new(@"/Info\s+(\d+\s+\d+\s+R)", RegexOptions.Compiled);
    private static readonly Regex IdRegex = new(@"/ID\s+\[(.+?)\]", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ObjectRegex = new(@"(\d+)\s+(\d+)\s+obj", RegexOptions.Compiled);

    public static bool TryAppendOverlayIncrementalUpdate(
        byte[] originalBytes,
        IReadOnlyList<PdfIncrementalOverlay> overlays,
        out byte[] updatedBytes,
        out string? error,
        out List<string> issues)
    {
        issues = new List<string>();
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentNullException.ThrowIfNull(overlays);

        if (!TryParseTrailer(originalBytes, out var trailer, out error))
        {
            updatedBytes = Array.Empty<byte>();
            return false;
        }

        var pageObjects = ParsePageObjects(originalBytes);
        if (pageObjects.Count == 0)
        {
            updatedBytes = Array.Empty<byte>();
            error = "No page objects found in source PDF.";
            return false;
        }

        var overlayMap = overlays
            .GroupBy(overlay => overlay.PageIndex)
            .ToDictionary(group => group.Key, group => group.ToList());

        var nextObjectId = trailer.Size;
        var newObjects = new List<PdfObjectWrite>();
        var updatedPages = new List<PdfObjectWrite>();

        foreach (var page in pageObjects)
        {
            if (!overlayMap.TryGetValue(page.PageIndex, out var pageOverlays) || pageOverlays.Count == 0)
            {
                continue;
            }

            var imageOverlays = pageOverlays
                .Where(overlay => overlay.Kind == PdfIncrementalOverlayKind.Image && overlay.ImageBytes is { Length: > 0 })
                .ToList();
            var textOverlays = pageOverlays
                .Where(overlay => overlay.Kind == PdfIncrementalOverlayKind.Text && !string.IsNullOrWhiteSpace(overlay.Text))
                .ToList();

            if (imageOverlays.Count == 0 && textOverlays.Count == 0)
            {
                continue;
            }

            if (!TryGetPageSize(page.Body, out var pageWidth, out var pageHeight))
            {
                pageWidth = 0;
                pageHeight = 0;
            }

            PdfIncrementalOverlay? imageOverlay = null;
            if (imageOverlays.Count > 0)
            {
                if (imageOverlays.Count > 1)
                {
                    issues.Add($"Page {page.PageIndex + 1}: multiple image overlays detected; using the first.");
                }

                imageOverlay = imageOverlays[0];
            }

            string? imageName = null;
            string? imageRef = null;

            if (imageOverlay is not null)
            {
                if (imageOverlay.ImageWidth <= 0 || imageOverlay.ImageHeight <= 0)
                {
                    issues.Add($"Page {page.PageIndex + 1}: image overlay has invalid dimensions.");
                    imageOverlay = null;
                }
                else
                {
                    imageName = $"VIm{page.PageIndex + 1}";
                    imageRef = $"{nextObjectId} 0 R";
                    var imageObject = BuildImageObject(nextObjectId, imageOverlay);
                    newObjects.Add(imageObject);
                    nextObjectId++;
                }
            }

            var overlayStreamRef = $"{nextObjectId} 0 R";
            var overlayStream = BuildOverlayStream(imageOverlay, imageName, textOverlays, pageWidth, pageHeight);
            newObjects.Add(new PdfObjectWrite(nextObjectId, 0, overlayStream));
            nextObjectId++;

            if (!TryUpdatePageObject(page.Body, overlayStreamRef, imageName, imageRef, textOverlays.Count > 0, out var updatedBody, out var issue))
            {
                issues.Add($"Page {page.PageIndex + 1}: {issue}");
                continue;
            }

            updatedPages.Add(new PdfObjectWrite(page.ObjectNumber, page.Generation, updatedBody));
        }

        if (newObjects.Count == 0 && updatedPages.Count == 0)
        {
            updatedBytes = Array.Empty<byte>();
            error = "No overlays could be applied.";
            return false;
        }

        using var stream = new MemoryStream();
        stream.Write(originalBytes, 0, originalBytes.Length);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.WriteLine();
        writer.WriteLine("% ProEdit incremental overlay update");

        var objectOffsets = new Dictionary<int, (long Offset, int Generation)>();

        foreach (var obj in updatedPages)
        {
            var offset = stream.Position;
            writer.WriteLine($"{obj.ObjectNumber} {obj.Generation} obj");
            writer.WriteLine(obj.Body);
            writer.WriteLine("endobj");
            writer.Flush();
            objectOffsets[obj.ObjectNumber] = (offset, obj.Generation);
        }

        foreach (var obj in newObjects)
        {
            var offset = stream.Position;
            writer.WriteLine($"{obj.ObjectNumber} {obj.Generation} obj");
            writer.WriteLine(obj.Body);
            writer.WriteLine("endobj");
            writer.Flush();
            objectOffsets[obj.ObjectNumber] = (offset, obj.Generation);
        }

        var xrefOffset = stream.Position;
        WriteXref(writer, objectOffsets);
        WriteTrailer(writer, trailer, objectOffsets.Keys.Max());
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("%%EOF");
        writer.Flush();

        updatedBytes = stream.ToArray();
        error = null;
        return true;
    }

    public static bool TryAppendPlaceholderIncrementalUpdate(
        byte[] originalBytes,
        out byte[] updatedBytes,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(originalBytes);
        if (!TryParseTrailer(originalBytes, out var trailer, out error))
        {
            updatedBytes = Array.Empty<byte>();
            return false;
        }

        var objectId = trailer.Size;
        using var stream = new MemoryStream();
        stream.Write(originalBytes, 0, originalBytes.Length);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.WriteLine();
        writer.WriteLine("% ProEdit incremental update");

        var objectOffset = stream.Position;
        writer.WriteLine($"{objectId} 0 obj");
        writer.WriteLine("<< /Type /XObject /Subtype /Form /Length 0 >>");
        writer.WriteLine("stream");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        var xrefOffset = stream.Position;
        var offsets = new Dictionary<int, (long Offset, int Generation)>
        {
            [objectId] = (objectOffset, 0)
        };
        WriteXref(writer, offsets);
        WriteTrailer(writer, trailer, objectId);
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("%%EOF");
        writer.Flush();

        updatedBytes = stream.ToArray();
        error = null;
        return true;
    }

    private static void WriteXref(StreamWriter writer, Dictionary<int, (long Offset, int Generation)> offsets)
    {
        writer.WriteLine("xref");
        var ordered = offsets.Keys.OrderBy(id => id).ToList();
        var index = 0;
        while (index < ordered.Count)
        {
            var start = ordered[index];
            var count = 1;
            while (index + count < ordered.Count && ordered[index + count] == start + count)
            {
                count++;
            }

            writer.WriteLine($"{start} {count}");
            for (var i = 0; i < count; i++)
            {
                var key = start + i;
                var entry = offsets[key];
                writer.WriteLine($"{entry.Offset:0000000000} {entry.Generation:00000} n ");
            }

            index += count;
        }
    }

    private static void WriteTrailer(StreamWriter writer, PdfTrailerInfo trailer, int maxObjectId)
    {
        writer.WriteLine("trailer");
        writer.Write("<< ");
        writer.Write($"/Size {Math.Max(maxObjectId + 1, trailer.Size)} ");
        writer.Write($"/Root {trailer.RootRef} ");
        if (!string.IsNullOrWhiteSpace(trailer.InfoRef))
        {
            writer.Write($"/Info {trailer.InfoRef} ");
        }

        if (!string.IsNullOrWhiteSpace(trailer.IdToken))
        {
            writer.Write($"/ID {trailer.IdToken} ");
        }

        writer.Write($"/Prev {trailer.StartXref} ");
        writer.WriteLine(">>");
    }

    private static PdfObjectWrite BuildImageObject(int objectId, PdfIncrementalOverlay overlay)
    {
        var encodedData = EncodeAsciiHex(overlay.ImageBytes ?? Array.Empty<byte>());
        var streamData = $"{encodedData}\n";
        var length = Encoding.ASCII.GetByteCount(streamData);
        var dict = $"<< /Type /XObject /Subtype /Image /Width {overlay.ImageWidth} /Height {overlay.ImageHeight} " +
                   "/ColorSpace /DeviceRGB /BitsPerComponent 8 " +
                   "/Filter [ /ASCIIHexDecode /DCTDecode ] " +
                   $"/Length {length} >>";
        var body = $"{dict}\nstream\n{streamData}endstream";
        return new PdfObjectWrite(objectId, 0, body);
    }

    private static string BuildOverlayStream(
        PdfIncrementalOverlay? imageOverlay,
        string? imageName,
        IReadOnlyList<PdfIncrementalOverlay> textOverlays,
        double pageWidth,
        double pageHeight)
    {
        var contentBuilder = new StringBuilder();

        if (imageOverlay is not null && !string.IsNullOrWhiteSpace(imageName))
        {
            var bounds = imageOverlay.Bounds;
            var width = bounds.Width > 0
                ? bounds.Width
                : (pageWidth > 0 ? pageWidth : imageOverlay.ImageWidth);
            var height = bounds.Height > 0
                ? bounds.Height
                : (pageHeight > 0 ? pageHeight : imageOverlay.ImageHeight);
            var x = bounds.Width > 0 || bounds.Height > 0 ? bounds.X : 0;
            var y = bounds.Width > 0 || bounds.Height > 0 ? bounds.Y : 0;

            if (width > 0 && height > 0)
            {
                contentBuilder.AppendLine("q");
                contentBuilder.AppendLine($"{width.ToString(CultureInfo.InvariantCulture)} 0 0 {height.ToString(CultureInfo.InvariantCulture)} " +
                                          $"{x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} cm");
                contentBuilder.AppendLine($"/{imageName} Do");
                contentBuilder.AppendLine("Q");
            }
        }

        if (textOverlays.Count > 0)
        {
            var y = pageHeight > 0 ? Math.Max(36.0, pageHeight - 36.0) : 36.0;
            foreach (var overlay in textOverlays)
            {
                if (string.IsNullOrWhiteSpace(overlay.Text))
                {
                    continue;
                }

                contentBuilder.AppendLine("BT");
                contentBuilder.AppendLine("/FVI 10 Tf");
                contentBuilder.AppendLine("0 0 0 rg");
                contentBuilder.AppendLine($"36 {y.ToString(CultureInfo.InvariantCulture)} Td");
                contentBuilder.AppendLine($"({EscapePdfText(overlay.Text)}) Tj");
                contentBuilder.AppendLine("ET");
            }
        }

        var content = contentBuilder.ToString();
        if (content.Length > 0 && !content.EndsWith('\n'))
        {
            content += "\n";
        }

        var length = Encoding.ASCII.GetByteCount(content);
        return $"<< /Length {length} >>\nstream\n{content}endstream";
    }

    private static bool TryUpdatePageObject(
        string body,
        string overlayStreamRef,
        string? xObjectName,
        string? xObjectRef,
        bool includeFont,
        out string updatedBody,
        out string issue)
    {
        updatedBody = body;
        issue = string.Empty;

        if (!TryUpdateResources(ref updatedBody, xObjectName, xObjectRef, includeFont, out var resourceIssue))
        {
            issue = resourceIssue;
            return false;
        }

        if (!TryUpdateContents(ref updatedBody, overlayStreamRef, out var contentsIssue))
        {
            issue = contentsIssue;
            return false;
        }

        return true;
    }

    private static bool TryUpdateResources(
        ref string body,
        string? xObjectName,
        string? xObjectRef,
        bool includeFont,
        out string issue)
    {
        issue = string.Empty;
        var index = body.IndexOf("/Resources", StringComparison.Ordinal);
        if (index < 0)
        {
            var insertAt = body.LastIndexOf(">>", StringComparison.Ordinal);
            if (insertAt < 0)
            {
                issue = "Unable to locate page dictionary end.";
                return false;
            }

            var resourcesBuilder = new StringBuilder(" /Resources <<");
            if (!string.IsNullOrWhiteSpace(xObjectName) && !string.IsNullOrWhiteSpace(xObjectRef))
            {
                resourcesBuilder.Append($" /XObject << /{xObjectName} {xObjectRef} >>");
            }

            if (includeFont)
            {
                resourcesBuilder.Append(" /Font << /FVI << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
            }

            resourcesBuilder.Append(" >>");
            body = body.Insert(insertAt, resourcesBuilder.ToString());
            return true;
        }

        var cursor = index + "/Resources".Length;
        SkipWhitespace(body, ref cursor);
        if (cursor >= body.Length)
        {
            issue = "Invalid /Resources entry.";
            return false;
        }

        if (IsIndirectRef(body, cursor, out _))
        {
            issue = "/Resources is an indirect reference.";
            return false;
        }

        if (!body.AsSpan(cursor).StartsWith("<<", StringComparison.Ordinal))
        {
            issue = "Unsupported /Resources format.";
            return false;
        }

        if (!TryExtractBalanced(body, cursor, "<<", ">>", out var endIndex))
        {
            issue = "Unable to parse /Resources dictionary.";
            return false;
        }

        var resourcesContent = body.Substring(cursor, endIndex - cursor);
        if (!string.IsNullOrWhiteSpace(xObjectName) && !string.IsNullOrWhiteSpace(xObjectRef))
        {
            if (!TryEnsureXObjectResource(resourcesContent, xObjectName, xObjectRef, out var xObjectUpdated, out issue))
            {
                return false;
            }

            resourcesContent = xObjectUpdated;
        }

        if (includeFont)
        {
            resourcesContent = EnsureFontResource(resourcesContent);
        }

        body = body[..cursor] + resourcesContent + body[endIndex..];
        return true;
    }

    private static bool TryEnsureXObjectResource(string resources, string name, string reference, out string updated, out string issue)
    {
        issue = string.Empty;
        updated = resources;
        var xObjectIndex = resources.IndexOf("/XObject", StringComparison.Ordinal);
        if (xObjectIndex < 0)
        {
            updated = resources.Insert(resources.Length - 2, $" /XObject << /{name} {reference} >>");
            return true;
        }

        var cursor = xObjectIndex + "/XObject".Length;
        SkipWhitespace(resources, ref cursor);
        if (cursor >= resources.Length)
        {
            issue = "Invalid /XObject entry.";
            return false;
        }

        if (IsIndirectRef(resources, cursor, out _))
        {
            issue = "/XObject is an indirect reference.";
            return false;
        }

        if (!resources.AsSpan(cursor).StartsWith("<<", StringComparison.Ordinal))
        {
            issue = "Unsupported /XObject format.";
            return false;
        }

        if (!TryExtractBalanced(resources, cursor, "<<", ">>", out var endIndex))
        {
            issue = "Unable to parse /XObject dictionary.";
            return false;
        }

        var xObjectDict = resources.Substring(cursor, endIndex - cursor);
        if (xObjectDict.Contains($"/{name}", StringComparison.Ordinal))
        {
            return true;
        }

        var updatedDict = xObjectDict.Insert(xObjectDict.Length - 2, $" /{name} {reference}");
        updated = resources[..cursor] + updatedDict + resources[endIndex..];
        return true;
    }

    private static string EnsureFontResource(string resources)
    {
        var fontIndex = resources.IndexOf("/Font", StringComparison.Ordinal);
        if (fontIndex < 0)
        {
            return resources.Insert(resources.Length - 2, " /Font << /FVI << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        }

        var cursor = fontIndex + "/Font".Length;
        SkipWhitespace(resources, ref cursor);
        if (cursor >= resources.Length)
        {
            return resources;
        }

        if (IsIndirectRef(resources, cursor, out _))
        {
            return resources;
        }

        if (!resources.AsSpan(cursor).StartsWith("<<", StringComparison.Ordinal))
        {
            return resources;
        }

        if (!TryExtractBalanced(resources, cursor, "<<", ">>", out var endIndex))
        {
            return resources;
        }

        var fontDict = resources.Substring(cursor, endIndex - cursor);
        if (fontDict.Contains("/FVI", StringComparison.Ordinal))
        {
            return resources;
        }

        var updatedFontDict = fontDict.Insert(fontDict.Length - 2, " /FVI << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        return resources[..cursor] + updatedFontDict + resources[endIndex..];
    }

    private static bool TryUpdateContents(ref string body, string overlayStreamRef, out string issue)
    {
        issue = string.Empty;
        var index = body.IndexOf("/Contents", StringComparison.Ordinal);
        if (index < 0)
        {
            var insertAt = body.LastIndexOf(">>", StringComparison.Ordinal);
            if (insertAt < 0)
            {
                issue = "Unable to locate page dictionary end for /Contents.";
                return false;
            }

            body = body.Insert(insertAt, $" /Contents {overlayStreamRef}");
            return true;
        }

        var cursor = index + "/Contents".Length;
        SkipWhitespace(body, ref cursor);
        if (cursor >= body.Length)
        {
            issue = "Invalid /Contents entry.";
            return false;
        }

        if (body[cursor] == '[')
        {
            if (!TryExtractBalanced(body, cursor, "[", "]", out var endIndex))
            {
                issue = "Unable to parse /Contents array.";
                return false;
            }

            var arrayContent = body.Substring(cursor, endIndex - cursor);
            var updatedArray = arrayContent.Insert(arrayContent.Length - 1, $" {overlayStreamRef}");
            body = body[..cursor] + updatedArray + body[endIndex..];
            return true;
        }

        if (IsIndirectRef(body, cursor, out var refToken))
        {
            var updated = $"[ {refToken} {overlayStreamRef} ]";
            body = body[..cursor] + updated + body[(cursor + refToken.Length)..];
            return true;
        }

        issue = "Unsupported /Contents format.";
        return false;
    }

    private static bool IsIndirectRef(string text, int startIndex, out string refToken)
    {
        refToken = string.Empty;
        var span = text.AsSpan(startIndex);
        var match = Regex.Match(span.ToString(), @"^(\d+)\s+(\d+)\s+R");
        if (!match.Success)
        {
            return false;
        }

        refToken = match.Value;
        return true;
    }

    private static bool TryExtractBalanced(string text, int startIndex, string open, string close, out int endIndex)
    {
        endIndex = -1;
        var depth = 0;
        for (var i = startIndex; i < text.Length - 1; i++)
        {
            if (text.AsSpan(i).StartsWith(open, StringComparison.Ordinal))
            {
                depth++;
                i += open.Length - 1;
                continue;
            }

            if (text.AsSpan(i).StartsWith(close, StringComparison.Ordinal))
            {
                depth--;
                i += close.Length - 1;
                if (depth == 0)
                {
                    endIndex = i + 1;
                    return true;
                }
            }
        }

        return false;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

    private static bool TryGetPageSize(string body, out double width, out double height)
    {
        width = 0;
        height = 0;
        var mediaIndex = body.IndexOf("/MediaBox", StringComparison.Ordinal);
        if (mediaIndex < 0)
        {
            return false;
        }

        var cursor = mediaIndex + "/MediaBox".Length;
        SkipWhitespace(body, ref cursor);
        if (cursor >= body.Length || body[cursor] != '[')
        {
            return false;
        }

        var endIndex = body.IndexOf(']', cursor);
        if (endIndex < 0)
        {
            return false;
        }

        var content = body.Substring(cursor + 1, endIndex - cursor - 1);
        var parts = content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x0))
        {
            return false;
        }

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y0))
        {
            return false;
        }

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x1))
        {
            return false;
        }

        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y1))
        {
            return false;
        }

        width = x1 - x0;
        height = y1 - y0;
        return width > 0 && height > 0;
    }

    private static string EscapePdfText(string text)
    {
        return text.Replace(@"\", @"\\")
                   .Replace("(", @"\(")
                   .Replace(")", @"\)");
    }

    private static string EncodeAsciiHex(byte[] data)
    {
        if (data.Length == 0)
        {
            return ">";
        }

        var builder = new StringBuilder(data.Length * 2 + 1);
        foreach (var value in data)
        {
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        builder.Append('>');
        return builder.ToString();
    }

    private static List<PdfPageObjectInfo> ParsePageObjects(byte[] bytes)
    {
        var content = Encoding.ASCII.GetString(bytes);
        var matches = ObjectRegex.Matches(content);
        var results = new List<PdfPageObjectInfo>();
        var pageIndex = 0;

        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectNumber))
            {
                continue;
            }

            if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
            {
                generation = 0;
            }

            var bodyStart = match.Index + match.Length;
            var endObjIndex = content.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
            if (endObjIndex < 0)
            {
                continue;
            }

            var body = content.Substring(bodyStart, endObjIndex - bodyStart).Trim();
            if (!body.Contains("/Type /Page", StringComparison.Ordinal) || body.Contains("/Type /Pages", StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(new PdfPageObjectInfo(objectNumber, generation, body, pageIndex));
            pageIndex++;
        }

        return results;
    }

    private static bool TryParseTrailer(byte[] bytes, out PdfTrailerInfo trailer, out string? error)
    {
        trailer = default;
        var text = DecodeTail(bytes);
        var startXrefIndex = text.LastIndexOf("startxref", StringComparison.OrdinalIgnoreCase);
        if (startXrefIndex < 0)
        {
            error = "Missing startxref marker.";
            return false;
        }

        var startXrefText = text[startXrefIndex..];
        if (!TryParseStartXref(startXrefText, out var startXref))
        {
            error = "Unable to parse startxref offset.";
            return false;
        }

        var trailerIndex = text.LastIndexOf("trailer", startXrefIndex, StringComparison.OrdinalIgnoreCase);
        if (trailerIndex < 0)
        {
            error = "Missing trailer section.";
            return false;
        }

        var trailerText = text[trailerIndex..startXrefIndex];
        var sizeMatch = SizeRegex.Match(trailerText);
        var rootMatch = RootRegex.Match(trailerText);

        if (!sizeMatch.Success || !rootMatch.Success)
        {
            error = "Trailer is missing required /Size or /Root entries.";
            return false;
        }

        if (!int.TryParse(sizeMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            error = "Invalid /Size value in trailer.";
            return false;
        }

        var infoMatch = InfoRegex.Match(trailerText);
        var idMatch = IdRegex.Match(trailerText);

        trailer = new PdfTrailerInfo(size, rootMatch.Groups[1].Value, startXref)
        {
            InfoRef = infoMatch.Success ? infoMatch.Groups[1].Value : null,
            IdToken = idMatch.Success ? $"[{idMatch.Groups[1].Value}]" : null
        };

        error = null;
        return true;
    }

    private static string DecodeTail(byte[] bytes)
    {
        var tailLength = Math.Min(bytes.Length, 65536);
        var start = bytes.Length - tailLength;
        return Encoding.ASCII.GetString(bytes, start, tailLength);
    }

    private static bool TryParseStartXref(string tail, out long startXref)
    {
        startXref = 0;
        var lines = tail.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].Trim().Equals("startxref", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (long.TryParse(lines[i + 1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out startXref))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct PdfTrailerInfo(int Size, string RootRef, long StartXref)
    {
        public string? InfoRef { get; init; }
        public string? IdToken { get; init; }
    }

    private readonly record struct PdfObjectWrite(int ObjectNumber, int Generation, string Body);

    private readonly record struct PdfPageObjectInfo(int ObjectNumber, int Generation, string Body, int PageIndex);
}
