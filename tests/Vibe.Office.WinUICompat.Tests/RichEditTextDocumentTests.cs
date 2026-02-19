using Vibe.Office.WinUICompat.Text;
using Vibe.Office.WinUICompat.Documents;
using Xunit;

namespace Vibe.Office.WinUICompat.Tests;

public sealed class RichEditTextDocumentTests
{
    [Fact]
    public void SetText_GetText_RoundTrips()
    {
        var document = new RichEditTextDocument();

        document.SetText("hello");

        Assert.Equal("hello", document.GetText());
    }

    [Fact]
    public void UndoRedo_RestoresSnapshots()
    {
        var document = new RichEditTextDocument();
        document.SetText("a");
        document.SetText("b");

        Assert.True(document.Undo());
        Assert.Equal("a", document.GetText());

        Assert.True(document.Redo());
        Assert.Equal("b", document.GetText());
    }

    [Fact]
    public void Range_SetText_ReplacesSegment()
    {
        var document = new RichEditTextDocument();
        document.SetText("abcdef");

        var range = document.GetRange(1, 4);
        range.SetText("Z");

        Assert.Equal("aZef", document.GetText());
    }

    [Fact]
    public void SetSelection_NormalizesAndClampsOffsets()
    {
        var document = new RichEditTextDocument();
        document.SetText("hello");

        document.SetSelection(4, 1);
        Assert.Equal(1, document.SelectionStartOffset);
        Assert.Equal(4, document.SelectionEndOffset);

        document.SetSelection(-5, 99);
        Assert.Equal(0, document.SelectionStartOffset);
        Assert.Equal(5, document.SelectionEndOffset);
    }

    [Fact]
    public void SetText_PreservesMultiParagraphRoundtrip()
    {
        var document = new RichEditTextDocument();
        document.SetText("line1\nline2\nline3");

        Assert.Equal(3, document.Document.Blocks.Count);
        Assert.Equal("line1\nline2\nline3", document.GetText());
    }

    [Fact]
    public void PointerOffset_RoundTripsAcrossParagraphs()
    {
        var document = new RichEditTextDocument();
        document.SetText("ab\ncd\nef");
        var text = document.GetText();

        for (var offset = 0; offset <= text.Length; offset++)
        {
            var pointer = document.GetTextPointer(offset);
            var resolved = document.GetOffsetFromTextPointer(pointer);
            Assert.Equal(offset, resolved);
        }
    }

    [Fact]
    public void GetOffsetFromTextPointer_ClampsOutOfRangeParagraph()
    {
        var document = new RichEditTextDocument();
        document.SetText("a\nb");

        var resolved = document.GetOffsetFromTextPointer(new TextPointer(999, 0));

        Assert.Equal(document.GetText().Length, resolved);
    }

    [Fact]
    public void ReplaceSelection_WithStructuredFragment_PreservesListAndTableBlocks()
    {
        var document = new RichEditTextDocument();
        document.SetText("placeholder");
        document.SetSelection(0, document.GetText().Length);

        var fragment = new RichTextDocument();
        var list = new List();
        var item = new ListItem();
        item.Blocks.Add(new Paragraph("item"));
        list.ListItems.Add(item);
        fragment.Blocks.Add(list);

        var table = new Table();
        var group = new TableRowGroup();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Blocks.Add(new Paragraph("cell"));
        row.Cells.Add(cell);
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        fragment.Blocks.Add(table);

        Assert.True(document.ReplaceSelection(fragment));

        var listIndex = -1;
        var tableIndex = -1;
        for (var i = 0; i < document.Document.Blocks.Count; i++)
        {
            if (document.Document.Blocks[i] is List)
            {
                listIndex = i;
            }
            else if (document.Document.Blocks[i] is Table)
            {
                tableIndex = i;
            }
        }

        Assert.True(listIndex >= 0);
        Assert.True(tableIndex >= 0);
        Assert.True(listIndex < tableIndex);
    }

    [Fact]
    public void InsertTable_InsertsStructuredTableBlock()
    {
        var document = new RichEditTextDocument();
        document.SetText("table");
        var end = document.GetText().Length;
        document.SetSelection(end, end);

        Assert.True(document.InsertTable(2, 3));
        var table = Assert.Single(document.Document.Blocks, static block => block is Table);
        var typed = Assert.IsType<Table>(table);
        var rowGroup = Assert.Single(typed.RowGroups);
        Assert.Equal(2, rowGroup.Rows.Count);
        Assert.Equal(3, rowGroup.Rows[0].Cells.Count);
    }

