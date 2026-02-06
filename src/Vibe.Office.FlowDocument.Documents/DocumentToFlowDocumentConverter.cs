using System.Globalization;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using DocumentBlock = Vibe.Office.Documents.Block;
using DocumentInline = Vibe.Office.Documents.Inline;
using DocumentParagraph = Vibe.Office.Documents.ParagraphBlock;
using DocumentTableCell = Vibe.Office.Documents.TableCell;
using DocumentTableRow = Vibe.Office.Documents.TableRow;
using FlowAnchoredBlock = Vibe.Office.FlowDocument.AnchoredBlock;
using FlowBlock = Vibe.Office.FlowDocument.Block;
using FlowBlockCollection = Vibe.Office.FlowDocument.BlockCollection;
using FlowInline = Vibe.Office.FlowDocument.Inline;
using FlowInlineCollection = Vibe.Office.FlowDocument.InlineCollection;
using FlowList = Vibe.Office.FlowDocument.List;
using FlowListItem = Vibe.Office.FlowDocument.ListItem;
using FlowParagraph = Vibe.Office.FlowDocument.Paragraph;
using FlowTableCell = Vibe.Office.FlowDocument.TableCell;
using FlowTableRow = Vibe.Office.FlowDocument.TableRow;
using FlowTableRowGroup = Vibe.Office.FlowDocument.TableRowGroup;

namespace Vibe.Office.FlowDocument.Documents;

/// <summary>
/// Converts <see cref="Document"/> content into <see cref="Vibe.Office.FlowDocument.FlowDocument"/> instances.
/// </summary>
public sealed class DocumentToFlowDocumentConverter
{
    private readonly DocumentToFlowDocumentConverterOptions _options;
    private Document? _currentDocument;
    private DocumentStyleResolver? _styleResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentToFlowDocumentConverter"/> class.
    /// </summary>
    public DocumentToFlowDocumentConverter()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentToFlowDocumentConverter"/> class.
    /// </summary>
    /// <param name="options">Converter options.</param>
    public DocumentToFlowDocumentConverter(DocumentToFlowDocumentConverterOptions? options)
    {
        _options = options ?? new DocumentToFlowDocumentConverterOptions();
    }

    /// <summary>
    /// Converts the specified <see cref="Document"/> into a <see cref="Vibe.Office.FlowDocument.FlowDocument"/>.
    /// </summary>
    /// <param name="source">The source document.</param>
    /// <returns>The converted flow document.</returns>
    public Vibe.Office.FlowDocument.FlowDocument Convert(Document source)
    {
        ArgumentNullException.ThrowIfNull(source);

        BeginConversion(source);
        try
        {
            var document = new Vibe.Office.FlowDocument.FlowDocument();
            ApplyDocumentDefaults(source, document);
            AppendBlocks(source.Blocks, document.Blocks);

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new FlowParagraph());
            }

