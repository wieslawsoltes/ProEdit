using Vibe.Office.Documents;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.Office.RichText.Avalonia;

/// <summary>
/// Represents a mutable text range over a <see cref="FlowDocumentModel"/>.
/// </summary>
public class FlowTextRange
{
    /// <summary>
    /// Sentinel value returned by <see cref="GetPropertyValue"/> when selected text has mixed formatting.
    /// </summary>
    public static readonly object MixedValue = new();

    private readonly RichTextBox _owner;
    private readonly bool _bindToSelection;
    private readonly FlowDocumentModel? _document;
    private TextRange _range;

    public FlowTextRange(FlowTextPointer start, FlowTextPointer end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (!ReferenceEquals(start.Document, end.Document))
        {
            throw new InvalidOperationException("Start and end pointers must reference the same FlowDocument.");
        }

        if (!RichTextBox.TryGetOwner(start.Document, out var owner))
        {
            throw new InvalidOperationException(
                "FlowTextRange requires a FlowDocument attached to a RichTextBox.");
        }

        _owner = owner;
        _document = start.Document;
        _range = _owner.NormalizeRange(new TextRange(start.ToTextPosition(), end.ToTextPosition()));
    }

    internal FlowTextRange(RichTextBox owner, bool bindToSelection, TextRange initialRange)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _bindToSelection = bindToSelection;
        _document = bindToSelection ? null : owner.DocumentForRanges;
        _range = bindToSelection ? default : owner.NormalizeRange(initialRange);
    }

    public FlowTextPointer Start => _owner.CreatePointer(CurrentRange.Start);

    public FlowTextPointer End => _owner.CreatePointer(CurrentRange.End);

    public bool IsEmpty => CurrentRange.IsEmpty;

    public virtual void Select(FlowTextPointer start, FlowTextPointer end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        var expectedDocument = ResolveExpectedDocument();
        if (!ReferenceEquals(start.Document, expectedDocument) || !ReferenceEquals(end.Document, expectedDocument))
        {
            throw new InvalidOperationException("Pointers must reference the same FlowDocument as this range.");
        }

        var range = _owner.NormalizeRange(new TextRange(start.ToTextPosition(), end.ToTextPosition()));
        if (_bindToSelection)
        {
            _owner.SelectRangeForRanges(range, ensureVisible: false);
            return;
        }

        EnsureRangeDocument();
        _range = range;
    }

    public void Select(FlowTextRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        Select(range.Start, range.End);
    }

    public void ApplyPropertyValue(FlowTextRangeProperty property, object? value)
    {
        _owner.ApplyRangePropertyValue(CurrentRange, property, value, preserveCurrentSelection: !_bindToSelection);
    }

    public object? GetPropertyValue(FlowTextRangeProperty property)
    {
        return _owner.GetRangePropertyValue(CurrentRange, property, preserveCurrentSelection: !_bindToSelection);
    }

    protected TextRange CurrentRange
    {
        get
        {
            if (_bindToSelection)
            {
                return _owner.GetSelectionRangeForRanges();
            }

            EnsureRangeDocument();
            return _owner.NormalizeRange(_range);
        }
    }

    private FlowDocumentModel ResolveExpectedDocument()
    {
        return _bindToSelection ? _owner.DocumentForRanges : _document!;
    }

    private void EnsureRangeDocument()
    {
        if (_bindToSelection || _document is null)
        {
            return;
        }

        if (!ReferenceEquals(_owner.DocumentForRanges, _document))
        {
            throw new InvalidOperationException(
                "This FlowTextRange targets a previous document instance. Create a new range for the current RichTextBox.Document.");
        }
    }
}
