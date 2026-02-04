using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.InlineImages;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using Vibe.Office.Pdf;

namespace Vibe.Office.Pdf.PdfPig;

public sealed class PdfPigParser : IPdfParser
{
    public string ProviderId => PdfProviderIds.PdfPig;
    private static readonly uint[] CrcTable = BuildCrcTable();
    private static readonly byte[] PngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public PdfDocumentAst Parse(Stream stream, PdfParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var effectiveOptions = options ?? new PdfParserOptions();

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        buffer.Position = 0;

        using var document = PdfDocument.Open(buffer);
        var result = new PdfDocumentAst
        {
            OriginalBytes = effectiveOptions.PreserveSourceBytes ? bytes : null,
            ParserProviderId = ProviderId
        };
        PopulateMetadata(document, result.Metadata);

        var pageIndex = 0;
        foreach (var page in document.GetPages())
        {
            var pageAst = new PdfPageAst
            {
                Index = pageIndex,
                Width = page.Width,
                Height = page.Height,
                Rotation = page.Rotation.Value,
                ExtractedText = page.Text
            };

            ExtractTextGlyphs(page, pageAst, effectiveOptions);
            ExtractTextRuns(page, pageAst, effectiveOptions);
            ExtractImages(document, page, pageAst);
            ExtractPaths(page, pageAst, effectiveOptions);
            BuildContentOrder(page, pageAst);

            result.Pages.Add(pageAst);
            pageIndex++;
        }

        ExtractEmbeddedFonts(document, result, effectiveOptions);
        return result;
    }

    private static void PopulateMetadata(PdfDocument document, PdfMetadata metadata)
    {
        var info = document.Information;
        metadata.Title = info?.Title;
        metadata.Author = info?.Author;
        metadata.Subject = info?.Subject;
        metadata.Keywords = info?.Keywords;
        metadata.Created = info?.GetCreatedDateTimeOffset();
        metadata.Modified = info?.GetModifiedDateTimeOffset();
    }

    private static void ExtractTextRuns(Page page, PdfPageAst pageAst, PdfParserOptions options)
    {
        foreach (var word in page.GetWords())
        {
            if (string.IsNullOrWhiteSpace(word.Text))
            {
                continue;
            }

            var rect = ToRect(word.BoundingBox);
            var fontName = string.IsNullOrWhiteSpace(word.FontName)
                ? word.Letters.FirstOrDefault()?.FontName
                : word.FontName;
            var fontInfo = BuildFontInfo(fontName, options.NormalizeFontNames);
            var fontSize = ResolveFontSize(word.Letters);
            var baselineY = ResolveBaselineY(word.Letters, rect);
            var text = BuildWordText(word);
            var averageGap = ResolveAverageLetterGap(word.Letters);

            var color = ResolveTextColor(word.Letters);
            var sequence = ResolveTextSequence(word.Letters);
            var index = pageAst.TextRuns.Count;
            pageAst.TextRuns.Add(new PdfTextRun(text, rect, fontInfo, fontSize, baselineY, averageGap, color, sequence, index));
        }
    }

    private static void ExtractTextGlyphs(Page page, PdfPageAst pageAst, PdfParserOptions options)
    {
        if (!options.ExtractTextGlyphs)
        {
            return;
        }

        var letters = page.Letters;
        if (letters is null || letters.Count == 0)
        {
            return;
        }

        var index = 0;
        foreach (var letter in letters)
        {
            if (string.IsNullOrEmpty(letter.Value))
            {
                continue;
            }

            var rect = ToRect(letter.GlyphRectangle);
            var fontInfo = BuildFontInfo(letter.FontName, options.NormalizeFontNames);
            var fontSize = letter.PointSize > 0 ? letter.PointSize : letter.FontSize;
            var color = letter.Color is null ? null : ToPdfColor(letter.Color);
            var orientation = MapOrientation(letter.TextOrientation);
            var baselineX = letter.StartBaseLine.X;
            var baselineY = letter.StartBaseLine.Y;
            var advance = letter.Width;

            pageAst.Glyphs.Add(new PdfTextGlyph(
                letter.Value,
                rect,
                fontInfo,
                fontSize,
                baselineX,
                baselineY,
                advance,
                orientation,
                color,
                letter.TextSequence,
                index++));
        }
    }

    private static void ExtractImages(PdfDocument document, Page page, PdfPageAst pageAst)
    {
        foreach (var image in page.GetImages())
        {
            var rect = ToRect(image.Bounds);
            if (!TryResolveImageData(document, image, out var bytes, out var mimeType))
            {
                continue;
            }

            pageAst.Images.Add(new PdfImageObject(rect, bytes, mimeType));
        }
    }

