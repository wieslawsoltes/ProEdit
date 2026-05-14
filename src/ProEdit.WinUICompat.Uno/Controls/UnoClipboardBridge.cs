using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using Windows.ApplicationModel.DataTransfer;
using ProEdit.Editing;
using ProEdit.OpenXml;
using ProEdit.WinUICompat.Bridges;
using ProEdit.WinUICompat.Documents;

namespace ProEdit.WinUICompat.Controls;

internal sealed class UnoClipboardBridge : ICompatClipboardBridge
{
    private const string OoxmlMimeFormat = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string OoxmlUtiFormat = "org.openxmlformats.wordprocessingml.document";
    private const string OoxmlWindowsFormat = "Office Open XML";
    private static readonly CompatDocumentBridge DocumentBridge = new();

    private readonly ICompatClipboardBridge _fallback;

    public UnoClipboardBridge(ICompatClipboardBridge fallback)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public bool TrySetPlainText(string text)
    {
        var safeText = text ?? string.Empty;

        if (TrySetSystemClipboardText(safeText))
        {
            return true;
        }

        return _fallback.TrySetPlainText(safeText);
    }

    public bool TryGetPlainText(out string text)
    {
        if (TryGetSystemClipboardText(out text))
        {
            return true;
        }

        return _fallback.TryGetPlainText(out text);
    }

    public bool TrySetRichDocument(RichTextDocument document, string plainText)
    {
        ArgumentNullException.ThrowIfNull(document);

        var safePlainText = plainText ?? string.Empty;
        var systemSet = TrySetSystemClipboardContent(document, safePlainText);
        var fallbackSet = _fallback.TrySetRichDocument(document, safePlainText);
        return systemSet || fallbackSet;
    }

    public bool TryGetRichDocument(out RichTextDocument document)
    {
        if (TryGetSystemClipboardRichDocument(out document))
        {
            return true;
        }

        if (_fallback.TryGetRichDocument(out document))
        {
            return true;
        }

        if (!TryGetSystemClipboardText(out var text))
        {
            document = new RichTextDocument();
            return false;
        }

        var rich = new RichTextDocument();
        rich.Blocks.Add(new Paragraph(text));
        document = rich;
        return true;
    }

    private static bool TrySetSystemClipboardContent(RichTextDocument document, string plainText)
    {
        try
        {
            var engineDocument = DocumentBridge.ToEditorDocument(document);
            var content = ClipboardDocumentConverter.FromDocument(engineDocument);
            var package = new DataPackage();
            package.SetText(string.IsNullOrWhiteSpace(plainText) ? ClipboardPlainTextSerializer.ToPlainText(content) : plainText);

            var html = ClipboardHtmlSerializer.ToHtml(engineDocument);
            if (!string.IsNullOrWhiteSpace(html))
            {
                package.SetHtmlFormat(ClipboardHtmlSerializer.ToClipboardHtml(html));
                package.SetData(StandardDataFormats.Html, html);
                package.SetData("HTML Format", ClipboardHtmlSerializer.ToClipboardHtml(html));
            }

            var rtf = ClipboardRtfSerializer.ToRtf(engineDocument);
            if (!string.IsNullOrWhiteSpace(rtf))
            {
                package.SetRtf(rtf);
                package.SetData(StandardDataFormats.Rtf, rtf);
                package.SetData("Rich Text Format", rtf);
            }

            var ooxml = TryBuildOpenXml(engineDocument);
            if (ooxml is { Length: > 0 })
            {
                var buffer = ooxml.AsBuffer();
                package.SetData(OoxmlMimeFormat, buffer);
                package.SetData(OoxmlUtiFormat, buffer);
                package.SetData(OoxmlWindowsFormat, buffer);
            }

            Clipboard.SetContent(package);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetSystemClipboardRichDocument(out RichTextDocument document)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content is null)
            {
                document = new RichTextDocument();
                return false;
            }

            return TryBuildRichDocumentFromDataPackageView(content, out document);
        }
        catch
        {
            document = new RichTextDocument();
        }

