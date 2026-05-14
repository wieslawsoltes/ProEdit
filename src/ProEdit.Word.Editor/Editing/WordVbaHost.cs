using System.Linq;
using System.Text;
using System.Threading;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.OpenXml;
using ProEdit.Vba.Runtime;

namespace ProEdit.Word.Editor.Editing;

public sealed class WordVbaHost : IVbaHost
{
    private const string ApplicationPath = "Application";
    private const string ActiveDocumentPath = "ActiveDocument";
    private const string DocumentsPath = "Documents";
    private const string SelectionPath = "Selection";
    private const string RangeSelectionPath = "Range:Selection";
    private const string RangeDocumentPath = "Range:Document";
    private const string ParagraphsPath = "Paragraphs";
    private const string TablesPath = "Tables";
    private const string ShapesPath = "Shapes";
    private const string ParagraphPrefix = "Paragraph:";
    private const string RangePrefix = "Range:";
    private const string RangeParagraphPrefix = "Range:Paragraph:";
    private const string RangeTablePrefix = "Range:Table:";
    private const string RangeTableRowPrefix = "Range:TableRow:";
    private const string RangeTableColumnPrefix = "Range:TableColumn:";
    private const string RangeCellPrefix = "Range:Cell:";
    private const string TableRowsPrefix = "TableRows:";
    private const string TableColumnsPrefix = "TableColumns:";
    private const string TableRowPrefix = "TableRow:";
    private const string TableColumnPrefix = "TableColumn:";
    private const string TableCellsPrefix = "TableCells:";
    private const string CellPrefix = "Cell:";
    private const string TablePrefix = "Table:";
    private const string ShapePrefix = "Shape:";
    private const string FindPrefix = "Find:";
    private const int WdUnitCharacter = 1;
    private const int WdUnitWord = 2;
    private const int WdUnitParagraph = 4;
    private const int WdUnitLine = 5;
    private const int WdUnitStory = 6;
    private const int WdCollapseStart = 0;
    private const int WdCollapseEnd = 1;
    private const int WdFindWrapStop = 0;
    private const int WdFindWrapContinue = 1;
    private const int WdFindWrapAsk = 2;

    private readonly IEditorMutableSession _session;
    private readonly ISelectionTextService _selectionText;
    private IVbaRuntime? _runtime;
    private readonly Func<Document> _documentFactory;
    private readonly Dictionary<string, TextRange> _rangeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FindState> _findStates = new(StringComparer.OrdinalIgnoreCase);
    private int _rangeCounter;

    public WordVbaHost(
        IEditorMutableSession session,
        ISelectionTextService? selectionText = null,
        IVbaRuntime? runtime = null,
        Func<Document>? documentFactory = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selectionText = selectionText ?? new EditorSelectionTextServiceAdapter(session);
        _runtime = runtime;
        _documentFactory = documentFactory ?? (() => new Document());
    }

