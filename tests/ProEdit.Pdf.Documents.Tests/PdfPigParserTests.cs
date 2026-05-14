using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UglyToad.PdfPig.Graphics.Colors;
using ProEdit.Pdf;
using ProEdit.Pdf.PdfPig;
using Xunit;

namespace ProEdit.Pdf.Documents.Tests;

public sealed class PdfPigParserTests
{
    [Fact]
    public void ToPdfColorIgnoresPatternColors()
    {
        var color = new ThrowingColor();

        var result = PdfPigParser.ToPdfColor(color);

        Assert.Null(result);
    }

    [Fact]
    public void ParseAppliesExtGStateOpacityToPathColors()
    {
        var bytes = BuildPdfWithOpacity(0.5, 0.25);
        var parser = new PdfPigParser();

        using var stream = new MemoryStream(bytes);
        var doc = parser.Parse(stream, new PdfParserOptions
        {
            ExtractPaths = true,
            ExtractTextGlyphs = false,
            ExtractEmbeddedFonts = false
        });

        var page = Assert.Single(doc.Pages);
        var path = Assert.Single(page.Paths);

        Assert.True(path.Style.IsFilled);
        Assert.True(path.Style.IsStroked);
        Assert.NotNull(path.Style.FillColor);
        Assert.NotNull(path.Style.StrokeColor);
        Assert.Equal(128, path.Style.FillColor!.Value.A);
        Assert.Equal(64, path.Style.StrokeColor!.Value.A);
    }

    [Fact]
    public void ParseClipsFilledPaths()
    {
        var bytes = BuildPdfWithClip();
        var parser = new PdfPigParser();

        using var stream = new MemoryStream(bytes);
        var doc = parser.Parse(stream, new PdfParserOptions
        {
            ExtractPaths = true,
            ExtractTextGlyphs = false,
            ExtractEmbeddedFonts = false
        });

        var page = Assert.Single(doc.Pages);
        var path = Assert.Single(page.Paths);

        Assert.InRange(path.Bounds.X, -0.01, 0.01);
        Assert.InRange(path.Bounds.Y, -0.01, 0.01);
        Assert.InRange(path.Bounds.Width, 9.9, 10.1);
        Assert.InRange(path.Bounds.Height, 9.9, 10.1);
    }

    private static byte[] BuildPdfWithOpacity(double fillAlpha, double strokeAlpha)
    {
        var content = string.Join('\n', new[]
        {
            "q",
            "/Gs1 gs",
            "1 0 0 rg",
            "0 0 0 RG",
            "1 w",
            "0 0 100 100 re",
            "B",
            "Q",
            string.Empty
        });

        var contentBytes = Encoding.ASCII.GetBytes(content);
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Count 1 /Kids [3 0 R] >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /ExtGState << /Gs1 4 0 R >> >> /MediaBox [0 0 200 200] /Contents 5 0 R >>\nendobj\n",
            $"4 0 obj\n<< /Type /ExtGState /ca {fillAlpha.ToString(CultureInfo.InvariantCulture)} /CA {strokeAlpha.ToString(CultureInfo.InvariantCulture)} >>\nendobj\n",
            $"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{content}endstream\nendobj\n"
        };

        using var stream = new MemoryStream();
        Write(stream, "%PDF-1.4\n");

        var offsets = new List<long>(objects.Length);
        foreach (var obj in objects)
        {
            offsets.Add(stream.Position);
            Write(stream, obj);
        }

        var xrefStart = stream.Position;
        Write(stream, "xref\n");
        Write(stream, $"0 {objects.Length + 1}\n");
        Write(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write(stream, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        Write(stream, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");
        return stream.ToArray();
    }

    private static byte[] BuildPdfWithClip()
    {
        var content = string.Join('\n', new[]
        {
            "q",
            "0 0 10 10 re",
            "W",
            "n",
            "0 0 20 20 re",
            "f",
            "Q",
            string.Empty
        });

        var contentBytes = Encoding.ASCII.GetBytes(content);
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Count 1 /Kids [3 0 R] >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>\nendobj\n",
            $"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{content}endstream\nendobj\n"
        };

        using var stream = new MemoryStream();
        Write(stream, "%PDF-1.4\n");

        var offsets = new List<long>(objects.Length);
        foreach (var obj in objects)
        {
            offsets.Add(stream.Position);
            Write(stream, obj);
        }

        var xrefStart = stream.Position;
        Write(stream, "xref\n");
        Write(stream, $"0 {objects.Length + 1}\n");
        Write(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write(stream, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        Write(stream, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");
        return stream.ToArray();
    }

    private static void Write(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private sealed class ThrowingColor : IColor
    {
        public ColorSpace ColorSpace => ColorSpace.DeviceRGB;

        public (double r, double g, double b) ToRGBValues()
        {
            throw new InvalidOperationException("Cannot call ToRGBValues in a Pattern color.");
        }
    }
}
