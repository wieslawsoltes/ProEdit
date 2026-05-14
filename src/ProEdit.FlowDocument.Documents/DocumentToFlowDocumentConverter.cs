using System.Globalization;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Primitives;
using DocumentBlock = ProEdit.Documents.Block;
using DocumentInline = ProEdit.Documents.Inline;
using DocumentParagraph = ProEdit.Documents.ParagraphBlock;
using DocumentTableCell = ProEdit.Documents.TableCell;
using DocumentTableRow = ProEdit.Documents.TableRow;
using FlowAnchoredBlock = ProEdit.FlowDocument.AnchoredBlock;
using FlowBlock = ProEdit.FlowDocument.Block;
using FlowBlockCollection = ProEdit.FlowDocument.BlockCollection;
using FlowInline = ProEdit.FlowDocument.Inline;
using FlowInlineCollection = ProEdit.FlowDocument.InlineCollection;
using FlowList = ProEdit.FlowDocument.List;
using FlowListItem = ProEdit.FlowDocument.ListItem;
using FlowParagraph = ProEdit.FlowDocument.Paragraph;
using FlowTableCell = ProEdit.FlowDocument.TableCell;
using FlowTableRow = ProEdit.FlowDocument.TableRow;
using FlowTableRowGroup = ProEdit.FlowDocument.TableRowGroup;

namespace ProEdit.FlowDocument.Documents;

