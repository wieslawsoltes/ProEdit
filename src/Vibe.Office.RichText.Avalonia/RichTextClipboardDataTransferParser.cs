using System.IO;
using Avalonia.Input;
using Vibe.Office.Editing;
using Vibe.Office.OpenXml;

namespace Vibe.Office.RichText.Avalonia;

internal static class RichTextClipboardDataTransferParser
{
    private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> HtmlWindowsFormat = DataFormat.CreateStringPlatformFormat("HTML Format");
    private static readonly DataFormat<string> RtfFormat = DataFormat.CreateStringPlatformFormat("text/rtf");
    private static readonly DataFormat<string> RtfWindowsFormat = DataFormat.CreateStringPlatformFormat("Rich Text Format");
    private static readonly DataFormat<byte[]> OoxmlFormat = DataFormat.CreateBytesPlatformFormat("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    private static readonly DataFormat<byte[]> OoxmlUtiFormat = DataFormat.CreateBytesPlatformFormat("org.openxmlformats.wordprocessingml.document");
    private static readonly DataFormat<byte[]> OoxmlWindowsFormat = DataFormat.CreateBytesPlatformFormat("Office Open XML");

    public static bool ContainsSupportedFormats(IDataTransfer dataTransfer)
    {
        ArgumentNullException.ThrowIfNull(dataTransfer);

        return dataTransfer.Contains(DataFormat.Text)
               || dataTransfer.Contains(HtmlFormat)
               || dataTransfer.Contains(HtmlWindowsFormat)
               || dataTransfer.Contains(RtfFormat)
               || dataTransfer.Contains(RtfWindowsFormat)
               || dataTransfer.Contains(OoxmlFormat)
               || dataTransfer.Contains(OoxmlUtiFormat)
               || dataTransfer.Contains(OoxmlWindowsFormat);
    }

    public static bool TryBuildClipboardContent(IDataTransfer dataTransfer, out ClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(dataTransfer);

        if (TryReadOpenXml(dataTransfer, out var ooxmlBytes)
            && TryParseOpenXml(ooxmlBytes, out content))
        {
            return true;
        }

        if ((TryReadString(dataTransfer, RtfFormat, out var rtf)
             || TryReadString(dataTransfer, RtfWindowsFormat, out rtf))
            && ClipboardRtfSerializer.TryParse(rtf, out var rtfDocument))
        {
            content = ClipboardDocumentConverter.FromDocument(rtfDocument);
            return true;
        }

        if ((TryReadString(dataTransfer, HtmlFormat, out var html)
             || TryReadString(dataTransfer, HtmlWindowsFormat, out html))
            && ClipboardHtmlSerializer.TryParse(html, out var htmlDocument))
        {
            content = ClipboardDocumentConverter.FromDocument(htmlDocument);
            return true;
        }

        var text = dataTransfer.TryGetText();
        if (!string.IsNullOrEmpty(text))
        {
            var document = ClipboardPlainTextSerializer.ToDocument(text);
            content = ClipboardDocumentConverter.FromDocument(document);
            return true;
        }

        content = ClipboardContent.Empty();
        return false;
    }

    private static bool TryReadString(IDataTransfer dataTransfer, DataFormat<string> format, out string value)
    {
        value = dataTransfer.TryGetValue(format) ?? string.Empty;
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryReadOpenXml(IDataTransfer dataTransfer, out byte[] data)
    {
        if (TryReadBytes(dataTransfer, OoxmlFormat, out data))
        {
            return true;
        }

        if (TryReadBytes(dataTransfer, OoxmlUtiFormat, out data))
        {
            return true;
        }

        return TryReadBytes(dataTransfer, OoxmlWindowsFormat, out data);
    }

    private static bool TryReadBytes(IDataTransfer dataTransfer, DataFormat<byte[]> format, out byte[] data)
    {
        data = dataTransfer.TryGetValue(format) ?? Array.Empty<byte>();
        return data.Length > 0;
    }

    private static bool TryParseOpenXml(byte[] data, out ClipboardContent content)
    {
        content = ClipboardContent.Empty();
        try
        {
            using var stream = new MemoryStream(data);
            var importer = new DocxImporter();
            var document = importer.Load(stream);
            content = ClipboardDocumentConverter.FromDocument(document);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
