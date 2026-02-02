using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public readonly record struct ProofingUnderlineSpan(
    int Start,
    int Length,
    ProofingIssueKind Kind,
    DocUnderlineStyle UnderlineStyle,
    DocColor Color);

public interface IProofingSpanProvider
{
    bool TryGetParagraphSpans(int paragraphIndex, out IReadOnlyList<ProofingUnderlineSpan> spans);
}