    public void SetRuntime(IVbaRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public bool TryGetMember(string name, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var path = StripApplicationPrefix(name);
        if (EqualsIgnoreCase(path, ApplicationPath))
        {
            result = VbaValue.FromObjectPath(ApplicationPath);
            return true;
        }

        if (EqualsIgnoreCase(path, ActiveDocumentPath))
        {
            result = VbaValue.FromObjectPath(ActiveDocumentPath);
            return true;
        }

        if (EqualsIgnoreCase(path, DocumentsPath))
        {
            result = VbaValue.FromObjectPath(DocumentsPath);
            return true;
        }

        if (EqualsIgnoreCase(path, SelectionPath))
        {
            result = VbaValue.FromObjectPath(SelectionPath);
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.Range"))
        {
            result = CreateRangeObject(_session.Selection.Normalize());
            return true;
        }

        if (EqualsIgnoreCase(path, "ActiveDocument.Range")
            || EqualsIgnoreCase(path, "ActiveDocument.Content"))
        {
            if (TryResolveRange("Document", out var documentRange))
            {
                result = CreateRangeObject(documentRange);
                return true;
            }

            result = VbaValue.FromObjectPath(RangeDocumentPath);
            return true;
        }

        if (EqualsIgnoreCase(path, "ActiveDocument.Paragraphs"))
        {
            result = VbaValue.FromObjectPath(ParagraphsPath);
            return true;
        }

        if (EqualsIgnoreCase(path, "ActiveDocument.Tables"))
        {
            result = VbaValue.FromObjectPath(TablesPath);
            return true;
        }

        if (EqualsIgnoreCase(path, "ActiveDocument.Shapes"))
        {
            result = VbaValue.FromObjectPath(ShapesPath);
            return true;
        }

        if (EqualsIgnoreCase(path, "Documents.Count"))
        {
            result = VbaValue.FromDouble(1d);
            return true;
        }

        if (EqualsIgnoreCase(path, "Paragraphs.Count"))
        {
            result = VbaValue.FromDouble(_session.Document.ParagraphCount);
            return true;
        }

        if (EqualsIgnoreCase(path, "Tables.Count"))
        {
            result = VbaValue.FromDouble(CountTables());
            return true;
        }

        if (EqualsIgnoreCase(path, "Shapes.Count"))
        {
            result = VbaValue.FromDouble(CountShapes());
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.Text"))
        {
            result = VbaValue.FromString(GetSelectionText());
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.Start"))
        {
            result = VbaValue.FromDouble(GetDocumentOffset(_session.Selection.Start));
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.End"))
        {
            result = VbaValue.FromDouble(GetDocumentOffset(_session.Selection.End));
            return true;
        }

        if (EqualsIgnoreCase(path, $"{RangeSelectionPath}.Text"))
        {
            result = VbaValue.FromString(GetSelectionText());
            return true;
        }

        if (EqualsIgnoreCase(path, $"{RangeDocumentPath}.Text"))
        {
            result = VbaValue.FromString(BuildDocumentText());
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.Find"))
        {
            result = VbaValue.FromObjectPath($"{FindPrefix}Selection");
            return true;
        }

        if (TryGetFindMember(path, out result))
        {
            return true;
        }

        if (TryNormalizeRangePath(path, out var rangeId, out var rangeMember)
            && TryGetRangeMember(BuildRangePath(rangeId, rangeMember), out result))
        {
            return true;
        }

        if (TryGetRangeMember(path, out result))
        {
            return true;
        }

        if (TryGetParagraphMember(path, out result))
        {
            return true;
        }

        if (TryGetTableMember(path, out result))
        {
            return true;
        }

        if (TryGetTableRowsMember(path, out result))
        {
            return true;
        }

        if (TryGetTableColumnsMember(path, out result))
        {
            return true;
        }

        if (TryGetTableRowMember(path, out result))
        {
            return true;
        }

        if (TryGetTableColumnMember(path, out result))
        {
            return true;
        }

        if (TryGetCellMember(path, out result))
        {
            return true;
        }

        if (TryGetShapeMember(path, out result))
        {
            return true;
        }

        return false;
    }

    public bool TrySetMember(string name, VbaValue value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var path = StripApplicationPrefix(name);
        if (EqualsIgnoreCase(path, "Selection.Text")
            || EqualsIgnoreCase(path, $"{RangeSelectionPath}.Text"))
        {
            ReplaceSelectionText(value.AsString());
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.Start") || EqualsIgnoreCase(path, $"{RangeSelectionPath}.Start"))
        {
            SetSelectionStart((int)Math.Round(value.AsDouble()));
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.End") || EqualsIgnoreCase(path, $"{RangeSelectionPath}.End"))
        {
            SetSelectionEnd((int)Math.Round(value.AsDouble()));
            return true;
        }

        if (TrySetFindMember(path, value))
        {
            return true;
        }

        if (TryNormalizeRangePath(path, out var rangeId, out var rangeMember)
            && TrySetRangeMember(BuildRangePath(rangeId, rangeMember), value))
        {
            return true;
        }

        if (TrySetRangeMember(path, value))
        {
            return true;
        }

        if (TrySetParagraphMember(path, value))
        {
            return true;
        }

        if (TrySetTableRowMember(path, value))
        {
            return true;
        }

        if (TrySetTableColumnMember(path, value))
        {
            return true;
        }

        if (TrySetShapeMember(path, value))
        {
            return true;
        }

        return false;
    }

    public bool TryInvokeMember(string name, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var path = StripApplicationPrefix(name);
        if (TryInvokeFindMember(path, arguments, out result))
        {
            return true;
        }

        if (TryNormalizeRangePath(path, out var rangeId, out var rangeMember)
            && TryInvokeRange(BuildRangePath(rangeId, rangeMember), arguments, out result))
        {
            return true;
        }

        if (EqualsIgnoreCase(path, "Run") || EqualsIgnoreCase(path, "Application.Run"))
        {
            return TryRunMacro(arguments, out result);
        }

        if (EqualsIgnoreCase(path, "Documents.Add"))
        {
            result = CreateNewDocument();
            return true;
        }

        if (EqualsIgnoreCase(path, "Documents.Open"))
        {
            result = OpenDocument(arguments);
            return result.Kind != VbaValueKind.Empty;
        }

        if (EqualsIgnoreCase(path, "Selection.TypeText"))
        {
            var text = arguments.Count > 0 ? arguments[0].AsString() : string.Empty;
            ReplaceSelectionText(text);
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.TypeParagraph"))
        {
            _session.InsertParagraphBreak();
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.TypeBackspace"))
        {
            _session.Backspace();
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.Delete"))
        {
            _session.DeleteForward();
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.Collapse"))
        {
            CollapseSelection(arguments);
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.WholeStory"))
        {
            if (TryResolveRange("Document", out var range))
            {
                _session.SetSelection(range);
            }

            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.MoveStart"))
        {
            result = VbaValue.FromDouble(MoveSelectionEdge(moveStart: true, arguments));
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.MoveEnd"))
        {
            result = VbaValue.FromDouble(MoveSelectionEdge(moveStart: false, arguments));
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.MoveLeft"))
        {
            MoveSelection(_session.MoveLeft, arguments);
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.MoveRight"))
        {
            MoveSelection(_session.MoveRight, arguments);
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.MoveUp"))
        {
            MoveSelection(_session.MoveUp, arguments);
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.MoveDown"))
        {
            MoveSelection(_session.MoveDown, arguments);
            return true;
        }

        if (EqualsIgnoreCase(path, "Selection.SetRange"))
        {
            var start = arguments.Count > 0 ? (int)Math.Round(arguments[0].AsDouble()) : 0;
            var end = arguments.Count > 1 ? (int)Math.Round(arguments[1].AsDouble()) : start;
            var startPos = GetPositionFromDocumentOffset(start);
            var endPos = GetPositionFromDocumentOffset(end);
            _session.SetSelection(new TextRange(startPos, endPos));
            return true;
        }

        if (TryInvokeRange(path, arguments, out result))
        {
            return true;
        }

        if (TryInvokeParagraphs(path, arguments, out result))
        {
            return true;
        }

        if (EqualsIgnoreCase(path, "Tables.Add"))
        {
            var rowCount = arguments.Count > 1 ? (int)Math.Max(1d, arguments[1].AsDouble()) : 1;
            var columnCount = arguments.Count > 2 ? (int)Math.Max(1d, arguments[2].AsDouble()) : 1;
            var table = CreateTable(rowCount, columnCount);
            _session.InsertBlock(table);
            var tableIndex = CountTables();
            result = VbaValue.FromObjectPath($"{TablePrefix}{tableIndex}");
            return true;
        }

        if (EqualsIgnoreCase(path, "Tables.Item"))
        {
            var index = arguments.Count > 0 ? (int)Math.Max(1d, arguments[0].AsDouble()) : 1;
            if (TryGetTable(index, out _))
            {
                result = VbaValue.FromObjectPath($"{TablePrefix}{index}");
                return true;
            }

            return false;
        }

        if (TryInvokeTableRows(path, arguments, out result))
        {
            return true;
        }

        if (TryInvokeTableColumns(path, arguments, out result))
        {
            return true;
        }

        if (TryInvokeTableRow(path, arguments, out result))
        {
            return true;
        }

        if (TryInvokeTableColumn(path, arguments, out result))
        {
            return true;
        }

        if (TryInvokeCell(path, arguments, out result))
        {
            return true;
        }

        if (EqualsIgnoreCase(path, "Shapes.AddShape") || EqualsIgnoreCase(path, "Shapes.Add"))
        {
            var width = arguments.Count > 3 ? (float)arguments[3].AsDouble() : 160f;
            var height = arguments.Count > 4 ? (float)arguments[4].AsDouble() : 120f;
            var shape = CreateDefaultShape(width, height);
            _session.InsertInline(shape);
            var shapeIndex = CountShapes();
            result = VbaValue.FromObjectPath($"{ShapePrefix}{shapeIndex}");
            return true;
        }

        if (EqualsIgnoreCase(path, "Shapes.Item"))
        {
            var index = arguments.Count > 0 ? (int)Math.Max(1d, arguments[0].AsDouble()) : 1;
            if (TryGetShape(index, out _))
            {
                result = VbaValue.FromObjectPath($"{ShapePrefix}{index}");
                return true;
            }

            return false;
        }

        return false;
    }

    private bool TryGetTableMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexed(path, TablePrefix, out var index, out var member))
        {
            return false;
        }

        if (!TryGetTable(index, out var table))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{TablePrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Rows"))
        {
            result = VbaValue.FromObjectPath($"{TableRowsPrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Columns"))
        {
            result = VbaValue.FromObjectPath($"{TableColumnsPrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Range"))
        {
            result = VbaValue.FromObjectPath($"{RangeTablePrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Rows.Count"))
        {
            result = VbaValue.FromDouble(table.Rows.Count);
            return true;
        }

        if (EqualsIgnoreCase(member, "Columns.Count"))
        {
            result = VbaValue.FromDouble(CountTableColumns(table));
            return true;
        }

        return false;
    }

    private bool TryGetShapeMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexed(path, ShapePrefix, out var index, out var member))
        {
            return false;
        }

        if (!TryGetShape(index, out var shape))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{ShapePrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Width"))
        {
            result = VbaValue.FromDouble(shape.Width);
            return true;
        }

        if (EqualsIgnoreCase(member, "Height"))
        {
            result = VbaValue.FromDouble(shape.Height);
            return true;
        }

        if (EqualsIgnoreCase(member, "Name"))
        {
            result = VbaValue.FromString(shape.Name ?? string.Empty);
            return true;
        }

        if (EqualsIgnoreCase(member, "Rotation"))
        {
            result = VbaValue.FromDouble(shape.Properties.Rotation);
            return true;
        }

        return false;
    }

    private bool TrySetShapeMember(string path, VbaValue value)
    {
        if (!TryParseIndexed(path, ShapePrefix, out var index, out var member))
        {
            return false;
        }

        if (!TryGetShape(index, out var shape))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Width"))
        {
            shape.Width = (float)value.AsDouble();
            _session.RefreshLayout();
            return true;
        }

        if (EqualsIgnoreCase(member, "Height"))
        {
            shape.Height = (float)value.AsDouble();
            _session.RefreshLayout();
            return true;
        }

        if (EqualsIgnoreCase(member, "Name"))
        {
            shape.Name = value.AsString();
            return true;
        }

        if (EqualsIgnoreCase(member, "Rotation"))
        {
            shape.Properties.Rotation = (float)value.AsDouble();
            _session.RefreshLayout();
            return true;
        }

        return false;
    }

    private bool TryGetRangeMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseRangePath(path, out var rangeId, out var member))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{RangePrefix}{rangeId}");
            return true;
        }

        if (!TryResolveRange(rangeId, out var range))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Text"))
        {
            result = VbaValue.FromString(BuildRangeText(range));
            return true;
        }

        if (EqualsIgnoreCase(member, "Find"))
        {
            result = VbaValue.FromObjectPath($"{FindPrefix}{rangeId}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Duplicate"))
        {
            var createdRangeId = CreateRangeId(range);
            result = VbaValue.FromObjectPath($"{RangePrefix}{createdRangeId}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Start"))
        {
            result = VbaValue.FromDouble(GetDocumentOffset(range.Start));
            return true;
        }

        if (EqualsIgnoreCase(member, "End"))
        {
            result = VbaValue.FromDouble(GetDocumentOffset(range.End));
            return true;
        }

        return false;
    }

    private bool TrySetRangeMember(string path, VbaValue value)
    {
        if (!TryParseRangePath(path, out var rangeId, out var member))
        {
            return false;
        }

        if (!TryResolveRange(rangeId, out var range))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Text"))
        {
            var text = value.AsString();
            var startOffset = GetDocumentOffset(range.Start);
            ReplaceRangeText(range, text);
            UpdateRangeFromOffsets(rangeId, startOffset, startOffset + GetNormalizedTextLength(text));
            return true;
        }

        if (EqualsIgnoreCase(member, "Start"))
        {
            var start = GetPositionFromDocumentOffset((int)Math.Round(value.AsDouble()));
            var end = range.End;
            if (start > end)
            {
                end = start;
            }

            if (UpdateRangeState(rangeId, new TextRange(start, end)))
            {
                return true;
            }

            _session.SetSelection(new TextRange(start, end));
            return true;
        }

        if (EqualsIgnoreCase(member, "End"))
        {
            var end = GetPositionFromDocumentOffset((int)Math.Round(value.AsDouble()));
            var start = range.Start;
            if (end < start)
            {
                start = end;
            }

            if (UpdateRangeState(rangeId, new TextRange(start, end)))
            {
                return true;
            }

            _session.SetSelection(new TextRange(start, end));
            return true;
        }

        return false;
    }

    private bool TryInvokeRange(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseRangePath(path, out var rangeId, out var member))
        {
            return false;
        }

        if (!TryResolveRange(rangeId, out var range))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Select"))
        {
            _session.SetSelection(range);
            return true;
        }

        if (EqualsIgnoreCase(member, "Delete"))
        {
            ReplaceRangeText(range, string.Empty);
            UpdateRangeState(rangeId, new TextRange(range.Start, range.Start));
            return true;
        }

        if (EqualsIgnoreCase(member, "Collapse"))
        {
            var collapseToEnd = arguments.Count > 0 && arguments[0].AsDouble() != 0d;
            var position = collapseToEnd ? range.End : range.Start;
            UpdateRangeState(rangeId, new TextRange(position, position));
            return true;
        }

        if (EqualsIgnoreCase(member, "InsertAfter"))
        {
            var text = arguments.Count > 0 ? arguments[0].AsString() : string.Empty;
            var startOffset = GetDocumentOffset(range.Start);
            var endOffset = GetDocumentOffset(range.End);
            var insertionRange = new TextRange(range.End, range.End);
            ReplaceRangeText(insertionRange, text);
            UpdateRangeFromOffsets(rangeId, startOffset, endOffset + GetNormalizedTextLength(text));
            return true;
        }

        if (EqualsIgnoreCase(member, "InsertBefore"))
        {
            var text = arguments.Count > 0 ? arguments[0].AsString() : string.Empty;
            var startOffset = GetDocumentOffset(range.Start);
            var endOffset = GetDocumentOffset(range.End);
            var insertionRange = new TextRange(range.Start, range.Start);
            ReplaceRangeText(insertionRange, text);
            UpdateRangeFromOffsets(rangeId, startOffset, endOffset + GetNormalizedTextLength(text));
            return true;
        }

        if (EqualsIgnoreCase(member, "MoveStart"))
        {
            result = VbaValue.FromDouble(MoveRangeEdge(rangeId, range, moveStart: true, arguments));
            return true;
        }

        if (EqualsIgnoreCase(member, "MoveEnd"))
        {
            result = VbaValue.FromDouble(MoveRangeEdge(rangeId, range, moveStart: false, arguments));
            return true;
        }

        if (EqualsIgnoreCase(member, "SetRange"))
        {
            var start = arguments.Count > 0 ? (int)Math.Round(arguments[0].AsDouble()) : 0;
            var end = arguments.Count > 1 ? (int)Math.Round(arguments[1].AsDouble()) : start;
            var startPos = GetPositionFromDocumentOffset(start);
            var endPos = GetPositionFromDocumentOffset(end);
            var updatedRange = new TextRange(startPos, endPos);
            if (!UpdateRangeState(rangeId, updatedRange))
            {
                _session.SetSelection(updatedRange);
            }

            return true;
        }

        return false;
    }

    private bool TryGetParagraphMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexed(path, ParagraphPrefix, out var index, out var member))
        {
            return false;
        }

        if (!TryGetParagraphByIndex(index, out var paragraph))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{ParagraphPrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Range"))
        {
            result = VbaValue.FromObjectPath($"{RangeParagraphPrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Text"))
        {
            result = VbaValue.FromString(DocumentEditHelpers.GetParagraphText(paragraph));
            return true;
        }

        return false;
    }

    private bool TrySetParagraphMember(string path, VbaValue value)
    {
        if (!TryParseIndexed(path, ParagraphPrefix, out var index, out var member))
        {
            return false;
        }

        if (!EqualsIgnoreCase(member, "Text"))
        {
            return false;
        }

        if (!TryResolveRange($"Paragraph:{index}", out var range))
        {
            return false;
        }

        ReplaceRangeText(range, value.AsString());
        return true;
    }

    private bool TryInvokeParagraphs(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (EqualsIgnoreCase(path, "Paragraphs.Item"))
        {
            var index = arguments.Count > 0 ? (int)Math.Max(1d, arguments[0].AsDouble()) : 1;
            if (TryGetParagraphByIndex(index, out _))
            {
                result = VbaValue.FromObjectPath($"{ParagraphPrefix}{index}");
                return true;
            }

            return false;
        }

        if (EqualsIgnoreCase(path, "Paragraphs.Add"))
        {
            _session.InsertParagraphBreak();
            var paragraphIndex = _session.Caret.ParagraphIndex + 1;
            result = VbaValue.FromObjectPath($"{ParagraphPrefix}{paragraphIndex}");
            return true;
        }

        return false;
    }

    private bool TryGetTableRowsMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexed(path, TableRowsPrefix, out var index, out var member))
        {
            return false;
        }

        if (!TryGetTable(index, out var table))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{TableRowsPrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Count"))
        {
            result = VbaValue.FromDouble(table.Rows.Count);
            return true;
        }

        return false;
    }

    private bool TryGetTableColumnsMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexed(path, TableColumnsPrefix, out var index, out var member))
        {
            return false;
        }

        if (!TryGetTable(index, out var table))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{TableColumnsPrefix}{index}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Count"))
        {
            result = VbaValue.FromDouble(CountTableColumns(table));
            return true;
        }

        return false;
    }

    private bool TryGetTableRowMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexedPair(path, TableRowPrefix, out var tableIndex, out var rowIndex, out var member))
        {
            return false;
        }

        if (!TryGetTableRow(tableIndex, rowIndex, out var row))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{TableRowPrefix}{tableIndex}:{rowIndex}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Height"))
        {
            result = VbaValue.FromDouble(row.Properties.Height ?? 0f);
            return true;
        }

        if (EqualsIgnoreCase(member, "Range"))
        {
            result = VbaValue.FromObjectPath($"{RangeTableRowPrefix}{tableIndex}:{rowIndex}");
            return true;
        }

        return false;
    }

    private bool TrySetTableRowMember(string path, VbaValue value)
    {
        if (!TryParseIndexedPair(path, TableRowPrefix, out var tableIndex, out var rowIndex, out var member))
        {
            return false;
        }

        if (!EqualsIgnoreCase(member, "Height"))
        {
            return false;
        }

        if (!TryGetTableRow(tableIndex, rowIndex, out var row))
        {
            return false;
        }

        row.Properties.Height = (float)value.AsDouble();
        _session.RefreshLayout();
        return true;
    }

    private bool TryGetTableColumnMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexedPair(path, TableColumnPrefix, out var tableIndex, out var columnIndex, out var member))
        {
            return false;
        }

        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{TableColumnPrefix}{tableIndex}:{columnIndex}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Width"))
        {
            var width = columnIndex <= table.Properties.ColumnWidths.Count
                ? table.Properties.ColumnWidths[columnIndex - 1]
                : 0f;
            result = VbaValue.FromDouble(width);
            return true;
        }

        if (EqualsIgnoreCase(member, "Range"))
        {
            result = VbaValue.FromObjectPath($"{RangeTableColumnPrefix}{tableIndex}:{columnIndex}");
            return true;
        }

        return false;
    }

    private bool TrySetTableColumnMember(string path, VbaValue value)
    {
        if (!TryParseIndexedPair(path, TableColumnPrefix, out var tableIndex, out var columnIndex, out var member))
        {
            return false;
        }

        if (!EqualsIgnoreCase(member, "Width"))
        {
            return false;
        }

        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        EnsureColumnWidths(table, columnIndex);
        table.Properties.ColumnWidths[columnIndex - 1] = (float)value.AsDouble();
        _session.RefreshLayout();
        return true;
    }

    private bool TryGetCellMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexedTriple(path, CellPrefix, out var tableIndex, out var rowIndex, out var columnIndex, out var member))
        {
            return false;
        }

        if (string.IsNullOrEmpty(member))
        {
            result = VbaValue.FromObjectPath($"{CellPrefix}{tableIndex}:{rowIndex}:{columnIndex}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Range"))
        {
            result = VbaValue.FromObjectPath($"{RangeCellPrefix}{tableIndex}:{rowIndex}:{columnIndex}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Text"))
        {
            if (TryResolveRange($"Cell:{tableIndex}:{rowIndex}:{columnIndex}", out var range))
            {
                result = VbaValue.FromString(BuildRangeText(range));
                return true;
            }
        }

        return false;
    }

    private bool TryInvokeTableRows(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexed(path, TableRowsPrefix, out var tableIndex, out var member))
        {
            return false;
        }

        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Item"))
        {
            var rowIndex = arguments.Count > 0 ? (int)Math.Max(1d, arguments[0].AsDouble()) : 1;
            if (rowIndex <= 0 || rowIndex > table.Rows.Count)
            {
                return false;
            }

            result = VbaValue.FromObjectPath($"{TableRowPrefix}{tableIndex}:{rowIndex}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Add"))
        {
            var rowIndex = table.Rows.Count + 1;
            InsertRowAt(table, table.Rows.Count);
            _session.RefreshLayout();
            result = VbaValue.FromObjectPath($"{TableRowPrefix}{tableIndex}:{rowIndex}");
            return true;
        }

        return false;
    }

    private bool TryInvokeTableColumns(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexed(path, TableColumnsPrefix, out var tableIndex, out var member))
        {
            return false;
        }

        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Item"))
        {
            var columnIndex = arguments.Count > 0 ? (int)Math.Max(1d, arguments[0].AsDouble()) : 1;
            if (columnIndex <= 0 || columnIndex > CountTableColumns(table))
            {
                return false;
            }

            result = VbaValue.FromObjectPath($"{TableColumnPrefix}{tableIndex}:{columnIndex}");
            return true;
        }

        if (EqualsIgnoreCase(member, "Add"))
        {
            var columnIndex = CountTableColumns(table) + 1;
            InsertColumnAt(table, columnIndex - 1);
            _session.RefreshLayout();
            result = VbaValue.FromObjectPath($"{TableColumnPrefix}{tableIndex}:{columnIndex}");
            return true;
        }

        return false;
    }

    private bool TryInvokeTableRow(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexedPair(path, TableRowPrefix, out var tableIndex, out var rowIndex, out var member))
        {
            return false;
        }

        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Delete"))
        {
            if (rowIndex <= 0 || rowIndex > table.Rows.Count)
            {
                return false;
            }

            table.Rows.RemoveAt(rowIndex - 1);
            _session.RefreshLayout();
            return true;
        }

        if (EqualsIgnoreCase(member, "Select"))
        {
            if (TryResolveRange($"TableRow:{tableIndex}:{rowIndex}", out var range))
            {
                _session.SetSelection(range);
                return true;
            }
        }

        return false;
    }