            return document;
        }
        finally
        {
            EndConversion();
        }
    }

    /// <summary>
    /// Converts a single top-level block from a <see cref="Document"/> into a flow block.
    /// </summary>
    /// <param name="source">The source document.</param>
    /// <param name="blockIndex">The top-level block index to convert.</param>
    /// <param name="block">The converted flow block when conversion succeeds.</param>
    /// <returns><see langword="true"/> when conversion succeeds; otherwise <see langword="false"/>.</returns>
    public bool TryConvertTopLevelBlock(
        Document source,
        int blockIndex,
        out Vibe.Office.FlowDocument.Block block)
    {
        ArgumentNullException.ThrowIfNull(source);

        BeginConversion(source);
        try
        {
            block = null!;
            if ((uint)blockIndex >= (uint)source.Blocks.Count)
            {
                return false;
            }

            if (source.Blocks[blockIndex] is DocumentParagraph paragraph)
            {
                if (paragraph.ListInfo is not null)
                {
                    return false;
                }

                if (TryConvertBlockUiContainer(paragraph, out var blockUiContainer))
                {
                    block = blockUiContainer;
                    return true;
                }

                block = ConvertParagraph(paragraph);
                return true;
            }

            if (source.Blocks[blockIndex] is TableBlock table)
            {
                block = ConvertTable(table);
                return true;
            }

            return false;
        }
        finally
        {
            EndConversion();
        }
    }

    private void BeginConversion(Document source)
    {
        _currentDocument = source;
        _styleResolver = _options.ResolveInheritedStyles
            ? new DocumentStyleResolver(source)
            : null;
    }

    private void EndConversion()
    {
        _styleResolver = null;
        _currentDocument = null;
    }

    private void ApplyDocumentDefaults(Document source, Vibe.Office.FlowDocument.FlowDocument target)
    {
        var text = source.DefaultTextStyle;
        if (!string.IsNullOrWhiteSpace(text.FontFamily))
        {
            target.FontFamily = text.FontFamily;
        }

        if (text.FontSize > 0f)
        {
            target.FontSize = text.FontSize;
        }

        target.FontWeight = text.FontWeight == DocFontWeight.Bold
            ? Vibe.Office.FlowDocument.FlowFontWeight.Bold
            : Vibe.Office.FlowDocument.FlowFontWeight.Normal;
        target.FontStyle = text.FontStyle == DocFontStyle.Italic
            ? Vibe.Office.FlowDocument.FlowFontStyle.Italic
            : Vibe.Office.FlowDocument.FlowFontStyle.Normal;

        if (text.Color != DocColor.Black)
        {
            target.Foreground = ToFlowColor(text.Color);
        }

        if (text.HighlightColor.HasValue)
        {
            target.Background = ToFlowColor(text.HighlightColor.Value);
        }

        if (source.DefaultParagraphStyleProperties.Alignment.HasValue)
        {
            target.TextAlignment = ToFlowAlignment(source.DefaultParagraphStyleProperties.Alignment.Value);
        }

        var section = source.SectionProperties;
        if (section.PageWidth.HasValue && section.PageWidth.Value > 0f)
        {
            target.PageWidth = section.PageWidth.Value;
        }

        if (section.PageHeight.HasValue && section.PageHeight.Value > 0f)
        {
            target.PageHeight = section.PageHeight.Value;
        }

        var pagePadding = new Vibe.Office.FlowDocument.FlowThickness(
            section.MarginLeft.GetValueOrDefault(0f),
            section.MarginTop.GetValueOrDefault(0f),
            section.MarginRight.GetValueOrDefault(0f),
            section.MarginBottom.GetValueOrDefault(0f));
        if (!pagePadding.IsEmpty)
        {
            target.PagePadding = pagePadding;
        }

        if (section.ColumnGap.HasValue && section.ColumnGap.Value >= 0f)
        {
            target.ColumnGap = section.ColumnGap.Value;
        }

        if (section.ColumnWidths.Count > 0 && section.ColumnWidths[0] > 0f)
        {
            target.ColumnWidth = section.ColumnWidths[0];
        }

        if (section.ColumnSeparator == true)
        {
            target.ColumnRuleWidth = 1d;
        }

        if (section.PageBackgroundColor.HasValue)
        {
            target.Background = ToFlowColor(section.PageBackgroundColor.Value);
        }
    }

    private void AppendBlocks(IReadOnlyList<DocumentBlock> source, FlowBlockCollection target)
    {
        var index = 0;
        while (index < source.Count)
        {
            if (source[index] is DocumentParagraph paragraphWithList && paragraphWithList.ListInfo is not null)
            {
                var list = ConvertListSequence(source, ref index);
                target.Add(list);
                continue;
            }

            if (source[index] is DocumentParagraph paragraph && TryConvertBlockUiContainer(paragraph, out var blockUi))
            {
                target.Add(blockUi);
                index++;
                continue;
            }

            if (source[index] is DocumentParagraph paragraphBlock)
            {
                target.Add(ConvertParagraph(paragraphBlock));
                index++;
                continue;
            }

            if (source[index] is TableBlock tableBlock)
            {
                target.Add(ConvertTable(tableBlock));
                index++;
                continue;
            }

            index++;
        }
    }

    private FlowList ConvertListSequence(IReadOnlyList<DocumentBlock> source, ref int index)
    {
        var firstParagraph = (DocumentParagraph)source[index];
        var firstInfo = firstParagraph.ListInfo ?? new ListInfo(ListKind.Bullet);
        var root = CreateList(firstInfo);

        var stack = new Stack<ListContext>();
        stack.Push(new ListContext(root, firstInfo));

        while (index < source.Count && source[index] is DocumentParagraph paragraph && paragraph.ListInfo is not null)
        {
            var info = paragraph.ListInfo;
            if (info.Level < firstInfo.Level)
            {
                break;
            }

            while (stack.Count > 0 && info.Level < stack.Peek().Info.Level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                break;
            }

            if (info.Level > stack.Peek().Info.Level)
            {
                var parent = stack.Peek();
                if (parent.LastItem is null)
                {
                    var syntheticItem = new FlowListItem();
                    parent.List.ListItems.Add(syntheticItem);
                    parent.LastItem = syntheticItem;
                }

                var nested = CreateList(info);
                parent.LastItem.Blocks.Add(nested);
                stack.Push(new ListContext(nested, info));
            }
            else if (!CanContinueList(stack.Peek().Info, info))
            {
                break;
            }

            var current = stack.Peek();
            var item = new FlowListItem();
            item.Blocks.Add(ConvertParagraph(paragraph));
            current.List.ListItems.Add(item);
            current.LastItem = item;
            index++;
        }

        return root;
    }

    private static bool CanContinueList(ListInfo current, ListInfo next)
    {
        if (current.Level != next.Level)
        {
            return false;
        }

        if (current.ListId.HasValue && next.ListId.HasValue && current.ListId.Value != next.ListId.Value)
        {
            return false;
        }

        var currentMarker = ToFlowMarkerStyle(current);
        var nextMarker = ToFlowMarkerStyle(next);
        return currentMarker == nextMarker;
    }

    private static FlowList CreateList(ListInfo info)
    {
        var list = new FlowList
        {
            MarkerStyle = ToFlowMarkerStyle(info)
        };

        if (info.StartAt.HasValue)
        {
            list.StartIndex = info.StartAt.Value;
        }

        if (info.LeftIndent.HasValue)
        {
            list.MarkerOffset = info.LeftIndent.Value;
        }

        return list;
    }

    private static Vibe.Office.FlowDocument.FlowListMarkerStyle ToFlowMarkerStyle(ListInfo info)
    {
        if (info.Kind == ListKind.None)
        {
            return Vibe.Office.FlowDocument.FlowListMarkerStyle.None;
        }

        if (info.Kind == ListKind.Bullet)
        {
            return info.BulletSymbol switch
            {
                "○" => Vibe.Office.FlowDocument.FlowListMarkerStyle.Circle,
                "■" => Vibe.Office.FlowDocument.FlowListMarkerStyle.Square,
                _ => Vibe.Office.FlowDocument.FlowListMarkerStyle.Disc
            };
        }

        return info.NumberFormat switch
        {
            ListNumberFormat.LowerLetter => Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerLatin,
            ListNumberFormat.UpperLetter => Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperLatin,
            ListNumberFormat.LowerRoman => Vibe.Office.FlowDocument.FlowListMarkerStyle.LowerRoman,
            ListNumberFormat.UpperRoman => Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman,
            _ => Vibe.Office.FlowDocument.FlowListMarkerStyle.Decimal
        };
    }

    private FlowParagraph ConvertParagraph(DocumentParagraph source)
    {
        var paragraph = new FlowParagraph();
        var resolvedParagraphProperties = ResolveParagraphProperties(source);
        ApplyParagraphProperties(resolvedParagraphProperties, paragraph);
        var paragraphStyle = ResolveParagraphTextStyle(source);
        ApplyResolvedTextStyle(paragraphStyle, paragraph);

        if (source.Inlines.Count == 0)
        {
            if (!string.IsNullOrEmpty(source.Text))
            {
                paragraph.Inlines.Add(new Vibe.Office.FlowDocument.Run(source.Text));
            }
        }
        else
        {
            for (var inlineIndex = 0; inlineIndex < source.Inlines.Count; inlineIndex++)
            {
                AppendInline(source, paragraphStyle, source.Inlines[inlineIndex], paragraph.Inlines);
            }
        }

        AppendFloatingObjects(source, paragraph);
        return paragraph;
    }

    private bool TryConvertBlockUiContainer(DocumentParagraph source, out Vibe.Office.FlowDocument.BlockUIContainer blockUiContainer)
    {
        blockUiContainer = null!;

        if (source.Inlines.Count == 1 && source.Inlines[0] is ShapeInline shape)
        {
            var resolved = ResolveEmbeddedUiChild(shape, false);
            if (resolved.IsMatch && !resolved.IsInline)
            {
                var container = new Vibe.Office.FlowDocument.BlockUIContainer
                {
                    Child = resolved.Child
                };
                ApplyParagraphProperties(source.Properties, container);
                blockUiContainer = container;
                return true;
            }
        }

        if (source.Inlines.Count == 1
            && source.Inlines[0] is RunInline placeholderRun
            && string.Equals(placeholderRun.GetText(), _options.BlockUiPlaceholderText, StringComparison.Ordinal))
        {
            var container = new Vibe.Office.FlowDocument.BlockUIContainer
            {
                Child = _options.BlockUiPlaceholderChild
            };
            ApplyParagraphProperties(source.Properties, container);
            blockUiContainer = container;
            return true;
        }

        return false;
    }

    private void AppendFloatingObjects(DocumentParagraph source, FlowParagraph target)
    {
        if (source.FloatingObjects.Count == 0)
        {
            return;
        }

        for (var index = 0; index < source.FloatingObjects.Count; index++)
        {
            var floating = source.FloatingObjects[index];
            if (floating.Content is not ShapeInline shape)
            {
                continue;
            }

            var anchored = ConvertFloatingObject(floating, shape);
            target.Inlines.Add(anchored);
        }
    }

    private FlowAnchoredBlock ConvertFloatingObject(FloatingObject source, ShapeInline shape)
    {
        FlowAnchoredBlock anchored = source.Anchor.HorizontalAlignment == FloatingHorizontalAlignment.Right
            ? new Vibe.Office.FlowDocument.Floater
            {
                Width = Math.Max(1f, shape.Width),
                HorizontalAlignment = source.Anchor.HorizontalAlignment.ToString()
            }
            : new Vibe.Office.FlowDocument.Figure
            {
                Width = Math.Max(1f, shape.Width),
                Height = Math.Max(1f, shape.Height),
                HorizontalAnchor = source.Anchor.HorizontalReference.ToString(),
                VerticalAnchor = source.Anchor.VerticalReference.ToString(),
                HorizontalOffset = source.Anchor.OffsetX,
                VerticalOffset = source.Anchor.OffsetY
            };

        anchored.Margin = ToFlowThickness(source.Anchor.Distance);

        if (shape.TextBox is { } textBox)
        {
            AppendBlocks(textBox.Blocks, anchored.Blocks);
        }

        if (anchored.Blocks.Count == 0)
        {
            anchored.Blocks.Add(new FlowParagraph());
        }

        return anchored;
    }

    private void AppendInline(
        DocumentParagraph paragraph,
        TextStyle paragraphStyle,
        DocumentInline source,
        FlowInlineCollection target)
    {
        if (source is RunInline runInline)
        {
            AppendRunInline(paragraph, paragraphStyle, runInline, target);
            return;
        }

        if (source is ShapeInline shapeInline)
        {
            var resolved = ResolveEmbeddedUiChild(shapeInline, true);
            if (resolved.IsMatch)
            {
                target.Add(new Vibe.Office.FlowDocument.InlineUIContainer
                {
                    Child = resolved.Child
                });
                return;
            }

            target.Add(new Vibe.Office.FlowDocument.Run(_options.InlineUiPlaceholderText));
            return;
        }

        if (source is ImageInline)
        {
            target.Add(new Vibe.Office.FlowDocument.Run(_options.InlineUiPlaceholderText));
            return;
        }
    }

    private void AppendRunInline(
        DocumentParagraph paragraph,
        TextStyle paragraphStyle,
        RunInline source,
        FlowInlineCollection target)
    {
        var text = source.GetText();
        if (text.Length == 0)
        {
            return;
        }

        var runStyle = ResolveRunTextStyle(paragraph, source, paragraphStyle);
        var runStyleDelta = BuildFlowCompatibleStyleDelta(paragraphStyle, runStyle);
        var parts = text.Split('\n');
        for (var index = 0; index < parts.Length; index++)
        {
            var segment = parts[index];
            if (segment.Length > 0)
            {
                var run = new Vibe.Office.FlowDocument.Run(segment);
                AddStyledInline(target, run, runStyleDelta, source.Hyperlink);
            }

            if (index + 1 < parts.Length)
            {
                var lineBreak = new Vibe.Office.FlowDocument.LineBreak();
                AddStyledInline(target, lineBreak, runStyleDelta, source.Hyperlink);
            }
        }
    }

    private ParagraphProperties ResolveParagraphProperties(DocumentParagraph source)
    {
        if (_styleResolver is null || !_options.ResolveInheritedStyles)
        {
            return source.Properties;
        }

        return _styleResolver.ResolveParagraphProperties(source);
    }

    private TextStyle ResolveParagraphTextStyle(DocumentParagraph source)
    {
        var document = _currentDocument;
        var defaultStyle = document?.DefaultTextStyle.Clone() ?? new TextStyle();
        if (_styleResolver is null || !_options.ResolveInheritedStyles)
        {
            return defaultStyle;
        }

        return _styleResolver.ResolveParagraphTextStyle(source, defaultStyle);
    }

    private TextStyle ResolveRunTextStyle(DocumentParagraph paragraph, RunInline run, TextStyle paragraphStyle)
    {
        if (_styleResolver is not null && _options.ResolveInheritedStyles)
        {
            return _styleResolver.ResolveRunStyle(paragraph, run, paragraphStyle);
        }

        var style = paragraphStyle.Clone();
        run.Style?.ApplyTo(style);
        return style;
    }

    private static TextStyleProperties? BuildFlowCompatibleStyleDelta(TextStyle baseStyle, TextStyle runStyle)
    {
        var delta = new TextStyleProperties();

        if (!string.Equals(runStyle.FontFamily, baseStyle.FontFamily, StringComparison.Ordinal))
        {
            delta.FontFamily = runStyle.FontFamily;
        }

        if (Math.Abs(runStyle.FontSize - baseStyle.FontSize) > 0.01f)
        {
            delta.FontSize = runStyle.FontSize;
        }

        if (runStyle.FontWeight != baseStyle.FontWeight)
        {
            delta.FontWeight = runStyle.FontWeight;
        }

        if (runStyle.FontStyle != baseStyle.FontStyle)
        {
            delta.FontStyle = runStyle.FontStyle;
        }

        if (runStyle.Color != baseStyle.Color)
        {
            delta.Color = runStyle.Color;
        }

        if (!Nullable.Equals(runStyle.HighlightColor, baseStyle.HighlightColor))
        {
            delta.HighlightColor = runStyle.HighlightColor;
        }

        if (runStyle.VerticalPosition != baseStyle.VerticalPosition)
        {
            delta.VerticalPosition = runStyle.VerticalPosition;
        }

        if (runStyle.Underline != baseStyle.Underline || runStyle.UnderlineStyle != baseStyle.UnderlineStyle)
        {
            delta.Underline = runStyle.Underline;
            delta.UnderlineStyle = runStyle.UnderlineStyle;
        }

        if (runStyle.Strikethrough != baseStyle.Strikethrough)
        {
            delta.Strikethrough = runStyle.Strikethrough;
        }

        return delta.HasValues ? delta : null;
    }

    private static void ApplyResolvedTextStyle(TextStyle source, Vibe.Office.FlowDocument.TextElement target)
    {
        if (source is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(source.FontFamily))
        {
            target.FontFamily = source.FontFamily;
        }

        if (source.FontSize > 0f)
        {
            target.FontSize = source.FontSize;
        }

        target.FontWeight = source.FontWeight == DocFontWeight.Bold
            ? Vibe.Office.FlowDocument.FlowFontWeight.Bold
            : Vibe.Office.FlowDocument.FlowFontWeight.Normal;
        target.FontStyle = source.FontStyle == DocFontStyle.Italic
            ? Vibe.Office.FlowDocument.FlowFontStyle.Italic
            : Vibe.Office.FlowDocument.FlowFontStyle.Normal;
        target.Foreground = ToFlowColor(source.Color);

        if (source.HighlightColor.HasValue)
        {
            target.Background = ToFlowColor(source.HighlightColor.Value);
        }

        var decorations = ToFlowTextDecorations(source.Underline, source.UnderlineStyle, source.Strikethrough);
        if (decorations != Vibe.Office.FlowDocument.FlowTextDecorations.None)
        {
            if (target is FlowParagraph paragraph)
            {
                paragraph.TextDecorations = decorations;
            }
            else if (target is Vibe.Office.FlowDocument.Span span)
            {
                span.TextDecorations = decorations;
            }
        }
    }

    private static void AddStyledInline(
        FlowInlineCollection target,
        FlowInline leaf,
        TextStyleProperties? style,
        HyperlinkInfo? hyperlink)
    {
        FlowInline current = leaf;

        var styleSpan = CreateStyleSpan(style);
        if (styleSpan is not null)
        {
            styleSpan.Inlines.Add(current);
            current = styleSpan;
        }

        if (hyperlink is not null && !hyperlink.IsEmpty)
        {
            var link = new Vibe.Office.FlowDocument.Hyperlink();
            if (!string.IsNullOrWhiteSpace(hyperlink.Uri))
            {
                link.NavigateUri = hyperlink.Uri;
            }
            else if (!string.IsNullOrWhiteSpace(hyperlink.Anchor))
            {
                link.NavigateUri = $"#{hyperlink.Anchor}";
            }

            if (!string.IsNullOrWhiteSpace(hyperlink.Anchor))
            {
                link.TargetName = hyperlink.Anchor;
            }

            if (!string.IsNullOrWhiteSpace(hyperlink.Tooltip))
            {
                link.SetValue(Vibe.Office.FlowDocument.Hyperlink.ToolTipProperty, hyperlink.Tooltip);
            }

            link.Inlines.Add(current);
            current = link;
        }

        target.Add(current);
    }

    private static Vibe.Office.FlowDocument.Span? CreateStyleSpan(TextStyleProperties? style)
    {
        if (style is null || !style.HasValues)
        {
            return null;
        }

        var span = new Vibe.Office.FlowDocument.Span();
        var hasValues = false;

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            span.FontFamily = style.FontFamily;
            hasValues = true;
        }

        if (style.FontSize.HasValue && style.FontSize.Value > 0f)
        {
            span.FontSize = style.FontSize.Value;
            hasValues = true;
        }

        if (style.FontWeight.HasValue)
        {
            span.FontWeight = style.FontWeight == DocFontWeight.Bold
                ? Vibe.Office.FlowDocument.FlowFontWeight.Bold
                : Vibe.Office.FlowDocument.FlowFontWeight.Normal;
            hasValues = true;
        }

        if (style.FontStyle.HasValue)
        {
            span.FontStyle = style.FontStyle == DocFontStyle.Italic
                ? Vibe.Office.FlowDocument.FlowFontStyle.Italic
                : Vibe.Office.FlowDocument.FlowFontStyle.Normal;
            hasValues = true;
        }

        if (style.Color.HasValue)
        {
            span.Foreground = ToFlowColor(style.Color.Value);
            hasValues = true;
        }

        if (style.HighlightColor.HasValue)
        {
            span.Background = ToFlowColor(style.HighlightColor.Value);
            hasValues = true;
        }

        if (style.VerticalPosition.HasValue)
        {
            var baseline = style.VerticalPosition.Value switch
            {
                DocVerticalPosition.Superscript => Vibe.Office.FlowDocument.FlowBaselineAlignment.Superscript,
                DocVerticalPosition.Subscript => Vibe.Office.FlowDocument.FlowBaselineAlignment.Subscript,
                _ => Vibe.Office.FlowDocument.FlowBaselineAlignment.Baseline
            };
            if (baseline != Vibe.Office.FlowDocument.FlowBaselineAlignment.Baseline)
            {
                span.BaselineAlignment = baseline;
                hasValues = true;
            }
        }

        var decorations = ToFlowTextDecorations(style.Underline, style.UnderlineStyle, style.Strikethrough);
        if (decorations != Vibe.Office.FlowDocument.FlowTextDecorations.None)
        {
            span.TextDecorations = decorations;
            hasValues = true;
        }

        return hasValues ? span : null;
    }

    private static Vibe.Office.FlowDocument.FlowTextDecorations ToFlowTextDecorations(
        bool? underline,
        DocUnderlineStyle? underlineStyle,
        bool? strikethrough)
    {
        var result = Vibe.Office.FlowDocument.FlowTextDecorations.None;
        if (underline == true || (underlineStyle.HasValue && underlineStyle.Value != DocUnderlineStyle.None))
        {
            result |= Vibe.Office.FlowDocument.FlowTextDecorations.Underline;
        }

        if (strikethrough == true)
        {
            result |= Vibe.Office.FlowDocument.FlowTextDecorations.Strikethrough;
        }

        return result;
    }

    private EmbeddedUiResolution ResolveEmbeddedUiChild(ShapeInline shape, bool inlineFallback)
    {
        if (!FlowDocumentConverter.TryParseEmbeddedUiElementId(shape.Name, _options.EmbeddedUiShapePrefix, out var elementId))
        {
            return EmbeddedUiResolution.NoMatch;
        }

        if (_options.EmbeddedUiElementsById is not null
            && _options.EmbeddedUiElementsById.TryGetValue(elementId, out var element))
        {
            return new EmbeddedUiResolution(true, element.IsInline, element.Child);
        }

        var child = _options.EmbeddedUiElementFactory?.Invoke(elementId, inlineFallback);
        if (child is not null)
        {
            return new EmbeddedUiResolution(true, inlineFallback, child);
        }

        return new EmbeddedUiResolution(
            true,
            inlineFallback,
            inlineFallback ? _options.InlineUiPlaceholderChild : _options.BlockUiPlaceholderChild);
    }

    private static void ApplyParagraphProperties(ParagraphProperties source, FlowBlock target)
    {
        if (source.Alignment.HasValue)
        {
            target.TextAlignment = ToFlowAlignment(source.Alignment.Value);
        }

        var margin = new Vibe.Office.FlowDocument.FlowThickness(
            source.IndentLeft.GetValueOrDefault(0f),
            source.SpacingBefore.GetValueOrDefault(0f),
            source.IndentRight.GetValueOrDefault(0f),
            source.SpacingAfter.GetValueOrDefault(0f));
        if (!margin.IsEmpty)
        {
            target.Margin = margin;
        }

        if (source.LineSpacing.HasValue && source.LineSpacing.Value > 0)
        {
            target.LineHeight = source.LineSpacing.Value;
        }

        // The shared document model does not expose a column-break flag, so
        // BreakColumnBefore cannot be reconstructed on Flow blocks.
        target.BreakPageBefore = source.PageBreakBefore;
        target.FlowDirection = ResolveFlowDirection(source.Bidi, source.TextDirection);
        target.LineStackingStrategy = source.LineSpacingRule?.ToString();

        if (source.ShadingColor.HasValue)
        {
            target.Background = ToFlowColor(source.ShadingColor.Value);
        }

        var borderThickness = new Vibe.Office.FlowDocument.FlowThickness(
            source.Borders.Left?.Thickness ?? 0f,
            source.Borders.Top?.Thickness ?? 0f,
            source.Borders.Right?.Thickness ?? 0f,
            source.Borders.Bottom?.Thickness ?? 0f);
        if (!borderThickness.IsEmpty)
        {
            target.BorderThickness = borderThickness;
        }

        var borderColor = source.Borders.Left?.Color
                          ?? source.Borders.Top?.Color
                          ?? source.Borders.Right?.Color
                          ?? source.Borders.Bottom?.Color;
        if (borderColor.HasValue)
        {
            target.BorderBrush = ToFlowColor(borderColor.Value);
        }

        if (target is FlowParagraph paragraph)
        {
            paragraph.TextIndent = source.FirstLineIndent;
            paragraph.KeepWithNext = source.KeepWithNext;
            paragraph.KeepTogether = source.KeepLinesTogether;
        }
    }

    private static string? ResolveFlowDirection(bool? bidi, DocTextDirection? textDirection)
    {
        if (bidi == true)
        {
            return "RightToLeft";
        }

        return textDirection?.ToString();
    }

    private static Vibe.Office.FlowDocument.FlowTextAlignment ToFlowAlignment(ParagraphAlignment alignment)
    {
        return alignment switch
        {
            ParagraphAlignment.Center => Vibe.Office.FlowDocument.FlowTextAlignment.Center,
            ParagraphAlignment.Right => Vibe.Office.FlowDocument.FlowTextAlignment.Right,
            ParagraphAlignment.Justify => Vibe.Office.FlowDocument.FlowTextAlignment.Justify,
            _ => Vibe.Office.FlowDocument.FlowTextAlignment.Left
        };
    }

    private Vibe.Office.FlowDocument.Table ConvertTable(TableBlock source)
    {
        var tableStyle = _styleResolver is not null && _options.ResolveInheritedStyles
            ? _styleResolver.ResolveTableStyle(source)
            : null;
        var mergedTableProperties = MergeTableProperties(tableStyle?.TableProperties, source.Properties);
        var tableLook = ResolveTableLook(source.Properties.Look ?? mergedTableProperties.Look);
        var rowCount = source.Rows.Count;
        var columnCount = ResolveTableColumnCount(source.Rows, mergedTableProperties);

        var table = new Vibe.Office.FlowDocument.Table();
        if (mergedTableProperties.CellSpacing.HasValue && mergedTableProperties.CellSpacing.Value >= 0f)
        {
            table.CellSpacing = mergedTableProperties.CellSpacing.Value;
        }

        if (mergedTableProperties.ColumnWidths.Count > 0)
        {
            for (var columnIndex = 0; columnIndex < mergedTableProperties.ColumnWidths.Count; columnIndex++)
            {
                var width = mergedTableProperties.ColumnWidths[columnIndex];
                table.Columns.Add(new Vibe.Office.FlowDocument.TableColumn
                {
                    Width = width > 0f ? width : null
                });
            }
        }

        var group = new FlowTableRowGroup();
        table.RowGroups.Add(group);

        for (var rowIndex = 0; rowIndex < source.Rows.Count; rowIndex++)
        {
            var sourceRow = source.Rows[rowIndex];
            var row = new FlowTableRow();
            group.Rows.Add(row);

            var column = Math.Max(0, sourceRow.Properties.GridBefore ?? 0);
            for (var cellIndex = 0; cellIndex < sourceRow.Cells.Count; cellIndex++)
            {
                var sourceCell = sourceRow.Cells[cellIndex];
                var columnSpan = Math.Max(1, sourceCell.ColumnSpan);

                if (sourceCell.VerticalMerge == TableCellVerticalMerge.Continue)
                {
                    column += columnSpan;
                    continue;
                }

                var cell = new FlowTableCell
                {
                    ColumnSpan = columnSpan,
                    RowSpan = ComputeRowSpan(source.Rows, rowIndex, column, columnSpan)
                };

                var resolvedProperties = ResolveTableCellProperties(
                    sourceRow,
                    sourceCell,
                    source.Properties,
                    tableStyle,
                    tableLook,
                    rowIndex,
                    column,
                    rowCount,
                    columnCount);
                ApplyFlowTableCellProperties(sourceCell, resolvedProperties, cell);
                AppendBlocks(sourceCell.Blocks, cell.Blocks);
                if (cell.Blocks.Count == 0)
                {
                    cell.Blocks.Add(new FlowParagraph());
                }

                row.Cells.Add(cell);
                column += columnSpan;
            }
        }

        return table;
    }

    private TableCellProperties ResolveTableCellProperties(
        DocumentTableRow row,
        DocumentTableCell cell,
        TableProperties tableProperties,
        TableStyleDefinition? tableStyle,
        TableLook tableLook,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        var resolved = new TableCellProperties();

        if (tableStyle is not null)
        {
            ApplyTablePropertiesToCell(resolved, tableStyle.TableProperties, rowIndex, columnIndex, rowCount, columnCount);
            ApplyTableCellProperties(resolved, tableStyle.CellProperties);

            if (_options.ApplyTableStyleConditions)
            {
                foreach (var condition in GetApplicableTableStyleConditions(tableLook, rowIndex, columnIndex, rowCount, columnCount))
                {
                    if (!tableStyle.Conditions.TryGetValue(condition, out var conditionProperties))
                    {
                        continue;
                    }

                    ApplyTablePropertiesToCell(resolved, conditionProperties.TableProperties, rowIndex, columnIndex, rowCount, columnCount);
                    ApplyTableCellProperties(resolved, conditionProperties.CellProperties);
                }
            }
        }

        ApplyTablePropertiesToCell(resolved, tableProperties, rowIndex, columnIndex, rowCount, columnCount);
        ApplyTableRowPropertiesToCell(resolved, row.Properties);
        ApplyTableCellProperties(resolved, cell.Properties);
        return resolved;
    }

    private static TableProperties MergeTableProperties(TableProperties? baseProperties, TableProperties overrideProperties)
    {
        var merged = baseProperties?.Clone() ?? new TableProperties();
        ApplyTableProperties(merged, overrideProperties);
        return merged;
    }

    private static TableLook ResolveTableLook(TableLook? look)
    {
        return look?.Clone() ?? new TableLook();
    }

    private static int ResolveTableColumnCount(IReadOnlyList<DocumentTableRow> rows, TableProperties properties)
    {
        if (properties.ColumnWidths.Count > 0)
        {
            return properties.ColumnWidths.Count;
        }

        var maxColumns = 0;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var current = Math.Max(0, row.Properties.GridBefore ?? 0);
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                current += Math.Max(1, row.Cells[cellIndex].ColumnSpan);
            }

            current += Math.Max(0, row.Properties.GridAfter ?? 0);
            if (current > maxColumns)
            {
                maxColumns = current;
            }
        }

        return Math.Max(1, maxColumns);
    }

    private static int ComputeRowSpan(IReadOnlyList<DocumentTableRow> rows, int rowIndex, int column, int columnSpan)
    {
        var rowSpan = 1;
        for (var nextRow = rowIndex + 1; nextRow < rows.Count; nextRow++)
        {
            if (!TryGetCellAtColumn(rows[nextRow], column, out var cell, out var startColumn))
            {
                break;
            }

            if (startColumn != column
                || Math.Max(1, cell.ColumnSpan) != columnSpan
                || cell.VerticalMerge != TableCellVerticalMerge.Continue)
            {
                break;
            }

            rowSpan++;
        }

        return rowSpan;
    }

    private static bool TryGetCellAtColumn(DocumentTableRow row, int targetColumn, out DocumentTableCell cell, out int startColumn)
    {
        var column = Math.Max(0, row.Properties.GridBefore ?? 0);
        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
        {
            var current = row.Cells[cellIndex];
            var span = Math.Max(1, current.ColumnSpan);
            if (targetColumn >= column && targetColumn < column + span)
            {
                cell = current;
                startColumn = column;
                return true;
            }

            column += span;
        }

        cell = null!;
        startColumn = -1;
        return false;
    }

    private void ApplyFlowTableCellProperties(DocumentTableCell source, TableCellProperties resolvedProperties, FlowTableCell target)
    {
        if (resolvedProperties.Padding.HasValue)
        {
            target.Padding = ToFlowThickness(resolvedProperties.Padding.Value);
        }

        if (resolvedProperties.ShadingColor.HasValue)
        {
            target.Background = ToFlowColor(resolvedProperties.ShadingColor.Value);
        }

        var borderThickness = new Vibe.Office.FlowDocument.FlowThickness(
            resolvedProperties.Borders.Left?.Thickness ?? 0f,
            resolvedProperties.Borders.Top?.Thickness ?? 0f,
            resolvedProperties.Borders.Right?.Thickness ?? 0f,
            resolvedProperties.Borders.Bottom?.Thickness ?? 0f);
        if (!borderThickness.IsEmpty)
        {
            target.BorderThickness = borderThickness;
        }

        var borderColor = resolvedProperties.Borders.Left?.Color
                          ?? resolvedProperties.Borders.Top?.Color
                          ?? resolvedProperties.Borders.Right?.Color
                          ?? resolvedProperties.Borders.Bottom?.Color;
        if (borderColor.HasValue)
        {
            target.BorderBrush = ToFlowColor(borderColor.Value);
        }

        target.FlowDirection = resolvedProperties.TextDirection?.ToString();

        var alignment = InferCellTextAlignment(source.Blocks);
        if (alignment.HasValue)
        {
            target.TextAlignment = alignment.Value;
        }
    }

    private Vibe.Office.FlowDocument.FlowTextAlignment? InferCellTextAlignment(IReadOnlyList<DocumentBlock> blocks)
    {
        Vibe.Office.FlowDocument.FlowTextAlignment? result = null;
        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index] is not DocumentParagraph paragraph)
            {
                continue;
            }

            var resolved = ResolveParagraphProperties(paragraph);
            if (!resolved.Alignment.HasValue)
            {
                continue;
            }

            var current = ToFlowAlignment(resolved.Alignment.Value);
            if (!result.HasValue)
            {
                result = current;
                continue;
            }

            if (result.Value != current)
            {
                return null;
            }
        }

        return result;
    }

    private static IEnumerable<TableStyleCondition> GetApplicableTableStyleConditions(
        TableLook look,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        var hasFirstRow = look.FirstRow && rowCount > 0;
        var hasLastRow = look.LastRow && rowCount > 1;
        var hasFirstColumn = look.FirstColumn && columnCount > 0;
        var hasLastColumn = look.LastColumn && columnCount > 1;

        if (look.BandedRows)
        {
            var bandStart = hasFirstRow ? 1 : 0;
            var bandEnd = hasLastRow ? rowCount - 1 : rowCount;
            if (rowIndex >= bandStart && rowIndex < bandEnd)
            {
                var bandIndex = rowIndex - bandStart;
                yield return bandIndex % 2 == 0 ? TableStyleCondition.Band1Horizontal : TableStyleCondition.Band2Horizontal;
            }
        }

        if (look.BandedColumns)
        {
            var bandStart = hasFirstColumn ? 1 : 0;
            var bandEnd = hasLastColumn ? columnCount - 1 : columnCount;
            if (columnIndex >= bandStart && columnIndex < bandEnd)
            {
                var bandIndex = columnIndex - bandStart;
                yield return bandIndex % 2 == 0 ? TableStyleCondition.Band1Vertical : TableStyleCondition.Band2Vertical;
            }
        }

        if (look.FirstRow && rowIndex == 0)
        {
            yield return TableStyleCondition.FirstRow;
        }

        if (look.LastRow && rowIndex == rowCount - 1)
        {
            yield return TableStyleCondition.LastRow;
        }

        if (look.FirstColumn && columnIndex == 0)
        {
            yield return TableStyleCondition.FirstColumn;
        }

        if (look.LastColumn && columnIndex == columnCount - 1)
        {
            yield return TableStyleCondition.LastColumn;
        }

        if (look.FirstRow && look.FirstColumn && rowIndex == 0 && columnIndex == 0)
        {
            yield return TableStyleCondition.NorthWestCell;
        }

        if (look.FirstRow && look.LastColumn && rowIndex == 0 && columnIndex == columnCount - 1)
        {
            yield return TableStyleCondition.NorthEastCell;
        }

        if (look.LastRow && look.FirstColumn && rowIndex == rowCount - 1 && columnIndex == 0)
        {
            yield return TableStyleCondition.SouthWestCell;
        }

        if (look.LastRow && look.LastColumn && rowIndex == rowCount - 1 && columnIndex == columnCount - 1)
        {
            yield return TableStyleCondition.SouthEastCell;
        }
    }

    private static void ApplyTableProperties(TableProperties target, TableProperties source)
    {
        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.Clear();
            target.ColumnWidths.AddRange(source.ColumnWidths);
        }

        if (source.Width.HasValue)
        {
            target.Width = source.Width;
        }

        if (source.WidthUnit.HasValue)
        {
            target.WidthUnit = source.WidthUnit;
        }

        if (source.Indent.HasValue)
        {
            target.Indent = source.Indent;
        }

        if (source.IndentUnit.HasValue)
        {
            target.IndentUnit = source.IndentUnit;
        }

        if (source.Alignment.HasValue)
        {
            target.Alignment = source.Alignment;
        }

        if (source.LayoutMode.HasValue)
        {
            target.LayoutMode = source.LayoutMode;
        }

        if (source.CellSpacing.HasValue)
        {
            target.CellSpacing = source.CellSpacing;
        }

        if (source.CellSpacingUnit.HasValue)
        {
            target.CellSpacingUnit = source.CellSpacingUnit;
        }

        if (source.CellPadding.HasValue)
        {
            target.CellPadding = MergePadding(target.CellPadding, source.CellPadding.Value);
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        if (source.Look is not null)
        {
            target.Look = source.Look.Clone();
        }

        ApplyTableBorders(target.Borders, source.Borders);
    }

    private static void ApplyTablePropertiesToCell(
        TableCellProperties target,
        TableProperties source,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        if (source.CellPadding.HasValue)
        {
            target.Padding = MergePadding(target.Padding, source.CellPadding.Value);
        }

        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }

        ApplyTableBordersToCell(target.Borders, source.Borders, rowIndex, columnIndex, rowCount, columnCount);
    }

    private static void ApplyTableRowPropertiesToCell(TableCellProperties target, TableRowProperties source)
    {
        if (source.ShadingColor.HasValue)
        {
            target.ShadingColor = source.ShadingColor;
        }
    }

    private static void ApplyTableCellProperties(TableCellProperties target, TableCellProperties source)
    {
        if (source.Padding.HasValue)
        {
            target.Padding = MergePadding(target.Padding, source.Padding.Value);
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

        ApplyTableCellBorders(target.Borders, source.Borders);
    }

    private static void ApplyTableBorders(TableBorders target, TableBorders source)
    {
        if (source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }

        if (source.InsideHorizontal is not null)
        {
            target.InsideHorizontal = source.InsideHorizontal.Clone();
        }

        if (source.InsideVertical is not null)
        {
            target.InsideVertical = source.InsideVertical.Clone();
        }
    }

    private static void ApplyTableBordersToCell(
        TableCellBorders target,
        TableBorders source,
        int rowIndex,
        int columnIndex,
        int rowCount,
        int columnCount)
    {
        var isFirstRow = rowIndex == 0;
        var isLastRow = rowIndex == rowCount - 1;
        var isFirstColumn = columnIndex == 0;
        var isLastColumn = columnIndex == columnCount - 1;

        if (isFirstRow && source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (isLastRow && source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (isFirstColumn && source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (isLastColumn && source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }

        if (source.InsideHorizontal is not null && !isLastRow)
        {
            target.Bottom = source.InsideHorizontal.Clone();
        }

        if (source.InsideVertical is not null && !isLastColumn)
        {
            target.Right = source.InsideVertical.Clone();
        }
    }

    private static void ApplyTableCellBorders(TableCellBorders target, TableCellBorders source)
    {
        if (source.Top is not null)
        {
            target.Top = source.Top.Clone();
        }

        if (source.Bottom is not null)
        {
            target.Bottom = source.Bottom.Clone();
        }

        if (source.Left is not null)
        {
            target.Left = source.Left.Clone();
        }

        if (source.Right is not null)
        {
            target.Right = source.Right.Clone();
        }
    }

    private static DocThickness MergePadding(DocThickness? basePadding, DocThickness overridePadding)
    {
        if (!basePadding.HasValue)
        {
            return overridePadding;
        }

        var value = basePadding.Value;
        return new DocThickness(
            float.IsNaN(overridePadding.Left) ? value.Left : overridePadding.Left,
            float.IsNaN(overridePadding.Top) ? value.Top : overridePadding.Top,
            float.IsNaN(overridePadding.Right) ? value.Right : overridePadding.Right,
            float.IsNaN(overridePadding.Bottom) ? value.Bottom : overridePadding.Bottom);
    }

    private static Vibe.Office.FlowDocument.FlowThickness ToFlowThickness(DocThickness value)
    {
        return new Vibe.Office.FlowDocument.FlowThickness(value.Left, value.Top, value.Right, value.Bottom);
    }

    private static string ToFlowColor(DocColor color)
    {
        return color.A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private sealed class ListContext
    {
        public ListContext(FlowList list, ListInfo info)
        {
            List = list;
            Info = info;
        }

        public FlowList List { get; }

        public ListInfo Info { get; }

        public FlowListItem? LastItem { get; set; }
    }

    private readonly record struct EmbeddedUiResolution(bool IsMatch, bool IsInline, object? Child)
    {
        public static EmbeddedUiResolution NoMatch => new(false, false, null);
    }
}