        return false;
    }

    internal static bool TryBuildRichDocumentFromDataPackageView(DataPackageView content, out RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (TryReadOpenXml(content, out var ooxml)
            && TryParseOpenXml(ooxml, out var ooxmlDocument))
        {
            document = ooxmlDocument;
            return true;
        }

        if ((TryReadRtf(content, out var rtf) || TryReadString(content, "Rich Text Format", out rtf))
            && ClipboardRtfSerializer.TryParse(rtf, out var rtfDocument))
        {
            document = ToCompatDocument(rtfDocument);
            return true;
        }

        if ((TryReadHtml(content, out var html)
             || TryReadString(content, "HTML Format", out html)
             || TryReadString(content, "text/html", out html))
            && ClipboardHtmlSerializer.TryParse(NormalizeClipboardHtml(html), out var htmlDocument))
        {
            document = ToCompatDocument(htmlDocument);
            return true;
        }

        document = new RichTextDocument();
        return false;
    }

    private static bool TryReadRtf(DataPackageView content, out string rtf)
    {
        rtf = string.Empty;
        try
        {
            if (!content.Contains(StandardDataFormats.Rtf))
            {
                return false;
            }

            rtf = content.GetRtfAsync().AsTask().GetAwaiter().GetResult() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(rtf);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadHtml(DataPackageView content, out string html)
    {
        html = string.Empty;
        try
        {
            if (!content.Contains(StandardDataFormats.Html))
            {
                return false;
            }

            html = content.GetHtmlFormatAsync().AsTask().GetAwaiter().GetResult() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(html);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadString(DataPackageView content, string formatId, out string value)
    {
        value = string.Empty;
        try
        {
            if (!content.Contains(formatId))
            {
                return false;
            }

            var data = content.GetDataAsync(formatId).AsTask().GetAwaiter().GetResult();
            switch (data)
            {
                case string text when !string.IsNullOrWhiteSpace(text):
                    value = text;
                    return true;
                case IBuffer buffer:
                    var bytes = buffer.ToArray();
                    var decoded = System.Text.Encoding.UTF8.GetString(bytes);
                    if (!string.IsNullOrWhiteSpace(decoded))
                    {
                        value = decoded;
                        return true;
                    }

                    break;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryReadOpenXml(DataPackageView content, out byte[] data)
    {
        return TryReadBytes(content, OoxmlMimeFormat, out data)
               || TryReadBytes(content, OoxmlUtiFormat, out data)
               || TryReadBytes(content, OoxmlWindowsFormat, out data);
    }

    private static bool TryReadBytes(DataPackageView content, string formatId, out byte[] data)
    {
        data = Array.Empty<byte>();
        try
        {
            if (!content.Contains(formatId))
            {
                return false;
            }

            var payload = content.GetDataAsync(formatId).AsTask().GetAwaiter().GetResult();
            switch (payload)
            {
                case byte[] bytes when bytes.Length > 0:
                    data = bytes;
                    return true;
                case IBuffer buffer when buffer.Length > 0:
                    data = buffer.ToArray();
                    return data.Length > 0;
                case string base64 when !string.IsNullOrWhiteSpace(base64):
                    if (TryDecodeBase64(base64, out var decoded))
                    {
                        data = decoded;
                        return true;
                    }

                    break;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryDecodeBase64(string value, out byte[] data)
    {
        data = Array.Empty<byte>();
        try
        {
            data = Convert.FromBase64String(value);
            return data.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? TryBuildOpenXml(ProEdit.Documents.Document document)
    {
        try
        {
            using var stream = new MemoryStream();
            var exporter = new DocxExporter();
            exporter.Save(document, stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseOpenXml(byte[] data, out RichTextDocument document)
    {
        document = new RichTextDocument();
        try
        {
            using var stream = new MemoryStream(data);
            var importer = new DocxImporter();
            var editorDocument = importer.Load(stream);
            document = ToCompatDocument(editorDocument);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeClipboardHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        if (!html.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var startIndex = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var endIndex = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0 && endIndex > startIndex)
        {
            startIndex += startMarker.Length;
            return html[startIndex..endIndex];
        }

        var htmlIndex = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        return htmlIndex >= 0 ? html[htmlIndex..] : html;
    }

    private static RichTextDocument ToCompatDocument(ProEdit.Documents.Document source)
    {
        var compat = DocumentBridge.FromEditorDocument(source);
        if (compat.Blocks.Count == 0)
        {
            compat.Blocks.Add(new Paragraph());
        }

        return compat;
    }

    private static bool TrySetSystemClipboardText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetSystemClipboardText(out string text)
    {
        text = string.Empty;
        try
        {
            var content = Clipboard.GetContent();
            if (content is null || !content.Contains(StandardDataFormats.Text))
            {
                return false;
            }

            text = content.GetTextAsync().AsTask().GetAwaiter().GetResult() ?? string.Empty;
            return true;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }
}