    private static void ExtractPaths(Page page, PdfPageAst pageAst, PdfParserOptions options)
    {
        if (!options.ExtractPaths)
        {
            return;
        }

        var paths = page.ExperimentalAccess.Paths;
        if (paths is null || paths.Count == 0)
        {
            return;
        }
        foreach (var path in paths)
        {
            if (path.IsClipping)
            {
                continue;
            }

            var pathObject = BuildPathObject(path);
            if (pathObject is null)
            {
                continue;
            }

            pageAst.Paths.Add(pathObject);
        }
    }

    private static void BuildContentOrder(Page page, PdfPageAst pageAst)
    {
        if (pageAst.TextRuns.Count == 0 && pageAst.Images.Count == 0 && pageAst.Paths.Count == 0)
        {
            return;
        }

        var textQueue = BuildTextQueue(pageAst);
        var imageQueue = new Queue<int>(Enumerable.Range(0, pageAst.Images.Count));
        var pathQueue = new Queue<int>(Enumerable.Range(0, pageAst.Paths.Count));

        if (page.Operations is { Count: > 0 })
        {
            foreach (var op in page.Operations)
            {
                if (IsTextShowOperation(op))
                {
                    EnqueueContent(textQueue, pageAst.ContentOrder, PdfContentItemKind.TextRun);
                    continue;
                }

                if (IsImageOperation(op))
                {
                    EnqueueContent(imageQueue, pageAst.ContentOrder, PdfContentItemKind.Image);
                    continue;
                }

                if (IsPathPaintOperation(op))
                {
                    EnqueueContent(pathQueue, pageAst.ContentOrder, PdfContentItemKind.Path);
                }
            }
        }

        while (textQueue.Count > 0)
        {
            pageAst.ContentOrder.Add(new PdfContentItem(PdfContentItemKind.TextRun, textQueue.Dequeue()));
        }

        while (imageQueue.Count > 0)
        {
            pageAst.ContentOrder.Add(new PdfContentItem(PdfContentItemKind.Image, imageQueue.Dequeue()));
        }

        while (pathQueue.Count > 0)
        {
            pageAst.ContentOrder.Add(new PdfContentItem(PdfContentItemKind.Path, pathQueue.Dequeue()));
        }
    }

    private static Queue<int> BuildTextQueue(PdfPageAst pageAst)
    {
        var ordered = pageAst.TextRuns
            .Select(run => new { run.Index, run.Sequence })
            .OrderBy(item => item.Sequence >= 0 ? item.Sequence : int.MaxValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Index);

        return new Queue<int>(ordered);
    }

    private static void EnqueueContent(Queue<int> queue, List<PdfContentItem> target, PdfContentItemKind kind)
    {
        if (queue.Count == 0)
        {
            return;
        }

        target.Add(new PdfContentItem(kind, queue.Dequeue()));
    }

    private static bool IsTextShowOperation(IGraphicsStateOperation operation)
    {
        var ns = operation.GetType().Namespace;
        return ns is not null && ns.Contains("TextShowing", StringComparison.Ordinal);
    }

    private static bool IsPathPaintOperation(IGraphicsStateOperation operation)
    {
        var ns = operation.GetType().Namespace;
        if (ns is null || !ns.Contains("PathPainting", StringComparison.Ordinal))
        {
            return false;
        }

        var name = operation.GetType().Name;
        return name.Contains("Stroke", StringComparison.Ordinal)
               || name.Contains("Fill", StringComparison.Ordinal);
    }

    private static bool IsImageOperation(IGraphicsStateOperation operation)
        => operation is EndInlineImage || operation is InvokeNamedXObject;

