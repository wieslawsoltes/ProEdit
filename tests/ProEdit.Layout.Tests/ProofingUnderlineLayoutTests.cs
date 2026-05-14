using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Primitives;
using Xunit;

namespace ProEdit.Layout.Tests;

public class ProofingUnderlineLayoutTests
{
    [Fact]
    public void Layout_AppliesProofingUnderlineSpans()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("hello wrld"));

        var proofing = new TestProofingProvider(
            0,
            new ProofingUnderlineSpan(6, 4, ProofingIssueKind.Spelling, DocUnderlineStyle.Wave, new DocColor(204, 0, 0)));

        var layout = new DocumentLayouter().Layout(document, new LayoutSettings(), new TestTextMeasurer(), proofing);

        Assert.NotEmpty(layout.Lines);
        var run = layout.Lines.SelectMany(line => line.Runs)
            .FirstOrDefault(item => item.Style.UnderlineStyle == DocUnderlineStyle.Wave);
        Assert.NotNull(run);
        Assert.True(run!.Style.Underline);
        Assert.Contains("wrld", run.Text, StringComparison.Ordinal);
    }

    private sealed class TestProofingProvider : IProofingSpanProvider
    {
        private readonly int _paragraphIndex;
        private readonly IReadOnlyList<ProofingUnderlineSpan> _spans;

        public TestProofingProvider(int paragraphIndex, params ProofingUnderlineSpan[] spans)
        {
            _paragraphIndex = paragraphIndex;
            _spans = spans;
        }

        public bool TryGetParagraphSpans(int paragraphIndex, out IReadOnlyList<ProofingUnderlineSpan> spans)
        {
            if (paragraphIndex == _paragraphIndex)
            {
                spans = _spans;
                return spans.Count > 0;
            }

            spans = Array.Empty<ProofingUnderlineSpan>();
            return false;
        }
    }
}