/// <summary>
/// Converts <see cref="Document"/> content into <see cref="ProEdit.FlowDocument.FlowDocument"/> instances.
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
    /// Converts the specified <see cref="Document"/> into a <see cref="ProEdit.FlowDocument.FlowDocument"/>.
    /// </summary>
    /// <param name="source">The source document.</param>
    /// <returns>The converted flow document.</returns>
    public ProEdit.FlowDocument.FlowDocument Convert(Document source)
    {
        ArgumentNullException.ThrowIfNull(source);

        BeginConversion(source);
        try
        {
            var document = new ProEdit.FlowDocument.FlowDocument();
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
        out ProEdit.FlowDocument.Block block)
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

    private void ApplyDocumentDefaults(Document source, ProEdit.FlowDocument.FlowDocument target)
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
            ? ProEdit.FlowDocument.FlowFontWeight.Bold
            : ProEdit.FlowDocument.FlowFontWeight.Normal;
        target.FontStyle = text.FontStyle == DocFontStyle.Italic
            ? ProEdit.FlowDocument.FlowFontStyle.Italic
            : ProEdit.FlowDocument.FlowFontStyle.Normal;

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

        var pagePadding = new ProEdit.FlowDocument.FlowThickness(
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

    private static ProEdit.FlowDocument.FlowListMarkerStyle ToFlowMarkerStyle(ListInfo info)
    {
        if (info.Kind == ListKind.None)
        {
            return ProEdit.FlowDocument.FlowListMarkerStyle.None;
        }

        if (info.Kind == ListKind.Bullet)
        {
            return info.BulletSymbol switch
            {
                "○" => ProEdit.FlowDocument.FlowListMarkerStyle.Circle,
                "■" => ProEdit.FlowDocument.FlowListMarkerStyle.Square,
                _ => ProEdit.FlowDocument.FlowListMarkerStyle.Disc
            };
        }

        return info.NumberFormat switch
        {
            ListNumberFormat.LowerLetter => ProEdit.FlowDocument.FlowListMarkerStyle.LowerLatin,
            ListNumberFormat.UpperLetter => ProEdit.FlowDocument.FlowListMarkerStyle.UpperLatin,
            ListNumberFormat.LowerRoman => ProEdit.FlowDocument.FlowListMarkerStyle.LowerRoman,
            ListNumberFormat.UpperRoman => ProEdit.FlowDocument.FlowListMarkerStyle.UpperRoman,
            _ => ProEdit.FlowDocument.FlowListMarkerStyle.Decimal
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
                paragraph.Inlines.Add(new ProEdit.FlowDocument.Run(source.Text));
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

    private bool TryConvertBlockUiContainer(DocumentParagraph source, out ProEdit.FlowDocument.BlockUIContainer blockUiContainer)
    {
        blockUiContainer = null!;

        if (source.Inlines.Count == 1 && source.Inlines[0] is ShapeInline shape)
        {
            var resolved = ResolveEmbeddedUiChild(shape, false);
            if (resolved.IsMatch && !resolved.IsInline)
            {
                var container = new ProEdit.FlowDocument.BlockUIContainer
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
            var container = new ProEdit.FlowDocument.BlockUIContainer
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
            ? new ProEdit.FlowDocument.Floater
            {
                Width = Math.Max(1f, shape.Width),
                HorizontalAlignment = source.Anchor.HorizontalAlignment.ToString()
            }
            : new ProEdit.FlowDocument.Figure
            {
                Width = Math.Max(1f, shape.Width),
                Height = Math.Max(1f, shape.Height),
                HorizontalAnchor = source.Anchor.HorizontalReference.ToString(),
                VerticalAnchor = source.Anchor.VerticalReference.ToString(),
                HorizontalOffset = source.Anchor.OffsetX,
                VerticalOffset = source.Anchor.OffsetY
            };

        anchored.Margin = ToFlowThickness(source.Anchor.Distance);

        var resolved = ResolveEmbeddedUiChild(shape, inlineFallback: false);
        if (resolved.IsMatch)
        {
            if (resolved.IsInline)
            {
                var paragraph = new FlowParagraph();
                paragraph.Inlines.Add(new ProEdit.FlowDocument.InlineUIContainer
                {
                    Child = resolved.Child
                });
                anchored.Blocks.Add(paragraph);
            }
            else
            {
                anchored.Blocks.Add(new ProEdit.FlowDocument.BlockUIContainer
                {
                    Child = resolved.Child
                });
            }

            return anchored;
        }

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
                target.Add(new ProEdit.FlowDocument.InlineUIContainer
                {
                    Child = resolved.Child
                });
                return;
            }

            target.Add(new ProEdit.FlowDocument.Run(_options.InlineUiPlaceholderText));
            return;
        }

        if (source is ImageInline imageInline)
        {
            if (TryCreateFlowInlineImage(imageInline, out var container))
            {
                AddStyledInline(target, container, style: null, source.Hyperlink);
                return;
            }

            target.Add(new ProEdit.FlowDocument.Run(_options.InlineUiPlaceholderText));
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
                var run = new ProEdit.FlowDocument.Run(segment);
                AddStyledInline(target, run, runStyleDelta, source.Hyperlink);
            }

            if (index + 1 < parts.Length)
            {
                var lineBreak = new ProEdit.FlowDocument.LineBreak();
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

    private static void ApplyResolvedTextStyle(TextStyle source, ProEdit.FlowDocument.TextElement target)
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
            ? ProEdit.FlowDocument.FlowFontWeight.Bold
            : ProEdit.FlowDocument.FlowFontWeight.Normal;
        target.FontStyle = source.FontStyle == DocFontStyle.Italic
            ? ProEdit.FlowDocument.FlowFontStyle.Italic
            : ProEdit.FlowDocument.FlowFontStyle.Normal;
        target.Foreground = ToFlowColor(source.Color);

        if (source.HighlightColor.HasValue)
        {
            target.Background = ToFlowColor(source.HighlightColor.Value);
        }

        var decorations = ToFlowTextDecorations(source.Underline, source.UnderlineStyle, source.Strikethrough);
        if (decorations != ProEdit.FlowDocument.FlowTextDecorations.None)
        {
            if (target is FlowParagraph paragraph)
            {
                paragraph.TextDecorations = decorations;
            }
            else if (target is ProEdit.FlowDocument.Span span)
            {
                span.TextDecorations = decorations;
            }
        }
    }

    private static bool TryCreateFlowInlineImage(ImageInline image, out ProEdit.FlowDocument.InlineUIContainer container)
    {
        container = null!;
        if (image.Data.Length == 0)
        {
            return false;
        }

        var bytes = new byte[image.Data.Length];
        image.Data.CopyTo(bytes, 0);
        container = new ProEdit.FlowDocument.InlineUIContainer
        {
            Child = new FlowInlineImageData(bytes, image.Width, image.Height, image.ContentType)
        };

        return true;
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
            var link = new ProEdit.FlowDocument.Hyperlink();
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
                link.SetValue(ProEdit.FlowDocument.Hyperlink.ToolTipProperty, hyperlink.Tooltip);
            }

            link.Inlines.Add(current);
            current = link;
        }

        target.Add(current);
    }

    private static ProEdit.FlowDocument.Span? CreateStyleSpan(TextStyleProperties? style)
    {
        if (style is null || !style.HasValues)
        {
            return null;
        }

        var span = new ProEdit.FlowDocument.Span();
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
                ? ProEdit.FlowDocument.FlowFontWeight.Bold
                : ProEdit.FlowDocument.FlowFontWeight.Normal;
            hasValues = true;
        }

        if (style.FontStyle.HasValue)
        {
            span.FontStyle = style.FontStyle == DocFontStyle.Italic
                ? ProEdit.FlowDocument.FlowFontStyle.Italic
                : ProEdit.FlowDocument.FlowFontStyle.Normal;
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
                DocVerticalPosition.Superscript => ProEdit.FlowDocument.FlowBaselineAlignment.Superscript,
                DocVerticalPosition.Subscript => ProEdit.FlowDocument.FlowBaselineAlignment.Subscript,
                _ => ProEdit.FlowDocument.FlowBaselineAlignment.Baseline
            };
            if (baseline != ProEdit.FlowDocument.FlowBaselineAlignment.Baseline)
            {
                span.BaselineAlignment = baseline;
                hasValues = true;
            }
        }

        var decorations = ToFlowTextDecorations(style.Underline, style.UnderlineStyle, style.Strikethrough);
        if (decorations != ProEdit.FlowDocument.FlowTextDecorations.None)
        {
            span.TextDecorations = decorations;
            hasValues = true;
        }

        return hasValues ? span : null;
    }

    private static ProEdit.FlowDocument.FlowTextDecorations ToFlowTextDecorations(
        bool? underline,
        DocUnderlineStyle? underlineStyle,
        bool? strikethrough)
    {
        var result = ProEdit.FlowDocument.FlowTextDecorations.None;
        if (underline == true || (underlineStyle.HasValue && underlineStyle.Value != DocUnderlineStyle.None))
        {
            result |= ProEdit.FlowDocument.FlowTextDecorations.Underline;
        }

        if (strikethrough == true)
        {
            result |= ProEdit.FlowDocument.FlowTextDecorations.Strikethrough;
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

        var margin = new ProEdit.FlowDocument.FlowThickness(
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

        var borderThickness = new ProEdit.FlowDocument.FlowThickness(
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

    private static ProEdit.FlowDocument.FlowTextAlignment ToFlowAlignment(ParagraphAlignment alignment)
    {
        return alignment switch
        {
            ParagraphAlignment.Center => ProEdit.FlowDocument.FlowTextAlignment.Center,
            ParagraphAlignment.Right => ProEdit.FlowDocument.FlowTextAlignment.Right,
            ParagraphAlignment.Justify => ProEdit.FlowDocument.FlowTextAlignment.Justify,
            _ => ProEdit.FlowDocument.FlowTextAlignment.Left
        };
    }

    private ProEdit.FlowDocument.Table ConvertTable(TableBlock source)
    {
        var tableStyle = _styleResolver is not null && _options.ResolveInheritedStyles
            ? _styleResolver.ResolveTableStyle(source)
            : null;
        var mergedTableProperties = MergeTableProperties(tableStyle?.TableProperties, source.Properties);
        var tableLook = ResolveTableLook(source.Properties.Look ?? mergedTableProperties.Look);
        var rowCount = source.Rows.Count;
        var columnCount = ResolveTableColumnCount(source.Rows, mergedTableProperties);

        var table = new ProEdit.FlowDocument.Table();
        if (mergedTableProperties.CellSpacing.HasValue && mergedTableProperties.CellSpacing.Value >= 0f)
        {
            table.CellSpacing = mergedTableProperties.CellSpacing.Value;
        }

        if (mergedTableProperties.ColumnWidths.Count > 0)
        {
            for (var columnIndex = 0; columnIndex < mergedTableProperties.ColumnWidths.Count; columnIndex++)
            {
                var width = mergedTableProperties.ColumnWidths[columnIndex];
                table.Columns.Add(new ProEdit.FlowDocument.TableColumn
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

        var borderThickness = new ProEdit.FlowDocument.FlowThickness(
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

    private ProEdit.FlowDocument.FlowTextAlignment? InferCellTextAlignment(IReadOnlyList<DocumentBlock> blocks)
    {
        ProEdit.FlowDocument.FlowTextAlignment? result = null;
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

    private static ProEdit.FlowDocument.FlowThickness ToFlowThickness(DocThickness value)
    {
        return new ProEdit.FlowDocument.FlowThickness(value.Left, value.Top, value.Right, value.Bottom);
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
