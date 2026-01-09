using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Editor.Editing;

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

        if (_session.Document.ParagraphCount == 0)
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
                listLevel.Build());
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            var resolved = _resolver.ResolveParagraphProperties(paragraph);

            alignment.Add(resolved.Alignment ?? ParagraphAlignment.Left);
            indentLeft.Add(resolved.IndentLeft);
            indentRight.Add(resolved.IndentRight);
            firstLineIndent.Add(resolved.FirstLineIndent);
            spacingBefore.Add(resolved.SpacingBefore);
            spacingAfter.Add(resolved.SpacingAfter);
            lineSpacing.Add(resolved.LineSpacing);
            lineSpacingRule.Add(resolved.LineSpacingRule);

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
            listLevel.Build());
    }
}
