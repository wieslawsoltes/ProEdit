using System.Diagnostics;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Rendering.Skia;

namespace Vibe.Office.Layout.Benchmarks;

internal static class Program
{
    private const string SampleSentence =
        "This is a longer sample paragraph that contains a variety of words and spacing to exercise line breaking " +
        "and justification logic under realistic conditions. It should be long enough to wrap across multiple lines.";

    private static void Main(string[] args)
    {
        var iterations = ReadIterations(args, 200);
        var measurer = new SkiaTextMeasurer { UseHarfBuzz = true };
        var style = new TextStyle { FontFamily = "Times New Roman", FontSize = 12f };
        var text = BuildParagraph(24);
        var spans = new List<InlineSpan> { new InlineSpan(0, text.Length, text, style, null, null, null, null, 0f) };
        var firstLineWidth = 420f;
        var otherLineWidth = 420f;

        Warmup(measurer, style, text, spans, firstLineWidth, otherLineWidth);

        var lineBreaks = BenchmarkLineBreaking(iterations, measurer, spans, text, firstLineWidth, otherLineWidth);
        var lineLayouts = BuildLineLayouts(text, lineBreaks, style, measurer);
        BenchmarkJustification(iterations, lineLayouts, otherLineWidth, measurer);
    }

    private static void Warmup(
        SkiaTextMeasurer measurer,
        TextStyle style,
        string text,
        IReadOnlyList<InlineSpan> spans,
        float firstLineWidth,
        float otherLineWidth)
    {
        KnuthPlassLineBreaker.TryBreakParagraph(text, spans, firstLineWidth, otherLineWidth, measurer, out _);
        var metrics = measurer.MeasureText(text, style);
        _ = metrics.Width;
    }

    private static List<ParagraphLineBreak> BenchmarkLineBreaking(
        int iterations,
        SkiaTextMeasurer measurer,
        IReadOnlyList<InlineSpan> spans,
        string text,
        float firstLineWidth,
        float otherLineWidth)
    {
        var breaks = new List<ParagraphLineBreak>();
        var sw = Stopwatch.StartNew();
        var totalLines = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (KnuthPlassLineBreaker.TryBreakParagraph(text, spans, firstLineWidth, otherLineWidth, measurer, out var result))
            {
                totalLines += result.Count;
                breaks = result;
            }
        }
        sw.Stop();
        Console.WriteLine($"Line breaking: {sw.ElapsedMilliseconds} ms ({iterations} iters, {totalLines} total lines)");
        return breaks;
    }

    private static void BenchmarkJustification(
        int iterations,
        IReadOnlyList<LineLayout> lines,
        float targetWidth,
        SkiaTextMeasurer measurer)
    {
        var sw = Stopwatch.StartNew();
        var totalWidth = 0f;
        for (var i = 0; i < iterations; i++)
        {
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                totalWidth += LineJustifier.Justify(lines[lineIndex], targetWidth, measurer).Width;
            }
        }
        sw.Stop();
        Console.WriteLine($"Justification: {sw.ElapsedMilliseconds} ms ({iterations} iters, checksum {totalWidth:F2})");
    }

    private static List<LineLayout> BuildLineLayouts(string text, IReadOnlyList<ParagraphLineBreak> breaks, TextStyle style, SkiaTextMeasurer measurer)
    {
        var lines = new List<LineLayout>(breaks.Count);
        for (var i = 0; i < breaks.Count; i++)
        {
            var line = breaks[i];
            var lineText = line.Text(text);
            var metrics = measurer.MeasureText(lineText, style);
            var runs = new List<LayoutRun>
            {
                new LayoutRun(lineText, style, 0f, metrics.Width, lineText.Length, false, 0f)
            };

            lines.Add(new LineLayout(
                runs,
                Array.Empty<LayoutImage>(),
                Array.Empty<LayoutShape>(),
                Array.Empty<LayoutChart>(),
                Array.Empty<LayoutEquation>(),
                metrics.Width,
                metrics.Height,
                metrics.Ascent));
        }

        return lines;
    }

    private static string BuildParagraph(int repeatCount)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < repeatCount; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(SampleSentence);
        }

        return builder.ToString();
    }

    private static int ReadIterations(string[] args, int fallback)
    {
        if (args.Length == 0)
        {
            return fallback;
        }

        return int.TryParse(args[0], out var iterations) && iterations > 0 ? iterations : fallback;
    }
}
