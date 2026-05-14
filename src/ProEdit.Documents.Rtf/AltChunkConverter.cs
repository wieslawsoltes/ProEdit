using System;
using System.Text;

namespace ProEdit.Documents;

public static class AltChunkConverter
{
    public static bool TryConvert(AltChunkBlock altChunk, out IReadOnlyList<Block> blocks)
    {
        blocks = Array.Empty<Block>();
        if (altChunk is null)
        {
            return false;
        }

        if (altChunk.Data is not { Length: > 0 })
        {
            return false;
        }

        switch (ResolveContentKind(altChunk))
        {
            case AltChunkContentKind.Rtf:
                return TryParseAltChunkText(altChunk.Data, DocumentRtfParser.TryParse, out blocks);
            case AltChunkContentKind.Html:
                return TryParseAltChunkText(altChunk.Data, DocumentHtmlParser.TryParse, out blocks);
            case AltChunkContentKind.PlainText:
            {
                var plainText = DecodeAltChunkText(altChunk.Data);
                blocks = DocumentPlainTextParser.FromPlainText(plainText.AsSpan()).Blocks;
                return true;
            }
            default:
                return false;
        }
    }

    private enum AltChunkContentKind
    {
        Unknown,
        Rtf,
        Html,
        PlainText
    }

    private static bool TryParseAltChunkText(byte[] data, TryParseDelegate parser, out IReadOnlyList<Block> blocks)
    {
        var text = DecodeAltChunkText(data);
        if (string.IsNullOrWhiteSpace(text))
        {
            blocks = Array.Empty<Block>();
            return false;
        }

        if (parser(text, out var document))
        {
            blocks = document.Blocks;
            return true;
        }

        blocks = Array.Empty<Block>();
        return false;
    }

    private delegate bool TryParseDelegate(string text, out Document document);

    private static AltChunkContentKind ResolveContentKind(AltChunkBlock altChunk)
    {
        var contentType = altChunk.ContentType;
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (IsRtfContentType(contentType))
            {
                return AltChunkContentKind.Rtf;
            }

            if (IsHtmlContentType(contentType))
            {
                return AltChunkContentKind.Html;
            }

            if (IsPlainTextContentType(contentType))
            {
                return AltChunkContentKind.PlainText;
            }
        }

        if (altChunk.Data is not { Length: > 0 })
        {
            return AltChunkContentKind.Unknown;
        }

        var probe = SkipAltChunkPreamble(altChunk.Data.AsSpan());
        if (StartsWithAscii(probe, "{\\rtf"))
        {
            return AltChunkContentKind.Rtf;
        }

        if (StartsWithAsciiIgnoreCase(probe, "<!doctype")
            || StartsWithAsciiIgnoreCase(probe, "<html")
            || StartsWithAsciiIgnoreCase(probe, "<body"))
        {
            return AltChunkContentKind.Html;
        }

        return AltChunkContentKind.Unknown;
    }

    private static bool IsRtfContentType(string contentType)
    {
        return contentType.Contains("text/rtf", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("application/rtf", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("application/x-rtf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlContentType(string contentType)
    {
        return contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("application/xhtml", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("application/html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlainTextContentType(string contentType)
    {
        return contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOnlySpan<byte> SkipAltChunkPreamble(ReadOnlySpan<byte> data)
    {
        var span = TrimLeadingWhitespace(data);
        span = SkipBom(span);
        return TrimLeadingWhitespace(span);
    }

    private static ReadOnlySpan<byte> TrimLeadingWhitespace(ReadOnlySpan<byte> data)
    {
        var index = 0;
        while (index < data.Length)
        {
            var value = data[index];
            if (value != (byte)' ' && value != (byte)'\t' && value != (byte)'\r' && value != (byte)'\n')
            {
                break;
            }

            index++;
        }

        return data[index..];
    }

    private static ReadOnlySpan<byte> SkipBom(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return data[3..];
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return data[2..];
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return data[2..];
        }

        return data;
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> data, string prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            var expected = prefix[i];
            if (data[i] != (byte)expected)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithAsciiIgnoreCase(ReadOnlySpan<byte> data, string prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            var expected = char.ToLowerInvariant(prefix[i]);
            var value = (char)data[i];
            if (char.ToLowerInvariant(value) != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static string DecodeAltChunkText(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        }

        return Encoding.UTF8.GetString(data);
    }
}
