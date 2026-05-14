using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Layout;

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
