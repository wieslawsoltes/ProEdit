using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using ProEdit.Primitives;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorParagraphServiceAdapter : IParagraphService
{
    private readonly IEditorSession _session;
    private readonly DocumentStyleResolver _resolver;

    public EditorParagraphServiceAdapter(IEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _resolver = new DocumentStyleResolver(_session.Document);
    }

    public EditorParagraphSnapshot GetSnapshot()
    {
        var alignment = new EditorValueAccumulator<ParagraphAlignment>();
        var indentLeft = new NullableEditorValueAccumulator<float>();
        var indentRight = new NullableEditorValueAccumulator<float>();
        var firstLineIndent = new NullableEditorValueAccumulator<float>();
        var spacingBefore = new NullableEditorValueAccumulator<float>();
        var spacingAfter = new NullableEditorValueAccumulator<float>();
        var lineSpacing = new NullableEditorValueAccumulator<int>();
        var lineSpacingRule = new NullableEditorValueAccumulator<DocLineSpacingRule>();
        var listKind = new EditorValueAccumulator<ListKind>();
        var listLevel = new EditorValueAccumulator<int>();
        var shadingColor = new NullableEditorValueAccumulator<DocColor>();
        var keepWithNext = new NullableEditorValueAccumulator<bool>();
        var keepLinesTogether = new NullableEditorValueAccumulator<bool>();
        var widowControl = new NullableEditorValueAccumulator<bool>();
        var pageBreakBefore = new NullableEditorValueAccumulator<bool>();
        var suppressLineNumbers = new NullableEditorValueAccumulator<bool>();
        var contextualSpacing = new NullableEditorValueAccumulator<bool>();
        var bidi = new NullableEditorValueAccumulator<bool>();
        var textDirection = new NullableEditorValueAccumulator<DocTextDirection>();

        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return new EditorParagraphSnapshot(
                alignment.Build(),
                indentLeft.Build(),
                indentRight.Build(),
                firstLineIndent.Build(),
                spacingBefore.Build(),
                spacingAfter.Build(),
                lineSpacing.Build(),
                lineSpacingRule.Build(),
                listKind.Build(),
                listLevel.Build(),
                shadingColor.Build(),
                keepWithNext.Build(),
                keepLinesTogether.Build(),
                widowControl.Build(),
                pageBreakBefore.Build(),
                suppressLineNumbers.Build(),
                contextualSpacing.Build(),
                bidi.Build(),
                textDirection.Build());
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            var resolved = _resolver.ResolveParagraphProperties(paragraph);

            alignment.Add(resolved.Alignment ?? ParagraphAlignment.Left);
            indentLeft.Add(resolved.IndentLeft);
            indentRight.Add(resolved.IndentRight);
            firstLineIndent.Add(resolved.FirstLineIndent);
            spacingBefore.Add(resolved.SpacingBefore);
            spacingAfter.Add(resolved.SpacingAfter);
            lineSpacing.Add(resolved.LineSpacing);
            lineSpacingRule.Add(resolved.LineSpacingRule);
            shadingColor.Add(resolved.ShadingColor);
            keepWithNext.Add(resolved.KeepWithNext);
            keepLinesTogether.Add(resolved.KeepLinesTogether);
            widowControl.Add(resolved.WidowControl);
            pageBreakBefore.Add(resolved.PageBreakBefore);
            suppressLineNumbers.Add(resolved.SuppressLineNumbers);
            contextualSpacing.Add(resolved.ContextualSpacing);
            bidi.Add(resolved.Bidi);
            textDirection.Add(resolved.TextDirection);

            var listInfo = paragraph.ListInfo;
            listKind.Add(listInfo?.Kind ?? ListKind.None);
            listLevel.Add(listInfo?.Level ?? 0);
        }

        return new EditorParagraphSnapshot(
            alignment.Build(),
            indentLeft.Build(),
            indentRight.Build(),
            firstLineIndent.Build(),
            spacingBefore.Build(),
            spacingAfter.Build(),
            lineSpacing.Build(),
            lineSpacingRule.Build(),
            listKind.Build(),
            listLevel.Build(),
            shadingColor.Build(),
            keepWithNext.Build(),
            keepLinesTogether.Build(),
            widowControl.Build(),
            pageBreakBefore.Build(),
            suppressLineNumbers.Build(),
            contextualSpacing.Build(),
            bidi.Build(),
            textDirection.Build());
    }
}
