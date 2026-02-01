using Vibe.Office.Documents.Formats;
using Vibe.Office.Editing;

namespace Vibe.Office.Markdown;

public static class MarkdownCommandPolicy
{
    private static readonly HashSet<string> AlwaysAllow = new(StringComparer.OrdinalIgnoreCase)
    {
        EditorHomeCommandIds.Clipboard.Paste,
        EditorHomeCommandIds.Clipboard.PasteKeepSource,
        EditorHomeCommandIds.Clipboard.PasteMatchDestination,
        EditorHomeCommandIds.Clipboard.PasteTextOnly,
        EditorHomeCommandIds.Clipboard.PasteMarkdown,
        EditorHomeCommandIds.Clipboard.Cut,
        EditorHomeCommandIds.Clipboard.Copy,
        EditorHomeCommandIds.Clipboard.CopyAsMarkdown,
        EditorHomeCommandIds.Editing.Find,
        EditorHomeCommandIds.Editing.Replace,
        EditorHomeCommandIds.Editing.ReplaceAll,
        EditorHomeCommandIds.Editing.Undo,
        EditorHomeCommandIds.Editing.Redo,
        EditorHomeCommandIds.Editing.SelectAll,
        EditorHomeCommandIds.Editing.SelectObjects,
        EditorHomeCommandIds.Editing.SelectSimilarFormatting,
        EditorHomeCommandIds.Styles.Apply,
        EditorHomeCommandIds.Styles.OpenPane,
        EditorHomeCommandIds.Styles.Manage,
        EditorHomeCommandIds.Font.ChangeCaseSentence,
        EditorHomeCommandIds.Font.ChangeCaseLower,
        EditorHomeCommandIds.Font.ChangeCaseUpper,
        EditorHomeCommandIds.Font.ChangeCaseCapitalize,
        EditorHomeCommandIds.Font.ChangeCaseToggle,
        EditorHomeCommandIds.Font.ClearFormatting,
        EditorHomeCommandIds.Paragraph.Sort,
        EditorHomeCommandIds.Paragraph.ShowInvisiblesToggle
    };

    private static readonly Dictionary<string, DocumentFormatCapability> CapabilityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { EditorHomeCommandIds.Font.BoldToggle, DocumentFormatCapability.Strong },
        { EditorHomeCommandIds.Font.ItalicToggle, DocumentFormatCapability.Emphasis },
        { EditorHomeCommandIds.Font.StrikethroughToggle, DocumentFormatCapability.Strikethrough },
        { EditorHomeCommandIds.Paragraph.ListBullets, DocumentFormatCapability.Lists },
        { EditorHomeCommandIds.Paragraph.ListNumbering, DocumentFormatCapability.Lists },
        { EditorHomeCommandIds.Paragraph.ListMultilevel, DocumentFormatCapability.Lists },
        { EditorHomeCommandIds.Paragraph.IndentIncrease, DocumentFormatCapability.Lists },
        { EditorHomeCommandIds.Paragraph.IndentDecrease, DocumentFormatCapability.Lists },
        { EditorInsertCommandIds.Tables.InsertTable, DocumentFormatCapability.Tables },
        { EditorInsertCommandIds.Illustrations.Pictures, DocumentFormatCapability.Images },
        { EditorInsertCommandIds.Links.Hyperlink, DocumentFormatCapability.Links }
    };

    private static readonly HashSet<string> TableCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        EditorTableCommandIds.Rows.InsertAbove,
        EditorTableCommandIds.Rows.InsertBelow,
        EditorTableCommandIds.Rows.Delete,
        EditorTableCommandIds.Columns.InsertLeft,
        EditorTableCommandIds.Columns.InsertRight,
        EditorTableCommandIds.Columns.Delete,
        EditorTableCommandIds.Delete.Table,
        EditorTableCommandIds.Merge.Cells,
        EditorTableCommandIds.Merge.Split,
        EditorTableCommandIds.Layout.AutoFitContents,
        EditorTableCommandIds.Layout.AutoFitWindow,
        EditorTableCommandIds.Layout.FixedColumnWidth,
        EditorTableCommandIds.Layout.DistributeColumns,
        EditorTableCommandIds.Layout.DistributeRows,
        EditorTableCommandIds.Layout.ColumnWidthsSet,
        EditorTableCommandIds.Layout.RowHeightSet,
        EditorTableCommandIds.Layout.RepeatHeaderRows,
        EditorTableCommandIds.Layout.PropertiesApply,
        EditorTableCommandIds.Alignment.AlignTop,
        EditorTableCommandIds.Alignment.AlignMiddle,
        EditorTableCommandIds.Alignment.AlignBottom,
        EditorTableCommandIds.Design.ApplyStyle,
        EditorTableCommandIds.Design.ToggleHeaderRow,
        EditorTableCommandIds.Design.ToggleTotalRow,
        EditorTableCommandIds.Design.ToggleFirstColumn,
        EditorTableCommandIds.Design.ToggleLastColumn,
        EditorTableCommandIds.Design.ToggleBandedRows,
        EditorTableCommandIds.Design.ToggleBandedColumns
    };

    public static Func<string, bool> Create(DocumentFormatProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return commandId => CanExecute(profile, commandId);
    }

    public static bool CanExecute(DocumentFormatProfile profile, string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return false;
        }

        if (AlwaysAllow.Contains(commandId))
        {
            return true;
        }

        if (TableCommands.Contains(commandId))
        {
            return profile.Supports(DocumentFormatCapability.Tables);
        }

        if (CapabilityMap.TryGetValue(commandId, out var capability))
        {
            return profile.Supports(capability);
        }

        return false;
    }
}