    private bool TryInvokeTableColumn(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseIndexedPair(path, TableColumnPrefix, out var tableIndex, out var columnIndex, out var member))
        {
            return false;
        }

        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        if (EqualsIgnoreCase(member, "Delete"))
        {
            if (columnIndex <= 0 || columnIndex > CountTableColumns(table))
            {
                return false;
            }

            DeleteColumnAt(table, columnIndex - 1);
            _session.RefreshLayout();
            return true;
        }

        if (EqualsIgnoreCase(member, "Select"))
        {
            if (TryResolveRange($"TableColumn:{tableIndex}:{columnIndex}", out var range))
            {
                _session.SetSelection(range);
                return true;
            }
        }

        return false;
    }

    private bool TryInvokeCell(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (TryParseIndexed(path, TablePrefix, out var tableIndex, out var member)
            && EqualsIgnoreCase(member, "Cell"))
        {
            var rowIndex = arguments.Count > 0 ? (int)Math.Max(1d, arguments[0].AsDouble()) : 1;
            var columnIndex = arguments.Count > 1 ? (int)Math.Max(1d, arguments[1].AsDouble()) : 1;
            if (TryGetCell(tableIndex, rowIndex, columnIndex, out _))
            {
                result = VbaValue.FromObjectPath($"{CellPrefix}{tableIndex}:{rowIndex}:{columnIndex}");
                return true;
            }

            return false;
        }

        if (!TryParseIndexedTriple(path, CellPrefix, out var cellTable, out var cellRow, out var cellColumn, out var memberName))
        {
            return false;
        }

        if (EqualsIgnoreCase(memberName, "Select"))
        {
            if (TryResolveRange($"Cell:{cellTable}:{cellRow}:{cellColumn}", out var range))
            {
                _session.SetSelection(range);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseRangePath(string path, out string rangeId, out string member)
    {
        rangeId = string.Empty;
        member = string.Empty;
        if (!path.StartsWith(RangePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = path.Substring(RangePrefix.Length);
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var dotIndex = remainder.IndexOf('.');
        rangeId = dotIndex >= 0 ? remainder[..dotIndex] : remainder;
        member = dotIndex >= 0 ? remainder[(dotIndex + 1)..] : string.Empty;
        return true;
    }

    private bool TryResolveRange(string rangeId, out TextRange range)
    {
        range = default;
        if (_rangeCache.TryGetValue(rangeId, out range))
        {
            return true;
        }

        if (EqualsIgnoreCase(rangeId, "Selection"))
        {
            range = _session.Selection.Normalize();
            return true;
        }

        if (EqualsIgnoreCase(rangeId, "Document"))
        {
            var start = new TextPosition(0, 0);
            var lastIndex = Math.Max(0, _session.Document.ParagraphCount - 1);
            var lastParagraph = _session.Document.GetParagraph(lastIndex);
            var end = new TextPosition(lastIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph));
            range = new TextRange(start, end);
            return true;
        }

        if (rangeId.StartsWith("Paragraph:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(rangeId.Substring("Paragraph:".Length), out var paragraphIndex))
        {
            if (!TryGetParagraphByIndex(paragraphIndex, out var paragraph))
            {
                return false;
            }

            var start = new TextPosition(paragraphIndex - 1, 0);
            var end = new TextPosition(paragraphIndex - 1, DocumentEditHelpers.GetParagraphLength(paragraph));
            range = new TextRange(start, end);
            return true;
        }

        if (rangeId.StartsWith("Table:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(rangeId.Substring("Table:".Length), out var tableIndex))
        {
            return TryGetTableRange(tableIndex, out range);
        }

        if (rangeId.StartsWith("TableRow:", StringComparison.OrdinalIgnoreCase)
            && TryParseIndexPair(rangeId.Substring("TableRow:".Length), out var tableRowTable, out var tableRowIndex))
        {
            return TryGetTableRowRange(tableRowTable, tableRowIndex, out range);
        }

        if (rangeId.StartsWith("TableColumn:", StringComparison.OrdinalIgnoreCase)
            && TryParseIndexPair(rangeId.Substring("TableColumn:".Length), out var tableColumnTable, out var tableColumnIndex))
        {
            return TryGetTableColumnRange(tableColumnTable, tableColumnIndex, out range);
        }

        if (rangeId.StartsWith("Cell:", StringComparison.OrdinalIgnoreCase)
            && TryParseIndexTriple(rangeId.Substring("Cell:".Length), out var cellTable, out var cellRow, out var cellColumn))
        {
            return TryGetCellRange(cellTable, cellRow, cellColumn, out range);
        }

        return false;
    }

    private bool TryGetParagraphByIndex(int index, out ParagraphBlock paragraph)
    {
        paragraph = null!;
        if (index <= 0 || index > _session.Document.ParagraphCount)
        {
            return false;
        }

        paragraph = _session.Document.GetParagraph(index - 1);
        return true;
    }

    private string BuildRangeText(TextRange range)
    {
        var normalized = range.Normalize();
        var startIndex = Math.Clamp(normalized.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(normalized.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var builder = new StringBuilder();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? normalized.Start.Offset : 0;
            var endOffset = i == endIndex ? normalized.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (endOffset > startOffset)
            {
                AppendParagraphSlice(builder, paragraph, startOffset, endOffset);
            }

            if (i < endIndex)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void AppendParagraphSlice(StringBuilder builder, ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            if (startOffset < text.Length)
            {
                var length = Math.Clamp(endOffset - startOffset, 0, text.Length - startOffset);
                if (length > 0)
                {
                    builder.Append(text.AsSpan(startOffset, length));
                }
            }

            return;
        }

        var position = 0;
        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inlineEnd <= startOffset || inlineStart >= endOffset)
            {
                continue;
            }

            var sliceStart = Math.Max(startOffset, inlineStart) - inlineStart;
            var sliceEnd = Math.Min(endOffset, inlineEnd) - inlineStart;
            AppendInlineSlice(builder, inline, sliceStart, sliceEnd - sliceStart);
        }
    }

    private static void AppendInlineSlice(StringBuilder builder, Inline inline, int start, int length)
    {
        if (length <= 0)
        {
            return;
        }

        switch (inline)
        {
            case RunInline run:
                builder.Append(run.Text.GetSlice(start, length));
                break;
            case ImageInline:
            case ShapeInline:
            case ChartInline:
            case EquationInline:
            case PageNumberInline:
            case TotalPagesInline:
                builder.Append(DocumentConstants.ObjectReplacementChar);
                break;
            default:
                break;
        }
    }

    private void ReplaceRangeText(TextRange range, string text)
    {
        var previousSelection = _session.Selection;
        _session.SetSelection(range);
        if (string.IsNullOrEmpty(text))
        {
            _session.Backspace();
        }
        else
        {
            ReplaceSelectionText(text);
        }

        _session.SetSelection(previousSelection);
    }

    private int GetDocumentOffset(TextPosition position)
    {
        var offset = 0;
        for (var i = 0; i < position.ParagraphIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            offset += DocumentEditHelpers.GetParagraphLength(paragraph) + 1;
        }

        offset += position.Offset;
        return offset;
    }

    private TextPosition GetPositionFromDocumentOffset(int offset)
    {
        var paragraphCount = _session.Document.ParagraphCount;
        var current = 0;
        for (var i = 0; i < paragraphCount; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            var length = DocumentEditHelpers.GetParagraphLength(paragraph);
            if (offset <= current + length)
            {
                return new TextPosition(i, Math.Max(0, offset - current));
            }

            current += length + 1;
        }

        var lastIndex = Math.Max(0, paragraphCount - 1);
        var lastParagraph = _session.Document.GetParagraph(lastIndex);
        return new TextPosition(lastIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph));
    }

    private bool TryGetTableRange(int tableIndex, out TextRange range)
    {
        range = default;
        if (!TryGetTableParagraphSpan(tableIndex, out var startIndex, out var endIndex))
        {
            return false;
        }

        var start = new TextPosition(startIndex, 0);
        var lastParagraph = _session.Document.GetParagraph(endIndex);
        var end = new TextPosition(endIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph));
        range = new TextRange(start, end);
        return true;
    }

    private bool TryGetTableRowRange(int tableIndex, int rowIndex, out TextRange range)
    {
        range = default;
        if (!TryGetTableRow(tableIndex, rowIndex, out var row) || row.Cells.Count == 0)
        {
            return false;
        }

        if (!TryGetFirstAndLastParagraph(row.Cells, out var firstParagraph, out var lastParagraph))
        {
            return false;
        }

        if (firstParagraph is null || lastParagraph is null)
        {
            return false;
        }

        var startIndex = FindParagraphIndex(firstParagraph);
        var endIndex = FindParagraphIndex(lastParagraph);
        if (startIndex < 0 || endIndex < 0)
        {
            return false;
        }

        var start = new TextPosition(startIndex, 0);
        var end = new TextPosition(endIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph));
        range = new TextRange(start, end);
        return true;
    }

    private bool TryGetTableColumnRange(int tableIndex, int columnIndex, out TextRange range)
    {
        range = default;
        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        ParagraphBlock? firstParagraph = null;
        ParagraphBlock? lastParagraph = null;
        foreach (var row in table.Rows)
        {
            if (!TryGetCellAtColumn(row, columnIndex - 1, out var cell, out _, out _))
            {
                continue;
            }

            if (!TryGetFirstAndLastParagraph(cell, out var cellFirst, out var cellLast))
            {
                continue;
            }

            firstParagraph ??= cellFirst;
            lastParagraph = cellLast;
        }

        if (firstParagraph is null || lastParagraph is null)
        {
            return false;
        }

        var startIndex = FindParagraphIndex(firstParagraph);
        var endIndex = FindParagraphIndex(lastParagraph);
        if (startIndex < 0 || endIndex < 0)
        {
            return false;
        }

        range = new TextRange(
            new TextPosition(startIndex, 0),
            new TextPosition(endIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph)));
        return true;
    }

    private bool TryGetCellRange(int tableIndex, int rowIndex, int columnIndex, out TextRange range)
    {
        range = default;
        if (!TryGetCell(tableIndex, rowIndex, columnIndex, out var cell))
        {
            return false;
        }

        if (!TryGetFirstAndLastParagraph(cell, out var firstParagraph, out var lastParagraph))
        {
            return false;
        }

        if (firstParagraph is null || lastParagraph is null)
        {
            return false;
        }

        var startIndex = FindParagraphIndex(firstParagraph);
        var endIndex = FindParagraphIndex(lastParagraph);
        if (startIndex < 0 || endIndex < 0)
        {
            return false;
        }

        range = new TextRange(
            new TextPosition(startIndex, 0),
            new TextPosition(endIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph)));
        return true;
    }

    private static bool TryGetFirstAndLastParagraph(
        IReadOnlyList<TableCell> cells,
        out ParagraphBlock? first,
        out ParagraphBlock? last)
    {
        first = null;
        last = null;
        foreach (var cell in cells)
        {
            if (!TryGetFirstAndLastParagraph(cell, out var cellFirst, out var cellLast))
            {
                continue;
            }

            first ??= cellFirst;
            last = cellLast;
        }

        return first is not null && last is not null;
    }

    private static bool TryGetFirstAndLastParagraph(
        TableCell cell,
        out ParagraphBlock? first,
        out ParagraphBlock? last)
    {
        first = null;
        last = null;
        if (cell.Paragraphs.Count == 0)
        {
            return false;
        }

        first = cell.Paragraphs[0];
        last = cell.Paragraphs[cell.Paragraphs.Count - 1];
        return true;
    }

    private bool TryGetTableRow(int tableIndex, int rowIndex, out TableRow row)
    {
        row = null!;
        if (!TryGetTable(tableIndex, out var table))
        {
            return false;
        }

        if (rowIndex <= 0 || rowIndex > table.Rows.Count)
        {
            return false;
        }

        row = table.Rows[rowIndex - 1];
        return true;
    }

    private bool TryGetCell(int tableIndex, int rowIndex, int columnIndex, out TableCell cell)
    {
        cell = null!;
        if (!TryGetTableRow(tableIndex, rowIndex, out var row))
        {
            return false;
        }

        if (!TryGetCellAtColumn(row, columnIndex - 1, out cell, out _, out _))
        {
            return false;
        }

        return true;
    }

    private bool TryGetTableParagraphSpan(int tableIndex, out int startIndex, out int endIndex)
    {
        startIndex = -1;
        endIndex = -1;
        var paragraphIndex = 0;
        var tableCount = 0;
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock:
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    tableCount++;
                    var tableParagraphCount = CountParagraphsInTable(table);
                    if (tableCount == tableIndex)
                    {
                        startIndex = paragraphIndex;
                        endIndex = paragraphIndex + Math.Max(0, tableParagraphCount - 1);
                        return true;
                    }

                    paragraphIndex += tableParagraphCount;
                    break;
            }
        }

        return false;
    }

    private static int CountParagraphsInTable(TableBlock table)
    {
        var count = 0;
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                count += cell.Paragraphs.Count;
            }
        }

        return count;
    }

    private int FindParagraphIndex(ParagraphBlock paragraph)
    {
        var index = 0;
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraphBlock:
                    if (ReferenceEquals(paragraphBlock, paragraph))
                    {
                        return index;
                    }

                    index++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellParagraph in cell.Paragraphs)
                            {
                                if (ReferenceEquals(cellParagraph, paragraph))
                                {
                                    return index;
                                }

                                index++;
                            }
                        }
                    }

                    break;
            }
        }

        return -1;
    }

    private static bool TryGetCellAtColumn(
        TableRow row,
        int columnIndex,
        out TableCell cell,
        out int cellIndex,
        out int cellStart)
    {
        cell = null!;
        cellIndex = -1;
        cellStart = -1;
        var cursor = Math.Max(0, row.Properties.GridBefore ?? 0);
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var current = row.Cells[i];
            var span = Math.Max(1, current.ColumnSpan);
            if (columnIndex >= cursor && columnIndex < cursor + span)
            {
                cell = current;
                cellIndex = i;
                cellStart = cursor;
                return true;
            }

            cursor += span;
        }

        return false;
    }

    private static void InsertRowAt(TableBlock table, int rowIndex)
    {
        var template = table.Rows.Count > 0
            ? table.Rows[Math.Clamp(rowIndex - 1, 0, table.Rows.Count - 1)]
            : null;
        var row = CreateRowFromTemplate(template, CountTableColumns(table));
        rowIndex = Math.Clamp(rowIndex, 0, table.Rows.Count);
        table.Rows.Insert(rowIndex, row);
    }

    private static void InsertColumnAt(TableBlock table, int columnIndex)
    {
        foreach (var row in table.Rows)
        {
            var gridBefore = Math.Max(0, row.Properties.GridBefore ?? 0);
            var gridAfter = Math.Max(0, row.Properties.GridAfter ?? 0);
            var totalColumns = gridBefore + CountRowColumns(row) + gridAfter;
            if (columnIndex <= gridBefore)
            {
                row.Properties.GridBefore = gridBefore + 1;
                continue;
            }

            if (columnIndex >= totalColumns - gridAfter)
            {
                row.Cells.Add(CreateCellFromTemplate(row.Cells.Count > 0 ? row.Cells[^1] : null));
                continue;
            }

            if (TryGetCellAtColumn(row, columnIndex, out var cell, out var cellIndex, out var cellStart))
            {
                if (columnIndex == cellStart)
                {
                    row.Cells.Insert(cellIndex, CreateCellFromTemplate(cell));
                }
                else
                {
                    cell.ColumnSpan = Math.Max(1, cell.ColumnSpan) + 1;
                }
            }
        }

        EnsureColumnWidths(table, columnIndex + 1);
    }

    private static void DeleteColumnAt(TableBlock table, int columnIndex)
    {
        foreach (var row in table.Rows)
        {
            var gridBefore = Math.Max(0, row.Properties.GridBefore ?? 0);
            var gridAfter = Math.Max(0, row.Properties.GridAfter ?? 0);
            var totalColumns = gridBefore + CountRowColumns(row) + gridAfter;
            if (columnIndex < gridBefore)
            {
                row.Properties.GridBefore = Math.Max(0, gridBefore - 1);
                continue;
            }

            if (columnIndex >= totalColumns - gridAfter)
            {
                row.Properties.GridAfter = Math.Max(0, gridAfter - 1);
                continue;
            }

            if (TryGetCellAtColumn(row, columnIndex, out var cell, out var cellIndex, out _))
            {
                if (cell.ColumnSpan > 1)
                {
                    cell.ColumnSpan = Math.Max(1, cell.ColumnSpan - 1);
                }
                else
                {
                    row.Cells.RemoveAt(cellIndex);
                }
            }
        }

        TrimColumnWidths(table, columnIndex);
    }

    private static TableRow CreateRowFromTemplate(TableRow? template, int columnCount)
    {
        var row = new TableRow();
        if (template is not null)
        {
            CopyTableRowProperties(template.Properties, row.Properties);
            foreach (var cell in template.Cells)
            {
                var newCell = CreateCellFromTemplate(cell);
                newCell.ColumnSpan = cell.ColumnSpan;
                row.Cells.Add(newCell);
            }

            return row;
        }

        for (var i = 0; i < Math.Max(1, columnCount); i++)
        {
            row.Cells.Add(CreateCellFromTemplate(null));
        }

        return row;
    }

    private static TableCell CreateCellFromTemplate(TableCell? template)
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(new ParagraphBlock());
        if (template is null)
        {
            return cell;
        }

        CopyTableCellProperties(template.Properties, cell.Properties);
        return cell;
    }

    private static void CopyTableRowProperties(TableRowProperties source, TableRowProperties target)
    {
        target.Height = source.Height;
        target.HeightRule = source.HeightRule;
        target.CantSplit = source.CantSplit;
        target.RepeatOnEachPage = source.RepeatOnEachPage;
        target.ShadingColor = source.ShadingColor;
        target.GridBefore = source.GridBefore;
        target.GridAfter = source.GridAfter;
    }

    private static void CopyTableCellProperties(TableCellProperties source, TableCellProperties target)
    {
        target.Padding = source.Padding;
        target.ShadingColor = source.ShadingColor;
        target.VerticalAlignment = source.VerticalAlignment;
        target.TextDirection = source.TextDirection;
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private static int CountRowColumns(TableRow row)
    {
        var count = 0;
        foreach (var cell in row.Cells)
        {
            count += Math.Max(1, cell.ColumnSpan);
        }

        return count;
    }

    private static void EnsureColumnWidths(TableBlock table, int columnIndex)
    {
        while (table.Properties.ColumnWidths.Count < columnIndex)
        {
            table.Properties.ColumnWidths.Add(0f);
        }
    }

    private static void TrimColumnWidths(TableBlock table, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= table.Properties.ColumnWidths.Count)
        {
            return;
        }

        table.Properties.ColumnWidths.RemoveAt(columnIndex);
    }

    private static bool TryParseIndexedPair(string path, string prefix, out int first, out int second, out string member)
    {
        first = 0;
        second = 0;
        member = string.Empty;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = path.Substring(prefix.Length);
        var dotIndex = remainder.IndexOf('.');
        var idText = dotIndex >= 0 ? remainder[..dotIndex] : remainder;
        if (!TryParseIndexPair(idText, out first, out second))
        {
            return false;
        }

        member = dotIndex >= 0 ? remainder[(dotIndex + 1)..] : string.Empty;
        return true;
    }

    private static bool TryParseIndexedTriple(string path, string prefix, out int first, out int second, out int third, out string member)
    {
        first = 0;
        second = 0;
        third = 0;
        member = string.Empty;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = path.Substring(prefix.Length);
        var dotIndex = remainder.IndexOf('.');
        var idText = dotIndex >= 0 ? remainder[..dotIndex] : remainder;
        if (!TryParseIndexTriple(idText, out first, out second, out third))
        {
            return false;
        }

        member = dotIndex >= 0 ? remainder[(dotIndex + 1)..] : string.Empty;
        return true;
    }

    private static bool TryParseIndexPair(string text, out int first, out int second)
    {
        first = 0;
        second = 0;
        var parts = text.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        return int.TryParse(parts[0], out first)
               && int.TryParse(parts[1], out second)
               && first > 0
               && second > 0;
    }

    private static bool TryParseIndexTriple(string text, out int first, out int second, out int third)
    {
        first = 0;
        second = 0;
        third = 0;
        var parts = text.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        return int.TryParse(parts[0], out first)
               && int.TryParse(parts[1], out second)
               && int.TryParse(parts[2], out third)
               && first > 0
               && second > 0
               && third > 0;
    }

    private static bool IsSelectionRange(string rangeId)
    {
        return string.Equals(rangeId, "Selection", StringComparison.OrdinalIgnoreCase);
    }

    private string CreateRangeId(TextRange range)
    {
        var id = $"Temp:{Interlocked.Increment(ref _rangeCounter)}";
        _rangeCache[id] = range;
        return id;
    }

    private bool UpdateRangeState(string rangeId, TextRange range)
    {
        var updated = false;
        if (IsSelectionRange(rangeId))
        {
            _session.SetSelection(range);
            updated = true;
        }

        if (_rangeCache.ContainsKey(rangeId))
        {
            _rangeCache[rangeId] = range;
            updated = true;
        }

        return updated;
    }

    private VbaValue CreateRangeObject(TextRange range)
    {
        var rangeId = CreateRangeId(range);
        return VbaValue.FromObjectPath($"{RangePrefix}{rangeId}");
    }

    private static string BuildRangePath(string rangeId, string member)
    {
        return string.IsNullOrWhiteSpace(member)
            ? $"{RangePrefix}{rangeId}"
            : $"{RangePrefix}{rangeId}.{member}";
    }

    private bool TryNormalizeRangePath(string path, out string rangeId, out string member)
    {
        rangeId = string.Empty;
        member = string.Empty;
        if (TryParseRangePath(path, out rangeId, out member))
        {
            return true;
        }

        const string selectionRangePrefix = "Selection.Range";
        const string activeRangePrefix = "ActiveDocument.Range";
        const string activeContentPrefix = "ActiveDocument.Content";
        if (path.StartsWith(selectionRangePrefix, StringComparison.OrdinalIgnoreCase))
        {
            rangeId = "Selection";
            member = ExtractMember(path, selectionRangePrefix);
            return true;
        }

        if (path.StartsWith(activeRangePrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(activeContentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            rangeId = "Document";
            member = ExtractMember(path, path.StartsWith(activeRangePrefix, StringComparison.OrdinalIgnoreCase)
                ? activeRangePrefix
                : activeContentPrefix);
            return true;
        }

        return false;
    }

    private static string ExtractMember(string path, string prefix)
    {
        if (path.Length == prefix.Length)
        {
            return string.Empty;
        }

        if (path.Length > prefix.Length && path[prefix.Length] == '.')
        {
            return path[(prefix.Length + 1)..];
        }

        return string.Empty;
    }

    private bool TryGetFindMember(string path, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseFindPath(path, out var rangeId, out var member))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(member))
        {
            result = VbaValue.FromObjectPath($"{FindPrefix}{rangeId}");
            return true;
        }

        var state = GetFindState(rangeId);
        if (EqualsIgnoreCase(member, "Text"))
        {
            result = VbaValue.FromString(state.Text ?? string.Empty);
            return true;
        }

        if (EqualsIgnoreCase(member, "Forward"))
        {
            result = VbaValue.FromBoolean(state.Forward);
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchCase"))
        {
            result = VbaValue.FromBoolean(state.MatchCase);
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchWholeWord"))
        {
            result = VbaValue.FromBoolean(state.MatchWholeWord);
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchWildcards"))
        {
            result = VbaValue.FromBoolean(state.MatchWildcards);
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchAllWordForms"))
        {
            result = VbaValue.FromBoolean(state.MatchAllWordForms);
            return true;
        }

        if (EqualsIgnoreCase(member, "Wrap"))
        {
            result = VbaValue.FromDouble(state.WrapMode);
            return true;
        }

        return false;
    }

    private bool TrySetFindMember(string path, VbaValue value)
    {
        if (!TryParseFindPath(path, out var rangeId, out var member))
        {
            return false;
        }

        var state = GetFindState(rangeId);
        if (EqualsIgnoreCase(member, "Text"))
        {
            state.Text = value.AsString();
            return true;
        }

        if (EqualsIgnoreCase(member, "Forward"))
        {
            state.Forward = value.AsBoolean();
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchCase"))
        {
            state.MatchCase = value.AsBoolean();
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchWholeWord"))
        {
            state.MatchWholeWord = value.AsBoolean();
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchWildcards"))
        {
            state.MatchWildcards = value.AsBoolean();
            return true;
        }

        if (EqualsIgnoreCase(member, "MatchAllWordForms"))
        {
            state.MatchAllWordForms = value.AsBoolean();
            return true;
        }

        if (EqualsIgnoreCase(member, "Wrap"))
        {
            state.WrapMode = NormalizeWrapMode(value);
            return true;
        }

        return false;
    }

    private bool TryInvokeFindMember(string path, IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (!TryParseFindPath(path, out var rangeId, out var member))
        {
            return false;
        }

        if (!EqualsIgnoreCase(member, "Execute"))
        {
            return false;
        }

        if (!TryResolveRange(rangeId, out var range))
        {
            result = VbaValue.FromBoolean(false);
            return true;
        }

        var state = GetFindState(rangeId);
        ApplyFindArguments(state, arguments);
        if (string.IsNullOrWhiteSpace(state.Text))
        {
            result = VbaValue.FromBoolean(false);
            return true;
        }

        var found = ExecuteFind(rangeId, range, state);
        result = VbaValue.FromBoolean(found);
        return true;
    }

    private static bool TryParseFindPath(string path, out string rangeId, out string member)
    {
        rangeId = string.Empty;
        member = string.Empty;
        if (path.StartsWith(FindPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = path.Substring(FindPrefix.Length);
            var dotIndex = remainder.IndexOf('.');
            rangeId = dotIndex >= 0 ? remainder[..dotIndex] : remainder;
            member = dotIndex >= 0 ? remainder[(dotIndex + 1)..] : string.Empty;
            return !string.IsNullOrWhiteSpace(rangeId);
        }

        var findIndex = path.IndexOf(".Find", StringComparison.OrdinalIgnoreCase);
        if (findIndex < 0)
        {
            return false;
        }

        var rangePath = path[..findIndex];
        if (!TryResolveRangeIdFromPath(rangePath, out rangeId))
        {
            return false;
        }

        var findRemainder = path[(findIndex + ".Find".Length)..];
        member = findRemainder.StartsWith(".", StringComparison.Ordinal) ? findRemainder[1..] : findRemainder;
        return true;
    }

    private static bool TryResolveRangeIdFromPath(string path, out string rangeId)
    {
        rangeId = string.Empty;
        if (path.StartsWith(RangePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = path.Substring(RangePrefix.Length);
            rangeId = string.IsNullOrWhiteSpace(remainder) ? string.Empty : remainder;
            return !string.IsNullOrWhiteSpace(rangeId);
        }

        if (EqualsIgnoreCase(path, "Selection") || EqualsIgnoreCase(path, "Selection.Range"))
        {
            rangeId = "Selection";
            return true;
        }

        if (EqualsIgnoreCase(path, "ActiveDocument.Range") || EqualsIgnoreCase(path, "ActiveDocument.Content"))
        {
            rangeId = "Document";
            return true;
        }

        if (path.StartsWith(ParagraphPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(TablePrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(TableRowPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(TableColumnPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(CellPrefix, StringComparison.OrdinalIgnoreCase))
        {
            rangeId = path;
            return true;
        }

        return false;
    }

    private FindState GetFindState(string rangeId)
    {
        if (_findStates.TryGetValue(rangeId, out var state))
        {
            return state;
        }

        state = new FindState();
        _findStates[rangeId] = state;
        return state;
    }

    private static void ApplyFindArguments(FindState state, IReadOnlyList<VbaValue> arguments)
    {
        if (arguments.Count > 0)
        {
            var text = arguments[0].AsString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                state.Text = text;
            }
        }

        if (arguments.Count > 1)
        {
            state.MatchCase = arguments[1].AsBoolean();
        }

        if (arguments.Count > 2)
        {
            state.MatchWholeWord = arguments[2].AsBoolean();
        }

        if (arguments.Count > 3)
        {
            state.MatchWildcards = arguments[3].AsBoolean();
        }

        if (arguments.Count > 5)
        {
            state.MatchAllWordForms = arguments[5].AsBoolean();
        }

        if (arguments.Count > 6)
        {
            state.Forward = arguments[6].AsBoolean();
        }

        if (arguments.Count > 7)
        {
            state.WrapMode = NormalizeWrapMode(arguments[7]);
        }
    }

    private static int NormalizeWrapMode(VbaValue value)
    {
        var raw = (int)Math.Round(value.AsDouble());
        return raw switch
        {
            WdFindWrapStop => WdFindWrapStop,
            WdFindWrapContinue => WdFindWrapContinue,
            WdFindWrapAsk => WdFindWrapAsk,
            _ => raw > 0 ? WdFindWrapContinue : WdFindWrapStop
        };
    }

    private bool ExecuteFind(string rangeId, TextRange range, FindState state)
    {
        var documentText = BuildDocumentText();
        if (string.IsNullOrEmpty(documentText) || string.IsNullOrEmpty(state.Text))
        {
            return false;
        }

        var normalized = range.Normalize();
        var rangeStart = GetDocumentOffset(normalized.Start);
        var rangeEnd = GetDocumentOffset(normalized.End);
        if (rangeStart > rangeEnd)
        {
            (rangeStart, rangeEnd) = (rangeEnd, rangeStart);
        }

        var documentLength = documentText.Length;
        if (!state.HasScope || rangeStart < state.ScopeStart || rangeEnd > state.ScopeEnd)
        {
            if (rangeStart == rangeEnd)
            {
                state.ScopeStart = 0;
                state.ScopeEnd = documentLength;
            }
            else
            {
                state.ScopeStart = rangeStart;
                state.ScopeEnd = rangeEnd;
            }

            state.HasScope = true;
            state.HasLastMatch = false;
        }

        var scopeStart = Math.Clamp(state.ScopeStart, 0, documentLength);
        var scopeEnd = Math.Clamp(state.ScopeEnd, scopeStart, documentLength);
        if (scopeEnd <= scopeStart)
        {
            return false;
        }

        var comparison = state.MatchCase
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;

        var foundIndex = -1;
        var matchLength = 0;
        var allowWrap = state.WrapMode == WdFindWrapContinue;
        var wholeWord = state.MatchWholeWord || state.MatchAllWordForms;
        var isLastMatch = state.HasLastMatch
            && rangeStart == state.LastMatchStart
            && rangeEnd == state.LastMatchEnd;

        if (state.MatchWildcards)
        {
            if (state.Forward)
            {
                var startIndex = Math.Clamp(isLastMatch ? rangeEnd : rangeStart, scopeStart, scopeEnd);
                if (startIndex < scopeEnd)
                {
                    TryFindWildcardForward(documentText, state.Text!, startIndex, scopeEnd, state.MatchCase, wholeWord, out foundIndex, out matchLength);
                }

                if (foundIndex < 0 && allowWrap)
                {
                    TryFindWildcardForward(documentText, state.Text!, scopeStart, scopeEnd, state.MatchCase, wholeWord, out foundIndex, out matchLength);
                }
            }
            else
            {
                var startIndex = isLastMatch ? rangeStart - 1 : rangeEnd - 1;
                if (startIndex > scopeEnd - 1)
                {
                    startIndex = scopeEnd - 1;
                }

                if (startIndex >= scopeStart)
                {
                    TryFindWildcardBackward(documentText, state.Text!, startIndex, scopeStart, scopeEnd, state.MatchCase, wholeWord, out foundIndex, out matchLength);
                }

                if (foundIndex < 0 && allowWrap)
                {
                    var wrapIndex = scopeEnd - 1;
                    if (wrapIndex >= scopeStart)
                    {
                        TryFindWildcardBackward(documentText, state.Text!, wrapIndex, scopeStart, scopeEnd, state.MatchCase, wholeWord, out foundIndex, out matchLength);
                    }
                }
            }
        }
        else
        {
            if (state.Forward)
            {
                var startIndex = Math.Clamp(isLastMatch ? rangeEnd : rangeStart, scopeStart, scopeEnd);
                if (startIndex < scopeEnd)
                {
                    foundIndex = FindNextMatch(documentText, state.Text!, startIndex, scopeEnd, comparison, wholeWord);
                }

                if (foundIndex < 0 && allowWrap)
                {
                    foundIndex = FindNextMatch(documentText, state.Text!, scopeStart, scopeEnd, comparison, wholeWord);
                }
            }
            else
            {
                var startIndex = isLastMatch ? rangeStart - 1 : rangeEnd - 1;
                if (startIndex > scopeEnd - 1)
                {
                    startIndex = scopeEnd - 1;
                }

                if (startIndex >= scopeStart)
                {
                    foundIndex = FindPreviousMatch(documentText, state.Text!, startIndex, scopeStart, scopeEnd, comparison, wholeWord);
                }

                if (foundIndex < 0 && allowWrap)
                {
                    var wrapIndex = scopeEnd - 1;
                    if (wrapIndex >= scopeStart)
                    {
                        foundIndex = FindPreviousMatch(documentText, state.Text!, wrapIndex, scopeStart, scopeEnd, comparison, wholeWord);
                    }
                }
            }

            if (foundIndex >= 0)
            {
                matchLength = state.Text!.Length;
            }
        }

        if (foundIndex < 0 || matchLength <= 0)
        {
            return false;
        }

        var startOffset = foundIndex;
        var endOffset = startOffset + matchLength;
        var start = GetPositionFromDocumentOffset(startOffset);
        var end = GetPositionFromDocumentOffset(endOffset);
        var foundRange = new TextRange(start, end);
        UpdateRangeState(rangeId, foundRange);
        state.LastMatchStart = startOffset;
        state.LastMatchEnd = endOffset;
        state.HasLastMatch = true;
        return true;
    }

    private static int FindNextMatch(
        string text,
        string needle,
        int startIndex,
        int endIndex,
        StringComparison comparison,
        bool wholeWord)
    {
        if (startIndex < 0 || endIndex <= startIndex || needle.Length == 0)
        {
            return -1;
        }

        var limit = endIndex - needle.Length;
        var index = startIndex;
        while (index <= limit)
        {
            var matchIndex = text.IndexOf(needle, index, endIndex - index, comparison);
            if (matchIndex < 0 || matchIndex + needle.Length > endIndex)
            {
                return -1;
            }

            if (!wholeWord || IsWholeWordMatch(text, matchIndex, needle.Length))
            {
                return matchIndex;
            }

            index = matchIndex + 1;
        }

        return -1;
    }

    private static int FindPreviousMatch(
        string text,
        string needle,
        int startIndex,
        int scopeStart,
        int scopeEnd,
        StringComparison comparison,
        bool wholeWord)
    {
        if (scopeEnd <= scopeStart || needle.Length == 0)
        {
            return -1;
        }

        var index = Math.Min(startIndex, scopeEnd - 1);
        while (index >= scopeStart)
        {
            var count = index - scopeStart + 1;
            var matchIndex = text.LastIndexOf(needle, index, count, comparison);
            if (matchIndex < 0)
            {
                return -1;
            }

            if (matchIndex + needle.Length <= scopeEnd
                && (!wholeWord || IsWholeWordMatch(text, matchIndex, needle.Length)))
            {
                return matchIndex;
            }

            index = matchIndex - 1;
        }

        return -1;
    }

    private static void TryFindWildcardForward(
        string text,
        string pattern,
        int startIndex,
        int endIndex,
        bool matchCase,
        bool wholeWord,
        out int matchIndex,
        out int matchLength)
    {
        matchIndex = -1;
        matchLength = 0;
        if (startIndex < 0 || endIndex <= startIndex || string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var limit = Math.Min(endIndex, text.Length);
        for (var i = startIndex; i < limit; i++)
        {
            if (!TryMatchWildcardAt(text, pattern, i, limit, matchCase, out var length))
            {
                continue;
            }

            if (length <= 0)
            {
                continue;
            }

            if (wholeWord && !IsWholeWordMatch(text, i, length))
            {
                continue;
            }

            matchIndex = i;
            matchLength = length;
            return;
        }
    }

    private static void TryFindWildcardBackward(
        string text,
        string pattern,
        int startIndex,
        int scopeStart,
        int scopeEnd,
        bool matchCase,
        bool wholeWord,
        out int matchIndex,
        out int matchLength)
    {
        matchIndex = -1;
        matchLength = 0;
        if (scopeEnd <= scopeStart || string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var start = Math.Min(startIndex, scopeEnd - 1);
        for (var i = start; i >= scopeStart; i--)
        {
            if (!TryMatchWildcardAt(text, pattern, i, scopeEnd, matchCase, out var length))
            {
                continue;
            }

            if (length <= 0)
            {
                continue;
            }

            if (wholeWord && !IsWholeWordMatch(text, i, length))
            {
                continue;
            }

            matchIndex = i;
            matchLength = length;
            return;
        }
    }

    private static bool TryMatchWildcardAt(
        string text,
        string pattern,
        int startIndex,
        int endIndex,
        bool matchCase,
        out int matchLength)
    {
        matchLength = 0;
        if (startIndex < 0 || startIndex >= endIndex || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var textIndex = startIndex;
        var patternIndex = 0;
        var starIndex = -1;
        var matchIndex = startIndex;

        while (textIndex < endIndex)
        {
            if (patternIndex == pattern.Length)
            {
                matchLength = textIndex - startIndex;
                return matchLength > 0;
            }

            var token = pattern[patternIndex];
            if (token == '*')
            {
                starIndex = patternIndex++;
                matchIndex = textIndex;
                continue;
            }

            if (token == '?' || CharsEqual(text[textIndex], token, matchCase))
            {
                textIndex++;
                patternIndex++;
                continue;
            }

            if (starIndex != -1)
            {
                patternIndex = starIndex + 1;
                matchIndex++;
                textIndex = matchIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        if (patternIndex == pattern.Length)
        {
            matchLength = textIndex - startIndex;
            return matchLength > 0;
        }

        return false;
    }

    private static bool CharsEqual(char left, char right, bool matchCase)
    {
        if (matchCase)
        {
            return left == right;
        }

        return char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
    }

    private static bool IsWholeWordMatch(string text, int index, int length)
    {
        var start = index;
        var end = index + length;
        var hasLeft = start > 0;
        var hasRight = end < text.Length;
        var leftOk = !hasLeft || !IsWordChar(text[start - 1]);
        var rightOk = !hasRight || !IsWordChar(text[end]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }

    private int MoveSelectionEdge(bool moveStart, IReadOnlyList<VbaValue> arguments)
    {
        var selection = _session.Selection.Normalize();
        return MoveRangeEdge("Selection", selection, moveStart, arguments);
    }

    private int MoveRangeEdge(string rangeId, TextRange range, bool moveStart, IReadOnlyList<VbaValue> arguments)
    {
        var unit = arguments.Count > 0 ? (int)Math.Round(arguments[0].AsDouble()) : WdUnitCharacter;
        var count = arguments.Count > 1 ? (int)Math.Round(arguments[1].AsDouble()) : 1;
        if (count == 0)
        {
            return 0;
        }

        var documentText = BuildDocumentText();
        var startOffset = GetDocumentOffset(range.Start);
        var endOffset = GetDocumentOffset(range.End);
        var moveOffset = moveStart ? startOffset : endOffset;
        var (newOffset, moved) = MoveOffsetByUnit(documentText, moveOffset, unit, count);

        if (moveStart)
        {
            startOffset = newOffset;
            if (startOffset > endOffset)
            {
                endOffset = startOffset;
            }
        }
        else
        {
            endOffset = newOffset;
            if (endOffset < startOffset)
            {
                startOffset = endOffset;
            }
        }

        var updatedRange = new TextRange(
            GetPositionFromDocumentOffset(startOffset),
            GetPositionFromDocumentOffset(endOffset));
        UpdateRangeState(rangeId, updatedRange);
        return moved;
    }

    private static (int Offset, int Moved) MoveOffsetByUnit(string documentText, int offset, int unit, int count)
    {
        var length = documentText.Length;
        var current = Math.Clamp(offset, 0, length);
        var direction = Math.Sign(count);
        var remaining = Math.Abs(count);
        var moved = 0;

        if (unit == WdUnitStory)
        {
            var target = direction >= 0 ? length : 0;
            moved = target == current ? 0 : direction;
            return (target, moved);
        }

        while (remaining > 0)
        {
            var next = current;
            if (unit == WdUnitWord)
            {
                next = direction >= 0
                    ? MoveToNextWord(documentText, current)
                    : MoveToPreviousWord(documentText, current);
            }
            else if (unit == WdUnitParagraph || unit == WdUnitLine)
            {
                next = direction >= 0
                    ? MoveToNextParagraph(documentText, current)
                    : MoveToPreviousParagraph(documentText, current);
            }
            else
            {
                next = Math.Clamp(current + direction, 0, length);
            }

            if (next == current)
            {
                break;
            }

            current = next;
            moved += direction;
            remaining--;
        }

        return (current, moved);
    }

    private static int MoveToNextWord(string text, int offset)
    {
        var length = text.Length;
        var index = Math.Clamp(offset, 0, length);
        while (index < length && IsWordChar(text[index]))
        {
            index++;
        }

        while (index < length && !IsWordChar(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int MoveToPreviousWord(string text, int offset)
    {
        var index = Math.Clamp(offset - 1, 0, Math.Max(0, text.Length - 1));
        while (index >= 0 && !IsWordChar(text[index]))
        {
            index--;
        }

        while (index >= 0 && IsWordChar(text[index]))
        {
            index--;
        }

        return Math.Max(0, index + 1);
    }

    private static int MoveToNextParagraph(string text, int offset)
    {
        var length = text.Length;
        var index = Math.Clamp(offset, 0, length);
        var newlineIndex = text.IndexOf('\n', index);
        return newlineIndex < 0 ? length : Math.Min(length, newlineIndex + 1);
    }

    private static int MoveToPreviousParagraph(string text, int offset)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var index = Math.Clamp(offset - 1, 0, text.Length - 1);
        if (text[index] == '\n')
        {
            index--;
            if (index < 0)
            {
                return 0;
            }
        }

        var newlineIndex = text.LastIndexOf('\n', index);
        return newlineIndex < 0 ? 0 : Math.Min(text.Length, newlineIndex + 1);
    }

    private bool TryRunMacro(IReadOnlyList<VbaValue> arguments, out VbaValue result)
    {
        result = VbaValue.Empty;
        if (arguments.Count == 0)
        {
            return false;
        }

        var macroName = ResolveMacroName(arguments[0].AsString());
        if (string.IsNullOrWhiteSpace(macroName))
        {
            return false;
        }

        var macro = FindMacroDefinition(macroName);
        if (macro is null)
        {
            return false;
        }

        if (!_session.Document.Macros.IsTrusted && !macro.IsTrusted)
        {
            return false;
        }

        var args = arguments.Count > 1
            ? arguments.Skip(1).ToArray()
            : Array.Empty<VbaValue>();

        if (_runtime is null || macro.Source is null)
        {
            return false;
        }

        var runResult = _runtime.ExecuteAsync(macro.Source, macro.Name, args).GetAwaiter().GetResult();
        if (!runResult.Success)
        {
            result = VbaValue.FromBoolean(false);
            return true;
        }

        result = runResult.ReturnValue;
        return true;
    }

    private static string ResolveMacroName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        var dotIndex = trimmed.LastIndexOf('.');
        return dotIndex >= 0 ? trimmed[(dotIndex + 1)..] : trimmed;
    }

    private MacroDefinition? FindMacroDefinition(string name)
    {
        foreach (var macro in _session.Document.Macros.Items)
        {
            if (macro.Language == MacroLanguage.Vba
                && string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return macro;
            }
        }

        return null;
    }

    private VbaValue CreateNewDocument()
    {
        var document = _documentFactory();
        DocumentClone.Copy(document, _session.Document);
        _session.RefreshLayout();
        _session.SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(0, 0)));
        _rangeCache.Clear();
        _findStates.Clear();
        return VbaValue.FromObjectPath(ActiveDocumentPath);
    }

    private VbaValue OpenDocument(IReadOnlyList<VbaValue> arguments)
    {
        if (arguments.Count == 0)
        {
            return VbaValue.Empty;
        }

        var path = arguments[0].AsString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return VbaValue.Empty;
        }

        Document? loaded;
        try
        {
            loaded = new DocxImporter().Load(path);
        }
        catch (Exception)
        {
            return VbaValue.Empty;
        }

        DocumentClone.Copy(loaded, _session.Document);
        _session.RefreshLayout();
        _session.SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(0, 0)));
        _rangeCache.Clear();
        _findStates.Clear();
        return VbaValue.FromObjectPath(ActiveDocumentPath);
    }

    private void CollapseSelection(IReadOnlyList<VbaValue> arguments)
    {
        var collapseToEnd = arguments.Count > 0 && arguments[0].AsDouble() != 0d;
        var selection = _session.Selection.Normalize();
        var position = collapseToEnd ? selection.End : selection.Start;
        _session.SetSelection(new TextRange(position, position));
    }

    private void SetSelectionStart(int offset)
    {
        var normalized = _session.Selection.Normalize();
        var start = GetPositionFromDocumentOffset(Math.Max(0, offset));
        var end = normalized.End;
        if (start > end)
        {
            end = start;
        }

        _session.SetSelection(new TextRange(start, end));
    }

    private void SetSelectionEnd(int offset)
    {
        var normalized = _session.Selection.Normalize();
        var end = GetPositionFromDocumentOffset(Math.Max(0, offset));
        var start = normalized.Start;
        if (end < start)
        {
            start = end;
        }

        _session.SetSelection(new TextRange(start, end));
    }

    private void MoveSelection(Action<bool> move, IReadOnlyList<VbaValue> arguments)
    {
        var count = arguments.Count > 0 ? (int)Math.Max(1d, arguments[0].AsDouble()) : 1;
        var extend = arguments.Count > 1 && arguments[1].AsBoolean();
        for (var i = 0; i < count; i++)
        {
            move(extend);
        }
    }

    private string GetSelectionText()
    {
        return _selectionText.TryGetSelectionText(out var text) ? text : string.Empty;
    }

    private void ReplaceSelectionText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (!_session.Selection.IsEmpty)
            {
                _session.Backspace();
            }

            return;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var segments = normalized.Split('\n');
        if (segments.Length == 0)
        {
            return;
        }

        _session.InsertText(segments[0]);
        for (var i = 1; i < segments.Length; i++)
        {
            _session.InsertParagraphBreak();
            if (!string.IsNullOrEmpty(segments[i]))
            {
                _session.InsertText(segments[i]);
            }
        }
    }

    private static int GetNormalizedTextLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var length = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                length++;
                continue;
            }

            length++;
        }

        return length;
    }

    private void UpdateRangeFromOffsets(string rangeId, int startOffset, int endOffset)
    {
        var updatedRange = new TextRange(
            GetPositionFromDocumentOffset(startOffset),
            GetPositionFromDocumentOffset(endOffset));
        UpdateRangeState(rangeId, updatedRange);
    }

    private string BuildDocumentText()
    {
        var builder = new StringBuilder();
        var paragraphCount = _session.Document.ParagraphCount;
        for (var i = 0; i < paragraphCount; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            builder.Append(DocumentEditHelpers.GetParagraphText(paragraph));
            if (i < paragraphCount - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private int CountTables()
    {
        var count = 0;
        foreach (var block in _session.Document.Blocks)
        {
            if (block is TableBlock)
            {
                count++;
            }
        }

        return count;
    }

    private bool TryGetTable(int index, out TableBlock table)
    {
        table = null!;
        if (index <= 0)
        {
            return false;
        }

        var current = 0;
        foreach (var block in _session.Document.Blocks)
        {
            if (block is not TableBlock tableBlock)
            {
                continue;
            }

            current++;
            if (current == index)
            {
                table = tableBlock;
                return true;
            }
        }

        return false;
    }

    private static int CountTableColumns(TableBlock table)
    {
        var max = 0;
        foreach (var row in table.Rows)
        {
            var count = Math.Max(0, row.Properties.GridBefore ?? 0);
            foreach (var cell in row.Cells)
            {
                count += Math.Max(1, cell.ColumnSpan);
            }

            count += Math.Max(0, row.Properties.GridAfter ?? 0);
            if (count > max)
            {
                max = count;
            }
        }

        return max;
    }

    private int CountShapes()
    {
        var count = 0;
        foreach (var block in _session.Document.Blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is ShapeInline)
                    {
                        count++;
                    }
                }

                foreach (var floating in paragraph.FloatingObjects)
                {
                    if (floating.Content is ShapeInline)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private bool TryGetShape(int index, out ShapeInline shape)
    {
        shape = null!;
        if (index <= 0)
        {
            return false;
        }

        var current = 0;
        foreach (var block in _session.Document.Blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is ShapeInline inlineShape)
                    {
                        current++;
                        if (current == index)
                        {
                            shape = inlineShape;
                            return true;
                        }
                    }
                }

                foreach (var floating in paragraph.FloatingObjects)
                {
                    if (floating.Content is ShapeInline floatingShape)
                    {
                        current++;
                        if (current == index)
                        {
                            shape = floatingShape;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static TableBlock CreateTable(int rows, int columns)
    {
        var table = new TableBlock();
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var row = new TableRow();
            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                var cell = new TableCell();
                cell.Paragraphs.Add(new ParagraphBlock());
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static ShapeInline CreateDefaultShape(float width = 160f, float height = 120f)
    {
        var properties = new ShapeProperties
        {
            PresetGeometry = "rect"
        };
        return new ShapeInline(width, height, properties, null, "Shape");
    }

    private static bool TryParseIndexed(string path, string prefix, out int index, out string member)
    {
        index = 0;
        member = string.Empty;

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = path.Substring(prefix.Length);
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var dotIndex = remainder.IndexOf('.');
        var indexText = dotIndex >= 0 ? remainder[..dotIndex] : remainder;
        if (!int.TryParse(indexText, out index) || index <= 0)
        {
            return false;
        }

        member = dotIndex >= 0 ? remainder[(dotIndex + 1)..] : string.Empty;
        return true;
    }

    private static string StripApplicationPrefix(string name)
    {
        const string prefix = "Application.";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name.Substring(prefix.Length)
            : name;
    }

    private static bool EqualsIgnoreCase(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FindState
    {
        public string? Text { get; set; }
        public bool Forward { get; set; } = true;
        public bool MatchCase { get; set; }
        public bool MatchWholeWord { get; set; }
        public bool MatchWildcards { get; set; }
        public bool MatchAllWordForms { get; set; }
        public int WrapMode { get; set; } = WdFindWrapContinue;
        public bool HasScope { get; set; }
        public int ScopeStart { get; set; }
        public int ScopeEnd { get; set; }
        public bool HasLastMatch { get; set; }
        public int LastMatchStart { get; set; }
        public int LastMatchEnd { get; set; }
    }
}
