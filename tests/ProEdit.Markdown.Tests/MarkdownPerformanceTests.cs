using Xunit;

namespace ProEdit.Markdown.Tests;

public class MarkdownPerformanceTests
{
    [Fact]
    public void Parse_LargeInput_AllocationWithinBudget()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var parser = new MarkdownParser(options);
        var text = BuildLargeMarkdown(4000);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = parser.Parse(text.AsSpan());
        var after = GC.GetAllocatedBytesForCurrentThread();

        var allocated = after - before;
        var budget = text.Length * 200L;
        Assert.True(allocated < budget, $"Allocated {allocated} bytes for {text.Length} chars (budget {budget}).");
    }

    private static string BuildLargeMarkdown(int lineCount)
    {
        var builder = new System.Text.StringBuilder(lineCount * 32);
        for (var i = 0; i < lineCount; i++)
        {
            builder.Append("- Item ").Append(i).Append('\n');
        }

        return builder.ToString();
    }
}
