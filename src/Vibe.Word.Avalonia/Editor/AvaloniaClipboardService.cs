using System.IO;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.OpenXml;

namespace Vibe.Word.Avalonia;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> HtmlWindowsFormat = DataFormat.CreateStringPlatformFormat("HTML Format");
    private static readonly DataFormat<string> RtfFormat = DataFormat.CreateStringPlatformFormat("text/rtf");
    private static readonly DataFormat<string> RtfWindowsFormat = DataFormat.CreateStringPlatformFormat("Rich Text Format");
    private static readonly DataFormat<byte[]> OoxmlFormat = DataFormat.CreateBytesPlatformFormat("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    private static readonly DataFormat<byte[]> OoxmlUtiFormat = DataFormat.CreateBytesPlatformFormat("org.openxmlformats.wordprocessingml.document");
    private static readonly DataFormat<byte[]> OoxmlWindowsFormat = DataFormat.CreateBytesPlatformFormat("Office Open XML");
    private static readonly IReadOnlyList<string> DefaultFormats = new[]
    {
        "text/plain",
        "text/html",
        "text/rtf",
        "HTML Format",
        "Rich Text Format",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "org.openxmlformats.wordprocessingml.document",
        "Office Open XML"
    };
    private readonly Func<IClipboard?> _clipboardProvider;
    private readonly Func<bool>? _canCopyEvaluator;
    private readonly Func<bool>? _canCutEvaluator;
    private readonly Func<bool>? _canPasteEvaluator;
    private readonly IReadOnlyList<string> _formats;
    private string? _lastText;
    private ClipboardContent? _content;

    public AvaloniaClipboardService(
        Func<IClipboard?> clipboardProvider,
        Func<bool>? canCopyEvaluator = null,
        Func<bool>? canCutEvaluator = null,
        Func<bool>? canPasteEvaluator = null,
        IReadOnlyList<string>? supportedFormats = null)
    {
        _clipboardProvider = clipboardProvider ?? throw new ArgumentNullException(nameof(clipboardProvider));
        _canCopyEvaluator = canCopyEvaluator;
        _canCutEvaluator = canCutEvaluator;
        _canPasteEvaluator = canPasteEvaluator;
        _formats = supportedFormats ?? DefaultFormats;
    }

    public bool CanCopy => _canCopyEvaluator?.Invoke() ?? true;

    public bool CanCut => _canCutEvaluator?.Invoke() ?? CanCopy;

    public bool CanPaste => _canPasteEvaluator?.Invoke()
                            ?? _content is not null
                            || TryGetText(out _)
                            || HasSupportedClipboardFormats();

    public IReadOnlyList<string> SupportedFormats => _formats;

    public bool TryGetText(out string text)
    {
        var clipboard = _clipboardProvider();
        if (clipboard is not null)
        {
            try
            {
                var result = clipboard.TryGetTextAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(result))
                {
                    _lastText = result;
                    text = result;
                    return true;
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrEmpty(_lastText))
        {
            text = _lastText;
            return true;
        }

        text = string.Empty;
        return false;
    }

    public void SetText(string text)
    {
        var clipboard = _clipboardProvider();
        _lastText = string.IsNullOrEmpty(text) ? null : text;
        _content = null;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            clipboard.SetTextAsync(text).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    public bool TryGetContent(out ClipboardContent content)
    {
        if (_content is not null)
        {
            content = _content;
            return true;
        }

        if (TryGetClipboardContent(out content))
        {
            _content = content;
            return true;
        }

        content = ClipboardContent.Empty();
        return false;
    }

    public void SetContent(ClipboardContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        var clipboard = _clipboardProvider();
        if (clipboard is null)
        {
            return;
        }

        try
        {
            var dataTransfer = BuildDataTransfer(content);
            clipboard.SetDataAsync(dataTransfer).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private bool HasSupportedClipboardFormats()
    {
        var clipboard = _clipboardProvider();
        if (clipboard is null)
        {
            return false;
        }

        try
        {
            var formats = clipboard.GetDataFormatsAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (formats is null || formats.Count == 0)
            {
                return false;
            }

            foreach (var format in formats)
            {
                if (format == DataFormat.Text || _formats.Contains(format.Identifier))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static DataTransfer BuildDataTransfer(ClipboardContent content)
    {
        var document = ClipboardDocumentConverter.ToDocument(content);
        var plainText = ClipboardPlainTextSerializer.ToPlainText(content);

        var item = new DataTransferItem();
        if (!string.IsNullOrEmpty(plainText))
        {
            item.Set(DataFormat.Text, plainText);
        }

        var html = ClipboardHtmlSerializer.ToHtml(document);
        if (!string.IsNullOrEmpty(html))
        {
            item.Set(HtmlFormat, html);
            item.Set(HtmlWindowsFormat, ClipboardHtmlSerializer.ToClipboardHtml(html));
        }

        var rtf = ClipboardRtfSerializer.ToRtf(document);
        if (!string.IsNullOrEmpty(rtf))
        {
            item.Set(RtfFormat, rtf);
            item.Set(RtfWindowsFormat, rtf);
        }

        var ooxml = TryBuildOpenXml(document);
        if (ooxml is { Length: > 0 })
        {
            item.Set(OoxmlFormat, ooxml);
            item.Set(OoxmlUtiFormat, ooxml);
            item.Set(OoxmlWindowsFormat, ooxml);
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(item);
        return dataTransfer;
    }

    private bool TryGetClipboardContent(out ClipboardContent content)
    {
        var clipboard = _clipboardProvider();
        if (clipboard is null)
        {
            content = ClipboardContent.Empty();
            return false;
        }

        try
        {
            using var dataTransfer = clipboard.TryGetDataAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (dataTransfer is null)
            {
                content = ClipboardContent.Empty();
                return false;
            }

            if (TryReadOpenXml(dataTransfer, out var ooxmlBytes)
                && TryParseOpenXml(ooxmlBytes, out content))
            {
                _lastText = ClipboardPlainTextSerializer.ToPlainText(content);
                return true;
            }

            if (TryReadString(dataTransfer, RtfFormat, out var rtf)
                || TryReadString(dataTransfer, RtfWindowsFormat, out rtf))
            {
                if (ClipboardRtfSerializer.TryParse(rtf, out var rtfDocument))
                {
                    content = ClipboardDocumentConverter.FromDocument(rtfDocument);
                    _lastText = ClipboardPlainTextSerializer.ToPlainText(content);
                    return true;
                }
            }

            if (TryReadString(dataTransfer, HtmlFormat, out var html)
                || TryReadString(dataTransfer, HtmlWindowsFormat, out html))
            {
                if (ClipboardHtmlSerializer.TryParse(html, out var htmlDocument))
                {
                    content = ClipboardDocumentConverter.FromDocument(htmlDocument);
                    _lastText = ClipboardPlainTextSerializer.ToPlainText(content);
                    return true;
                }
            }
        }
        catch
        {
        }

        if (TryGetText(out var text) && !string.IsNullOrEmpty(text))
        {
            var document = ClipboardPlainTextSerializer.ToDocument(text);
            content = ClipboardDocumentConverter.FromDocument(document);
            return true;
        }

        content = ClipboardContent.Empty();
        return false;
    }

    private static bool TryReadString(IAsyncDataTransfer dataTransfer, DataFormat<string> format, out string value)
    {
        value = string.Empty;
        foreach (var item in dataTransfer.Items)
        {
            if (!item.Contains(format))
            {
                continue;
            }

            var result = item.TryGetValueAsync(format).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(result))
            {
                value = result;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadOpenXml(IAsyncDataTransfer dataTransfer, out byte[] data)
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

    private static bool TryReadBytes(IAsyncDataTransfer dataTransfer, DataFormat<byte[]> format, out byte[] data)
    {
        data = Array.Empty<byte>();
        foreach (var item in dataTransfer.Items)
        {
            if (!item.Contains(format))
            {
                continue;
            }

            var result = item.TryGetValueAsync(format).ConfigureAwait(false).GetAwaiter().GetResult();
            if (result is { Length: > 0 })
            {
                data = result;
                return true;
            }
        }

        return false;
    }

    private static byte[]? TryBuildOpenXml(Document document)
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