    private static void ExtractEmbeddedFonts(PdfDocument document, PdfDocumentAst result, PdfParserOptions options)
    {
        if (!options.ExtractEmbeddedFonts)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in document.GetPages())
        {
            if (!TryResolveFontDictionary(document, page.Dictionary, out var fontDictionary) || fontDictionary is null)
            {
                continue;
            }

            foreach (var entry in fontDictionary.Data)
            {
                if (entry.Value is null)
                {
                    continue;
                }

                var fontToken = ResolveToken(document, entry.Value);
                if (fontToken is not DictionaryToken fontDict)
                {
                    continue;
                }

                if (TryExtractEmbeddedFont(document, fontDict, out var embedded))
                {
                    var key = $"{embedded.FamilyName}|{embedded.PostScriptName}|{embedded.IsBold}|{embedded.IsItalic}|{embedded.Data.Length}";
                    if (seen.Add(key))
                    {
                        result.EmbeddedFonts.Add(embedded);
                    }
                }
            }
        }
    }

    private static bool TryResolveFontDictionary(PdfDocument document, DictionaryToken pageDictionary, out DictionaryToken? fontDictionary)
    {
        fontDictionary = null;
        if (!TryResolveDictionary(document, pageDictionary, "Resources", out var resources))
        {
            return false;
        }

        if (resources is null || !TryResolveDictionary(document, resources, "Font", out var fonts))
        {
            return false;
        }

        fontDictionary = fonts;
        return true;
    }

    private static bool TryResolveDictionary(PdfDocument document, DictionaryToken source, string key, out DictionaryToken? dictionary)
    {
        dictionary = null;
        if (!source.TryGet(NameToken.Create(key), out var token) || token is null)
        {
            return false;
        }

        dictionary = ResolveToken(document, token) as DictionaryToken;
        return dictionary is not null;
    }

    private static IToken? ResolveToken(PdfDocument document, IToken token)
    {
        if (token is IndirectReferenceToken reference)
        {
            try
            {
                return document.Structure.GetObject(reference.Data).Data;
            }
            catch
            {
                return null;
            }
        }

        return token;
    }

    private static bool TryExtractEmbeddedFont(PdfDocument document, DictionaryToken fontDict, out PdfEmbeddedFont embedded)
    {
        embedded = default!;
        var baseFont = TryGetName(document, fontDict, "BaseFont");
        if (string.IsNullOrWhiteSpace(baseFont))
        {
            return false;
        }

        var normalized = NormalizeFontName(baseFont);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!fontDict.TryGet(NameToken.Create("FontDescriptor"), out var descriptorToken) || descriptorToken is null)
        {
            return false;
        }

        var descriptor = ResolveToken(document, descriptorToken) as DictionaryToken;
        if (descriptor is null)
        {
            return false;
        }

        if (!TryGetFontStream(document, descriptor, out var stream, out var contentType, out var descriptorInfo))
        {
            return false;
        }

        var data = DecodeStream(stream);
        if (data.Length == 0)
        {
            return false;
        }

        var isItalic = ResolveItalic(baseFont, descriptorInfo);
        var isBold = ResolveBold(baseFont, descriptorInfo);
        embedded = new PdfEmbeddedFont(normalized, data, contentType, isBold, isItalic, baseFont);
        return true;
    }

    private static bool ResolveItalic(string name, FontDescriptorInfo descriptor)
    {
        if (NameContainsItalic(name))
        {
            return true;
        }

        return descriptor.ItalicAngle.HasValue && Math.Abs(descriptor.ItalicAngle.Value) > 0.01;
    }

    private static bool ResolveBold(string name, FontDescriptorInfo descriptor)
    {
        if (NameContainsBold(name))
        {
            return true;
        }

        return descriptor.FontWeight.HasValue && descriptor.FontWeight.Value >= 600;
    }

    private static bool NameContainsBold(string name)
        => name.Contains("bold", StringComparison.OrdinalIgnoreCase)
           || name.Contains("black", StringComparison.OrdinalIgnoreCase)
           || name.Contains("semibold", StringComparison.OrdinalIgnoreCase)
           || name.Contains("demi", StringComparison.OrdinalIgnoreCase);

    private static bool NameContainsItalic(string name)
        => name.Contains("italic", StringComparison.OrdinalIgnoreCase)
           || name.Contains("oblique", StringComparison.OrdinalIgnoreCase);

    private readonly record struct FontDescriptorInfo(double? ItalicAngle, double? FontWeight);

    private static bool TryGetFontStream(
        PdfDocument document,
        DictionaryToken descriptor,
        out StreamToken stream,
        out string? contentType,
        out FontDescriptorInfo info)
    {
        stream = default!;
        contentType = null;
        info = new FontDescriptorInfo(
            TryGetNumber(document, descriptor, "ItalicAngle"),
            TryGetNumber(document, descriptor, "FontWeight"));

        if (TryResolveStream(document, descriptor, "FontFile2", out stream))
        {
            contentType = "font/ttf";
            return true;
        }

        if (TryResolveStream(document, descriptor, "FontFile3", out stream))
        {
            contentType = ResolveFontFile3ContentType(stream);
            return true;
        }

        if (TryResolveStream(document, descriptor, "FontFile", out stream))
        {
            contentType = "application/x-font-type1";
            return true;
        }

        return false;
    }

    private static string? ResolveFontFile3ContentType(StreamToken stream)
    {
        if (stream.StreamDictionary.TryGet(NameToken.Create("Subtype"), out var subtypeToken) && subtypeToken is not null)
        {
            var subtype = subtypeToken switch
            {
                NameToken name => name.Data,
                StringToken str => str.Data,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(subtype))
            {
                return subtype switch
                {
                    "Type1C" => "application/x-font-type1",
                    "CIDFontType0C" => "font/otf",
                    "OpenType" => "font/otf",
                    _ => "font/otf"
                };
            }
        }

        return "font/otf";
    }

    private static bool TryResolveStream(PdfDocument document, DictionaryToken dictionary, string key, out StreamToken stream)
    {
        stream = default!;
        if (!dictionary.TryGet(NameToken.Create(key), out var token) || token is null)
        {
            return false;
        }

        var resolved = ResolveToken(document, token);
        if (resolved is StreamToken streamToken)
        {
            stream = streamToken;
            return true;
        }

        return false;
    }

    private static double? TryGetNumber(PdfDocument document, DictionaryToken dictionary, string key)
    {
        if (!dictionary.TryGet(NameToken.Create(key), out var token) || token is null)
        {
            return null;
        }

        var resolvedToken = token is IndirectReferenceToken reference
            ? ResolveToken(document, reference)
            : token;

        return resolvedToken is NumericToken number ? number.Double : null;
    }

    private static byte[] DecodeStream(StreamToken stream)
    {
        var data = stream.Data.ToArray();
        if (!stream.StreamDictionary.ContainsKey(NameToken.Create("Filter")))
        {
            return data;
        }

        try
        {
            var filters = DefaultFilterProvider.Instance.GetFilters(stream.StreamDictionary);
            foreach (var filter in filters)
            {
                data = filter.Decode(data, stream.StreamDictionary, data.Length);
            }
        }
        catch
        {
            return data;
        }

        return data;
    }

    private static string? TryGetName(PdfDocument document, DictionaryToken dictionary, string key)
    {
        if (!dictionary.TryGet(NameToken.Create(key), out var token) || token is null)
        {
            return null;
        }

        token = token is IndirectReferenceToken reference
            ? ResolveToken(document, reference) ?? token
            : token;

        return token switch
        {
            NameToken name => name.Data,
            StringToken str => str.Data,
            _ => null
        };
    }

    private static bool TryResolveImageData(PdfDocument document, IPdfImage image, out byte[] bytes, out string? mimeType)
    {
        mimeType = null;
        if (TryResolveSoftMaskImage(document, image, out bytes))
        {
            mimeType = "image/png";
            return true;
        }

        if (image.TryGetPng(out var pngBytes))
        {
            bytes = pngBytes;
            mimeType = "image/png";
            return true;
        }

        if (image.RawBytes is { Count: > 0 })
        {
            bytes = image.RawBytes.ToArray();
            mimeType = DetectImageMimeType(bytes);
            return true;
        }

        if (image.TryGetBytes(out var decoded))
        {
            bytes = decoded.ToArray();
            mimeType = DetectImageMimeType(bytes);
            return bytes.Length > 0;
        }

        bytes = Array.Empty<byte>();
        return false;
    }

    private static bool TryResolveSoftMaskImage(PdfDocument document, IPdfImage image, out byte[] pngBytes)
    {
        pngBytes = Array.Empty<byte>();
        if (image.ImageDictionary is null)
        {
            return false;
        }

        if (!image.ImageDictionary.ContainsKey(NameToken.Create("SMask")))
        {
            return false;
        }

        if (!TryDecodeSoftMask(document, image, out var alpha, out var maskWidth, out var maskHeight))
        {
            return false;
        }

        if (!image.TryGetBytes(out var baseBytes))
        {
            return false;
        }

        var width = image.WidthInSamples;
        var height = image.HeightInSamples;
        if (width <= 0 || height <= 0 || width != maskWidth || height != maskHeight)
        {
            return false;
        }

        var pixelCount = width * height;
        if (pixelCount <= 0)
        {
            return false;
        }

        var baseData = baseBytes.ToArray();
        if (image.BitsPerComponent is > 0 and < 8)
        {
            baseData = UnpackComponents(baseData, image.BitsPerComponent);
        }

        var components = image.ColorSpaceDetails.NumberOfColorComponents;
        if (components <= 0 && baseData.Length >= pixelCount)
        {
            components = baseData.Length / pixelCount;
        }

        if (components <= 0)
        {
            return false;
        }

        if (baseData.Length < pixelCount * components)
        {
            return false;
        }

        if (baseData.Length > pixelCount * components)
        {
            components = baseData.Length / pixelCount;
            if (components <= 0)
            {
                return false;
            }
        }

        var alphaData = NormalizeAlpha(alpha, pixelCount);
        if (alphaData.Length != pixelCount)
        {
            return false;
        }

        var rgba = new byte[pixelCount * 4];
        var dataIndex = 0;
        var pixelIndex = 0;
        var rgbaIndex = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                byte r;
                byte g;
                byte b;

                if (components == 1)
                {
                    r = baseData[dataIndex];
                    g = r;
                    b = r;
                }
                else if (components >= 3)
                {
                    r = baseData[dataIndex];
                    g = baseData[dataIndex + 1];
                    b = baseData[dataIndex + 2];
                }
                else
                {
                    r = baseData[dataIndex];
                    g = r;
                    b = r;
                }

                if (components >= 4)
                {
                    var c = baseData[dataIndex] / 255.0;
                    var m = baseData[dataIndex + 1] / 255.0;
                    var yk = baseData[dataIndex + 2] / 255.0;
                    var k = baseData[dataIndex + 3] / 255.0;
                    r = (byte)Math.Clamp(Math.Round(255 * (1 - c) * (1 - k)), 0, 255);
                    g = (byte)Math.Clamp(Math.Round(255 * (1 - m) * (1 - k)), 0, 255);
                    b = (byte)Math.Clamp(Math.Round(255 * (1 - yk) * (1 - k)), 0, 255);
                }

                rgba[rgbaIndex++] = r;
                rgba[rgbaIndex++] = g;
                rgba[rgbaIndex++] = b;
                rgba[rgbaIndex++] = alphaData[pixelIndex];

                dataIndex += components;
                pixelIndex++;
            }
        }

        if (!TryEncodePng(width, height, rgba, out pngBytes))
        {
            return false;
        }
        return pngBytes.Length > 0;
    }

    private static bool TryDecodeSoftMask(
        PdfDocument document,
        IPdfImage image,
        out byte[] alpha,
        out int width,
        out int height)
    {
        alpha = Array.Empty<byte>();
        width = 0;
        height = 0;

        if (image.ImageDictionary is null)
        {
            return false;
        }

        if (!TryResolveStream(document, image.ImageDictionary, "SMask", out var stream))
        {
            return false;
        }

        if (!TryGetInt(document, stream.StreamDictionary, "Width", out width)
            || !TryGetInt(document, stream.StreamDictionary, "Height", out height))
        {
            return false;
        }

        if (!TryDecodeStream(document, stream, out var maskBytes))
        {
            return false;
        }

        var bitsPerComponent = TryGetInt(document, stream.StreamDictionary, "BitsPerComponent", out var bits)
            ? bits
            : 8;

        if (bitsPerComponent is > 0 and < 8)
        {
            maskBytes = UnpackComponents(maskBytes, bitsPerComponent);
        }

        if (TryGetDecode(document, stream.StreamDictionary, out var decode) && decode.Length >= 2 && decode[0] > decode[1])
        {
            for (var i = 0; i < maskBytes.Length; i++)
            {
                maskBytes[i] = (byte)(255 - maskBytes[i]);
            }
        }

        alpha = maskBytes;
        return true;
    }

    private static bool TryDecodeStream(PdfDocument document, StreamToken stream, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        var data = stream.Data?.ToArray();
        if (data is null || data.Length == 0)
        {
            return false;
        }

        var filters = DefaultFilterProvider.Instance.GetFilters(stream.StreamDictionary);
        foreach (var filter in filters)
        {
            if (!filter.IsSupported)
            {
                return false;
            }

            data = filter.Decode(data, stream.StreamDictionary, data.Length);
        }

        decoded = data;
        return decoded.Length > 0;
    }

    private static bool TryGetInt(PdfDocument document, DictionaryToken dictionary, string key, out int value)
    {
        value = 0;
        if (!dictionary.TryGet(NameToken.Create(key), out var token) || token is null)
        {
            return false;
        }

        var resolved = ResolveToken(document, token) ?? token;
        return TryGetInt(resolved, out value);
    }

    private static bool TryGetInt(IToken token, out int value)
    {
        value = 0;
        if (token is NumericToken number)
        {
            value = (int)Math.Round(number.Double);
            return true;
        }

        return false;
    }

    private static bool TryGetDecode(PdfDocument document, DictionaryToken dictionary, out double[] decode)
    {
        decode = Array.Empty<double>();
        if (!dictionary.TryGet(NameToken.Create("Decode"), out var token) || token is null)
        {
            return false;
        }

        var resolved = ResolveToken(document, token) ?? token;
        if (resolved is not ArrayToken array || array.Data.Count == 0)
        {
            return false;
        }

        var values = new List<double>(array.Data.Count);
        foreach (var item in array.Data)
        {
            if (item is NumericToken number)
            {
                values.Add(number.Double);
            }
        }

        if (values.Count == 0)
        {
            return false;
        }

        decode = values.ToArray();
        return true;
    }

    private static byte[] NormalizeAlpha(byte[] data, int pixelCount)
    {
        if (data.Length == pixelCount)
        {
            return data;
        }

        if (data.Length < pixelCount || pixelCount <= 0)
        {
            return Array.Empty<byte>();
        }

        var components = data.Length / pixelCount;
        if (components <= 0)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[pixelCount];
        var sourceIndex = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            result[i] = data[sourceIndex];
            sourceIndex += components;
        }

        return result;
    }

    private static bool TryEncodePng(int width, int height, byte[] rgba, out byte[] pngBytes)
    {
        pngBytes = Array.Empty<byte>();
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
        {
            return false;
        }

        var stride = width * 4;
        var scanlineLength = stride + 1;
        var rawLength = scanlineLength * height;
        var raw = new byte[rawLength];
        var srcIndex = 0;
        var destIndex = 0;
        for (var y = 0; y < height; y++)
        {
            raw[destIndex++] = 0; // no filter
            Buffer.BlockCopy(rgba, srcIndex, raw, destIndex, stride);
            srcIndex += stride;
            destIndex += stride;
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(compressedStream, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(raw, 0, raw.Length);
            }

            compressed = compressedStream.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(PngSignature, 0, PngSignature.Length);

        Span<byte> ihdr = stackalloc byte[13];
        WriteUInt32BigEndian(ihdr, 0, (uint)width);
        WriteUInt32BigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // color type RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);

        pngBytes = output.ToArray();
        return pngBytes.Length > 0;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        WriteUInt32BigEndian(lengthBytes, 0, (uint)data.Length);
        output.Write(lengthBytes);

        Span<byte> typeBytes = stackalloc byte[4];
        typeBytes[0] = (byte)type[0];
        typeBytes[1] = (byte)type[1];
        typeBytes[2] = (byte)type[2];
        typeBytes[3] = (byte)type[3];
        output.Write(typeBytes);

        if (data.Length > 0)
        {
            output.Write(data);
        }

        var crc = ComputeCrc(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteUInt32BigEndian(crcBytes, 0, crc);
        output.Write(crcBytes);
    }

    private static void WriteUInt32BigEndian(Span<byte> buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        for (var i = 0; i < typeBytes.Length; i++)
        {
            crc = CrcTable[(crc ^ typeBytes[i]) & 0xFF] ^ (crc >> 8);
        }

        for (var i = 0; i < data.Length; i++)
        {
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var j = 0; j < 8; j++)
            {
                value = (value & 1) == 1 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    private static byte[] UnpackComponents(byte[] data, int bitsPerComponent)
    {
        if (bitsPerComponent >= 8 || bitsPerComponent <= 0)
        {
            return data;
        }

        var mask = (1 << bitsPerComponent) - 1;
        var totalBits = data.Length * 8;
        var componentCount = totalBits / bitsPerComponent;
        if (componentCount <= 0)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[componentCount];
        var outIndex = 0;
        var buffer = 0;
        var bitsInBuffer = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsInBuffer += 8;
            while (bitsInBuffer >= bitsPerComponent && outIndex < result.Length)
            {
                bitsInBuffer -= bitsPerComponent;
                var component = (buffer >> bitsInBuffer) & mask;
                result[outIndex++] = (byte)(component * 255 / mask);
            }
        }

        if (outIndex == result.Length)
        {
            return result;
        }

        Array.Resize(ref result, outIndex);
        return result;
    }

    private static string? DetectImageMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x00
            && bytes[1] == 0x00
            && bytes[2] == 0x00
            && bytes[3] == 0x0C
            && bytes[4] == 0x6A
            && bytes[5] == 0x50
            && bytes[6] == 0x20
            && bytes[7] == 0x20
            && bytes[8] == 0x0D
            && bytes[9] == 0x0A
            && bytes[10] == 0x87
            && bytes[11] == 0x0A)
        {
            return "image/jp2";
        }

        return null;
    }

    private static PdfFontInfo? BuildFontInfo(string? fontName, bool normalize)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return null;
        }

        var normalized = fontName.Trim();
        if (normalize)
        {
            normalized = NormalizeFontName(normalized);
        }

        var lower = normalized.ToLower(CultureInfo.InvariantCulture);
        var isBold = lower.Contains("bold", StringComparison.Ordinal)
                     || lower.Contains("black", StringComparison.Ordinal)
                     || lower.Contains("semibold", StringComparison.Ordinal)
                     || lower.Contains("demi", StringComparison.Ordinal);
        var isItalic = lower.Contains("italic", StringComparison.Ordinal)
                       || lower.Contains("oblique", StringComparison.Ordinal);

        return new PdfFontInfo(normalized, isBold, isItalic);
    }

    private static string NormalizeFontName(string name)
    {
        var trimmed = name.Trim();
        var subsetIndex = trimmed.IndexOf('+');
        if (subsetIndex > 0 && subsetIndex <= 6)
        {
            trimmed = trimmed[(subsetIndex + 1)..];
        }

        trimmed = trimmed.Replace(",", " ", StringComparison.Ordinal);
        trimmed = trimmed.Replace("-", " ", StringComparison.Ordinal);
        trimmed = string.Join(" ", trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        return trimmed switch
        {
            "TimesNewRomanPSMT" => "Times New Roman",
            "TimesNewRomanPS" => "Times New Roman",
            "TimesNewRoman" => "Times New Roman",
            "Times" => "Times New Roman",
            "Helvetica" => "Arial",
            "HelveticaNeue" => "Helvetica Neue",
            "Courier" => "Courier New",
            "CourierNewPSMT" => "Courier New",
            "Symbol" => "Symbol",
            "ZapfDingbats" => "Zapf Dingbats",
            _ => trimmed
        };
    }

    private static double ResolveFontSize(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return 0;
        }

        double total = 0;
        var pointCount = 0;
        foreach (var letter in letters)
        {
            if (letter.PointSize > 0)
            {
                total += letter.PointSize;
                pointCount++;
                continue;
            }

            total += letter.FontSize;
        }

        var divisor = pointCount > 0 ? pointCount : letters.Count;
        return divisor > 0 ? total / divisor : 0;
    }

    private static double ResolveBaselineY(IReadOnlyList<Letter> letters, PdfRect bounds)
    {
        if (letters.Count == 0)
        {
            return bounds.Bottom;
        }

        double total = 0;
        foreach (var letter in letters)
        {
            total += letter.StartBaseLine.Y;
        }

        return total / letters.Count;
    }

    private static string BuildWordText(Word word)
    {
        var letters = word.Letters;
        if (letters.Count == 0)
        {
            return word.Text;
        }

        var threshold = ResolveLetterGapThreshold(letters);
        var builder = new StringBuilder(word.Text.Length + 4);

        for (var i = 0; i < letters.Count; i++)
        {
            var value = letters[i].Value;
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append(value);
            }

            if (i == letters.Count - 1)
            {
                continue;
            }

            var gap = letters[i + 1].GlyphRectangle.Left - letters[i].GlyphRectangle.Right;
            if (gap > threshold)
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

    private static double ResolveAverageLetterGap(IReadOnlyList<Letter> letters)
    {
        if (letters.Count < 2)
        {
            return 0;
        }

        double total = 0;
        var count = 0;
        for (var i = 0; i < letters.Count - 1; i++)
        {
            var gap = letters[i + 1].GlyphRectangle.Left - letters[i].GlyphRectangle.Right;
            if (gap <= 0)
            {
                continue;
            }

            total += gap;
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        return total / count;
    }

    private static double ResolveLetterGapThreshold(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return 0;
        }

        var averageWidth = letters.Average(letter => letter.Width);
        var averageSize = letters.Average(letter => letter.FontSize);
        var threshold = Math.Max(averageWidth * 0.6, averageSize * 0.2);
        return Math.Max(threshold, 0.5);
    }

    private static PdfRect ToRect(PdfRectangle rect)
        => new(rect.Left, rect.Bottom, rect.Width, rect.Height);

    private static PdfColor? ResolveTextColor(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return null;
        }

        foreach (var letter in letters)
        {
            if (letter.Color is null)
            {
                continue;
            }

            return ToPdfColor(letter.Color);
        }

        return null;
    }

    private static int ResolveTextSequence(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return -1;
        }

        var min = int.MaxValue;
        foreach (var letter in letters)
        {
            var sequence = letter.TextSequence;
            if (sequence >= 0 && sequence < min)
            {
                min = sequence;
            }
        }

        return min == int.MaxValue ? -1 : min;
    }

    private static PdfPathObject? BuildPathObject(PdfPath path)
    {
        if (path.Count == 0)
        {
            return null;
        }

        var segments = new List<PdfPathSegment>();
        var hasPoint = false;
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var subpath in path)
        {
            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        segments.Add(PdfPathSegment.MoveTo(move.Location.X, move.Location.Y));
                        Accumulate(move.Location);
                        break;
                    case PdfSubpath.Line line:
                        segments.Add(PdfPathSegment.LineTo(line.To.X, line.To.Y));
                        Accumulate(line.From);
                        Accumulate(line.To);
                        break;
                    case PdfSubpath.BezierCurve curve:
                        segments.Add(PdfPathSegment.CubicTo(
                            curve.FirstControlPoint.X,
                            curve.FirstControlPoint.Y,
                            curve.SecondControlPoint.X,
                            curve.SecondControlPoint.Y,
                            curve.EndPoint.X,
                            curve.EndPoint.Y));
                        Accumulate(curve.StartPoint);
                        Accumulate(curve.FirstControlPoint);
                        Accumulate(curve.SecondControlPoint);
                        Accumulate(curve.EndPoint);
                        break;
                    case PdfSubpath.Close:
                        segments.Add(PdfPathSegment.Close());
                        break;
                }
            }
        }

        if (segments.Count == 0)
        {
            return null;
        }

        var bounds = path.GetBoundingRectangle();
        PdfRect rect;
        if (bounds.HasValue)
        {
            var box = bounds.Value;
            rect = new PdfRect(box.Left, box.Bottom, box.Width, box.Height);
        }
        else if (hasPoint)
        {
            rect = new PdfRect(minX, minY, Math.Max(0.1, maxX - minX), Math.Max(0.1, maxY - minY));
        }
        else
        {
            return null;
        }

        var pathObject = new PdfPathObject
        {
            Bounds = rect,
            Style = BuildPathStyle(path)
        };
        pathObject.Segments.AddRange(segments);
        return pathObject;

        void Accumulate(PdfPoint point)
        {
            hasPoint = true;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
    }

    private static PdfPathStyle BuildPathStyle(PdfPath path)
    {
        var style = new PdfPathStyle
        {
            IsFilled = path.IsFilled,
            IsStroked = path.IsStroked,
            FillColor = ToPdfColor(path.FillColor),
            StrokeColor = ToPdfColor(path.StrokeColor),
            LineWidth = (double)path.LineWidth,
            LineCap = MapLineCap(path.LineCapStyle),
            LineJoin = MapLineJoin(path.LineJoinStyle),
            FillRule = path.FillingRule == FillingRule.EvenOdd ? PdfFillRule.EvenOdd : PdfFillRule.NonZero
        };

        if (path.LineDashPattern.HasValue)
        {
            var dash = path.LineDashPattern.Value;
            style.DashArray = dash.Array?.Select(value => (double)value).ToArray();
            style.DashPhase = dash.Phase;
        }

        return style;
    }

    private static PdfLineCap MapLineCap(LineCapStyle style)
    {
        return style switch
        {
            LineCapStyle.Round => PdfLineCap.Round,
            LineCapStyle.ProjectingSquare => PdfLineCap.Square,
            _ => PdfLineCap.Butt
        };
    }

    private static PdfLineJoin MapLineJoin(LineJoinStyle style)
    {
        return style switch
        {
            LineJoinStyle.Round => PdfLineJoin.Round,
            LineJoinStyle.Bevel => PdfLineJoin.Bevel,
            _ => PdfLineJoin.Miter
        };
    }

    private static PdfColor? ToPdfColor(IColor? color)
    {
        if (color is null)
        {
            return null;
        }

        var (r, g, b) = color.ToRGBValues();
        return new PdfColor(
            (byte)Math.Clamp(r * 255.0, 0, 255),
            (byte)Math.Clamp(g * 255.0, 0, 255),
            (byte)Math.Clamp(b * 255.0, 0, 255),
            255);
    }

    private static PdfTextOrientation MapOrientation(UglyToad.PdfPig.Content.TextOrientation orientation)
    {
        return orientation switch
        {
            UglyToad.PdfPig.Content.TextOrientation.Horizontal => PdfTextOrientation.Horizontal,
            UglyToad.PdfPig.Content.TextOrientation.Rotate180 => PdfTextOrientation.Rotate180,
            UglyToad.PdfPig.Content.TextOrientation.Rotate90 => PdfTextOrientation.Rotate90,
            UglyToad.PdfPig.Content.TextOrientation.Rotate270 => PdfTextOrientation.Rotate270,
            _ => PdfTextOrientation.Other
        };
    }
}
