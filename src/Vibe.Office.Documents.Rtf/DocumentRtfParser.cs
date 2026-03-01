using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public static class DocumentRtfParser
{
    private static int s_codePagesProviderInitialized;

    public static bool TryParse(string rtf, out Document document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(rtf))
        {
            return false;
        }

        try
        {
            var parser = new RtfParser(rtf);
            document = parser.Parse();
            return true;
        }
        catch
        {
            document = CreateEmptyDocument();
            return false;
        }
    }

    private static Document CreateEmptyDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(
            document.SectionProperties,
            document.Header,
            document.Footer,
            document.FirstHeader,
            document.FirstFooter,
            document.EvenHeader,
            document.EvenFooter));
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);
        return document;
    }

    private sealed class RtfParser
    {
        private readonly string _rtf;
        private readonly Dictionary<int, string> _fonts;
        private readonly List<DocColor?> _colors;
        private readonly Dictionary<int, ListDefinition> _listDefinitions;
        private readonly Dictionary<int, int> _listOverrides;
        private readonly List<Block> _blocks = new();
        private readonly Stack<ParserState> _stateStack = new();

        private ParserState _state = new();
        private ParagraphBlock? _paragraph;
        private StringBuilder? _runText;
        private CharacterStyle _runStyle = new();
        private TableBlock? _activeTable;
        private TableRow? _activeRow;
        private TableCell? _activeCell;
        private List<int>? _activeRowCellBoundsTwips;
        private List<TableCellDefinition>? _activeRowCellDefinitions;
        private int _activeRowCellIndex;
        private TableRowProperties _pendingRowProperties = new();
        private readonly PendingPaddingState _pendingRowPadding = new();
        private int? _pendingRowCellSpacingTwips;
        private float? _pendingRowIndent;
        private TableAlignment? _pendingTableAlignment;
        private TableLayoutMode? _pendingTableLayoutMode;
        private int? _pendingTableWidthRaw;
        private int? _pendingTableWidthUnitCode;
        private readonly TableBorders _pendingTableBorders = new();
        private readonly PendingCellState _pendingCellState = new();
        private BorderTarget _pendingBorderTarget;
        private BorderSide _pendingBorderSide = BorderSide.Top;
        private SectionProperties _parsedSectionProperties = new();
        private int _currentSectionColumnDefinitionIndex = -1;
        private bool? _parsedMirrorMargins;
        private bool? _parsedFacingPages;
        private bool? _parsedGutterAtTop;
        private SectionBreakType _pendingSectionBreakType = SectionBreakType.NextPage;
        private readonly HeaderFooter _parsedHeader = new();
        private readonly HeaderFooter _parsedFooter = new();
        private readonly HeaderFooter _parsedFirstHeader = new();
        private readonly HeaderFooter _parsedFirstFooter = new();
        private readonly HeaderFooter _parsedEvenHeader = new();
        private readonly HeaderFooter _parsedEvenFooter = new();

        private int _pos;
        private int _ansiCodePage = 1252;
        private Encoding? _ansiEncoding;
        private int _ansiEncodingCodePage;
        private int _defaultFontIndex;
        private int _unicodeSkip = 1;
        private int _pendingUnicodeSkip;

        public RtfParser(string rtf)
        {
            _rtf = rtf;
            _fonts = ParseFontTable(rtf);
            _colors = ParseColorTable(rtf);
            _listDefinitions = ParseListTable(rtf);
            _listOverrides = ParseListOverrideTable(rtf);
            _defaultFontIndex = 0;
            _state.Character.FontIndex = _defaultFontIndex;
        }

        public Document Parse()
        {
            while (_pos < _rtf.Length)
            {
                if (_pendingUnicodeSkip > 0 && SkipUnicodeFallback())
                {
                    continue;
                }

                var ch = _rtf[_pos];
                if (ch == '{')
                {
                    _stateStack.Push(_state.Clone());
                    _pos++;
                    continue;
                }

                if (ch == '}')
                {
                    PopGroup();
                    _pos++;
                    continue;
                }

                if (ch == '\\')
                {
                    _pos++;
                    ParseControl();
                    continue;
                }

                if (!_state.SkipDestination && ch != '\r' && ch != '\n')
                {
                    AppendPlainTextCharacter(ch);
                }

                _pos++;
            }

            FlushRun();
            FinalizeOpenTableStructures();
            PromoteCurrentParagraphToList();

            var document = CreateEmptyDocument();
            foreach (var block in _blocks)
            {
                document.Blocks.Add(block);
            }

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new ParagraphBlock());
            }

            if (_fonts.TryGetValue(_defaultFontIndex, out var defaultFont)
                && !string.IsNullOrWhiteSpace(defaultFont))
            {
                document.DefaultTextStyle.FontFamily = defaultFont;
            }

            foreach (var pair in _listDefinitions)
            {
                document.ListDefinitions[pair.Key] = pair.Value.Clone();
            }

            ApplyParsedSectionProperties(document);
            ApplyParsedHeaderFooters(document);

            return document;
        }

        private void PopGroup()
        {
            if (_stateStack.Count == 0)
            {
                return;
            }

            FlushRun();
            var completed = _state;
            var restored = _stateStack.Pop();
            MergeFieldState(completed, restored);
            _state = restored;
            if (!_state.Paragraph.InTable)
            {
                _activeCell = null;
            }
        }

        private void ParseControl()
        {
            if (_pos >= _rtf.Length)
            {
                return;
            }

            var ch = _rtf[_pos];
            if (ch == '\\' || ch == '{' || ch == '}')
            {
                if (!_state.SkipDestination)
                {
                    AppendText(ch);
                }
                _pos++;
                return;
            }

            if (ch == '*')
            {
                _state.IgnoreNextDestination = true;
                _pos++;
                return;
            }

            if (ch == '\'')
            {
                ParseHexEscapedCharacter();
                return;
            }

            if (!char.IsLetter(ch))
            {
                _pos++;
                if (!_state.SkipDestination)
                {
                    switch (ch)
                    {
                        case '~':
                            AppendText('\u00A0');
                            break;
                        case '_':
                            AppendText('\u2011');
                            break;
                        case '-':
                            AppendText('\u00AD');
                            break;
                    }
                }

                return;
            }

            var word = ReadControlWord(out var param, out var hasParam);
            if (_state.IgnoreNextDestination)
            {
                _state.IgnoreNextDestination = false;
                if (!IsKnownDestinationWord(word))
                {
                    _state.SkipDestination = true;
                    return;
                }
            }

            if (IsSkippedDestination(word))
            {
                _state.SkipDestination = true;
                return;
            }

            if (_state.SkipDestination)
            {
                return;
            }

            switch (word)
            {
                case "field":
                    StartField();
                    break;
                case "pict":
                    ParsePictureDestination();
                    break;
                case "header":
                case "footer":
                case "headerl":
                case "headerr":
                case "headerf":
                case "footerl":
                case "footerr":
                case "footerf":
                    ParseHeaderFooterDestination(word);
                    break;
                case "fldinst":
                    BeginFieldInstruction();
                    break;
                case "fldrslt":
                    BeginFieldResult();
                    break;
                case "listtext":
                case "pntext":
                    if (_state.Paragraph.ListOverrideIndex.HasValue)
                    {
                        _state.SkipDestination = true;
                        return;
                    }

                    break;
                case "ansi":
                    _ansiCodePage = 1252;
                    _ansiEncoding = null;
                    break;
                case "ansicpg":
                    if (hasParam)
                    {
                        _ansiCodePage = Math.Max(1, param);
                        _ansiEncoding = null;
                    }
                    break;
                case "deff":
                    if (hasParam)
                    {
                        _defaultFontIndex = Math.Max(0, param);
                        SetFont(_defaultFontIndex);
                    }
                    break;
                case "uc":
                    if (hasParam)
                    {
                        _unicodeSkip = Math.Max(0, param);
                    }
                    break;
                case "u":
                    if (hasParam)
                    {
                        var codePoint = param < 0 ? param + 65536 : param;
                        AppendText(char.ConvertFromUtf32(codePoint));
                        _pendingUnicodeSkip = _unicodeSkip;
                    }
                    break;
                case "par":
                    EndParagraph();
                    break;
                case "page":
                    AddBlockBreak(new PageBreakBlock());
                    break;
                case "column":
                    AddBlockBreak(new ColumnBreakBlock());
                    break;
                case "sectd":
                    _parsedSectionProperties = new SectionProperties();
                    _currentSectionColumnDefinitionIndex = -1;
                    break;
                case "sect":
                    AddSectionBreak();
                    break;
                case "sbknone":
                    _pendingSectionBreakType = SectionBreakType.Continuous;
                    break;
                case "sbkpage":
                    _pendingSectionBreakType = SectionBreakType.NextPage;
                    break;
                case "sbkeven":
                    _pendingSectionBreakType = SectionBreakType.EvenPage;
                    break;
                case "sbkodd":
                    _pendingSectionBreakType = SectionBreakType.OddPage;
                    break;
                case "sbkcol":
                    _pendingSectionBreakType = SectionBreakType.NextColumn;
                    break;
                case "paperw":
                case "pgwsxn":
                    if (hasParam && param > 0)
                    {
                        _parsedSectionProperties.PageWidth = TwipsToDip(param);
                    }
                    break;
                case "paperh":
                case "pghsxn":
                    if (hasParam && param > 0)
                    {
                        _parsedSectionProperties.PageHeight = TwipsToDip(param);
                    }
                    break;
                case "landscape":
                case "lndscpsxn":
                    _parsedSectionProperties.Orientation = !hasParam || param != 0
                        ? PageOrientation.Landscape
                        : PageOrientation.Portrait;
                    break;
                case "portrait":
                    _parsedSectionProperties.Orientation = PageOrientation.Portrait;
                    break;
                case "margl":
                case "marglsxn":
                    if (hasParam && param >= 0)
                    {
                        _parsedSectionProperties.MarginLeft = TwipsToDip(param);
                    }
                    break;
                case "margr":
                case "margrsxn":
                    if (hasParam && param >= 0)
                    {
                        _parsedSectionProperties.MarginRight = TwipsToDip(param);
                    }
                    break;
                case "margt":
                case "margtsxn":
                    if (hasParam && param >= 0)
                    {
                        _parsedSectionProperties.MarginTop = TwipsToDip(param);
                    }
                    break;
                case "margb":
                case "margbsxn":
                    if (hasParam && param >= 0)
                    {
                        _parsedSectionProperties.MarginBottom = TwipsToDip(param);
                    }
                    break;
                case "headery":
                    if (hasParam)
                    {
                        _parsedSectionProperties.HeaderOffset = TwipsToDip(param);
                    }
                    break;
                case "footery":
                    if (hasParam)
                    {
                        _parsedSectionProperties.FooterOffset = TwipsToDip(param);
                    }
                    break;
                case "gutter":
                case "guttersxn":
                    if (hasParam)
                    {
                        _parsedSectionProperties.Gutter = TwipsToDip(param);
                    }
                    break;
                case "titlepg":
                    _parsedSectionProperties.DifferentFirstPageHeaderFooter = !hasParam || param != 0;
                    break;
                case "cols":
                    if (hasParam)
                    {
                        _parsedSectionProperties.ColumnCount = Math.Max(1, param);
                        _parsedSectionProperties.ColumnEqualWidth ??= true;
                    }
                    break;
                case "colsx":
                    if (hasParam && param >= 0)
                    {
                        _parsedSectionProperties.ColumnGap = TwipsToDip(param);
                    }
                    break;
                case "linebetcol":
                    _parsedSectionProperties.ColumnSeparator = !hasParam || param != 0;
                    break;
                case "colno":
                    if (hasParam)
                    {
                        _currentSectionColumnDefinitionIndex = Math.Max(0, param);
                    }
                    break;
                case "colw":
                    if (hasParam && param > 0)
                    {
                        _parsedSectionProperties.ColumnEqualWidth = false;
                        SetSectionColumnWidth(TwipsToDip(param));
                    }
                    break;
                case "colsr":
                    if (hasParam && param >= 0)
                    {
                        _parsedSectionProperties.ColumnEqualWidth = false;
                        SetSectionColumnGap(TwipsToDip(param));
                    }
                    break;
                case "margmirror":
                case "margmirsxn":
                    _parsedMirrorMargins = !hasParam || param != 0;
                    break;
                case "facingp":
                    _parsedFacingPages = !hasParam || param != 0;
                    break;
                case "gutterprl":
                    _parsedGutterAtTop = !hasParam || param != 0;
                    break;
                case "line":
                    AppendText('\n');
                    break;
                case "tab":
                    AppendText('\t');
                    break;
                case "plain":
                    ResetCharacterStyle();
                    break;
                case "pard":
                    ResetParagraphState();
                    break;
                case "b":
                    SetBold(!hasParam || param != 0);
                    break;
                case "i":
                    SetItalic(!hasParam || param != 0);
                    break;
                case "ul":
                    SetUnderline(!hasParam || param != 0);
                    break;
                case "ulnone":
                    SetUnderline(false);
                    break;
                case "strike":
                    SetStrikethrough(!hasParam || param != 0);
                    break;
                case "fs":
                    if (hasParam)
                    {
                        SetFontSize(param);
                    }
                    break;
                case "f":
                    if (hasParam)
                    {
                        SetFont(param);
                    }
                    break;
                case "cf":
                    SetColor(hasParam ? param : 0);
                    break;
                case "highlight":
                    SetHighlight(hasParam ? param : 0);
                    break;
                case "super":
                    SetVerticalPosition(DocVerticalPosition.Superscript);
                    break;
                case "sub":
                    SetVerticalPosition(DocVerticalPosition.Subscript);
                    break;
                case "nosupersub":
                    SetVerticalPosition(DocVerticalPosition.Normal);
                    break;
                case "ql":
                    SetParagraphAlignment(ParagraphAlignment.Left);
                    break;
                case "qc":
                    SetParagraphAlignment(ParagraphAlignment.Center);
                    break;
                case "qr":
                    SetParagraphAlignment(ParagraphAlignment.Right);
                    break;
                case "qj":
                    SetParagraphAlignment(ParagraphAlignment.Justify);
                    break;
                case "li":
                    if (hasParam)
                    {
                        SetParagraphLeftIndent(TwipsToDip(param));
                    }
                    break;
                case "ri":
                    if (hasParam)
                    {
                        SetParagraphRightIndent(TwipsToDip(param));
                    }
                    break;
                case "fi":
                    if (hasParam)
                    {
                        SetParagraphFirstLineIndent(TwipsToDip(param));
                    }
                    break;
                case "sb":
                    if (hasParam)
                    {
                        SetParagraphSpacingBefore(TwipsToDip(param));
                    }
                    break;
                case "sa":
                    if (hasParam)
                    {
                        SetParagraphSpacingAfter(TwipsToDip(param));
                    }
                    break;
                case "sl":
                    if (hasParam)
                    {
                        SetParagraphLineSpacing(param);
                    }
                    break;
                case "slmult":
                    if (hasParam)
                    {
                        _state.Paragraph.LineSpacingRule = param == 1 ? DocLineSpacingRule.Auto : DocLineSpacingRule.AtLeast;
                        ApplyParagraphStateToCurrentParagraph();
                    }
                    break;
                case "tx":
                    if (hasParam)
                    {
                        _state.Paragraph.TabStops.Add(new TabStopDefinition(TwipsToDip(param)));
                        ApplyParagraphStateToCurrentParagraph();
                    }
                    break;
                case "ls":
                    if (hasParam)
                    {
                        _state.Paragraph.ListOverrideIndex = Math.Max(0, param);
                        ApplyParagraphStateToCurrentParagraph();
                    }
                    break;
                case "ilvl":
                    if (hasParam)
                    {
                        _state.Paragraph.ListLevel = Math.Max(0, param);
                        ApplyParagraphStateToCurrentParagraph();
                    }
                    break;
                case "intbl":
                    _state.Paragraph.InTable = true;
                    break;
                case "trowd":
                    StartTableRow();
                    break;
                case "trql":
                    _pendingTableAlignment = TableAlignment.Left;
                    break;
                case "trqc":
                    _pendingTableAlignment = TableAlignment.Center;
                    break;
                case "trqr":
                    _pendingTableAlignment = TableAlignment.Right;
                    break;
                case "trautofit":
                    if (!hasParam || param != 0)
                    {
                        _pendingTableLayoutMode = TableLayoutMode.Auto;
                    }
                    else
                    {
                        _pendingTableLayoutMode = TableLayoutMode.Fixed;
                    }
                    break;
                case "trwWidth":
                    if (hasParam)
                    {
                        _pendingTableWidthRaw = param;
                    }
                    break;
                case "trftsWidth":
                    if (hasParam)
                    {
                        _pendingTableWidthUnitCode = param;
                    }
                    break;
                case "trrh":
                    if (hasParam)
                    {
                        SetTableRowHeight(param);
                    }
                    break;
                case "trhdr":
                    SetTableRowHeader(!hasParam || param != 0);
                    break;
                case "trkeep":
                    SetTableRowCantSplit(!hasParam || param != 0);
                    break;
                case "trleft":
                    if (hasParam)
                    {
                        _pendingRowIndent = TwipsToDip(param);
                    }
                    break;
                case "tblind":
                    if (hasParam)
                    {
                        _pendingRowIndent = TwipsToDip(param);
                    }
                    break;
                case "trgaph":
                    if (hasParam)
                    {
                        _pendingRowCellSpacingTwips = Math.Max(0, param);
                    }
                    break;
                case "trbrdrt":
                    BeginTableBorder(BorderSide.Top);
                    break;
                case "trbrdrl":
                    BeginTableBorder(BorderSide.Left);
                    break;
                case "trbrdrb":
                    BeginTableBorder(BorderSide.Bottom);
                    break;
                case "trbrdrr":
                    BeginTableBorder(BorderSide.Right);
                    break;
                case "trbrdrh":
                    BeginTableBorder(BorderSide.InsideHorizontal);
                    break;
                case "trbrdrv":
                    BeginTableBorder(BorderSide.InsideVertical);
                    break;
                case "trcbpat":
                    if (hasParam && TryGetColorByIndex(param, out var rowShading))
                    {
                        _pendingRowProperties.ShadingColor = rowShading;
                    }
                    break;
                case "trpaddl":
                    if (hasParam)
                    {
                        _pendingRowPadding.Left = param;
                    }
                    break;
                case "trpaddr":
                    if (hasParam)
                    {
                        _pendingRowPadding.Right = param;
                    }
                    break;
                case "trpaddt":
                    if (hasParam)
                    {
                        _pendingRowPadding.Top = param;
                    }
                    break;
                case "trpaddb":
                    if (hasParam)
                    {
                        _pendingRowPadding.Bottom = param;
                    }
                    break;
                case "trpaddfl":
                    if (hasParam)
                    {
                        _pendingRowPadding.LeftUnit = param;
                    }
                    break;
                case "trpaddfr":
                    if (hasParam)
                    {
                        _pendingRowPadding.RightUnit = param;
                    }
                    break;
                case "trpaddft":
                    if (hasParam)
                    {
                        _pendingRowPadding.TopUnit = param;
                    }
                    break;
                case "trpaddfb":
                    if (hasParam)
                    {
                        _pendingRowPadding.BottomUnit = param;
                    }
                    break;
                case "clcbpat":
                case "clcfpat":
                    if (hasParam && TryGetColorByIndex(param, out var cellShading))
                    {
                        _pendingCellState.Properties.ShadingColor = cellShading;
                    }
                    break;
                case "clvertalt":
                    _pendingCellState.Properties.VerticalAlignment = TableCellVerticalAlignment.Top;
                    break;
                case "clvertalc":
                    _pendingCellState.Properties.VerticalAlignment = TableCellVerticalAlignment.Center;
                    break;
                case "clvertalb":
                    _pendingCellState.Properties.VerticalAlignment = TableCellVerticalAlignment.Bottom;
                    break;
                case "clpadl":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.Left = param;
                    }
                    break;
                case "clpadr":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.Right = param;
                    }
                    break;
                case "clpadt":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.Top = param;
                    }
                    break;
                case "clpadb":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.Bottom = param;
                    }
                    break;
                case "clpadfl":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.LeftUnit = param;
                    }
                    break;
                case "clpadfr":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.RightUnit = param;
                    }
                    break;
                case "clpadft":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.TopUnit = param;
                    }
                    break;
                case "clpadfb":
                    if (hasParam)
                    {
                        _pendingCellState.Padding.BottomUnit = param;
                    }
                    break;
                case "clwWidth":
                    if (hasParam)
                    {
                        _pendingCellState.PreferredWidth = param;
                    }
                    break;
                case "clftsWidth":
                    if (hasParam)
                    {
                        _pendingCellState.PreferredWidthUnitCode = param;
                    }
                    break;
                case "clvmgf":
                    _pendingCellState.VerticalMerge = TableCellVerticalMerge.Restart;
                    break;
                case "clvmrg":
                    _pendingCellState.VerticalMerge = TableCellVerticalMerge.Continue;
                    break;
                case "clbrdrt":
                    BeginCellBorder(BorderSide.Top);
                    break;
                case "clbrdrl":
                    BeginCellBorder(BorderSide.Left);
                    break;
                case "clbrdrb":
                    BeginCellBorder(BorderSide.Bottom);
                    break;
                case "clbrdrr":
                    BeginCellBorder(BorderSide.Right);
                    break;
                case "brdrnone":
                    SetPendingBorderStyle(DocBorderStyle.None);
                    break;
                case "brdrs":
                    SetPendingBorderStyle(DocBorderStyle.Single);
                    break;
                case "brdrth":
                    SetPendingBorderStyle(DocBorderStyle.Thick);
                    break;
                case "brdrdb":
                    SetPendingBorderStyle(DocBorderStyle.Double);
                    break;
                case "brdrdot":
                    SetPendingBorderStyle(DocBorderStyle.Dotted);
                    break;
                case "brdrdash":
                    SetPendingBorderStyle(DocBorderStyle.Dashed);
                    break;
                case "brdrdashd":
                    SetPendingBorderStyle(DocBorderStyle.DotDash);
                    break;
                case "brdrdashdd":
                    SetPendingBorderStyle(DocBorderStyle.DotDotDash);
                    break;
                case "brdrhair":
                    SetPendingBorderStyle(DocBorderStyle.Hairline);
                    break;
                case "brdrtriple":
                    SetPendingBorderStyle(DocBorderStyle.Triple);
                    break;
                case "brdrthtnsg":
                    SetPendingBorderStyle(DocBorderStyle.ThickThin);
                    break;
                case "brdrtnthsg":
                    SetPendingBorderStyle(DocBorderStyle.ThinThick);
                    break;
                case "brdrtnthtnsg":
                    SetPendingBorderStyle(DocBorderStyle.ThinThickThin);
                    break;
                case "brdrw":
                    if (hasParam)
                    {
                        SetPendingBorderWidth(param);
                    }
                    break;
                case "brdrcf":
                    if (hasParam)
                    {
                        SetPendingBorderColor(param);
                    }
                    break;
                case "cellx":
                    if (hasParam)
                    {
                        AddTableCellBoundary(param);
                    }
                    break;
                case "cell":
                case "nestcell":
                    EndTableCell();
                    break;
                case "row":
                case "nestrow":
                    EndTableRow();
                    break;
                case "emdash":
                    AppendText('\u2014');
                    break;
                case "endash":
                    AppendText('\u2013');
                    break;
                case "lquote":
                    AppendText('\u2018');
                    break;
                case "rquote":
                    AppendText('\u2019');
                    break;
                case "ldblquote":
                    AppendText('\u201C');
                    break;
                case "rdblquote":
                    AppendText('\u201D');
                    break;
                case "bullet":
                    AppendText('\u2022');
                    break;
                case "bin":
                    if (hasParam && param > 0)
                    {
                        _pos = Math.Min(_rtf.Length, _pos + param);
                    }
                    break;
            }
        }

        private string ReadControlWord(out int param, out bool hasParam)
        {
            param = 0;
            hasParam = false;

            var start = _pos;
            while (_pos < _rtf.Length && char.IsLetter(_rtf[_pos]))
            {
                _pos++;
            }

            var word = _rtf.Substring(start, _pos - start);
            if (_pos < _rtf.Length && (_rtf[_pos] == '-' || char.IsDigit(_rtf[_pos])))
            {
                var sign = 1;
                if (_rtf[_pos] == '-')
                {
                    sign = -1;
                    _pos++;
                }

                var valueStart = _pos;
                while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                {
                    _pos++;
                }

                if (int.TryParse(_rtf.Substring(valueStart, _pos - valueStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    param = value * sign;
                    hasParam = true;
                }
            }

            if (_pos < _rtf.Length && _rtf[_pos] == ' ')
            {
                _pos++;
            }

            return word;
        }

        private void ParseHexEscapedCharacter()
        {
            if (_pos + 2 < _rtf.Length)
            {
                var hex = _rtf.Substring(_pos + 1, 2);
                if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                {
                    if (!_state.SkipDestination)
                    {
                        AppendText(DecodeAnsiByte(value));
                    }
                }
            }

            _pos += 3;
        }

        private string DecodeAnsiByte(byte value)
        {
            if (_ansiCodePage == 1252)
            {
                return DecodeWindows1252Byte(value).ToString();
            }

            if (_ansiEncoding is null || _ansiEncodingCodePage != _ansiCodePage)
            {
                EnsureCodePagesProviderRegistered();
                try
                {
                    _ansiEncoding = Encoding.GetEncoding(_ansiCodePage);
                    _ansiEncodingCodePage = _ansiCodePage;
                }
                catch
                {
                    _ansiEncoding = Encoding.Latin1;
                    _ansiEncodingCodePage = _ansiCodePage;
                }
            }

            return _ansiEncoding.GetString([value]);
        }

        private static void EnsureCodePagesProviderRegistered()
        {
            if (Interlocked.CompareExchange(ref s_codePagesProviderInitialized, 1, 0) != 0)
            {
                return;
            }

            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
                // Ignore: parser falls back to Latin-1 when code pages are unavailable.
            }
        }

        private static char DecodeWindows1252Byte(byte value)
        {
            return value switch
            {
                0x80 => '\u20AC',
                0x82 => '\u201A',
                0x83 => '\u0192',
                0x84 => '\u201E',
                0x85 => '\u2026',
                0x86 => '\u2020',
                0x87 => '\u2021',
                0x88 => '\u02C6',
                0x89 => '\u2030',
                0x8A => '\u0160',
                0x8B => '\u2039',
                0x8C => '\u0152',
                0x8E => '\u017D',
                0x91 => '\u2018',
                0x92 => '\u2019',
                0x93 => '\u201C',
                0x94 => '\u201D',
                0x95 => '\u2022',
                0x96 => '\u2013',
                0x97 => '\u2014',
                0x98 => '\u02DC',
                0x99 => '\u2122',
                0x9A => '\u0161',
                0x9B => '\u203A',
                0x9C => '\u0153',
                0x9E => '\u017E',
                0x9F => '\u0178',
                _ => (char)value
            };
        }

        private void SetBold(bool value)
        {
            if (_state.Character.Bold == value)
            {
                return;
            }

            FlushRun();
            _state.Character.Bold = value;
        }

        private void SetItalic(bool value)
        {
            if (_state.Character.Italic == value)
            {
                return;
            }

            FlushRun();
            _state.Character.Italic = value;
        }

        private void SetUnderline(bool value)
        {
            if (_state.Character.Underline == value)
            {
                return;
            }

            FlushRun();
            _state.Character.Underline = value;
        }

        private void SetStrikethrough(bool value)
        {
            if (_state.Character.Strikethrough == value)
            {
                return;
            }

            FlushRun();
            _state.Character.Strikethrough = value;
        }

        private void SetFontSize(int halfPoints)
        {
            var dip = HalfPointsToDip(halfPoints);
            if (_state.Character.FontSize.HasValue && Math.Abs(_state.Character.FontSize.Value - dip) < 0.01f)
            {
                return;
            }

            FlushRun();
            _state.Character.FontSize = dip;
        }

        private void SetFont(int index)
        {
            if (_state.Character.FontIndex == index)
            {
                return;
            }

            FlushRun();
            _state.Character.FontIndex = index;
        }

        private void SetColor(int index)
        {
            int? value = index > 0 ? index : null;
            if (_state.Character.ColorIndex == value)
            {
                return;
            }

            FlushRun();
            _state.Character.ColorIndex = value;
        }

        private void SetHighlight(int index)
        {
            int? value = index > 0 ? index : null;
            if (_state.Character.HighlightIndex == value)
            {
                return;
            }

            FlushRun();
            _state.Character.HighlightIndex = value;
        }

        private void SetVerticalPosition(DocVerticalPosition position)
        {
            if (_state.Character.VerticalPosition == position)
            {
                return;
            }

            FlushRun();
            _state.Character.VerticalPosition = position;
        }

        private void SetHyperlink(HyperlinkInfo? hyperlink)
        {
            if (Equals(_state.Character.Hyperlink, hyperlink))
            {
                return;
            }

            FlushRun();
            _state.Character.Hyperlink = hyperlink;
        }

        private void StartField()
        {
            _state.Field = new FieldState();
        }

        private void BeginFieldInstruction()
        {
            _state.Field ??= new FieldState();
            _state.Field.Instruction = string.Empty;
            _state.Field.Hyperlink = null;
            _state.Field.InInstruction = true;
            _state.Field.InResult = false;
            SetHyperlink(null);
        }

        private void BeginFieldResult()
        {
            if (_state.Field is null)
            {
                return;
            }

            _state.Field.InInstruction = false;
            _state.Field.InResult = true;
            _state.Field.Hyperlink ??= ResolveHyperlinkFromFieldInstruction(_state.Field.Instruction);
            SetHyperlink(_state.Field.Hyperlink);
        }

        private static HyperlinkInfo? ResolveHyperlinkFromFieldInstruction(string? instruction)
        {
            var definition = FieldInstructionParser.Parse(instruction);
            if (definition is null || definition.Kind != FieldKind.Hyperlink)
            {
                return null;
            }

            var uri = definition.Arguments.Count > 0 ? NormalizeHyperlinkComponent(definition.Arguments[0].Value) : null;
            string? anchor = null;
            string? tooltip = null;
            for (var i = 0; i < definition.Switches.Count; i++)
            {
                var fieldSwitch = definition.Switches[i];
                if (fieldSwitch.Name.Equals(@"\l", StringComparison.OrdinalIgnoreCase))
                {
                    anchor = NormalizeHyperlinkComponent(fieldSwitch.Value);
                }
                else if (fieldSwitch.Name.Equals(@"\o", StringComparison.OrdinalIgnoreCase))
                {
                    tooltip = NormalizeHyperlinkComponent(fieldSwitch.Value);
                }
            }

            if (uri is not null && uri.StartsWith('#'))
            {
                var inlineAnchor = NormalizeHyperlinkComponent(uri[1..]);
                if (anchor is null)
                {
                    anchor = inlineAnchor;
                    uri = null;
                }
            }

            if (uri is null && anchor is null)
            {
                return null;
            }

            return new HyperlinkInfo(uri, anchor, tooltip);
        }

        private static string? NormalizeHyperlinkComponent(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private void ResetCharacterStyle()
        {
            FlushRun();
            _state.Character = new CharacterStyle
            {
                FontIndex = _defaultFontIndex
            };
        }

        private void SetSectionColumnWidth(float width)
        {
            if (width <= 0f)
            {
                return;
            }

            var index = _currentSectionColumnDefinitionIndex >= 0
                ? _currentSectionColumnDefinitionIndex
                : _parsedSectionProperties.ColumnWidths.Count;
            EnsureFloatListSize(_parsedSectionProperties.ColumnWidths, index + 1);
            _parsedSectionProperties.ColumnWidths[index] = width;
        }

        private void SetSectionColumnGap(float gap)
        {
            if (gap < 0f)
            {
                return;
            }

            var index = _currentSectionColumnDefinitionIndex >= 0
                ? _currentSectionColumnDefinitionIndex
                : Math.Max(0, _parsedSectionProperties.ColumnWidths.Count - 1);
            EnsureGapListSize(_parsedSectionProperties.ColumnGaps, index + 1);
            _parsedSectionProperties.ColumnGaps[index] = gap;
        }

        private static void EnsureFloatListSize(List<float> values, int size)
        {
            while (values.Count < size)
            {
                values.Add(0f);
            }
        }

        private static void EnsureGapListSize(List<float> values, int size)
        {
            while (values.Count < size)
            {
                values.Add(float.NaN);
            }
        }

        private static void TrimTrailingWidthPlaceholders(List<float> widths)
        {
            for (var index = widths.Count - 1; index >= 0; index--)
            {
                if (widths[index] > 0f)
                {
                    return;
                }

                widths.RemoveAt(index);
            }
        }

        private static void TrimTrailingGapPlaceholders(List<float> gaps)
        {
            for (var index = gaps.Count - 1; index >= 0; index--)
            {
                if (!float.IsNaN(gaps[index]))
                {
                    return;
                }

                gaps.RemoveAt(index);
            }
        }

        private void ApplyParsedSectionProperties(Document document)
        {
            var source = _parsedSectionProperties;
            TrimTrailingWidthPlaceholders(source.ColumnWidths);
            TrimTrailingGapPlaceholders(source.ColumnGaps);

            if (!source.ColumnCount.HasValue)
            {
                if (source.ColumnWidths.Count > 0)
                {
                    source.ColumnCount = source.ColumnWidths.Count;
                }
                else if (source.ColumnGaps.Count > 0)
                {
                    source.ColumnCount = source.ColumnGaps.Count + 1;
                }
            }

            var target = document.SectionProperties;
            if (source.PageWidth.HasValue)
            {
                target.PageWidth = source.PageWidth.Value;
            }

            if (source.PageHeight.HasValue)
            {
                target.PageHeight = source.PageHeight.Value;
            }

            if (source.Orientation.HasValue)
            {
                target.Orientation = source.Orientation.Value;
            }

            if (source.MarginLeft.HasValue)
            {
                target.MarginLeft = source.MarginLeft.Value;
            }

            if (source.MarginRight.HasValue)
            {
                target.MarginRight = source.MarginRight.Value;
            }

            if (source.MarginTop.HasValue)
            {
                target.MarginTop = source.MarginTop.Value;
            }

            if (source.MarginBottom.HasValue)
            {
                target.MarginBottom = source.MarginBottom.Value;
            }

            if (source.HeaderOffset.HasValue)
            {
                target.HeaderOffset = source.HeaderOffset.Value;
            }

            if (source.FooterOffset.HasValue)
            {
                target.FooterOffset = source.FooterOffset.Value;
            }

            if (source.Gutter.HasValue)
            {
                target.Gutter = source.Gutter.Value;
            }

            if (source.DifferentFirstPageHeaderFooter.HasValue)
            {
                target.DifferentFirstPageHeaderFooter = source.DifferentFirstPageHeaderFooter.Value;
            }

            if (source.ColumnCount.HasValue)
            {
                target.ColumnCount = source.ColumnCount.Value;
            }

            if (source.ColumnGap.HasValue)
            {
                target.ColumnGap = source.ColumnGap.Value;
            }

            if (source.ColumnEqualWidth.HasValue)
            {
                target.ColumnEqualWidth = source.ColumnEqualWidth.Value;
            }

            if (source.ColumnSeparator.HasValue)
            {
                target.ColumnSeparator = source.ColumnSeparator.Value;
            }

            if (source.ColumnWidths.Count > 0)
            {
                target.ColumnWidths.Clear();
                target.ColumnWidths.AddRange(source.ColumnWidths);
            }

            if (source.ColumnGaps.Count > 0)
            {
                target.ColumnGaps.Clear();
                target.ColumnGaps.AddRange(source.ColumnGaps);
            }

            if (_parsedMirrorMargins.HasValue)
            {
                document.MirrorMargins = _parsedMirrorMargins.Value;
            }

            if (_parsedFacingPages.HasValue)
            {
                document.EvenAndOddHeaders = _parsedFacingPages.Value;
            }

            if (_parsedGutterAtTop.HasValue)
            {
                document.GutterAtTop = _parsedGutterAtTop.Value;
            }
        }

        private void ApplyParsedHeaderFooters(Document document)
        {
            ApplyParsedHeaderFooter(document.Header, _parsedHeader);
            ApplyParsedHeaderFooter(document.Footer, _parsedFooter);
            ApplyParsedHeaderFooter(document.FirstHeader, _parsedFirstHeader);
            ApplyParsedHeaderFooter(document.FirstFooter, _parsedFirstFooter);
            ApplyParsedHeaderFooter(document.EvenHeader, _parsedEvenHeader);
            ApplyParsedHeaderFooter(document.EvenFooter, _parsedEvenFooter);
        }

        private static void ApplyParsedHeaderFooter(HeaderFooter target, HeaderFooter source)
        {
            if (!source.IsDefined)
            {
                return;
            }

            target.IsDefined = true;
            target.Blocks.Clear();
            for (var i = 0; i < source.Blocks.Count; i++)
            {
                target.Blocks.Add(source.Blocks[i]);
            }
        }

        private void ResetParagraphState()
        {
            _state.Paragraph = new ParagraphState();
            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetParagraphAlignment(ParagraphAlignment alignment)
        {
            _state.Paragraph.Alignment = alignment;
            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetParagraphLeftIndent(float value)
        {
            _state.Paragraph.IndentLeft = value;
            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetParagraphRightIndent(float value)
        {
            _state.Paragraph.IndentRight = value;
            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetParagraphFirstLineIndent(float value)
        {
            _state.Paragraph.FirstLineIndent = value;
            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetParagraphSpacingBefore(float value)
        {
            _state.Paragraph.SpacingBefore = value;
            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetParagraphSpacingAfter(float value)
        {
            _state.Paragraph.SpacingAfter = value;
            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetParagraphLineSpacing(int twips)
        {
            _state.Paragraph.LineSpacing = Math.Abs(twips);
            if (twips < 0)
            {
                _state.Paragraph.LineSpacingRule = DocLineSpacingRule.Exactly;
            }

            ApplyParagraphStateToCurrentParagraph();
        }

        private void SetTableRowHeight(int twips)
        {
            if (twips == 0)
            {
                _pendingRowProperties.Height = null;
                _pendingRowProperties.HeightRule = TableRowHeightRule.Auto;
                return;
            }

            var absolute = Math.Abs(twips);
            _pendingRowProperties.Height = absolute > 0 ? TwipsToDip(absolute) : null;
            _pendingRowProperties.HeightRule = twips < 0
                ? TableRowHeightRule.Exact
                : TableRowHeightRule.AtLeast;
        }

        private void SetTableRowHeader(bool value)
        {
            _pendingRowProperties.RepeatOnEachPage = value;
        }

        private void SetTableRowCantSplit(bool value)
        {
            _pendingRowProperties.CantSplit = value;
        }

        private bool TryGetColorByIndex(int index, out DocColor color)
        {
            if (index >= 0
                && index < _colors.Count
                && _colors[index].HasValue)
            {
                color = _colors[index]!.Value;
                return true;
            }

            color = default;
            return false;
        }

        private void StartTableRow()
        {
            FlushRun();

            if (_activeRow is not null)
            {
                EndTableRow();
            }

            if (_paragraph is not null
                && !_state.Paragraph.InTable
                && IsParagraphEmpty(_paragraph)
                && _activeCell is null
                && _blocks.Count > 0
                && ReferenceEquals(_blocks[^1], _paragraph))
            {
                _blocks.RemoveAt(_blocks.Count - 1);
                _paragraph = null;
            }

            if (_activeTable is null)
            {
                _activeTable = new TableBlock();
                _blocks.Add(_activeTable);
            }

            ResetPendingRowState();
            _activeRow = new TableRow();
            _activeCell = null;
            _activeRowCellBoundsTwips = new List<int>();
            _activeRowCellDefinitions = new List<TableCellDefinition>();
            _activeRowCellIndex = 0;
            _paragraph = null;
            _state.Paragraph.InTable = true;
        }

        private void AddTableCellBoundary(int twips)
        {
            _activeRowCellBoundsTwips ??= new List<int>();
            _activeRowCellDefinitions ??= new List<TableCellDefinition>();
            if (twips >= 0)
            {
                _activeRowCellBoundsTwips.Add(twips);
                _activeRowCellDefinitions.Add(BuildPendingCellDefinition());
                ResetPendingCellState();
            }
        }

        private void EndTableCell()
        {
            FlushRun();
            if (_activeRow is null)
            {
                return;
            }

            if (_activeCell is null)
            {
                _activeCell = new TableCell();
            }

            if (_activeCell.Blocks.Count == 0)
            {
                _activeCell.Blocks.Add(new ParagraphBlock());
            }

            ApplyPendingCellDefinition(_activeCell);
            _activeRow.Cells.Add(_activeCell);
            _activeCell = null;
            _paragraph = null;
            _state.Paragraph.InTable = true;
        }

        private void EndTableRow()
        {
            FlushRun();
            if (_activeRow is null)
            {
                return;
            }

            if (_activeCell is not null || (_paragraph is not null && _state.Paragraph.InTable))
            {
                EndTableCell();
            }

            if (_activeRow.Cells.Count == 0)
            {
                _activeRow.Cells.Add(new TableCell([new ParagraphBlock()]));
            }

            if (_activeTable is not null)
            {
                ApplyPendingRowDefinition(_activeTable, _activeRow);
                ApplyRowWidthsFromBoundaries(_activeTable, _activeRow, _activeRowCellBoundsTwips);
                _activeTable.Rows.Add(_activeRow);
            }

            _activeRow = null;
            _activeCell = null;
            _activeRowCellBoundsTwips = null;
            _activeRowCellDefinitions = null;
            _activeRowCellIndex = 0;
            _paragraph = null;
            _state.Paragraph.InTable = false;
            _pendingBorderTarget = BorderTarget.None;
            ResetPendingCellState();
        }

        private void ApplyRowWidthsFromBoundaries(TableBlock table, TableRow row, IReadOnlyList<int>? boundariesTwips)
        {
            if (boundariesTwips is not { Count: > 0 })
            {
                return;
            }

            var widths = new List<float>(boundariesTwips.Count);
            var previous = 0;
            for (var i = 0; i < boundariesTwips.Count; i++)
            {
                var boundary = boundariesTwips[i];
                var widthTwips = Math.Max(0, boundary - previous);
                previous = boundary;
                widths.Add(TwipsToDip(widthTwips));
            }

            if (table.Properties.ColumnWidths.Count == 0)
            {
                table.Properties.ColumnWidths.AddRange(widths);
            }

            for (var i = 0; i < row.Cells.Count && i < widths.Count; i++)
            {
                var preferredWidth = row.Cells[i].Properties.PreferredWidth;
                var preferredWidthUnit = row.Cells[i].Properties.PreferredWidthUnit;
                var hasExplicitWidth = preferredWidthUnit == TableWidthUnit.Auto
                                       || preferredWidthUnit == TableWidthUnit.Pct
                                       || (preferredWidth.HasValue && preferredWidth.Value > 0f);
                if (!hasExplicitWidth && widths[i] > 0f)
                {
                    row.Cells[i].Properties.PreferredWidth = widths[i];
                    row.Cells[i].Properties.PreferredWidthUnit = TableWidthUnit.Dxa;
                }
            }
        }

        private void ApplyPendingRowDefinition(TableBlock table, TableRow row)
        {
            row.Properties.Height = _pendingRowProperties.Height;
            row.Properties.HeightRule = _pendingRowProperties.HeightRule;
            row.Properties.CantSplit = _pendingRowProperties.CantSplit;
            row.Properties.RepeatOnEachPage = _pendingRowProperties.RepeatOnEachPage;
            row.Properties.ShadingColor = _pendingRowProperties.ShadingColor;
            row.Properties.GridBefore = _pendingRowProperties.GridBefore;
            row.Properties.GridAfter = _pendingRowProperties.GridAfter;

            if (_pendingTableAlignment.HasValue)
            {
                table.Properties.Alignment = _pendingTableAlignment;
            }

            if (_pendingTableLayoutMode.HasValue)
            {
                table.Properties.LayoutMode = _pendingTableLayoutMode;
            }

            if (_pendingTableWidthRaw.HasValue)
            {
                ApplyPendingTableWidth(table.Properties, _pendingTableWidthRaw.Value, _pendingTableWidthUnitCode);
            }

            if (_pendingRowIndent.HasValue)
            {
                table.Properties.Indent = _pendingRowIndent.Value;
                table.Properties.IndentUnit = TableWidthUnit.Dxa;
            }

            if (_pendingRowCellSpacingTwips.HasValue)
            {
                table.Properties.CellSpacing = TwipsToDip(_pendingRowCellSpacingTwips.Value);
                table.Properties.CellSpacingUnit = TableWidthUnit.Dxa;
            }

            if (TryCreatePadding(_pendingRowPadding, out var padding))
            {
                table.Properties.CellPadding = padding;
            }

            ApplyPendingTableBorders(table.Properties.Borders);
        }

        private static void ApplyPendingTableWidth(TableProperties properties, int widthRaw, int? widthUnitCode)
        {
            var widthUnit = MapPreferredWidthUnit(widthUnitCode);
            switch (widthUnit)
            {
                case TableWidthUnit.Auto:
                    properties.Width = null;
                    properties.WidthUnit = TableWidthUnit.Auto;
                    break;
                case TableWidthUnit.Pct:
                    if (widthRaw > 0)
                    {
                        properties.Width = widthRaw / 50f;
                        properties.WidthUnit = TableWidthUnit.Pct;
                    }

                    break;
                default:
                    if (widthRaw > 0)
                    {
                        properties.Width = TwipsToDip(widthRaw);
                        properties.WidthUnit = TableWidthUnit.Dxa;
                    }

                    break;
            }
        }

        private void ApplyPendingTableBorders(TableBorders target)
        {
            if (_pendingTableBorders.Top is not null)
            {
                target.Top = _pendingTableBorders.Top.Clone();
            }

            if (_pendingTableBorders.Bottom is not null)
            {
                target.Bottom = _pendingTableBorders.Bottom.Clone();
            }

            if (_pendingTableBorders.Left is not null)
            {
                target.Left = _pendingTableBorders.Left.Clone();
            }

            if (_pendingTableBorders.Right is not null)
            {
                target.Right = _pendingTableBorders.Right.Clone();
            }

            if (_pendingTableBorders.InsideHorizontal is not null)
            {
                target.InsideHorizontal = _pendingTableBorders.InsideHorizontal.Clone();
            }

            if (_pendingTableBorders.InsideVertical is not null)
            {
                target.InsideVertical = _pendingTableBorders.InsideVertical.Clone();
            }
        }

        private void ApplyPendingCellDefinition(TableCell cell)
        {
            if (_activeRowCellDefinitions is null || _activeRowCellIndex < 0 || _activeRowCellIndex >= _activeRowCellDefinitions.Count)
            {
                return;
            }

            var definition = _activeRowCellDefinitions[_activeRowCellIndex];
            _activeRowCellIndex++;

            if (definition.VerticalMerge != TableCellVerticalMerge.None)
            {
                cell.VerticalMerge = definition.VerticalMerge;
            }

            ApplyParsedCellProperties(definition.Properties, cell.Properties);
        }

        private static void ApplyParsedCellProperties(TableCellProperties source, TableCellProperties target)
        {
            if (source.Padding.HasValue)
            {
                target.Padding = source.Padding;
            }

            if (source.ShadingColor.HasValue)
            {
                target.ShadingColor = source.ShadingColor;
            }

            if (source.VerticalAlignment.HasValue)
            {
                target.VerticalAlignment = source.VerticalAlignment;
            }

            if (source.TextDirection.HasValue)
            {
                target.TextDirection = source.TextDirection;
            }

            if (source.PreferredWidth.HasValue)
            {
                target.PreferredWidth = source.PreferredWidth;
            }

            if (source.PreferredWidthUnit.HasValue)
            {
                target.PreferredWidthUnit = source.PreferredWidthUnit;
            }

            if (source.Borders.Top is not null)
            {
                target.Borders.Top = source.Borders.Top.Clone();
            }

            if (source.Borders.Bottom is not null)
            {
                target.Borders.Bottom = source.Borders.Bottom.Clone();
            }

            if (source.Borders.Left is not null)
            {
                target.Borders.Left = source.Borders.Left.Clone();
            }

            if (source.Borders.Right is not null)
            {
                target.Borders.Right = source.Borders.Right.Clone();
            }
        }

        private void ResetPendingRowState()
        {
            _pendingRowProperties = new TableRowProperties();
            _pendingRowPadding.Reset();
            _pendingRowCellSpacingTwips = null;
            _pendingRowIndent = null;
            _pendingTableAlignment = null;
            _pendingTableLayoutMode = null;
            _pendingTableWidthRaw = null;
            _pendingTableWidthUnitCode = null;
            _pendingTableBorders.Top = null;
            _pendingTableBorders.Bottom = null;
            _pendingTableBorders.Left = null;
            _pendingTableBorders.Right = null;
            _pendingTableBorders.InsideHorizontal = null;
            _pendingTableBorders.InsideVertical = null;
            ResetPendingCellState();
            _pendingBorderTarget = BorderTarget.None;
        }

        private void ResetPendingCellState()
        {
            _pendingCellState.Reset();
            _pendingBorderTarget = BorderTarget.None;
            _pendingBorderSide = BorderSide.Top;
        }

        private TableCellDefinition BuildPendingCellDefinition()
        {
            var properties = _pendingCellState.Properties.Clone();
            if (TryCreatePadding(_pendingCellState.Padding, out var padding))
            {
                properties.Padding = padding;
            }

            if (_pendingCellState.PreferredWidth.HasValue)
            {
                var widthUnit = MapPreferredWidthUnit(_pendingCellState.PreferredWidthUnitCode);
                var widthRaw = _pendingCellState.PreferredWidth.Value;
                switch (widthUnit)
                {
                    case TableWidthUnit.Pct:
                        if (widthRaw > 0)
                        {
                            properties.PreferredWidth = widthRaw / 50f;
                            properties.PreferredWidthUnit = TableWidthUnit.Pct;
                        }

                        break;
                    case TableWidthUnit.Auto:
                        properties.PreferredWidth = null;
                        properties.PreferredWidthUnit = TableWidthUnit.Auto;
                        break;
                    default:
                        if (widthRaw > 0)
                        {
                            properties.PreferredWidth = TwipsToDip(widthRaw);
                            properties.PreferredWidthUnit = TableWidthUnit.Dxa;
                        }

                        break;
                }
            }

            return new TableCellDefinition(properties, _pendingCellState.VerticalMerge);
        }

        private static TableWidthUnit MapPreferredWidthUnit(int? unitCode)
        {
            return unitCode switch
            {
                0 => TableWidthUnit.Auto,
                2 => TableWidthUnit.Pct,
                _ => TableWidthUnit.Dxa
            };
        }

        private static bool TryCreatePadding(PendingPaddingState state, out DocThickness padding)
        {
            if (!state.HasAnyValue)
            {
                padding = default;
                return false;
            }

            padding = new DocThickness(
                ConvertPaddingComponent(state.Left, state.LeftUnit),
                ConvertPaddingComponent(state.Top, state.TopUnit),
                ConvertPaddingComponent(state.Right, state.RightUnit),
                ConvertPaddingComponent(state.Bottom, state.BottomUnit));
            return true;
        }

        private static float ConvertPaddingComponent(int? value, int? unitCode)
        {
            if (!value.HasValue)
            {
                return 0f;
            }

            // RTF "3" is twips; default to twips for unknown/omitted units.
            return unitCode is null or 3
                ? TwipsToDip(value.Value)
                : TwipsToDip(value.Value);
        }

        private void BeginCellBorder(BorderSide side)
        {
            _pendingBorderTarget = BorderTarget.Cell;
            _pendingBorderSide = side;
            _ = GetPendingCellBorder(side);
        }

        private void BeginTableBorder(BorderSide side)
        {
            _pendingBorderTarget = BorderTarget.Table;
            _pendingBorderSide = side;
            _ = GetPendingTableBorder(side);
        }

        private void SetPendingBorderStyle(DocBorderStyle style)
        {
            var border = GetPendingBorder();
            if (border is null)
            {
                return;
            }

            border.Style = style;
            if (style == DocBorderStyle.None)
            {
                border.Thickness = 0f;
            }
        }

        private void SetPendingBorderWidth(int twips)
        {
            var border = GetPendingBorder();
            if (border is null)
            {
                return;
            }

            border.Thickness = Math.Max(0f, TwipsToDip(Math.Max(0, twips)));
        }

        private void SetPendingBorderColor(int colorIndex)
        {
            var border = GetPendingBorder();
            if (border is null)
            {
                return;
            }

            if (TryGetColorByIndex(colorIndex, out var color))
            {
                border.Color = color;
            }
        }

        private BorderLine? GetPendingBorder()
        {
            return _pendingBorderTarget switch
            {
                BorderTarget.Cell => GetPendingCellBorder(_pendingBorderSide),
                BorderTarget.Table => GetPendingTableBorder(_pendingBorderSide),
                _ => null
            };
        }

        private BorderLine GetPendingCellBorder(BorderSide side)
        {
            var borders = _pendingCellState.Properties.Borders;
            switch (side)
            {
                case BorderSide.Top:
                    borders.Top ??= new BorderLine();
                    return borders.Top;
                case BorderSide.Bottom:
                    borders.Bottom ??= new BorderLine();
                    return borders.Bottom;
                case BorderSide.Left:
                    borders.Left ??= new BorderLine();
                    return borders.Left;
                default:
                    borders.Right ??= new BorderLine();
                    return borders.Right;
            }
        }

        private BorderLine GetPendingTableBorder(BorderSide side)
        {
            switch (side)
            {
                case BorderSide.Top:
                    _pendingTableBorders.Top ??= new BorderLine();
                    return _pendingTableBorders.Top;
                case BorderSide.Bottom:
                    _pendingTableBorders.Bottom ??= new BorderLine();
                    return _pendingTableBorders.Bottom;
                case BorderSide.Left:
                    _pendingTableBorders.Left ??= new BorderLine();
                    return _pendingTableBorders.Left;
                case BorderSide.Right:
                    _pendingTableBorders.Right ??= new BorderLine();
                    return _pendingTableBorders.Right;
                case BorderSide.InsideHorizontal:
                    _pendingTableBorders.InsideHorizontal ??= new BorderLine();
                    return _pendingTableBorders.InsideHorizontal;
                default:
                    _pendingTableBorders.InsideVertical ??= new BorderLine();
                    return _pendingTableBorders.InsideVertical;
            }
        }

        private void EndParagraph()
        {
            FlushRun();
            PromoteCurrentParagraphToList();
            if (_paragraph is null)
            {
                _ = EnsureParagraph();
            }

            _paragraph = null;
        }

        private void AddSectionBreak()
        {
            var breakBlock = new SectionBreakBlock
            {
                BreakType = _pendingSectionBreakType,
                Properties = _parsedSectionProperties.Clone()
            };

            AddBlockBreak(breakBlock);
            _pendingSectionBreakType = SectionBreakType.NextPage;
        }

        private void AddBlockBreak(Block breakBlock)
        {
            if (_state.Paragraph.InTable || _activeRow is not null || _activeCell is not null)
            {
                AppendText('\n');
                return;
            }

            FlushRun();
            PromoteCurrentParagraphToList();
            DiscardCurrentStandaloneEmptyParagraph();
            _paragraph = null;
            _blocks.Add(breakBlock);
        }

        private void DiscardCurrentStandaloneEmptyParagraph()
        {
            if (_paragraph is null || !IsParagraphEmpty(_paragraph))
            {
                return;
            }

            if (_blocks.Count > 0 && ReferenceEquals(_blocks[^1], _paragraph))
            {
                _blocks.RemoveAt(_blocks.Count - 1);
            }
        }

        private void PromoteCurrentParagraphToList()
        {
            if (_paragraph is null || _state.Paragraph.InTable)
            {
                return;
            }

            if (_paragraph.ListInfo is not null)
            {
                TrimListPrefixFromParagraph(_paragraph);
                return;
            }

            TryPromoteParagraphToList(_paragraph);
        }

        private static void TrimListPrefixFromParagraph(ParagraphBlock paragraph)
        {
            if (paragraph.Inlines.Count == 0 || paragraph.Inlines[0] is not RunInline firstRun)
            {
                return;
            }

            var text = firstRun.GetText();
            if (TryParseBulletPrefix(text, out var prefixLength, out _)
                || TryParseNumberedPrefix(text, out prefixLength, out _))
            {
                TrimFirstRun(paragraph, firstRun, prefixLength);
            }
        }

        private static void TryPromoteParagraphToList(ParagraphBlock paragraph)
        {
            if (paragraph.ListInfo is not null || paragraph.Inlines.Count == 0 || paragraph.Inlines[0] is not RunInline firstRun)
            {
                return;
            }

            var text = firstRun.GetText();
            if (text.Length < 2)
            {
                return;
            }

            if (TryParseBulletPrefix(text, out var prefixLength, out var bullet))
            {
                TrimFirstRun(paragraph, firstRun, prefixLength);
                paragraph.ListInfo = new ListInfo(ListKind.Bullet)
                {
                    BulletSymbol = bullet
                };
                return;
            }

            if (TryParseNumberedPrefix(text, out prefixLength, out var startAt))
            {
                TrimFirstRun(paragraph, firstRun, prefixLength);
                paragraph.ListInfo = new ListInfo(ListKind.Numbered)
                {
                    StartAt = startAt
                };
            }
        }

        private static bool TryParseBulletPrefix(string text, out int prefixLength, out string symbol)
        {
            prefixLength = 0;
            symbol = string.Empty;

            if (text.Length < 2 || text[1] != '\t')
            {
                return false;
            }

            var marker = text[0];
            if (marker is '\u2022' or '\u25E6' or '\u00B7' or '*' or '-')
            {
                prefixLength = 2;
                symbol = marker.ToString();
                return true;
            }

            return false;
        }

        private static bool TryParseNumberedPrefix(string text, out int prefixLength, out int startAt)
        {
            prefixLength = 0;
            startAt = 0;

            var index = 0;
            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }

            if (index == 0 || index + 1 >= text.Length)
            {
                return false;
            }

            if (text[index] is not ('.' or ')') || text[index + 1] != '\t')
            {
                return false;
            }

            if (!int.TryParse(text.AsSpan(0, index), NumberStyles.None, CultureInfo.InvariantCulture, out startAt))
            {
                return false;
            }

            prefixLength = index + 2;
            return startAt > 0;
        }

        private static void TrimFirstRun(ParagraphBlock paragraph, RunInline run, int prefixLength)
        {
            var text = run.GetText();
            if (prefixLength <= 0 || prefixLength > text.Length)
            {
                return;
            }

            var remaining = text[prefixLength..];
            if (remaining.Length == 0)
            {
                paragraph.Inlines.RemoveAt(0);
                return;
            }

            var replacement = new RunInline(remaining, run.Style)
            {
                StyleId = run.StyleId,
                Hyperlink = run.Hyperlink
            };
            paragraph.Inlines[0] = replacement;
        }

        private static void MergeFieldState(ParserState source, ParserState target)
        {
            if (source.Field is null || target.Field is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(source.Field.Instruction))
            {
                target.Field.Instruction = source.Field.Instruction;
            }

            if (source.Field.Hyperlink is not null)
            {
                target.Field.Hyperlink = source.Field.Hyperlink;
            }

            target.Field.InInstruction = false;
            target.Field.InResult = false;
        }

        private void ParseHeaderFooterDestination(string destinationWord)
        {
            var groupEnd = FindCurrentGroupEndIndex();
            if (groupEnd < 0)
            {
                return;
            }

            if (TryParseDestinationBlocks(_rtf.AsSpan(_pos, groupEnd - _pos), out var blocks))
            {
                ApplyHeaderFooterDestination(destinationWord, blocks);
            }

            _pos = groupEnd;
        }

        private bool TryParseDestinationBlocks(ReadOnlySpan<char> destinationContent, out List<Block> blocks)
        {
            blocks = new List<Block>();
            var wrappedRtf = BuildDestinationRtf(destinationContent);
            if (!DocumentRtfParser.TryParse(wrappedRtf, out var destinationDocument))
            {
                return false;
            }

            for (var i = 0; i < destinationDocument.Blocks.Count; i++)
            {
                var block = destinationDocument.Blocks[i];
                if (IsParagraphBlockWithoutContent(block))
                {
                    continue;
                }

                blocks.Add(block);
            }

            return true;
        }

        private string BuildDestinationRtf(ReadOnlySpan<char> destinationContent)
        {
            var builder = new StringBuilder();
            builder.Append("{\\rtf1\\ansi\\ansicpg")
                .Append(_ansiCodePage.ToString(CultureInfo.InvariantCulture))
                .Append("\\deff")
                .Append(_defaultFontIndex.ToString(CultureInfo.InvariantCulture));
            AppendSyntheticFontTable(builder);
            AppendSyntheticColorTable(builder);
            builder.Append("\\uc")
                .Append(_unicodeSkip.ToString(CultureInfo.InvariantCulture))
                .Append(' ');
            builder.Append(destinationContent);
            builder.Append('}');
            return builder.ToString();
        }

        private void AppendSyntheticFontTable(StringBuilder builder)
        {
            if (_fonts.Count == 0)
            {
                return;
            }

            builder.Append("{\\fonttbl");
            foreach (var pair in _fonts.OrderBy(static item => item.Key))
            {
                builder.Append("{\\f")
                    .Append(pair.Key.ToString(CultureInfo.InvariantCulture))
                    .Append("\\fnil ");
                AppendEscapedRtfText(builder, pair.Value);
                builder.Append(";}");
            }

            builder.Append('}');
        }

        private void AppendSyntheticColorTable(StringBuilder builder)
        {
            if (_colors.Count <= 1)
            {
                return;
            }

            builder.Append("{\\colortbl;");
            for (var i = 1; i < _colors.Count; i++)
            {
                if (_colors[i].HasValue)
                {
                    var color = _colors[i]!.Value;
                    builder.Append("\\red").Append(color.R.ToString(CultureInfo.InvariantCulture));
                    builder.Append("\\green").Append(color.G.ToString(CultureInfo.InvariantCulture));
                    builder.Append("\\blue").Append(color.B.ToString(CultureInfo.InvariantCulture));
                }

                builder.Append(';');
            }

            builder.Append('}');
        }

        private static void AppendEscapedRtfText(StringBuilder builder, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch is '\\' or '{' or '}')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }
        }

        private void ApplyHeaderFooterDestination(string destinationWord, IReadOnlyList<Block> blocks)
        {
            var target = destinationWord switch
            {
                "header" or "headerr" => _parsedHeader,
                "footer" or "footerr" => _parsedFooter,
                "headerf" => _parsedFirstHeader,
                "footerf" => _parsedFirstFooter,
                "headerl" => _parsedEvenHeader,
                "footerl" => _parsedEvenFooter,
                _ => null
            };

            if (target is null)
            {
                return;
            }

            target.IsDefined = true;
            target.Blocks.Clear();
            for (var i = 0; i < blocks.Count; i++)
            {
                target.Blocks.Add(blocks[i]);
            }

            if (destinationWord is "headerf" or "footerf")
            {
                _parsedSectionProperties.DifferentFirstPageHeaderFooter = true;
            }

            if (destinationWord is "headerl" or "footerl" or "headerr" or "footerr")
            {
                _parsedFacingPages = true;
            }
        }

        private static bool IsParagraphBlockWithoutContent(Block block)
        {
            if (block is not ParagraphBlock paragraph)
            {
                return false;
            }

            return IsParagraphEmpty(paragraph);
        }

        private void ParsePictureDestination()
        {
            var groupEnd = FindCurrentGroupEndIndex();
            if (groupEnd < 0)
            {
                return;
            }

            if (TryParsePicture(_rtf.AsSpan(_pos, groupEnd - _pos), out var image))
            {
                FlushRun();
                image.Hyperlink = _state.Character.Hyperlink;
                EnsureParagraph().Inlines.Add(image);
            }

            _pos = groupEnd;
        }

        private int FindCurrentGroupEndIndex()
        {
            var depth = 1;
            for (var i = _pos; i < _rtf.Length; i++)
            {
                var ch = _rtf[i];
                if (ch == '\\')
                {
                    if (i + 1 >= _rtf.Length)
                    {
                        continue;
                    }

                    var next = _rtf[i + 1];
                    if (next is '\\' or '{' or '}')
                    {
                        i++;
                        continue;
                    }

                    if (next == '\'' && i + 3 < _rtf.Length)
                    {
                        i += 3;
                    }

                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static bool TryParsePicture(ReadOnlySpan<char> span, out ImageInline image)
        {
            image = null!;
            var mimeType = "image/png";
            int? widthGoalTwips = null;
            int? heightGoalTwips = null;
            int? widthPixels = null;
            int? heightPixels = null;
            var bytes = new List<byte>();
            var highNibble = -1;
            var index = 0;

            while (index < span.Length)
            {
                var ch = span[index];
                if (ch == '\\')
                {
                    index++;
                    if (index >= span.Length)
                    {
                        break;
                    }

                    if (span[index] == '\'')
                    {
                        if (index + 2 < span.Length
                            && TryParseHexDigit(span[index + 1], out var h1)
                            && TryParseHexDigit(span[index + 2], out var h2))
                        {
                            bytes.Add((byte)((h1 << 4) | h2));
                        }

                        index = Math.Min(span.Length, index + 3);
                        continue;
                    }

                    var wordStart = index;
                    while (index < span.Length && char.IsLetter(span[index]))
                    {
                        index++;
                    }

                    var word = span.Slice(wordStart, index - wordStart).ToString();
                    var sign = 1;
                    if (index < span.Length && span[index] == '-')
                    {
                        sign = -1;
                        index++;
                    }

                    var hasNumber = false;
                    var numberStart = index;
                    while (index < span.Length && char.IsDigit(span[index]))
                    {
                        hasNumber = true;
                        index++;
                    }

                    var number = 0;
                    if (hasNumber
                        && int.TryParse(span.Slice(numberStart, index - numberStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        number = parsed * sign;
                    }

                    if (index < span.Length && span[index] == ' ')
                    {
                        index++;
                    }

                    switch (word)
                    {
                        case "pngblip":
                            mimeType = "image/png";
                            break;
                        case "jpegblip":
                        case "jpgblip":
                            mimeType = "image/jpeg";
                            break;
                        case "dibitmap":
                        case "wbitmap":
                            mimeType = "image/bmp";
                            break;
                        case "emfblip":
                            mimeType = "image/emf";
                            break;
                        case "wmetafile":
                        case "wmetafile8":
                            mimeType = "image/wmf";
                            break;
                        case "picwgoal":
                            if (hasNumber)
                            {
                                widthGoalTwips = number;
                            }

                            break;
                        case "pichgoal":
                            if (hasNumber)
                            {
                                heightGoalTwips = number;
                            }

                            break;
                        case "picw":
                            if (hasNumber)
                            {
                                widthPixels = number;
                            }

                            break;
                        case "pich":
                            if (hasNumber)
                            {
                                heightPixels = number;
                            }

                            break;
                    }

                    continue;
                }

                if (TryParseHexDigit(ch, out var value))
                {
                    if (highNibble < 0)
                    {
                        highNibble = value;
                    }
                    else
                    {
                        bytes.Add((byte)((highNibble << 4) | value));
                        highNibble = -1;
                    }
                }

                index++;
            }

            if (bytes.Count == 0)
            {
                return false;
            }

            var width = widthGoalTwips.HasValue && widthGoalTwips.Value > 0
                ? TwipsToDip(widthGoalTwips.Value)
                : Math.Max(1f, widthPixels.GetValueOrDefault(1));
            var height = heightGoalTwips.HasValue && heightGoalTwips.Value > 0
                ? TwipsToDip(heightGoalTwips.Value)
                : Math.Max(1f, heightPixels.GetValueOrDefault(1));

            image = new ImageInline(bytes.ToArray(), width, height, mimeType);
            return true;
        }

        private static bool TryParseHexDigit(char ch, out int value)
        {
            if (ch is >= '0' and <= '9')
            {
                value = ch - '0';
                return true;
            }

            if (ch is >= 'a' and <= 'f')
            {
                value = ch - 'a' + 10;
                return true;
            }

            if (ch is >= 'A' and <= 'F')
            {
                value = ch - 'A' + 10;
                return true;
            }

            value = 0;
            return false;
        }

        private void ApplyParagraphStateToCurrentParagraph()
        {
            if (_paragraph is null)
            {
                return;
            }

            ApplyParagraphState(_paragraph, _state.Paragraph);
            ApplyParagraphListState(_paragraph, _state.Paragraph);
        }

        private static void ApplyParagraphState(ParagraphBlock paragraph, ParagraphState state)
        {
            var properties = paragraph.Properties;
            properties.Alignment = state.Alignment;
            properties.IndentLeft = state.IndentLeft;
            properties.IndentRight = state.IndentRight;
            properties.FirstLineIndent = state.FirstLineIndent;
            properties.SpacingBefore = state.SpacingBefore;
            properties.SpacingAfter = state.SpacingAfter;
            properties.LineSpacing = state.LineSpacing;
            properties.LineSpacingRule = state.LineSpacingRule;
            properties.TabStops.Clear();
            for (var i = 0; i < state.TabStops.Count; i++)
            {
                properties.TabStops.Add(state.TabStops[i].Clone());
            }
        }

        private void ApplyParagraphListState(ParagraphBlock paragraph, ParagraphState state)
        {
            paragraph.ListInfo = ResolveParagraphListInfo(state);
        }

        private ListInfo? ResolveParagraphListInfo(ParagraphState state)
        {
            if (!state.ListOverrideIndex.HasValue)
            {
                return null;
            }

            var overrideIndex = Math.Max(0, state.ListOverrideIndex.Value);
            var level = Math.Max(0, state.ListLevel ?? 0);
            var listId = _listOverrides.TryGetValue(overrideIndex, out var mappedListId)
                ? mappedListId
                : overrideIndex;
            if (!_listDefinitions.TryGetValue(listId, out var definition))
            {
                return null;
            }

            var levelDefinition = ResolveLevelDefinition(definition, level);
            if (levelDefinition is null)
            {
                return null;
            }

            var kind = levelDefinition.Format == ListNumberFormat.Bullet
                ? ListKind.Bullet
                : ListKind.Numbered;
            return new ListInfo(kind, level, listId)
            {
                NumberFormat = levelDefinition.Format,
                LevelText = levelDefinition.LevelText,
                BulletSymbol = levelDefinition.BulletSymbol,
                StartAt = levelDefinition.StartAt > 0 ? levelDefinition.StartAt : null,
                LeftIndent = levelDefinition.LeftIndent,
                HangingIndent = levelDefinition.HangingIndent,
                TabStop = levelDefinition.TabStop
            };
        }

        private static ListLevelDefinition? ResolveLevelDefinition(ListDefinition definition, int level)
        {
            if (definition.Levels.Count == 0)
            {
                return null;
            }

            if (definition.Levels.TryGetValue(level, out var exact))
            {
                return exact;
            }

            var candidate = definition.Levels
                .Where(pair => pair.Key <= level)
                .OrderByDescending(pair => pair.Key)
                .Select(pair => pair.Value)
                .FirstOrDefault();
            if (candidate is not null)
            {
                return candidate;
            }

            return definition.Levels
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value)
                .FirstOrDefault();
        }

        private static bool IsParagraphEmpty(ParagraphBlock paragraph)
        {
            if (paragraph.Inlines.Count == 0)
            {
                return string.IsNullOrEmpty(paragraph.Text);
            }

            for (var i = 0; i < paragraph.Inlines.Count; i++)
            {
                if (paragraph.Inlines[i] is RunInline run && run.Length == 0)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private ParagraphBlock EnsureParagraph()
        {
            if (_paragraph is not null)
            {
                return _paragraph;
            }

            if (_state.Paragraph.InTable && _activeRow is not null)
            {
                _activeCell ??= new TableCell();
            }
            else if (_activeTable is not null)
            {
                _activeTable = null;
            }

            var paragraph = new ParagraphBlock();
            ApplyParagraphState(paragraph, _state.Paragraph);
            ApplyParagraphListState(paragraph, _state.Paragraph);
            CurrentContainer().Add(paragraph);
            _paragraph = paragraph;
            return paragraph;
        }

        private IList<Block> CurrentContainer()
        {
            if (_activeCell is not null)
            {
                return _activeCell.Blocks;
            }

            return _blocks;
        }

        private void AppendText(char ch)
        {
            AppendText(ch.ToString());
        }

        private void AppendPlainTextCharacter(char ch)
        {
            if (ch <= 0x7f)
            {
                AppendText(ch);
                return;
            }

            if (ch <= 0xff)
            {
                AppendText(DecodeAnsiByte((byte)ch));
                return;
            }

            AppendText(ch);
        }

        private void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_state.Field is { InInstruction: true } field)
            {
                field.AppendInstruction(text);
                return;
            }

            _ = EnsureParagraph();
            if (_runText is null)
            {
                _runText = new StringBuilder();
                _runStyle = _state.Character.Clone();
            }

            _runText.Append(text);
        }

        private void FlushRun()
        {
            if (_runText is null || _runText.Length == 0)
            {
                _runText = null;
                return;
            }

            var paragraph = EnsureParagraph();
            var style = BuildRunStyle(_runStyle);
            RunInline run;
            if (style is null)
            {
                run = new RunInline(_runText.ToString());
            }
            else
            {
                run = new RunInline(_runText.ToString(), style);
            }

            run.Hyperlink = _runStyle.Hyperlink;
            paragraph.Inlines.Add(run);
            _runText = null;
        }

        private TextStyleProperties? BuildRunStyle(CharacterStyle styleState)
        {
            var style = new TextStyleProperties();
            if (styleState.FontIndex >= 0 && _fonts.TryGetValue(styleState.FontIndex, out var font))
            {
                style.FontFamily = font;
            }

            if (styleState.FontSize.HasValue)
            {
                style.FontSize = styleState.FontSize;
            }

            if (styleState.Bold)
            {
                style.FontWeight = DocFontWeight.Bold;
            }

            if (styleState.Italic)
            {
                style.FontStyle = DocFontStyle.Italic;
            }

            if (styleState.Underline)
            {
                style.Underline = true;
            }

            if (styleState.Strikethrough)
            {
                style.Strikethrough = true;
            }

            if (styleState.ColorIndex.HasValue
                && styleState.ColorIndex.Value >= 0
                && styleState.ColorIndex.Value < _colors.Count
                && _colors[styleState.ColorIndex.Value].HasValue)
            {
                style.Color = _colors[styleState.ColorIndex.Value];
            }

            if (styleState.HighlightIndex.HasValue
                && styleState.HighlightIndex.Value >= 0
                && styleState.HighlightIndex.Value < _colors.Count
                && _colors[styleState.HighlightIndex.Value].HasValue)
            {
                style.HighlightColor = _colors[styleState.HighlightIndex.Value];
            }

            if (styleState.VerticalPosition != DocVerticalPosition.Normal)
            {
                style.VerticalPosition = styleState.VerticalPosition;
            }

            return style.HasValues ? style : null;
        }

        private bool SkipUnicodeFallback()
        {
            if (_pendingUnicodeSkip <= 0 || _pos >= _rtf.Length)
            {
                return false;
            }

            var ch = _rtf[_pos];
            if (ch == '{' || ch == '}')
            {
                _pendingUnicodeSkip = 0;
                return false;
            }

            if (ch == '\\')
            {
                if (_pos + 1 >= _rtf.Length)
                {
                    _pendingUnicodeSkip = 0;
                    return false;
                }

                var next = _rtf[_pos + 1];
                if (next == '\'' && _pos + 3 < _rtf.Length)
                {
                    _pos += 4;
                    _pendingUnicodeSkip--;
                    return true;
                }

                if (next is '\\' or '{' or '}' or '~' or '_' or '-')
                {
                    _pos += 2;
                    _pendingUnicodeSkip--;
                    return true;
                }

                _pendingUnicodeSkip = 0;
                return false;
            }

            _pos++;
            _pendingUnicodeSkip--;
            return true;
        }

        private void FinalizeOpenTableStructures()
        {
            if (_activeCell is not null)
            {
                EndTableCell();
            }

            if (_activeRow is not null)
            {
                EndTableRow();
            }

            if (_activeTable is not null && _activeTable.Rows.Count == 0)
            {
                _activeTable = null;
            }
        }

        private static bool IsKnownDestinationWord(string word)
        {
            return word is "fonttbl"
                or "colortbl"
                or "stylesheet"
                or "info"
                or "pict"
                or "shppict"
                or "nonshppict"
                or "object"
                or "objdata"
                or "themedata"
                or "datastore"
                or "xmlnstbl"
                or "generator"
                or "header"
                or "footer"
                or "headerl"
                or "headerr"
                or "headerf"
                or "footerl"
                or "footerr"
                or "footerf"
                or "footnote"
                or "annotation"
                or "fldinst"
                or "fldrslt"
                or "listtable"
                or "listoverridetable"
                or "list"
                or "listoverride"
                or "listlevel"
                or "leveltext"
                or "levelnumbers"
                or "pntext"
                or "listtext";
        }

        private static bool IsSkippedDestination(string word)
        {
            return word is "fonttbl"
                or "colortbl"
                or "stylesheet"
                or "info"
                or "object"
                or "objdata"
                or "themedata"
                or "datastore"
                or "xmlnstbl"
                or "generator"
                or "footnote"
                or "annotation"
                or "listtable"
                or "listoverridetable"
                or "list"
                or "listoverride"
                or "listlevel"
                or "leveltext"
                or "levelnumbers"
                ;
        }

        private static float HalfPointsToDip(int halfPoints)
        {
            return halfPoints / 2f * 96f / 72f;
        }

        private static float TwipsToDip(int twips)
        {
            return twips / 20f * 96f / 72f;
        }

        private static Dictionary<int, ListDefinition> ParseListTable(string rtf)
        {
            var definitions = new Dictionary<int, ListDefinition>();
            if (!TryExtractGroupByToken(rtf, "{\\*\\listtable", out var listTableGroup)
                && !TryExtractGroupByToken(rtf, "{\\listtable", out listTableGroup))
            {
                return definitions;
            }

            var listGroups = EnumerateTopLevelChildGroups(listTableGroup);
            for (var i = 0; i < listGroups.Count; i++)
            {
                if (!TryParseListDefinition(listGroups[i], out var definition) || definition is null)
                {
                    continue;
                }

                definitions[definition.Id] = definition;
            }

            return definitions;
        }

        private static Dictionary<int, int> ParseListOverrideTable(string rtf)
        {
            var overrides = new Dictionary<int, int>();
            if (!TryExtractGroupByToken(rtf, "{\\*\\listoverridetable", out var overrideGroup)
                && !TryExtractGroupByToken(rtf, "{\\listoverridetable", out overrideGroup))
            {
                return overrides;
            }

            var listOverrides = EnumerateTopLevelChildGroups(overrideGroup);
            for (var i = 0; i < listOverrides.Count; i++)
            {
                var item = listOverrides[i];
                if (!item.Contains("\\listoverride", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryReadControlValue(item, "ls", out var overrideIndex) || overrideIndex < 0)
                {
                    continue;
                }

                if (!TryReadControlValue(item, "listid", out var listId) || listId <= 0)
                {
                    continue;
                }

                overrides[overrideIndex] = listId;
            }

            return overrides;
        }

        private static bool TryParseListDefinition(string listGroup, out ListDefinition? definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(listGroup)
                || !listGroup.Contains("\\list", StringComparison.OrdinalIgnoreCase)
                || !TryReadControlValue(listGroup, "listid", out var listId)
                || listId <= 0)
            {
                return false;
            }

            definition = new ListDefinition(listId);
            var children = EnumerateTopLevelChildGroups(listGroup);
            var levelIndex = 0;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (!child.Contains("\\listlevel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                definition.Levels[levelIndex] = ParseListLevelDefinition(child, levelIndex);
                levelIndex++;
            }

            if (definition.Levels.Count == 0)
            {
                definition.Levels[0] = new ListLevelDefinition(0);
            }

            return true;
        }

        private static ListLevelDefinition ParseListLevelDefinition(string levelGroup, int level)
        {
            var format = ListNumberFormat.Decimal;
            if (TryReadControlValue(levelGroup, "levelnfc", out var nfc)
                || TryReadControlValue(levelGroup, "levelnfcn", out nfc))
            {
                format = MapNfcToListNumberFormat(nfc);
            }

            var definition = new ListLevelDefinition(level)
            {
                Format = format
            };

            if (TryReadControlValue(levelGroup, "levelstartat", out var startAt) && startAt > 0)
            {
                definition.StartAt = startAt;
            }

            if (TryReadControlValue(levelGroup, "li", out var leftIndent))
            {
                definition.LeftIndent = TwipsToDip(leftIndent);
            }

            if (TryReadControlValue(levelGroup, "fi", out var firstLineIndent) && firstLineIndent < 0)
            {
                definition.HangingIndent = TwipsToDip(Math.Abs(firstLineIndent));
            }

            if (TryReadControlValue(levelGroup, "tx", out var tabStop))
            {
                definition.TabStop = TwipsToDip(tabStop);
            }

            ParseLevelText(levelGroup, level, definition);
            if (format != ListNumberFormat.Bullet && string.IsNullOrWhiteSpace(definition.LevelText))
            {
                definition.LevelText = $"%{level + 1}.";
            }

            return definition;
        }

        private static void ParseLevelText(string levelGroup, int level, ListLevelDefinition definition)
        {
            var levelTextGroup = EnumerateTopLevelChildGroups(levelGroup)
                .FirstOrDefault(static group => group.Contains("\\leveltext", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(levelTextGroup))
            {
                if (definition.Format == ListNumberFormat.Bullet)
                {
                    definition.BulletSymbol = "•";
                    definition.LevelText = definition.BulletSymbol;
                }

                return;
            }

            var text = ExtractListLevelText(levelTextGroup);
            if (definition.Format == ListNumberFormat.Bullet)
            {
                var symbol = "•";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var chars = text.Where(static ch => !char.IsControl(ch) && !char.IsWhiteSpace(ch)).Take(1).ToArray();
                    if (chars.Length > 0)
                    {
                        symbol = new string(chars);
                    }
                }

                definition.BulletSymbol = symbol;
                definition.LevelText = definition.BulletSymbol;
                return;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                definition.LevelText = text;
                return;
            }

            definition.LevelText = $"%{level + 1}.";
        }

        private static string ExtractListLevelText(string levelTextGroup)
        {
            if (TryReadControlValue(levelTextGroup, "u", out var unicode))
            {
                var codePoint = unicode < 0 ? unicode + 65536 : unicode;
                if (codePoint > 0 && codePoint <= 0x10FFFF)
                {
                    return char.ConvertFromUtf32(codePoint);
                }
            }

            var bytes = new List<byte>();
            for (var i = 0; i + 3 < levelTextGroup.Length; i++)
            {
                if (levelTextGroup[i] != '\\' || levelTextGroup[i + 1] != '\'')
                {
                    continue;
                }

                if (byte.TryParse(levelTextGroup.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                {
                    bytes.Add(value);
                }
            }

            if (bytes.Count > 0)
            {
                var start = bytes[0] <= 9 ? 1 : 0;
                if (start < bytes.Count)
                {
                    var builder = new StringBuilder(bytes.Count - start);
                    for (var i = start; i < bytes.Count; i++)
                    {
                        builder.Append(DecodeWindows1252Byte(bytes[i]));
                    }

                    return SanitizeListLevelText(builder.ToString());
                }
            }

            var marker = "\\leveltext";
            var index = levelTextGroup.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return string.Empty;
            }

            index += marker.Length;
            while (index < levelTextGroup.Length && char.IsWhiteSpace(levelTextGroup[index]))
            {
                index++;
            }

            var end = levelTextGroup.LastIndexOf(';');
            if (end <= index)
            {
                return string.Empty;
            }

            var raw = levelTextGroup.Substring(index, end - index);
            return SanitizeListLevelText(raw);
        }

        private static string SanitizeListLevelText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch is '{' or '}')
                {
                    continue;
                }

                if (ch == '\\')
                {
                    // Skip control words while preserving escaped punctuation and Unicode fallback chars.
                    if (i + 1 < value.Length && (value[i + 1] == '\\' || value[i + 1] == '{' || value[i + 1] == '}'))
                    {
                        i++;
                        builder.Append(value[i]);
                        continue;
                    }

                    while (i + 1 < value.Length && char.IsLetter(value[i + 1]))
                    {
                        i++;
                    }

                    if (i + 1 < value.Length && (value[i + 1] == '-' || char.IsDigit(value[i + 1])))
                    {
                        i++;
                        while (i + 1 < value.Length && char.IsDigit(value[i + 1]))
                        {
                            i++;
                        }
                    }

                    continue;
                }

                if (!char.IsControl(ch))
                {
                    builder.Append(ch);
                }
            }

            var text = builder.ToString().Trim();
            return text.EndsWith(';') ? text[..^1].Trim() : text;
        }

        private static ListNumberFormat MapNfcToListNumberFormat(int nfc)
        {
            return nfc switch
            {
                1 => ListNumberFormat.UpperRoman,
                2 => ListNumberFormat.LowerRoman,
                3 => ListNumberFormat.UpperLetter,
                4 => ListNumberFormat.LowerLetter,
                23 => ListNumberFormat.Bullet,
                _ => ListNumberFormat.Decimal
            };
        }

        private static bool TryReadControlValue(string text, string controlWord, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(controlWord))
            {
                return false;
            }

            var pattern = "\\" + controlWord;
            var searchStart = 0;
            while (searchStart < text.Length)
            {
                var index = text.IndexOf(pattern, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return false;
                }

                var numberStart = index + pattern.Length;
                if (numberStart < text.Length && char.IsLetter(text[numberStart]))
                {
                    searchStart = numberStart + 1;
                    continue;
                }

                var sign = 1;
                if (numberStart < text.Length && text[numberStart] == '-')
                {
                    sign = -1;
                    numberStart++;
                }

                var cursor = numberStart;
                while (cursor < text.Length && char.IsDigit(text[cursor]))
                {
                    cursor++;
                }

                if (cursor == numberStart)
                {
                    searchStart = index + pattern.Length;
                    continue;
                }

                if (!int.TryParse(text.AsSpan(numberStart, cursor - numberStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    searchStart = cursor;
                    continue;
                }

                value = parsed * sign;
                return true;
            }

            return false;
        }

        private static bool TryExtractGroupByToken(string text, string token, out string group)
        {
            group = string.Empty;
            var index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var extracted = ExtractGroup(text, index);
            if (string.IsNullOrEmpty(extracted))
            {
                return false;
            }

            group = extracted;
            return true;
        }

        private static List<string> EnumerateTopLevelChildGroups(string group)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(group) || group.Length < 2 || group[0] != '{')
            {
                return result;
            }

            var depth = 0;
            var start = -1;
            for (var i = 1; i < group.Length - 1; i++)
            {
                var ch = group[i];
                if (ch == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }

                    depth++;
                }
                else if (ch == '}')
                {
                    if (depth == 0)
                    {
                        continue;
                    }

                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        result.Add(group.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            return result;
        }

        private static Dictionary<int, string> ParseFontTable(string rtf)
        {
            var fonts = new Dictionary<int, string>();
            var index = rtf.IndexOf("{\\fonttbl", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return fonts;
            }

            var group = ExtractGroup(rtf, index);
            if (group is null)
            {
                return fonts;
            }

            var pos = 0;
            while (pos < group.Length)
            {
                if (group[pos] == '\\' && pos + 1 < group.Length && group[pos + 1] == 'f')
                {
                    pos += 2;
                    var start = pos;
                    while (pos < group.Length && char.IsDigit(group[pos]))
                    {
                        pos++;
                    }

                    if (pos == start)
                    {
                        pos++;
                        continue;
                    }

                    if (!int.TryParse(group.Substring(start, pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fontIndex))
                    {
                        continue;
                    }

                    var nameBuilder = new StringBuilder();
                    while (pos < group.Length && group[pos] != ';')
                    {
                        if (group[pos] == '\\')
                        {
                            pos++;
                            while (pos < group.Length && char.IsLetter(group[pos]))
                            {
                                pos++;
                            }

                            while (pos < group.Length && (group[pos] == '-' || char.IsDigit(group[pos])))
                            {
                                pos++;
                            }
                        }
                        else if (group[pos] != '{' && group[pos] != '}')
                        {
                            nameBuilder.Append(group[pos]);
                            pos++;
                        }
                        else
                        {
                            pos++;
                        }
                    }

                    if (pos < group.Length && group[pos] == ';')
                    {
                        pos++;
                    }

                    var name = nameBuilder.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !fonts.ContainsKey(fontIndex))
                    {
                        fonts[fontIndex] = name;
                    }

                    continue;
                }

                pos++;
            }

            return fonts;
        }

        private static List<DocColor?> ParseColorTable(string rtf)
        {
            var colors = new List<DocColor?> { null };
            var index = rtf.IndexOf("{\\colortbl", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return colors;
            }

            var group = ExtractGroup(rtf, index);
            if (group is null)
            {
                return colors;
            }

            var red = 0;
            var green = 0;
            var blue = 0;
            var firstEntry = true;
            var hasColorComponents = false;
            var pos = 0;
            while (pos < group.Length)
            {
                if (group[pos] == '\\')
                {
                    pos++;
                    var start = pos;
                    while (pos < group.Length && char.IsLetter(group[pos]))
                    {
                        pos++;
                    }
                    var word = group.Substring(start, pos - start);

                    var sign = 1;
                    if (pos < group.Length && group[pos] == '-')
                    {
                        sign = -1;
                        pos++;
                    }

                    var numberStart = pos;
                    while (pos < group.Length && char.IsDigit(group[pos]))
                    {
                        pos++;
                    }

                    if (int.TryParse(group.Substring(numberStart, pos - numberStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        value *= sign;
                        switch (word)
                        {
                            case "red":
                                red = value;
                                hasColorComponents = true;
                                break;
                            case "green":
                                green = value;
                                hasColorComponents = true;
                                break;
                            case "blue":
                                blue = value;
                                hasColorComponents = true;
                                break;
                        }
                    }
                }
                else if (group[pos] == ';')
                {
                    if (firstEntry && !hasColorComponents)
                    {
                        firstEntry = false;
                    }
                    else
                    {
                        colors.Add(new DocColor(ClampColor(red), ClampColor(green), ClampColor(blue)));
                        firstEntry = false;
                    }

                    red = 0;
                    green = 0;
                    blue = 0;
                    hasColorComponents = false;
                    pos++;
                }
                else
                {
                    pos++;
                }
            }

            return colors;
        }

        private static string? ExtractGroup(string text, int startIndex)
        {
            var braceIndex = text.IndexOf('{', startIndex);
            if (braceIndex < 0)
            {
                return null;
            }

            var depth = 0;
            for (var i = braceIndex; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(braceIndex, i - braceIndex + 1);
                    }
                }
            }

            return null;
        }

        private static byte ClampColor(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return (byte)value;
        }

        private enum BorderTarget
        {
            None,
            Cell,
            Table
        }

        private enum BorderSide
        {
            Top,
            Bottom,
            Left,
            Right,
            InsideHorizontal,
            InsideVertical
        }

        private sealed class TableCellDefinition
        {
            public TableCellDefinition(TableCellProperties properties, TableCellVerticalMerge verticalMerge)
            {
                Properties = properties;
                VerticalMerge = verticalMerge;
            }

            public TableCellProperties Properties { get; }

            public TableCellVerticalMerge VerticalMerge { get; }
        }

        private sealed class PendingCellState
        {
            public TableCellProperties Properties { get; } = new();

            public PendingPaddingState Padding { get; } = new();

            public TableCellVerticalMerge VerticalMerge { get; set; }

            public int? PreferredWidth { get; set; }

            public int? PreferredWidthUnitCode { get; set; }

            public void Reset()
            {
                Properties.Padding = null;
                Properties.ShadingColor = null;
                Properties.VerticalAlignment = null;
                Properties.TextDirection = null;
                Properties.PreferredWidth = null;
                Properties.PreferredWidthUnit = null;
                Properties.Borders.Top = null;
                Properties.Borders.Bottom = null;
                Properties.Borders.Left = null;
                Properties.Borders.Right = null;
                Padding.Reset();
                VerticalMerge = TableCellVerticalMerge.None;
                PreferredWidth = null;
                PreferredWidthUnitCode = null;
            }
        }

        private sealed class PendingPaddingState
        {
            public int? Left { get; set; }

            public int? Top { get; set; }

            public int? Right { get; set; }

            public int? Bottom { get; set; }

            public int? LeftUnit { get; set; }

            public int? TopUnit { get; set; }

            public int? RightUnit { get; set; }

            public int? BottomUnit { get; set; }

            public bool HasAnyValue => Left.HasValue || Top.HasValue || Right.HasValue || Bottom.HasValue;

            public void Reset()
            {
                Left = null;
                Top = null;
                Right = null;
                Bottom = null;
                LeftUnit = null;
                TopUnit = null;
                RightUnit = null;
                BottomUnit = null;
            }
        }

        private sealed class ParserState
        {
            public CharacterStyle Character { get; set; } = new();
            public ParagraphState Paragraph { get; set; } = new();
            public FieldState? Field { get; set; }
            public bool SkipDestination { get; set; }
            public bool IgnoreNextDestination { get; set; }

            public ParserState Clone()
            {
                return new ParserState
                {
                    Character = Character.Clone(),
                    Paragraph = Paragraph.Clone(),
                    Field = Field?.Clone(),
                    SkipDestination = SkipDestination,
                    IgnoreNextDestination = IgnoreNextDestination
                };
            }
        }

        private sealed class CharacterStyle
        {
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public bool Underline { get; set; }
            public bool Strikethrough { get; set; }
            public int FontIndex { get; set; } = -1;
            public float? FontSize { get; set; }
            public int? ColorIndex { get; set; }
            public int? HighlightIndex { get; set; }
            public DocVerticalPosition VerticalPosition { get; set; } = DocVerticalPosition.Normal;
            public HyperlinkInfo? Hyperlink { get; set; }

            public CharacterStyle Clone()
            {
                return new CharacterStyle
                {
                    Bold = Bold,
                    Italic = Italic,
                    Underline = Underline,
                    Strikethrough = Strikethrough,
                    FontIndex = FontIndex,
                    FontSize = FontSize,
                    ColorIndex = ColorIndex,
                    HighlightIndex = HighlightIndex,
                    VerticalPosition = VerticalPosition,
                    Hyperlink = Hyperlink is null ? null : new HyperlinkInfo(Hyperlink.Uri, Hyperlink.Anchor, Hyperlink.Tooltip)
                };
            }
        }

        private sealed class FieldState
        {
            public string? Instruction { get; set; }
            public HyperlinkInfo? Hyperlink { get; set; }
            public bool InInstruction { get; set; }
            public bool InResult { get; set; }

            public void AppendInstruction(string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                Instruction = string.Concat(Instruction ?? string.Empty, text);
            }

            public FieldState Clone()
            {
                return new FieldState
                {
                    Instruction = Instruction,
                    Hyperlink = Hyperlink is null ? null : new HyperlinkInfo(Hyperlink.Uri, Hyperlink.Anchor, Hyperlink.Tooltip),
                    InInstruction = InInstruction,
                    InResult = InResult
                };
            }
        }

        private sealed class ParagraphState
        {
            public ParagraphAlignment? Alignment { get; set; }
            public float? SpacingBefore { get; set; }
            public float? SpacingAfter { get; set; }
            public float? IndentLeft { get; set; }
            public float? IndentRight { get; set; }
            public float? FirstLineIndent { get; set; }
            public int? LineSpacing { get; set; }
            public DocLineSpacingRule? LineSpacingRule { get; set; }
            public int? ListOverrideIndex { get; set; }
            public int? ListLevel { get; set; }
            public List<TabStopDefinition> TabStops { get; } = new();
            public bool InTable { get; set; }

            public ParagraphState Clone()
            {
                var clone = new ParagraphState
                {
                    Alignment = Alignment,
                    SpacingBefore = SpacingBefore,
                    SpacingAfter = SpacingAfter,
                    IndentLeft = IndentLeft,
                    IndentRight = IndentRight,
                    FirstLineIndent = FirstLineIndent,
                    LineSpacing = LineSpacing,
                    LineSpacingRule = LineSpacingRule,
                    ListOverrideIndex = ListOverrideIndex,
                    ListLevel = ListLevel,
                    InTable = InTable
                };

                for (var i = 0; i < TabStops.Count; i++)
                {
                    clone.TabStops.Add(TabStops[i].Clone());
                }

                return clone;
            }
        }
    }
}
