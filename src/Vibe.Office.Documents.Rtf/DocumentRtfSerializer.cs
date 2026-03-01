using System.Globalization;
using System.Linq;
using System.Text;
using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public static class DocumentRtfSerializer
{
    public static string ToRtf(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var writer = new RtfWriter(document);
        return writer.Write();
    }

    private sealed class RtfWriter
    {
        private const int DefaultColumnWidthTwips = 1800;

        private readonly Document _document;
        private readonly Dictionary<string, int> _fonts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DocColor, int> _colors = new();
        private readonly Dictionary<(int ListId, int Level), int> _listCounters = new();
        private readonly Dictionary<int, ListDefinition> _listDefinitions = new();
        private readonly Dictionary<ListMetadataKey, int> _generatedListIds = new();
        private readonly StringBuilder _builder = new(4096);

        public RtfWriter(Document document)
        {
            _document = document;
            BuildListMetadata();
            CollectResources();
        }

        public string Write()
        {
            _builder.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0");
            AppendFontTable();
            AppendColorTable();
            AppendListTables();
            _builder.Append("\\viewkind4\\uc1 ");
            AppendSectionSetup();
            AppendHeaderFooterDestinations();
            var currentSectionIndex = 0;

            for (var i = 0; i < _document.Blocks.Count; i++)
            {
                switch (_document.Blocks[i])
                {
                    case ParagraphBlock paragraph:
                        WriteParagraph(paragraph, inTable: false);
                        break;
                    case TableBlock table:
                        WriteTable(table);
                        break;
                    case PageBreakBlock:
                        _builder.Append("\\page ");
                        break;
                    case ColumnBreakBlock:
                        _builder.Append("\\column ");
                        break;
                    case SectionBreakBlock sectionBreak:
                        WriteSectionBreak(sectionBreak, currentSectionIndex);
                        var nextIndex = sectionBreak.SectionIndex ?? Math.Min(currentSectionIndex + 1, _document.SectionCount - 1);
                        currentSectionIndex = Math.Max(0, nextIndex);
                        break;
                }
            }

            _builder.Append('}');
            return _builder.ToString();
        }

        private void CollectResources()
        {
            AddFont(_document.DefaultTextStyle.FontFamily);
            AddColor(_document.DefaultTextStyle.Color);
            if (_document.DefaultTextStyle.HighlightColor.HasValue)
            {
                AddColor(_document.DefaultTextStyle.HighlightColor.Value);
            }

            CollectHeaderFooterResources(_document.Header);
            CollectHeaderFooterResources(_document.Footer);
            CollectHeaderFooterResources(_document.FirstHeader);
            CollectHeaderFooterResources(_document.FirstFooter);
            CollectHeaderFooterResources(_document.EvenHeader);
            CollectHeaderFooterResources(_document.EvenFooter);

            for (var i = 0; i < _document.Blocks.Count; i++)
            {
                CollectBlockResources(_document.Blocks[i]);
            }
        }

        private void CollectHeaderFooterResources(HeaderFooter headerFooter)
        {
            if (!ShouldWriteHeaderFooterDestination(headerFooter))
            {
                return;
            }

            for (var i = 0; i < headerFooter.Blocks.Count; i++)
            {
                CollectBlockResources(headerFooter.Blocks[i]);
            }
        }

        private void BuildListMetadata()
        {
            foreach (var pair in _document.ListDefinitions)
            {
                if (pair.Key > 0)
                {
                    _listDefinitions[pair.Key] = pair.Value.Clone();
                }
            }

            var nextListId = Math.Max(1, _listDefinitions.Keys.DefaultIfEmpty(0).Max() + 1);
            var listInfos = new List<ListInfo>();
            CollectListInfosFromBlocks(_document.Blocks, listInfos);

            for (var i = 0; i < listInfos.Count; i++)
            {
                var info = listInfos[i];
                if (info.Kind == ListKind.None)
                {
                    continue;
                }

                var listId = ResolveListId(info, ref nextListId);
                if (!_listDefinitions.TryGetValue(listId, out var definition))
                {
                    definition = info.Kind == ListKind.Bullet
                        ? ListDefinitionDefaults.CreateBulleted(listId, info.Level + 1)
                        : ListDefinitionDefaults.CreateNumbered(listId, multilevel: false, info.Level + 1);
                    _listDefinitions[listId] = definition;
                }

                EnsureListLevelDefinition(definition, info);
            }
        }

        private static void CollectListInfosFromBlocks(IReadOnlyList<Block> blocks, List<ListInfo> target)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                switch (blocks[i])
                {
                    case ParagraphBlock paragraph when paragraph.ListInfo is not null:
                        target.Add(paragraph.ListInfo);
                        break;
                    case TableBlock table:
                        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                        {
                            var row = table.Rows[rowIndex];
                            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                            {
                                CollectListInfosFromBlocks(row.Cells[cellIndex].Blocks, target);
                            }
                        }

                        break;
                }
            }
        }

        private int ResolveListId(ListInfo info, ref int nextListId)
        {
            if (info.ListId.HasValue && info.ListId.Value > 0)
            {
                return info.ListId.Value;
            }

            var key = CreateListMetadataKey(info);
            if (_generatedListIds.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var generated = nextListId++;
            _generatedListIds[key] = generated;
            return generated;
        }

        private static ListMetadataKey CreateListMetadataKey(ListInfo info)
        {
            var format = info.NumberFormat ?? (info.Kind == ListKind.Bullet ? ListNumberFormat.Bullet : ListNumberFormat.Decimal);
            return new ListMetadataKey(
                info.Kind,
                Math.Max(0, info.Level),
                format,
                NormalizeListText(info.LevelText),
                NormalizeListText(info.BulletSymbol),
                info.StartAt ?? 1);
        }

        private static string? NormalizeListText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static void EnsureListLevelDefinition(ListDefinition definition, ListInfo info)
        {
            if (!definition.Levels.ContainsKey(info.Level))
            {
                ListDefinitionDefaults.EnsureLevels(
                    definition,
                    info.Kind == ListKind.Bullet ? ListKind.Bullet : ListKind.Numbered,
                    info.Level,
                    multilevel: false);
            }

            var level = definition.Levels[info.Level];
            var format = info.NumberFormat ?? (info.Kind == ListKind.Bullet ? ListNumberFormat.Bullet : ListNumberFormat.Decimal);
            level.Format = format;

            if (format == ListNumberFormat.Bullet)
            {
                if (!string.IsNullOrWhiteSpace(info.BulletSymbol))
                {
                    level.BulletSymbol = info.BulletSymbol;
                }

                level.BulletSymbol ??= "•";
                level.LevelText = level.BulletSymbol;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(info.LevelText))
                {
                    level.LevelText = info.LevelText;
                }
                else if (string.IsNullOrWhiteSpace(level.LevelText))
                {
                    level.LevelText = $"%{info.Level + 1}.";
                }
            }

            if (info.StartAt.HasValue && info.StartAt.Value > 0)
            {
                level.StartAt = info.StartAt.Value;
            }

            if (info.LeftIndent.HasValue)
            {
                level.LeftIndent = info.LeftIndent.Value;
            }

            if (info.HangingIndent.HasValue)
            {
                level.HangingIndent = info.HangingIndent.Value;
            }

            if (info.TabStop.HasValue)
            {
                level.TabStop = info.TabStop.Value;
            }
        }

        private void CollectBlockResources(Block block)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    CollectParagraphResources(paragraph);
                    break;
                case TableBlock table:
                    CollectTableResources(table);
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        var row = table.Rows[rowIndex];
                        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                        {
                            var cell = row.Cells[cellIndex];
                            for (var blockIndex = 0; blockIndex < cell.Blocks.Count; blockIndex++)
                            {
                                CollectBlockResources(cell.Blocks[blockIndex]);
                            }
                        }
                    }
                    break;
            }
        }

        private void CollectTableResources(TableBlock table)
        {
            AddColor(table.Properties.ShadingColor);
            AddTableBorderResources(table.Properties.Borders.Top);
            AddTableBorderResources(table.Properties.Borders.Bottom);
            AddTableBorderResources(table.Properties.Borders.Left);
            AddTableBorderResources(table.Properties.Borders.Right);
            AddTableBorderResources(table.Properties.Borders.InsideHorizontal);
            AddTableBorderResources(table.Properties.Borders.InsideVertical);

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                AddColor(row.Properties.ShadingColor);

                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    var cell = row.Cells[cellIndex];
                    AddColor(cell.Properties.ShadingColor);
                    AddTableBorderResources(cell.Properties.Borders.Top);
                    AddTableBorderResources(cell.Properties.Borders.Bottom);
                    AddTableBorderResources(cell.Properties.Borders.Left);
                    AddTableBorderResources(cell.Properties.Borders.Right);
                }
            }
        }

        private void AddTableBorderResources(BorderLine? border)
        {
            if (border is null)
            {
                return;
            }

            AddColor(border.Color);
        }

        private void CollectParagraphResources(ParagraphBlock paragraph)
        {
            for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
            {
                if (paragraph.Inlines[inlineIndex] is not RunInline run)
                {
                    continue;
                }

                var style = ResolveRunStyle(run);
                AddStyleResources(style);
            }
        }

        private void AddStyleResources(TextStyle style)
        {
            AddFont(style.FontFamily);
            AddColor(style.Color);
            if (style.HighlightColor.HasValue)
            {
                AddColor(style.HighlightColor.Value);
            }
        }

        private void AddFont(string? fontFamily)
        {
            var font = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
            if (!_fonts.ContainsKey(font))
            {
                _fonts[font] = _fonts.Count;
            }
        }

        private void AddColor(DocColor color)
        {
            if (!_colors.ContainsKey(color))
            {
                _colors[color] = _colors.Count + 1;
            }
        }

        private void AddColor(DocColor? color)
        {
            if (color.HasValue)
            {
                AddColor(color.Value);
            }
        }

        private void AppendFontTable()
        {
            _builder.Append("{\\fonttbl");
            foreach (var pair in _fonts.OrderBy(static item => item.Value))
            {
                _builder.Append("{\\f")
                    .Append(pair.Value.ToString(CultureInfo.InvariantCulture))
                    .Append("\\fnil ");
                AppendEscapedText(pair.Key);
                _builder.Append(";}");
            }

            _builder.Append('}');
        }

        private void AppendColorTable()
        {
            _builder.Append("{\\colortbl;");
            foreach (var pair in _colors.OrderBy(static item => item.Value))
            {
                var color = pair.Key;
                _builder.Append("\\red").Append(color.R.ToString(CultureInfo.InvariantCulture));
                _builder.Append("\\green").Append(color.G.ToString(CultureInfo.InvariantCulture));
                _builder.Append("\\blue").Append(color.B.ToString(CultureInfo.InvariantCulture));
                _builder.Append(';');
            }

            _builder.Append('}');
        }

        private void AppendListTables()
        {
            if (_listDefinitions.Count == 0)
            {
                return;
            }

            _builder.Append("{\\*\\listtable");
            foreach (var definition in _listDefinitions.Values.OrderBy(static item => item.Id))
            {
                AppendListDefinition(definition);
            }

            _builder.Append('}');
            _builder.Append("{\\*\\listoverridetable");
            foreach (var definition in _listDefinitions.Values.OrderBy(static item => item.Id))
            {
                _builder.Append("{\\listoverride\\listid")
                    .Append(definition.Id.ToString(CultureInfo.InvariantCulture))
                    .Append("\\listoverridecount0\\ls")
                    .Append(definition.Id.ToString(CultureInfo.InvariantCulture))
                    .Append('}');
            }

            _builder.Append('}');
        }

        private void AppendListDefinition(ListDefinition definition)
        {
            _builder.Append("{\\list\\listtemplateid")
                .Append(definition.Id.ToString(CultureInfo.InvariantCulture));
            var levels = definition.Levels.Count == 0
                ? new[] { new ListLevelDefinition(0) }
                : definition.Levels
                    .OrderBy(static pair => pair.Key)
                    .Select(static pair => pair.Value)
                    .ToArray();
            for (var i = 0; i < levels.Length; i++)
            {
                AppendListLevel(levels[i], i);
            }

            _builder.Append("\\listid").Append(definition.Id.ToString(CultureInfo.InvariantCulture)).Append('}');
        }

        private void AppendHeaderFooterDestinations()
        {
            AppendHeaderFooterDestination("header", _document.Header);
            AppendHeaderFooterDestination("footer", _document.Footer);
            AppendHeaderFooterDestination("headerf", _document.FirstHeader);
            AppendHeaderFooterDestination("footerf", _document.FirstFooter);
            AppendHeaderFooterDestination("headerl", _document.EvenHeader);
            AppendHeaderFooterDestination("footerl", _document.EvenFooter);
        }

        private void AppendHeaderFooterDestination(string destinationWord, HeaderFooter headerFooter)
        {
            if (!ShouldWriteHeaderFooterDestination(headerFooter))
            {
                return;
            }

            _builder.Append("{\\")
                .Append(destinationWord)
                .Append(' ');
            WriteHeaderFooterBlocks(headerFooter.Blocks);
            _builder.Append("} ");
        }

        private void WriteHeaderFooterBlocks(IReadOnlyList<Block> blocks)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                switch (blocks[i])
                {
                    case ParagraphBlock paragraph:
                        WriteParagraph(paragraph, inTable: false);
                        break;
                    case TableBlock table:
                        WriteTable(table);
                        break;
                    case PageBreakBlock:
                        _builder.Append("\\page ");
                        break;
                    case ColumnBreakBlock:
                        _builder.Append("\\column ");
                        break;
                }
            }
        }

        private static bool ShouldWriteHeaderFooterDestination(HeaderFooter headerFooter)
        {
            return headerFooter.IsDefined || HasMeaningfulBlocks(headerFooter.Blocks);
        }

        private static bool HasMeaningfulBlocks(IReadOnlyList<Block> blocks)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] is ParagraphBlock paragraph && IsParagraphEmpty(paragraph))
                {
                    continue;
                }

                return true;
            }

            return false;
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

        private void AppendListLevel(ListLevelDefinition level, int fallbackLevel)
        {
            var levelIndex = Math.Max(0, level.Level);
            var format = level.Format;
            var nfc = MapListNumberFormatToNfc(format);
            var startAt = Math.Max(1, level.StartAt);
            var leftIndent = level.LeftIndent ?? ((levelIndex + 1) * 24f);
            var hangingIndent = level.HangingIndent ?? 12f;
            var tabStop = level.TabStop ?? leftIndent;

            _builder.Append("{\\listlevel");
            _builder.Append("\\levelnfc").Append(nfc.ToString(CultureInfo.InvariantCulture));
            _builder.Append("\\levelnfcn").Append(nfc.ToString(CultureInfo.InvariantCulture));
            _builder.Append("\\leveljc0\\leveljcn0\\levelfollow0");
            _builder.Append("\\levelstartat").Append(startAt.ToString(CultureInfo.InvariantCulture));
            AppendListLevelText(format, level, levelIndex > 0 ? levelIndex : fallbackLevel);
            _builder.Append("{\\levelnumbers;}");
            _builder.Append("\\fi-").Append(DipToTwips(hangingIndent).ToString(CultureInfo.InvariantCulture));
            _builder.Append("\\li").Append(DipToTwips(leftIndent).ToString(CultureInfo.InvariantCulture));
            _builder.Append("\\tx").Append(DipToTwips(tabStop).ToString(CultureInfo.InvariantCulture));
            _builder.Append('}');
        }

        private void AppendListLevelText(ListNumberFormat format, ListLevelDefinition level, int levelIndex)
        {
            var levelText = format == ListNumberFormat.Bullet
                ? (string.IsNullOrWhiteSpace(level.BulletSymbol) ? "•" : level.BulletSymbol)
                : (string.IsNullOrWhiteSpace(level.LevelText) ? $"%{levelIndex + 1}." : level.LevelText);
            _builder.Append("{\\leveltext ");
            AppendEscapedText(levelText);
            _builder.Append(";}");
        }

        private static int MapListNumberFormatToNfc(ListNumberFormat format)
        {
            return format switch
            {
                ListNumberFormat.UpperRoman => 1,
                ListNumberFormat.LowerRoman => 2,
                ListNumberFormat.UpperLetter => 3,
                ListNumberFormat.LowerLetter => 4,
                ListNumberFormat.Bullet => 23,
                _ => 0
            };
        }

        private void WriteSectionBreak(SectionBreakBlock sectionBreak, int sectionIndex)
        {
            SectionProperties sectionProperties;
            if (sectionBreak.Properties.HasValues)
            {
                sectionProperties = sectionBreak.Properties;
            }
            else if (sectionIndex >= 0 && sectionIndex < _document.SectionCount)
            {
                sectionProperties = _document.GetSection(sectionIndex).Properties;
            }
            else
            {
                sectionProperties = _document.SectionProperties;
            }

            AppendSectionSetup(sectionProperties, includeDocumentFlags: false);
            _builder.Append(sectionBreak.BreakType switch
            {
                SectionBreakType.Continuous => "\\sbknone",
                SectionBreakType.EvenPage => "\\sbkeven",
                SectionBreakType.OddPage => "\\sbkodd",
                SectionBreakType.NextColumn => "\\sbkcol",
                _ => "\\sbkpage"
            });
            _builder.Append("\\sect ");
        }

        private void AppendSectionSetup()
        {
            AppendSectionSetup(_document.SectionProperties, includeDocumentFlags: true);
        }

        private void AppendSectionSetup(SectionProperties section, bool includeDocumentFlags)
        {
            if (!HasSectionSetup(section, includeDocumentFlags))
            {
                return;
            }

            _builder.Append("\\sectd");

            if (includeDocumentFlags && _document.MirrorMargins)
            {
                _builder.Append("\\margmirror");
            }

            if (includeDocumentFlags && (_document.EvenAndOddHeaders || HasEvenPageHeaderFooter()))
            {
                _builder.Append("\\facingp");
            }

            if (includeDocumentFlags && _document.GutterAtTop)
            {
                _builder.Append("\\gutterprl");
            }

            AppendSectionTwipsControl("\\paperw", section.PageWidth, requirePositive: true);
            AppendSectionTwipsControl("\\paperh", section.PageHeight, requirePositive: true);
            AppendSectionTwipsControl("\\margl", section.MarginLeft, requirePositive: false);
            AppendSectionTwipsControl("\\margr", section.MarginRight, requirePositive: false);
            AppendSectionTwipsControl("\\margt", section.MarginTop, requirePositive: false);
            AppendSectionTwipsControl("\\margb", section.MarginBottom, requirePositive: false);
            AppendSectionTwipsControl("\\headery", section.HeaderOffset, requirePositive: false);
            AppendSectionTwipsControl("\\footery", section.FooterOffset, requirePositive: false);
            AppendSectionTwipsControl("\\gutter", section.Gutter, requirePositive: false);

            if (section.Orientation.HasValue)
            {
                _builder.Append(section.Orientation.Value == PageOrientation.Landscape ? "\\landscape" : "\\portrait");
            }

            var hasFirstPageHeaderFooter = includeDocumentFlags && HasFirstPageHeaderFooter();
            if (section.DifferentFirstPageHeaderFooter.HasValue)
            {
                var useDifferentFirstPage = section.DifferentFirstPageHeaderFooter.Value || hasFirstPageHeaderFooter;
                _builder.Append(useDifferentFirstPage ? "\\titlepg" : "\\titlepg0");
            }
            else if (hasFirstPageHeaderFooter)
            {
                _builder.Append("\\titlepg");
            }

            var hasColumnCount = section.ColumnCount.HasValue && section.ColumnCount.Value > 0;
            if (hasColumnCount)
            {
                _builder.Append("\\cols")
                    .Append(section.ColumnCount!.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (section.ColumnGap.HasValue && section.ColumnGap.Value >= 0f)
            {
                _builder.Append("\\colsx")
                    .Append(DipToTwips(section.ColumnGap.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (section.ColumnSeparator.HasValue)
            {
                _builder.Append(section.ColumnSeparator.Value ? "\\linebetcol" : "\\linebetcol0");
            }

            AppendSectionColumnDefinitions(section, hasColumnCount);
            _builder.Append(' ');
        }

        private bool HasSectionSetup(SectionProperties section, bool includeDocumentFlags)
        {
            return (includeDocumentFlags && (_document.MirrorMargins
                || _document.EvenAndOddHeaders
                || _document.GutterAtTop
                || HasEvenPageHeaderFooter()
                || HasFirstPageHeaderFooter()))
                || section.PageWidth.HasValue
                || section.PageHeight.HasValue
                || section.Orientation.HasValue
                || section.MarginLeft.HasValue
                || section.MarginRight.HasValue
                || section.MarginTop.HasValue
                || section.MarginBottom.HasValue
                || section.HeaderOffset.HasValue
                || section.FooterOffset.HasValue
                || section.Gutter.HasValue
                || section.DifferentFirstPageHeaderFooter.HasValue
                || section.ColumnCount.HasValue
                || section.ColumnGap.HasValue
                || section.ColumnSeparator.HasValue
                || section.ColumnWidths.Count > 0
                || section.ColumnGaps.Count > 0;
        }

        private bool HasFirstPageHeaderFooter()
        {
            return ShouldWriteHeaderFooterDestination(_document.FirstHeader)
                || ShouldWriteHeaderFooterDestination(_document.FirstFooter);
        }

        private bool HasEvenPageHeaderFooter()
        {
            return ShouldWriteHeaderFooterDestination(_document.EvenHeader)
                || ShouldWriteHeaderFooterDestination(_document.EvenFooter);
        }

        private void AppendSectionTwipsControl(string controlWord, float? dip, bool requirePositive)
        {
            if (!dip.HasValue || float.IsNaN(dip.Value) || float.IsInfinity(dip.Value))
            {
                return;
            }

            var twips = DipToTwips(dip.Value);
            if (requirePositive)
            {
                if (twips <= 0)
                {
                    return;
                }
            }
            else if (twips < 0)
            {
                return;
            }

            _builder.Append(controlWord)
                .Append(twips.ToString(CultureInfo.InvariantCulture));
        }

        private void AppendSectionColumnDefinitions(SectionProperties section, bool hasColumnCount)
        {
            if (section.ColumnWidths.Count == 0 && section.ColumnGaps.Count == 0)
            {
                return;
            }

            var columnCount = ResolveSectionColumnCount(section);
            if (!hasColumnCount && columnCount > 0)
            {
                _builder.Append("\\cols").Append(columnCount.ToString(CultureInfo.InvariantCulture));
            }

            for (var i = 0; i < columnCount; i++)
            {
                _builder.Append("\\colno").Append(i.ToString(CultureInfo.InvariantCulture));

                if (i < section.ColumnWidths.Count && section.ColumnWidths[i] > 0f)
                {
                    _builder.Append("\\colw")
                        .Append(DipToTwips(section.ColumnWidths[i]).ToString(CultureInfo.InvariantCulture));
                }

                if (i < section.ColumnGaps.Count
                    && !float.IsNaN(section.ColumnGaps[i])
                    && section.ColumnGaps[i] >= 0f)
                {
                    _builder.Append("\\colsr")
                        .Append(DipToTwips(section.ColumnGaps[i]).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private static int ResolveSectionColumnCount(SectionProperties section)
        {
            if (section.ColumnCount.HasValue && section.ColumnCount.Value > 0)
            {
                return section.ColumnCount.Value;
            }

            if (section.ColumnWidths.Count > 0)
            {
                return section.ColumnWidths.Count;
            }

            if (section.ColumnGaps.Count > 0)
            {
                return section.ColumnGaps.Count + 1;
            }

            return 1;
        }

        private void WriteParagraph(ParagraphBlock paragraph, bool inTable)
        {
            _builder.Append("\\pard");
            if (inTable)
            {
                _builder.Append("\\intbl");
            }

            AppendParagraphProperties(paragraph.Properties);
            var hasListMetadata = !inTable && TryAppendParagraphListMetadata(paragraph);
            var listPrefix = inTable || hasListMetadata ? null : ResolveListPrefix(paragraph.ListInfo);
            if (!string.IsNullOrEmpty(listPrefix))
            {
                var style = _document.DefaultTextStyle.Clone();
                AppendRun(style, listPrefix);
            }

            if (paragraph.Inlines.Count > 0)
            {
                for (var i = 0; i < paragraph.Inlines.Count; i++)
                {
                    switch (paragraph.Inlines[i])
                    {
                        case RunInline run:
                        {
                            var style = ResolveRunStyle(run);
                            AppendRun(style, run.GetText(), run.Hyperlink);
                            break;
                        }
                        case ImageInline image:
                            AppendImageInline(image);
                            break;
                        case FootnoteReferenceInline footnote:
                            AppendRun(_document.DefaultTextStyle, footnote.Id.ToString(CultureInfo.InvariantCulture));
                            break;
                        case EndnoteReferenceInline endnote:
                            AppendRun(_document.DefaultTextStyle, endnote.Id.ToString(CultureInfo.InvariantCulture));
                            break;
                        case CommentReferenceInline comment:
                            AppendRun(_document.DefaultTextStyle, comment.Id.ToString(CultureInfo.InvariantCulture));
                            break;
                        case MetadataStartInline:
                        case MetadataEndInline:
                        case FieldStartInline:
                        case FieldSeparatorInline:
                        case FieldEndInline:
                        case BookmarkStartInline:
                        case BookmarkEndInline:
                        case CommentRangeStartInline:
                        case CommentRangeEndInline:
                        case ContentControlStartInline:
                        case ContentControlEndInline:
                        case RevisionStartInline:
                        case RevisionEndInline:
                        case RevisionRangeStartInline:
                        case RevisionRangeEndInline:
                            break;
                        default:
                            AppendRun(_document.DefaultTextStyle, DocumentConstants.ObjectReplacementChar.ToString());
                            break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(paragraph.Text))
            {
                AppendRun(_document.DefaultTextStyle, paragraph.Text);
            }

            _builder.Append("\\par ");
        }

        private void WriteTable(TableBlock table)
        {
            var baseColumnWidths = BuildColumnWidthsTwips(table);
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                _builder.Append("\\trowd");
                AppendTableRowDefinition(table, row);

                if (row.Cells.Count == 0)
                {
                    var emptyCell = new TableCell();
                    emptyCell.Blocks.Add(new ParagraphBlock());
                    AppendTableCellDefinition(emptyCell, DefaultColumnWidthTwips);
                    _builder.Append("\\cellx").Append(DefaultColumnWidthTwips.ToString(CultureInfo.InvariantCulture));
                    WriteTableCell(emptyCell);
                    _builder.Append("\\row ");
                    continue;
                }

                var cumulative = 0;
                var columnCursor = 0;
                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    var cell = row.Cells[cellIndex];
                    var span = Math.Max(1, cell.ColumnSpan);
                    var widthTwips = ResolveCellWidthTwips(cell, baseColumnWidths, columnCursor, span);
                    AppendTableCellDefinition(cell, widthTwips);
                    cumulative += widthTwips;
                    _builder.Append("\\cellx").Append(cumulative.ToString(CultureInfo.InvariantCulture));
                    columnCursor += span;
                }

                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    WriteTableCell(row.Cells[cellIndex]);
                }

                _builder.Append("\\row ");
            }
        }

        private void AppendTableRowDefinition(TableBlock table, TableRow row)
        {
            if (table.Properties.Alignment.HasValue)
            {
                _builder.Append(table.Properties.Alignment.Value switch
                {
                    TableAlignment.Center => "\\trqc",
                    TableAlignment.Right => "\\trqr",
                    _ => "\\trql"
                });
            }

            if (table.Properties.LayoutMode.HasValue)
            {
                _builder.Append(table.Properties.LayoutMode.Value == TableLayoutMode.Fixed ? "\\trautofit0" : "\\trautofit1");
            }

            AppendTableWidthDefinition(table.Properties);

            if (table.Properties.Indent.HasValue)
            {
                _builder.Append("\\trleft")
                    .Append(DipToTwips(table.Properties.Indent.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (table.Properties.CellSpacing.HasValue && table.Properties.CellSpacing.Value >= 0f)
            {
                _builder.Append("\\trgaph")
                    .Append(DipToTwips(table.Properties.CellSpacing.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (table.Properties.CellPadding.HasValue)
            {
                AppendRowPadding(table.Properties.CellPadding.Value);
            }

            if (row.Properties.Height.HasValue && row.Properties.Height.Value > 0f)
            {
                var heightTwips = Math.Max(1, DipToTwips(row.Properties.Height.Value));
                if (row.Properties.HeightRule == TableRowHeightRule.Exact)
                {
                    heightTwips *= -1;
                }

                _builder.Append("\\trrh").Append(heightTwips.ToString(CultureInfo.InvariantCulture));
            }

            if (row.Properties.CantSplit.HasValue)
            {
                _builder.Append(row.Properties.CantSplit.Value ? "\\trkeep" : "\\trkeep0");
            }

            if (row.Properties.RepeatOnEachPage.HasValue)
            {
                _builder.Append(row.Properties.RepeatOnEachPage.Value ? "\\trhdr" : "\\trhdr0");
            }

            if (row.Properties.ShadingColor.HasValue && TryGetColorIndex(row.Properties.ShadingColor.Value, out var shadingIndex))
            {
                _builder.Append("\\trcbpat").Append(shadingIndex.ToString(CultureInfo.InvariantCulture));
            }

            AppendTableBorderDefinition("\\trbrdrt", table.Properties.Borders.Top);
            AppendTableBorderDefinition("\\trbrdrb", table.Properties.Borders.Bottom);
            AppendTableBorderDefinition("\\trbrdrl", table.Properties.Borders.Left);
            AppendTableBorderDefinition("\\trbrdrr", table.Properties.Borders.Right);
            AppendTableBorderDefinition("\\trbrdrh", table.Properties.Borders.InsideHorizontal);
            AppendTableBorderDefinition("\\trbrdrv", table.Properties.Borders.InsideVertical);
        }

        private void AppendTableWidthDefinition(TableProperties properties)
        {
            var widthUnit = properties.WidthUnit ?? TableWidthUnit.Dxa;
            if (widthUnit == TableWidthUnit.Auto)
            {
                _builder.Append("\\trftsWidth0\\trwWidth0");
                return;
            }

            if (widthUnit == TableWidthUnit.Pct && properties.Width.HasValue)
            {
                var pctValue = Math.Max(0, (int)Math.Round(properties.Width.Value * 50f));
                _builder.Append("\\trftsWidth2\\trwWidth").Append(pctValue.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (properties.Width.HasValue && properties.Width.Value > 0f)
            {
                _builder.Append("\\trftsWidth3\\trwWidth")
                    .Append(DipToTwips(properties.Width.Value).ToString(CultureInfo.InvariantCulture));
            }
        }

        private void AppendTableBorderDefinition(string sideControlWord, BorderLine? border)
        {
            if (border is null)
            {
                return;
            }

            _builder.Append(sideControlWord);
            var borderWord = GetBorderControlWord(border.Style);
            if (!string.IsNullOrEmpty(borderWord))
            {
                _builder.Append(borderWord);
            }

            var widthTwips = Math.Max(0, DipToTwips(border.Thickness));
            if (widthTwips > 0)
            {
                _builder.Append("\\brdrw").Append(widthTwips.ToString(CultureInfo.InvariantCulture));
            }

            if (TryGetColorIndex(border.Color, out var colorIndex))
            {
                _builder.Append("\\brdrcf").Append(colorIndex.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void AppendRowPadding(DocThickness padding)
        {
            _builder.Append("\\trpaddl").Append(DipToTwips(padding.Left).ToString(CultureInfo.InvariantCulture)).Append("\\trpaddfl3");
            _builder.Append("\\trpaddt").Append(DipToTwips(padding.Top).ToString(CultureInfo.InvariantCulture)).Append("\\trpaddft3");
            _builder.Append("\\trpaddr").Append(DipToTwips(padding.Right).ToString(CultureInfo.InvariantCulture)).Append("\\trpaddfr3");
            _builder.Append("\\trpaddb").Append(DipToTwips(padding.Bottom).ToString(CultureInfo.InvariantCulture)).Append("\\trpaddfb3");
        }

        private void AppendTableCellDefinition(TableCell cell, int resolvedWidthTwips)
        {
            if (cell.VerticalMerge == TableCellVerticalMerge.Restart)
            {
                _builder.Append("\\clvmgf");
            }
            else if (cell.VerticalMerge == TableCellVerticalMerge.Continue)
            {
                _builder.Append("\\clvmrg");
            }

            if (cell.Properties.VerticalAlignment.HasValue)
            {
                _builder.Append(cell.Properties.VerticalAlignment.Value switch
                {
                    TableCellVerticalAlignment.Center => "\\clvertalc",
                    TableCellVerticalAlignment.Bottom => "\\clvertalb",
                    _ => "\\clvertalt"
                });
            }

            if (cell.Properties.ShadingColor.HasValue && TryGetColorIndex(cell.Properties.ShadingColor.Value, out var shadingIndex))
            {
                _builder.Append("\\clcbpat").Append(shadingIndex.ToString(CultureInfo.InvariantCulture));
            }

            if (cell.Properties.Padding.HasValue)
            {
                var padding = cell.Properties.Padding.Value;
                _builder.Append("\\clpadl").Append(DipToTwips(padding.Left).ToString(CultureInfo.InvariantCulture)).Append("\\clpadfl3");
                _builder.Append("\\clpadt").Append(DipToTwips(padding.Top).ToString(CultureInfo.InvariantCulture)).Append("\\clpadft3");
                _builder.Append("\\clpadr").Append(DipToTwips(padding.Right).ToString(CultureInfo.InvariantCulture)).Append("\\clpadfr3");
                _builder.Append("\\clpadb").Append(DipToTwips(padding.Bottom).ToString(CultureInfo.InvariantCulture)).Append("\\clpadfb3");
            }

            AppendTableCellBorder("\\clbrdrt", cell.Properties.Borders.Top);
            AppendTableCellBorder("\\clbrdrb", cell.Properties.Borders.Bottom);
            AppendTableCellBorder("\\clbrdrl", cell.Properties.Borders.Left);
            AppendTableCellBorder("\\clbrdrr", cell.Properties.Borders.Right);

            var preferredUnit = cell.Properties.PreferredWidthUnit ?? TableWidthUnit.Dxa;
            if (preferredUnit == TableWidthUnit.Pct && cell.Properties.PreferredWidth.HasValue)
            {
                var pctValue = Math.Max(0, (int)Math.Round(cell.Properties.PreferredWidth.Value * 50f));
                _builder.Append("\\clftsWidth2\\clwWidth").Append(pctValue.ToString(CultureInfo.InvariantCulture));
            }
            else if (preferredUnit == TableWidthUnit.Auto)
            {
                _builder.Append("\\clftsWidth0\\clwWidth0");
            }
            else
            {
                var widthTwips = cell.Properties.PreferredWidth.HasValue && cell.Properties.PreferredWidth.Value > 0f
                    ? Math.Max(1, DipToTwips(cell.Properties.PreferredWidth.Value))
                    : Math.Max(1, resolvedWidthTwips);
                _builder.Append("\\clftsWidth3\\clwWidth").Append(widthTwips.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void AppendTableCellBorder(string sideControlWord, BorderLine? border)
        {
            if (border is null)
            {
                return;
            }

            _builder.Append(sideControlWord);
            var borderWord = GetBorderControlWord(border.Style);
            if (!string.IsNullOrEmpty(borderWord))
            {
                _builder.Append(borderWord);
            }

            var widthTwips = Math.Max(0, DipToTwips(border.Thickness));
            if (widthTwips > 0)
            {
                _builder.Append("\\brdrw").Append(widthTwips.ToString(CultureInfo.InvariantCulture));
            }

            if (TryGetColorIndex(border.Color, out var colorIndex))
            {
                _builder.Append("\\brdrcf").Append(colorIndex.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static string GetBorderControlWord(DocBorderStyle style)
        {
            return style switch
            {
                DocBorderStyle.None => "\\brdrnone",
                DocBorderStyle.Single => "\\brdrs",
                DocBorderStyle.Double => "\\brdrdb",
                DocBorderStyle.Dotted => "\\brdrdot",
                DocBorderStyle.Dashed => "\\brdrdash",
                DocBorderStyle.DotDash => "\\brdrdashd",
                DocBorderStyle.DotDotDash => "\\brdrdashdd",
                DocBorderStyle.Thick => "\\brdrth",
                DocBorderStyle.Hairline => "\\brdrhair",
                DocBorderStyle.Triple => "\\brdrtriple",
                DocBorderStyle.ThickThin => "\\brdrthtnsg",
                DocBorderStyle.ThinThick => "\\brdrtnthsg",
                DocBorderStyle.ThinThickThin => "\\brdrtnthtnsg",
                _ => "\\brdrs"
            };
        }

        private bool TryGetColorIndex(DocColor color, out int index)
        {
            return _colors.TryGetValue(color, out index);
        }

        private void WriteTableCell(TableCell cell)
        {
            var hasParagraph = false;
            for (var blockIndex = 0; blockIndex < cell.Blocks.Count; blockIndex++)
            {
                switch (cell.Blocks[blockIndex])
                {
                    case ParagraphBlock paragraph:
                        WriteParagraph(paragraph, inTable: true);
                        hasParagraph = true;
                        break;
                    case TableBlock nestedTable:
                        var text = FlattenTableText(nestedTable);
                        WriteParagraph(new ParagraphBlock(text), inTable: true);
                        hasParagraph = true;
                        break;
                }
            }

            if (!hasParagraph)
            {
                _builder.Append("\\pard\\intbl\\par ");
            }

            _builder.Append("\\cell ");
        }

        private static string FlattenTableText(TableBlock table)
        {
            var text = new StringBuilder();
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    var cell = row.Cells[cellIndex];
                    if (cellIndex > 0)
                    {
                        text.Append('\t');
                    }

                    for (var blockIndex = 0; blockIndex < cell.Blocks.Count; blockIndex++)
                    {
                        if (cell.Blocks[blockIndex] is not ParagraphBlock paragraph)
                        {
                            continue;
                        }

                        if (blockIndex > 0)
                        {
                            text.Append(' ');
                        }

                        text.Append(GetParagraphText(paragraph));
                    }
                }

                if (rowIndex + 1 < table.Rows.Count)
                {
                    text.Append('\n');
                }
            }

            return text.ToString();
        }

        private static string GetParagraphText(ParagraphBlock paragraph)
        {
            if (paragraph.Inlines.Count == 0)
            {
                return paragraph.Text ?? string.Empty;
            }

            var text = new StringBuilder();
            for (var i = 0; i < paragraph.Inlines.Count; i++)
            {
                if (paragraph.Inlines[i] is RunInline run)
                {
                    text.Append(run.GetText());
                }
                else
                {
                    text.Append(DocumentConstants.ObjectReplacementChar);
                }
            }

            return text.ToString();
        }

        private static List<int> BuildColumnWidthsTwips(TableBlock table)
        {
            if (table.Properties.ColumnWidths.Count > 0)
            {
                var widths = new List<int>(table.Properties.ColumnWidths.Count);
                for (var i = 0; i < table.Properties.ColumnWidths.Count; i++)
                {
                    widths.Add(Math.Max(DefaultColumnWidthTwips, DipToTwips(table.Properties.ColumnWidths[i])));
                }

                return widths;
            }

            var maxColumns = 1;
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                var columns = 0;
                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    columns += Math.Max(1, row.Cells[cellIndex].ColumnSpan);
                }

                if (columns > maxColumns)
                {
                    maxColumns = columns;
                }
            }

            var fallback = new List<int>(maxColumns);
            for (var i = 0; i < maxColumns; i++)
            {
                fallback.Add(DefaultColumnWidthTwips);
            }

            return fallback;
        }

        private static int ResolveCellWidthTwips(TableCell cell, IReadOnlyList<int> baseColumns, int columnStart, int span)
        {
            if (cell.Properties.PreferredWidth.HasValue && cell.Properties.PreferredWidth.Value > 0f)
            {
                return Math.Max(DefaultColumnWidthTwips, DipToTwips(cell.Properties.PreferredWidth.Value));
            }

            var width = 0;
            for (var offset = 0; offset < span; offset++)
            {
                var index = Math.Min(columnStart + offset, baseColumns.Count - 1);
                width += baseColumns[index];
            }

            return width <= 0 ? DefaultColumnWidthTwips * span : width;
        }

        private void AppendParagraphProperties(ParagraphProperties properties)
        {
            if (properties.Alignment.HasValue)
            {
                var alignmentControl = properties.Alignment.Value switch
                {
                    ParagraphAlignment.Center => "\\qc",
                    ParagraphAlignment.Right => "\\qr",
                    ParagraphAlignment.Justify => "\\qj",
                    _ => "\\ql"
                };

                _builder.Append(alignmentControl);
            }

            if (properties.IndentLeft.HasValue)
            {
                _builder.Append("\\li").Append(DipToTwips(properties.IndentLeft.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (properties.IndentRight.HasValue)
            {
                _builder.Append("\\ri").Append(DipToTwips(properties.IndentRight.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (properties.FirstLineIndent.HasValue)
            {
                _builder.Append("\\fi").Append(DipToTwips(properties.FirstLineIndent.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (properties.SpacingBefore.HasValue)
            {
                _builder.Append("\\sb").Append(DipToTwips(properties.SpacingBefore.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (properties.SpacingAfter.HasValue)
            {
                _builder.Append("\\sa").Append(DipToTwips(properties.SpacingAfter.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (properties.LineSpacing.HasValue)
            {
                var value = Math.Abs(properties.LineSpacing.Value);
                var rule = properties.LineSpacingRule ?? DocLineSpacingRule.Auto;
                if (rule == DocLineSpacingRule.Exactly)
                {
                    _builder.Append("\\sl-").Append(value.ToString(CultureInfo.InvariantCulture)).Append("\\slmult0");
                }
                else if (rule == DocLineSpacingRule.AtLeast)
                {
                    _builder.Append("\\sl").Append(value.ToString(CultureInfo.InvariantCulture)).Append("\\slmult0");
                }
                else
                {
                    _builder.Append("\\sl").Append(value.ToString(CultureInfo.InvariantCulture)).Append("\\slmult1");
                }
            }

            if (properties.TabStops.Count > 0)
            {
                for (var i = 0; i < properties.TabStops.Count; i++)
                {
                    _builder.Append("\\tx")
                        .Append(DipToTwips(properties.TabStops[i].Position).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private bool TryAppendParagraphListMetadata(ParagraphBlock paragraph)
        {
            var listInfo = paragraph.ListInfo;
            if (listInfo is null || listInfo.Kind == ListKind.None)
            {
                return false;
            }

            var listId = ResolveParagraphListId(listInfo);
            if (listId <= 0)
            {
                return false;
            }

            _builder.Append("\\ls").Append(listId.ToString(CultureInfo.InvariantCulture));
            var level = Math.Max(0, listInfo.Level);
            _builder.Append("\\ilvl").Append(level.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private int ResolveParagraphListId(ListInfo listInfo)
        {
            if (listInfo.ListId.HasValue
                && listInfo.ListId.Value > 0
                && _listDefinitions.ContainsKey(listInfo.ListId.Value))
            {
                return listInfo.ListId.Value;
            }

            var key = CreateListMetadataKey(listInfo);
            return _generatedListIds.TryGetValue(key, out var listId) ? listId : -1;
        }

        private string? ResolveListPrefix(ListInfo? listInfo)
        {
            if (listInfo is null || listInfo.Kind == ListKind.None)
            {
                return null;
            }

            if (listInfo.Kind == ListKind.Bullet)
            {
                var symbol = string.IsNullOrWhiteSpace(listInfo.BulletSymbol) ? "\u2022" : listInfo.BulletSymbol;
                return symbol + "\t";
            }

            var listId = listInfo.ListId ?? -1;
            var key = (listId, Math.Max(0, listInfo.Level));
            var startAt = Math.Max(1, listInfo.StartAt ?? 1);
            if (!_listCounters.TryGetValue(key, out var current))
            {
                current = startAt;
            }

            _listCounters[key] = current + 1;
            return FormatListNumber(current, listInfo.NumberFormat ?? ListNumberFormat.Decimal) + "\t";
        }

        private static string FormatListNumber(int value, ListNumberFormat format)
        {
            return format switch
            {
                ListNumberFormat.LowerLetter => ToAlphabetic(value, upper: false) + ".",
                ListNumberFormat.UpperLetter => ToAlphabetic(value, upper: true) + ".",
                ListNumberFormat.LowerRoman => ToRoman(value, upper: false) + ".",
                ListNumberFormat.UpperRoman => ToRoman(value, upper: true) + ".",
                ListNumberFormat.Bullet => "\u2022",
                _ => value.ToString(CultureInfo.InvariantCulture) + "."
            };
        }

        private static string ToAlphabetic(int value, bool upper)
        {
            var number = Math.Max(1, value);
            var chars = new StringBuilder();
            while (number > 0)
            {
                number--;
                var offset = number % 26;
                chars.Insert(0, (char)((upper ? 'A' : 'a') + offset));
                number /= 26;
            }

            return chars.ToString();
        }

        private static string ToRoman(int value, bool upper)
        {
            var number = Math.Max(1, value);
            var map = new (int Value, string Symbol)[]
            {
                (1000, "M"),
                (900, "CM"),
                (500, "D"),
                (400, "CD"),
                (100, "C"),
                (90, "XC"),
                (50, "L"),
                (40, "XL"),
                (10, "X"),
                (9, "IX"),
                (5, "V"),
                (4, "IV"),
                (1, "I")
            };

            var text = new StringBuilder();
            for (var i = 0; i < map.Length; i++)
            {
                while (number >= map[i].Value)
                {
                    number -= map[i].Value;
                    text.Append(map[i].Symbol);
                }
            }

            return upper ? text.ToString() : text.ToString().ToLowerInvariant();
        }

        private TextStyle ResolveRunStyle(RunInline run)
        {
            var style = _document.DefaultTextStyle.Clone();
            run.Style?.ApplyTo(style);
            return style;
        }

        private void AppendRun(TextStyle style, string text, HyperlinkInfo? hyperlink = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (hyperlink is not null && TryBuildHyperlinkFieldInstruction(hyperlink, out var instruction))
            {
                AppendHyperlinkRun(style, text, instruction);
            }
            else
            {
                AppendStyledRun(style, text);
            }
        }

        private void AppendImageInline(ImageInline image)
        {
            if (image.Data.Length == 0)
            {
                AppendRun(_document.DefaultTextStyle, DocumentConstants.ObjectReplacementChar.ToString(), image.Hyperlink);
                return;
            }

            if (image.Hyperlink is not null && TryBuildHyperlinkFieldInstruction(image.Hyperlink, out var instruction))
            {
                _builder.Append("{\\field{\\*\\fldinst ");
                AppendEscapedText(instruction);
                _builder.Append("}{\\fldrslt ");
                AppendPictureGroup(image);
                _builder.Append("}}");
                return;
            }

            AppendPictureGroup(image);
        }

        private void AppendPictureGroup(ImageInline image)
        {
            var blip = ResolvePictureBlipControlWord(image.ContentType);
            var width = image.Width > 0f ? image.Width : 1f;
            var height = image.Height > 0f ? image.Height : 1f;
            var pixelWidth = Math.Max(1, (int)MathF.Round(width));
            var pixelHeight = Math.Max(1, (int)MathF.Round(height));
            var twipsWidth = Math.Max(1, DipToTwips(width));
            var twipsHeight = Math.Max(1, DipToTwips(height));

            _builder.Append("{\\pict\\");
            _builder.Append(blip);
            _builder.Append("\\picw").Append(pixelWidth.ToString(CultureInfo.InvariantCulture));
            _builder.Append("\\pich").Append(pixelHeight.ToString(CultureInfo.InvariantCulture));
            _builder.Append("\\picwgoal").Append(twipsWidth.ToString(CultureInfo.InvariantCulture));
            _builder.Append("\\pichgoal").Append(twipsHeight.ToString(CultureInfo.InvariantCulture));
            _builder.Append(' ');
            AppendHexData(image.Data);
            _builder.Append('}');
        }

        private static string ResolvePictureBlipControlWord(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return "pngblip";
            }

            var normalized = contentType.Trim();
            if (normalized.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            {
                return "jpegblip";
            }

            if (normalized.Contains("bmp", StringComparison.OrdinalIgnoreCase))
            {
                return "dibitmap";
            }

            if (normalized.Contains("emf", StringComparison.OrdinalIgnoreCase))
            {
                return "emfblip";
            }

            if (normalized.Contains("wmf", StringComparison.OrdinalIgnoreCase))
            {
                return "wmetafile8";
            }

            return "pngblip";
        }

        private void AppendHexData(byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                _builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        private void AppendStyledRun(TextStyle style, string text)
        {
            _builder.Append('{');
            AppendRunStyle(style);
            _builder.Append(' ');
            AppendEscapedText(text);
            _builder.Append('}');
        }

        private void AppendHyperlinkRun(TextStyle style, string text, string instruction)
        {
            _builder.Append("{\\field{\\*\\fldinst ");
            AppendEscapedText(instruction);
            _builder.Append("}{\\fldrslt ");
            AppendStyledRun(style, text);
            _builder.Append("}}");
        }

        private static bool TryBuildHyperlinkFieldInstruction(HyperlinkInfo hyperlink, out string instruction)
        {
            var uri = NormalizeHyperlinkComponent(hyperlink.Uri);
            var anchor = NormalizeHyperlinkComponent(hyperlink.Anchor);
            var tooltip = NormalizeHyperlinkComponent(hyperlink.Tooltip);

            if (uri is not null && uri.StartsWith('#') && anchor is null)
            {
                anchor = NormalizeHyperlinkComponent(uri[1..]);
                uri = null;
            }

            if (uri is null && anchor is null)
            {
                instruction = string.Empty;
                return false;
            }

            var builder = new StringBuilder("HYPERLINK");
            if (uri is not null)
            {
                builder.Append(' ');
                AppendQuotedFieldValue(builder, uri);
            }

            if (anchor is not null)
            {
                builder.Append(" \\l ");
                AppendQuotedFieldValue(builder, anchor);
            }

            if (tooltip is not null)
            {
                builder.Append(" \\o ");
                AppendQuotedFieldValue(builder, tooltip);
            }

            instruction = builder.ToString();
            return true;
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

        private static void AppendQuotedFieldValue(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch == '"')
                {
                    builder.Append("\"\"");
                }
                else
                {
                    builder.Append(ch);
                }
            }

            builder.Append('"');
        }

        private void AppendRunStyle(TextStyle style)
        {
            if (_fonts.TryGetValue(style.FontFamily, out var fontIndex))
            {
                _builder.Append("\\f").Append(fontIndex.ToString(CultureInfo.InvariantCulture));
            }

            _builder.Append("\\fs").Append(DipToHalfPoints(style.FontSize).ToString(CultureInfo.InvariantCulture));
            _builder.Append(style.FontWeight == DocFontWeight.Bold ? "\\b" : "\\b0");
            _builder.Append(style.FontStyle == DocFontStyle.Italic ? "\\i" : "\\i0");
            _builder.Append(style.Underline ? "\\ul" : "\\ulnone");
            _builder.Append(style.Strikethrough ? "\\strike" : "\\strike0");

            if (_colors.TryGetValue(style.Color, out var colorIndex))
            {
                _builder.Append("\\cf").Append(colorIndex.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                _builder.Append("\\cf0");
            }

            if (style.HighlightColor.HasValue && _colors.TryGetValue(style.HighlightColor.Value, out var highlightIndex))
            {
                _builder.Append("\\highlight").Append(highlightIndex.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                _builder.Append("\\highlight0");
            }

            if (style.VerticalPosition == DocVerticalPosition.Superscript)
            {
                _builder.Append("\\super");
            }
            else if (style.VerticalPosition == DocVerticalPosition.Subscript)
            {
                _builder.Append("\\sub");
            }
            else
            {
                _builder.Append("\\nosupersub");
            }
        }

        private void AppendEscapedText(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                switch (ch)
                {
                    case '\\':
                    case '{':
                    case '}':
                        _builder.Append('\\').Append(ch);
                        break;
                    case '\r':
                        break;
                    case '\n':
                        _builder.Append("\\line ");
                        break;
                    case '\t':
                        _builder.Append("\\tab ");
                        break;
                    default:
                        if (ch <= 0x7f)
                        {
                            _builder.Append(ch);
                        }
                        else
                        {
                            var signed = unchecked((short)ch);
                            _builder.Append("\\u").Append(signed.ToString(CultureInfo.InvariantCulture)).Append('?');
                        }

                        break;
                }
            }
        }

        private static int DipToHalfPoints(float dip)
        {
            var points = dip * 72f / 96f;
            return (int)MathF.Round(points * 2f);
        }

        private static int DipToTwips(float dip)
        {
            var twips = dip / (96f / 72f) * 20f;
            return (int)MathF.Round(twips);
        }

        private readonly record struct ListMetadataKey(
            ListKind Kind,
            int Level,
            ListNumberFormat Format,
            string? LevelText,
            string? BulletSymbol,
            int StartAt);
    }
}
