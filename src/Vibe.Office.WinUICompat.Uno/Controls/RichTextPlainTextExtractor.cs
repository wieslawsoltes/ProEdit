using System.Text;
using Vibe.Office.WinUICompat.Documents;
using CompatList = Vibe.Office.WinUICompat.Documents.List;

namespace Vibe.Office.WinUICompat.Controls;

internal static class RichTextPlainTextExtractor
{
    public static string Extract(BlockCollection blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return Extract((IEnumerable<Block>)blocks);
    }

    public static string Extract(IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            AppendBlock(builder, block);
        }

        return builder.ToString();
    }

    private static void AppendBlock(StringBuilder builder, Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                AppendInlines(builder, paragraph.Inlines);
                builder.Append('\n');
                break;
            case CompatList list:
                for (var i = 0; i < list.ListItems.Count; i++)
                {
                    for (var j = 0; j < list.ListItems[i].Blocks.Count; j++)
                    {
                        AppendBlock(builder, list.ListItems[i].Blocks[j]);
                    }
                }

                break;
            case Table table:
                for (var i = 0; i < table.RowGroups.Count; i++)
                {
                    var rowGroup = table.RowGroups[i];
                    for (var j = 0; j < rowGroup.Rows.Count; j++)
                    {
                        var row = rowGroup.Rows[j];
                        for (var k = 0; k < row.Cells.Count; k++)
                        {
                            var cell = row.Cells[k];
                            for (var m = 0; m < cell.Blocks.Count; m++)
                            {
                                AppendBlock(builder, cell.Blocks[m]);
                            }

                            if (k + 1 < row.Cells.Count)
                            {
                                builder.Append('\t');
                            }
                        }

                        builder.Append('\n');
                    }
                }

                break;
            case BlockUIContainer:
                builder.Append('\uFFFC');
                builder.Append('\n');
                break;
        }
    }

    private static void AppendInlines(StringBuilder builder, InlineCollection inlines)
    {
        for (var i = 0; i < inlines.Count; i++)
        {
            AppendInline(builder, inlines[i]);
        }
    }

    private static void AppendInline(StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case Run run:
                builder.Append(run.Text);
                break;
            case Span span:
                AppendInlines(builder, span.Inlines);
                break;
            case LineBreak:
                builder.Append('\n');
                break;
            case InlineUIContainer:
                builder.Append('\uFFFC');
                break;
        }
    }
}
