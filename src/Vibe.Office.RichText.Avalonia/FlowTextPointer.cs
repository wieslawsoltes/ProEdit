using Vibe.Office.Documents;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.Office.RichText.Avalonia;

/// <summary>
/// Represents a position inside a <see cref="FlowDocumentModel"/>.
/// </summary>
public sealed class FlowTextPointer : IComparable<FlowTextPointer>, IEquatable<FlowTextPointer>
{
    public FlowTextPointer(FlowDocumentModel document, int paragraphIndex, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (paragraphIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        Document = document;
        ParagraphIndex = paragraphIndex;
        Offset = offset;
    }

    public FlowDocumentModel Document { get; }

    public int ParagraphIndex { get; }

    public int Offset { get; }

    public int CompareTo(FlowTextPointer? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (!ReferenceEquals(Document, other.Document))
        {
            throw new InvalidOperationException("FlowTextPointer instances must target the same FlowDocument.");
        }

        return ToTextPosition().CompareTo(other.ToTextPosition());
    }

    public bool Equals(FlowTextPointer? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(Document, other.Document)
               && ParagraphIndex == other.ParagraphIndex
               && Offset == other.Offset;
    }

    public override bool Equals(object? obj)
    {
        return obj is FlowTextPointer other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Document, ParagraphIndex, Offset);
    }

    public override string ToString()
    {
        return $"{ParagraphIndex}:{Offset}";
    }

    internal TextPosition ToTextPosition()
    {
        return new TextPosition(ParagraphIndex, Offset);
    }
}