    [Fact]
    public void ToggleBulletedList_ProjectsSelectionAsListBlock()
    {
        var document = new RichEditTextDocument();
        document.SetText("one\ntwo");
        document.SetSelection(0, document.GetText().Length);

        Assert.True(document.ToggleBulletedList());
        var list = Assert.IsType<List>(Assert.Single(document.Document.Blocks));
        Assert.Equal(2, list.ListItems.Count);
    }

    [Fact]
    public void ToggleBold_PreservesRichInlineFormattingInCompatDom()
    {
        var document = new RichEditTextDocument();
        document.SetText("bold");
        document.SetSelection(0, document.GetText().Length);

        Assert.True(document.ToggleBold());

        var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Document.Blocks));
        Assert.True(ContainsBoldFormattingInlines(paragraph.Inlines));

        static bool ContainsBoldFormattingInlines(InlineCollection inlines)
        {
            for (var i = 0; i < inlines.Count; i++)
            {
                if (ContainsBoldFormattingInline(inlines[i]))
                {
                    return true;
                }
            }

            return false;
        }

        static bool ContainsBoldFormattingInline(Inline inline)
        {
            if (inline is Bold)
            {
                return true;
            }

            if (inline is Span span)
            {
                if (string.Equals(span.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                for (var i = 0; i < span.Inlines.Count; i++)
                {
                    if (ContainsBoldFormattingInline(span.Inlines[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    [Fact]
    public void TextRange_SetDocument_UsesStructuredReplacement()
    {
        var document = new RichEditTextDocument();
        document.SetText("plain");
        var range = document.GetRange(0, document.GetText().Length);

        var fragment = new RichTextDocument();
        var paragraph = new Paragraph();
        var italic = new Italic();
        italic.Inlines.Add(new Run("rich"));
        paragraph.Inlines.Add(italic);
        fragment.Blocks.Add(paragraph);

        Assert.True(range.SetDocument(fragment));

        var snapshot = document.GetRange(0, document.GetText().Length).GetDocument();
        Assert.Contains("rich", document.GetText(), StringComparison.Ordinal);
        Assert.Contains(snapshot.Blocks, static block =>
            block is Paragraph paragraph
            && paragraph.Inlines.Count > 0);
    }

    [Fact]
    public void SetDocument_UsesInternalSyncFlag_ForCompatDomMutation()
    {
        var document = new RichEditTextDocument();
        var observedExternalSyncMutation = false;
        document.Document.Blocks.CollectionChanged += (_, _) =>
        {
            if (!document.IsApplyingInternalDocumentSync)
            {
                observedExternalSyncMutation = true;
            }
        };

        var source = new RichTextDocument();
        source.Blocks.Add(new Paragraph("sync"));
        document.SetDocument(source);

        Assert.False(observedExternalSyncMutation);
    }

    [Fact]
    public void CreateEditorSnapshotDocument_ReturnsStructuredSnapshot()
    {
        var document = new RichEditTextDocument();
        document.SetText("one\ntwo");
        document.SetSelection(0, document.GetText().Length);
        Assert.True(document.ToggleNumberedList());

        var snapshot = document.CreateEditorSnapshotDocument();
        var list = Assert.IsType<List>(Assert.Single(snapshot.Blocks));
        Assert.Equal(2, list.ListItems.Count);
    }

    [Fact]
    public void ConfigureEmbeddedUiElements_TracksEmbeddedMap()
    {
        var document = new RichEditTextDocument();
        var inlineChild = new EmbeddedTestChild();
        var blockChild = new EmbeddedTestChild();

        Assert.True(document.ConfigureEmbeddedUiElements(
            enabled: true,
            elementPredicate: static child => child is EmbeddedTestChild,
            sizeResolver: static (child, _) => child is EmbeddedTestChild ? (80d, 24d) : null));

        var source = new RichTextDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new InlineUIContainer { Child = inlineChild });
        source.Blocks.Add(paragraph);
        source.Blocks.Add(new BlockUIContainer { Child = blockChild });

        document.SetDocument(source);
        Assert.Equal(2, document.EmbeddedUiElementsById.Count);
    }

    private sealed class EmbeddedTestChild;
}
