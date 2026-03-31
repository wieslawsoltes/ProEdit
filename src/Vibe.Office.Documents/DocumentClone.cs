using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public static class DocumentClone
{
    public static Document Clone(Document source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = new Document();
        Copy(source, clone);
        return clone;
    }

    public static DocumentStyles CloneStyles(DocumentStyles source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = new DocumentStyles();
        CopyDocumentStyles(source, clone);
        return clone;
    }

    public static DocumentFonts CloneFonts(DocumentFonts source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = new DocumentFonts();
        CopyDocumentFonts(source, clone);
        return clone;
    }

    public static DocumentThemeColorMap CloneThemeColors(DocumentThemeColorMap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = new DocumentThemeColorMap();
        CopyThemeColors(source, clone);
        return clone;
    }

    public static void Copy(Document source, Document target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        target.MirrorMargins = source.MirrorMargins;
        target.GutterAtTop = source.GutterAtTop;
        target.EvenAndOddHeaders = source.EvenAndOddHeaders;
        target.TrackChangesEnabled = source.TrackChangesEnabled;
        target.FormsDesignMode = source.FormsDesignMode;
        target.ReadOnlyRecommended = source.ReadOnlyRecommended;
        target.UpdateFieldsOnOpen = source.UpdateFieldsOnOpen;
        target.DefaultTabStop = source.DefaultTabStop;
        target.AutoHyphenation = source.AutoHyphenation;
        target.ConsecutiveHyphenLimit = source.ConsecutiveHyphenLimit;
        target.HyphenationZone = source.HyphenationZone;
        target.DoNotHyphenateCaps = source.DoNotHyphenateCaps;
        target.CitationStyle = source.CitationStyle;
        target.MailMergeData = source.MailMergeData?.Clone();
        CopyCitationSources(source.CitationSources, target.CitationSources);

        CopyTextStyle(source.DefaultTextStyle, target.DefaultTextStyle);
        CopyParagraphStyleProperties(source.DefaultParagraphStyleProperties, target.DefaultParagraphStyleProperties);

        CopySectionProperties(source.SectionProperties, target.SectionProperties);
        CopyHeaderFooter(source.Header, target.Header);
        CopyHeaderFooter(source.Footer, target.Footer);
        CopyHeaderFooter(source.FirstHeader, target.FirstHeader);
        CopyHeaderFooter(source.FirstFooter, target.FirstFooter);
        CopyHeaderFooter(source.EvenHeader, target.EvenHeader);
        CopyHeaderFooter(source.EvenFooter, target.EvenFooter);

        CopyDocumentStyles(source.Styles, target.Styles);
        CopyDocumentFonts(source.Fonts, target.Fonts);
        CopyThemeColors(source.ThemeColors, target.ThemeColors);
        CopyDocumentProperties(source.Properties, target.Properties);
        CopyCompatibilitySettings(source.Compatibility, target.Compatibility);
        CopyProtectionSettings(source.Protection, target.Protection);
        CopyCustomXmlParts(source.CustomXmlParts, target.CustomXmlParts);
        CopyRevisions(source.Revisions, target.Revisions);
        CopyMacros(source.Macros, target.Macros);

        CopyListDefinitions(source.ListDefinitions, target.ListDefinitions);
        CopyNotes(source.Footnotes, target.Footnotes, CloneFootnoteDefinition);
        CopyNotes(source.Endnotes, target.Endnotes, CloneEndnoteDefinition);
        CopyNoteSeparators(source.FootnoteSeparators, target.FootnoteSeparators);
        CopyNoteSeparators(source.EndnoteSeparators, target.EndnoteSeparators);
        CopyNotes(source.Comments, target.Comments, CloneCommentDefinition);

        CopyBlocks(source.Blocks, target.Blocks);
        CopySections(source.Sections, target.Sections);
    }

    private static void CopyCitationSources(CitationSourceCatalog source, CitationSourceCatalog target)
    {
        target.Sources.Clear();
        foreach (var citationSource in source.Sources)
        {
            target.Sources.Add(citationSource.Clone());
        }
    }

    private static void CopyDocumentProperties(DocumentProperties source, DocumentProperties target)
    {
        target.Clear();
        foreach (var pair in source.CoreProperties)
        {
            target.CoreProperties[pair.Key] = pair.Value;
        }

        foreach (var pair in source.CustomProperties)
        {
            target.CustomProperties[pair.Key] = pair.Value;
        }
    }

    private static void CopyCompatibilitySettings(DocumentCompatibilitySettings source, DocumentCompatibilitySettings target)
    {
        target.SuppressSpacingAtTopOfPage = source.SuppressSpacingAtTopOfPage;
        target.SuppressSpacingBeforeAfterPageBreak = source.SuppressSpacingBeforeAfterPageBreak;
        target.UseWord97LineBreakRules = source.UseWord97LineBreakRules;
        target.WrapTrailSpaces = source.WrapTrailSpaces;
        target.DoNotUseEastAsianBreakRules = source.DoNotUseEastAsianBreakRules;
        target.UseAltKinsokuLineBreakRules = source.UseAltKinsokuLineBreakRules;
        target.DoNotWrapTextWithPunctuation = source.DoNotWrapTextWithPunctuation;
    }

    private static void CopyProtectionSettings(DocumentProtectionSettings source, DocumentProtectionSettings target)
    {
        target.EditMode = source.EditMode;
        target.Enforcement = source.Enforcement;
        target.Formatting = source.Formatting;
        target.CryptProviderType = source.CryptProviderType;
        target.CryptAlgorithmClass = source.CryptAlgorithmClass;
        target.CryptAlgorithmType = source.CryptAlgorithmType;
        target.CryptAlgorithmSid = source.CryptAlgorithmSid;
        target.CryptSpinCount = source.CryptSpinCount;
        target.Hash = source.Hash;
        target.Salt = source.Salt;
    }

    private static void CopyCustomXmlParts(Dictionary<string, XDocument> source, Dictionary<string, XDocument> target)
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = new XDocument(pair.Value);
        }
    }

    private static void CopyMacros(DocumentMacros source, DocumentMacros target)
    {
        target.Items.Clear();
        target.VbaModules.Clear();
        target.References.Clear();
        target.IsTrusted = source.IsTrusted;
        target.VbaProject = CloneBytes(source.VbaProject);
        foreach (var macro in source.Items)
        {
            target.Items.Add(CloneMacroDefinition(macro));
        }

        foreach (var module in source.VbaModules)
        {
            target.VbaModules.Add(CloneVbaModuleInfo(module));
        }

        foreach (var reference in source.References)
        {
            target.References.Add(CloneVbaProjectReference(reference));
        }
    }

    private static MacroDefinition CloneMacroDefinition(MacroDefinition source)
    {
        var clone = new MacroDefinition
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Language = source.Language,
            IsTrusted = source.IsTrusted,
            Source = source.Source
        };

        foreach (var command in source.Commands)
        {
            clone.Commands.Add(CloneMacroCommand(command));
        }

        return clone;
    }

    private static MacroCommand CloneMacroCommand(MacroCommand source)
    {
        return new MacroCommand
        {
            CommandId = source.CommandId,
            Payload = source.Payload is null
                ? null
                : new MacroPayload
                {
                    TypeId = source.Payload.TypeId,
                    Json = source.Payload.Json
                }
        };
    }

    private static VbaModuleInfo CloneVbaModuleInfo(VbaModuleInfo source)
    {
        var clone = new VbaModuleInfo
        {
            Name = source.Name,
            StreamName = source.StreamName,
            Source = source.Source
        };

        foreach (var procedure in source.Procedures)
        {
            clone.Procedures.Add(procedure);
        }

        return clone;
    }

    private static VbaProjectReference CloneVbaProjectReference(VbaProjectReference source)
    {
        return new VbaProjectReference
        {
            Name = source.Name,
            Identifier = source.Identifier,
            Description = source.Description
        };
    }

    private static void CopySections(IReadOnlyList<DocumentSection> source, List<DocumentSection> target)
    {
        target.Clear();
        if (source.Count == 0)
        {
            return;
        }

        foreach (var section in source)
        {
            var properties = section.Properties.Clone();
            var header = CloneHeaderFooter(section.Header);
            var footer = CloneHeaderFooter(section.Footer);
            var firstHeader = CloneHeaderFooter(section.FirstHeader);
            var firstFooter = CloneHeaderFooter(section.FirstFooter);
            var evenHeader = CloneHeaderFooter(section.EvenHeader);
            var evenFooter = CloneHeaderFooter(section.EvenFooter);
            target.Add(new DocumentSection(properties, header, footer, firstHeader, firstFooter, evenHeader, evenFooter));
        }
    }

    private static HeaderFooter CloneHeaderFooter(HeaderFooter source)
    {
        var clone = new HeaderFooter();
        CopyHeaderFooter(source, clone);
        return clone;
    }

    private static void CopyHeaderFooter(HeaderFooter source, HeaderFooter target)
    {
        CopyBlocks(source.Blocks, target.Blocks);
        target.IsDefined = source.IsDefined;
    }

    private static void CopyBlocks(IReadOnlyList<Block> source, List<Block> target)
    {
        target.Clear();
        foreach (var block in source)
        {
            target.Add(CloneBlock(block));
        }
    }

    public static Block CloneBlock(Block block)
    {
        Block clone = block switch
        {
            ParagraphBlock paragraph => CloneParagraphBlock(paragraph),
            TableBlock table => CloneTableBlock(table),
            PageBreakBlock => new PageBreakBlock(),
            ColumnBreakBlock => new ColumnBreakBlock(),
            AltChunkBlock altChunk => CloneAltChunkBlock(altChunk),
            SectionBreakBlock section => CloneSectionBreakBlock(section),
            ContentControlStartBlock contentStart => new ContentControlStartBlock(contentStart.Properties.Clone()),
            ContentControlEndBlock contentEnd => new ContentControlEndBlock(contentEnd.Id),
            MetadataStartBlock metaStart => new MetadataStartBlock(CloneMetadataContainer(metaStart.Metadata)),
            MetadataEndBlock metaEnd => new MetadataEndBlock(CloneMetadataContainer(metaEnd.Metadata)),
            RevisionStartBlock revisionStart => new RevisionStartBlock(revisionStart.Revision.Clone()),
            RevisionEndBlock revisionEnd => new RevisionEndBlock(revisionEnd.Kind, revisionEnd.Id),
            _ => throw new NotSupportedException($"Unsupported block type: {block.GetType().Name}")
        };
        clone.NodeId = block.NodeId;
        return clone;
    }

    private static ParagraphBlock CloneParagraphBlock(ParagraphBlock source)
    {
        var clone = new ParagraphBlock(source.Text, source.ListInfo?.Clone())
        {
            StyleId = source.StyleId
        };

        CopyParagraphProperties(source.Properties, clone.Properties);
        foreach (var inline in source.Inlines)
        {
            clone.Inlines.Add(CloneInline(inline));
        }

        foreach (var floating in source.FloatingObjects)
        {
            clone.FloatingObjects.Add(CloneFloatingObject(floating));
        }

        return clone;
    }

    private static TableBlock CloneTableBlock(TableBlock source)
    {
        var clone = new TableBlock
        {
            StyleId = source.StyleId
        };

        CopyTableProperties(source.Properties, clone.Properties);
        foreach (var row in source.Rows)
        {
            clone.Rows.Add(CloneTableRow(row));
        }

        return clone;
    }

    private static TableRow CloneTableRow(TableRow source)
    {
        var clone = new TableRow
        {
            ContentControl = source.ContentControl?.Clone()
        };

        CopyTableRowProperties(source.Properties, clone.Properties);
        foreach (var cell in source.Cells)
        {
            clone.Cells.Add(CloneTableCell(cell));
        }

        foreach (var metadata in source.Metadata)
        {
            clone.Metadata.Add(CloneMetadataContainer(metadata));
        }

        return clone;
    }

    private static TableCell CloneTableCell(TableCell source)
    {
        var clone = new TableCell
        {
            ContentControl = source.ContentControl?.Clone(),
            ColumnSpan = source.ColumnSpan,
            VerticalMerge = source.VerticalMerge
        };

        CopyTableCellProperties(source.Properties, clone.Properties);
        foreach (var block in source.Blocks)
        {
            clone.Blocks.Add(CloneBlock(block));
        }

        foreach (var metadata in source.Metadata)
        {
            clone.Metadata.Add(CloneMetadataContainer(metadata));
        }

        return clone;
    }

    private static AltChunkBlock CloneAltChunkBlock(AltChunkBlock source)
    {
        return new AltChunkBlock
        {
            RelationshipId = source.RelationshipId,
            ContentType = source.ContentType,
            TargetUri = source.TargetUri,
            Data = CloneBytes(source.Data),
            Label = source.Label
        };
    }

    private static SectionBreakBlock CloneSectionBreakBlock(SectionBreakBlock source)
    {
        return new SectionBreakBlock
        {
            Properties = source.Properties.Clone(),
            BreakType = source.BreakType,
            SectionIndex = source.SectionIndex
        };
    }

    public static Inline CloneInline(Inline inline)
    {
        ArgumentNullException.ThrowIfNull(inline);
        Inline clone = inline switch
        {
            RunInline run => CloneRunInline(run),
            EquationInline equation => CloneEquationInline(equation),
            RubyInline ruby => CloneRubyInline(ruby),
            ImageInline image => CloneImageInline(image),
            ShapeInline shape => CloneShapeInline(shape),
            ChartInline chart => CloneChartInline(chart),
            PageNumberInline pageNumber => new PageNumberInline(pageNumber.Style?.Clone()),
            TotalPagesInline totalPages => new TotalPagesInline(totalPages.Style?.Clone()),
            FieldStartInline fieldStart => new FieldStartInline(fieldStart.Instruction)
            {
                Definition = fieldStart.Definition,
                IsLocked = fieldStart.IsLocked,
                IsDirty = fieldStart.IsDirty
            },
            FieldSeparatorInline => new FieldSeparatorInline(),
            FieldEndInline => new FieldEndInline(),
            BookmarkStartInline bookmarkStart => new BookmarkStartInline(bookmarkStart.Id, bookmarkStart.Name),
            BookmarkEndInline bookmarkEnd => new BookmarkEndInline(bookmarkEnd.Id),
            FootnoteReferenceInline footnote => new FootnoteReferenceInline(footnote.Id, footnote.Style?.Clone())
                { StyleId = footnote.StyleId },
            EndnoteReferenceInline endnote => new EndnoteReferenceInline(endnote.Id, endnote.Style?.Clone())
                { StyleId = endnote.StyleId },
            CommentRangeStartInline commentRangeStart => new CommentRangeStartInline(commentRangeStart.Id),
            CommentRangeEndInline commentRangeEnd => new CommentRangeEndInline(commentRangeEnd.Id),
            CommentReferenceInline commentRef => new CommentReferenceInline(commentRef.Id, commentRef.Style?.Clone())
                { StyleId = commentRef.StyleId },
            ContentControlStartInline contentStart => new ContentControlStartInline(contentStart.Properties.Clone()),
            ContentControlEndInline contentEnd => new ContentControlEndInline(contentEnd.Id),
            MetadataStartInline metaStart => new MetadataStartInline(CloneMetadataContainer(metaStart.Metadata)),
            MetadataEndInline metaEnd => new MetadataEndInline(CloneMetadataContainer(metaEnd.Metadata)),
            RevisionStartInline revisionStart => new RevisionStartInline(revisionStart.Revision.Clone()),
            RevisionEndInline revisionEnd => new RevisionEndInline(revisionEnd.Kind, revisionEnd.Id),
            RevisionRangeStartInline revisionRangeStart => new RevisionRangeStartInline(revisionRangeStart.Revision.Clone()),
            RevisionRangeEndInline revisionRangeEnd => new RevisionRangeEndInline(revisionRangeEnd.Kind, revisionRangeEnd.Id),
            TableInline => new TableInline(),
            _ => throw new NotSupportedException($"Unsupported inline type: {inline.GetType().Name}")
        };
        clone.Hyperlink = CloneHyperlink(inline.Hyperlink);
        clone.NodeId = inline.NodeId;
        return clone;
    }

    private static RunInline CloneRunInline(RunInline source)
    {
        var clone = new RunInline(CloneTextBuffer(source.Text), source.Style?.Clone())
        {
            StyleId = source.StyleId
        };
        return clone;
    }

    private static EquationInline CloneEquationInline(EquationInline source)
    {
        var clone = new EquationInline(CloneMathElement(source.Root))
        {
            Style = source.Style?.Clone(),
            StyleId = source.StyleId
        };
        return clone;
    }

    private static RubyInline CloneRubyInline(RubyInline source)
    {
        return new RubyInline(source.BaseText, source.RubyText)
        {
            BaseStyle = source.BaseStyle?.Clone(),
            BaseStyleId = source.BaseStyleId,
            RubyStyle = source.RubyStyle?.Clone(),
            RubyStyleId = source.RubyStyleId,
            RubyScale = source.RubyScale
        };
    }

    private static HyperlinkInfo? CloneHyperlink(HyperlinkInfo? source)
    {
        if (source is null)
        {
            return null;
        }

        return new HyperlinkInfo(source.Uri, source.Anchor, source.Tooltip);
    }

    private static ImageInline CloneImageInline(ImageInline source)
    {
        var clone = new ImageInline(CloneBytes(source.Data) ?? Array.Empty<byte>(), source.Width, source.Height, source.ContentType)
        {
            Rotation = source.Rotation,
            EmbeddedObject = CloneEmbeddedObject(source.EmbeddedObject),
            Diagram = CloneDiagram(source.Diagram),
            Effects = source.Effects?.Clone(),
            Crop = source.Crop?.Clone()
        };
        return clone;
    }

    private static ShapeInline CloneShapeInline(ShapeInline source)
    {
        return new ShapeInline(
            source.Width,
            source.Height,
            source.Properties.Clone(),
            CloneShapeTextBox(source.TextBox),
            source.Name);
    }

    private static ChartInline CloneChartInline(ChartInline source)
    {
        return new ChartInline(source.Width, source.Height, CloneChartModel(source.Model), CloneBytes(source.PartData))
        {
            Name = source.Name
        };
    }

    private static TextBuffer CloneTextBuffer(TextBuffer source)
    {
        return new TextBuffer(source.GetText());
    }

    private static MathElement CloneMathElement(MathElement source)
    {
        return source switch
        {
            MathRow row => CloneMathRow(row),
            MathRun run => new MathRun { Text = run.Text, Style = run.Style?.Clone() },
            MathFraction fraction => new MathFraction(CloneMathElement(fraction.Numerator), CloneMathElement(fraction.Denominator))
                { HasBar = fraction.HasBar },
            MathAccent accent => new MathAccent(CloneMathElement(accent.Base)) { AccentChar = accent.AccentChar },
            MathDelimiter delimiter => new MathDelimiter(CloneMathElement(delimiter.Body))
            {
                BeginChar = delimiter.BeginChar,
                EndChar = delimiter.EndChar,
                SeparatorChar = delimiter.SeparatorChar
            },
            MathFunction function => new MathFunction(CloneMathElement(function.Name), CloneMathElement(function.Argument)),
            MathBar bar => new MathBar(CloneMathElement(bar.Base))
            {
                Position = bar.Position
            },
            MathBoxElement box => new MathBoxElement(CloneMathElement(box.Base)),
            MathBorderBox border => new MathBorderBox(CloneMathElement(border.Base))
            {
                HideTop = border.HideTop,
                HideBottom = border.HideBottom,
                HideLeft = border.HideLeft,
                HideRight = border.HideRight,
                StrikeHorizontal = border.StrikeHorizontal,
                StrikeVertical = border.StrikeVertical,
                StrikeDiagonalUp = border.StrikeDiagonalUp,
                StrikeDiagonalDown = border.StrikeDiagonalDown
            },
            MathNary nary => new MathNary(CloneMathElement(nary.Base))
            {
                Subscript = nary.Subscript is null ? null : CloneMathElement(nary.Subscript),
                Superscript = nary.Superscript is null ? null : CloneMathElement(nary.Superscript),
                OperatorChar = nary.OperatorChar,
                HideSub = nary.HideSub,
                HideSup = nary.HideSup
            },
            MathMatrix matrix => CloneMathMatrix(matrix),
            MathLimit limit => new MathLimit(CloneMathElement(limit.Base), CloneMathElement(limit.Limit))
            {
                Position = limit.Position
            },
            MathScript script => new MathScript(CloneMathElement(script.Base))
            {
                Subscript = script.Subscript is null ? null : CloneMathElement(script.Subscript),
                Superscript = script.Superscript is null ? null : CloneMathElement(script.Superscript)
            },
            MathPreScript preScript => new MathPreScript(CloneMathElement(preScript.Base))
            {
                Subscript = preScript.Subscript is null ? null : CloneMathElement(preScript.Subscript),
                Superscript = preScript.Superscript is null ? null : CloneMathElement(preScript.Superscript)
            },
            MathRadical radical => new MathRadical(CloneMathElement(radical.Radicand))
            {
                Degree = radical.Degree is null ? null : CloneMathElement(radical.Degree)
            },
            MathGroupCharacter group => new MathGroupCharacter(CloneMathElement(group.Base))
            {
                Character = group.Character,
                Position = group.Position
            },
            MathPhantom phantom => new MathPhantom(CloneMathElement(phantom.Base))
            {
                Show = phantom.Show,
                ZeroWidth = phantom.ZeroWidth,
                ZeroAscent = phantom.ZeroAscent,
                ZeroDescent = phantom.ZeroDescent,
                Transparent = phantom.Transparent
            },
            _ => throw new NotSupportedException($"Unsupported math element type: {source.GetType().Name}")
        };
    }

    private static MathRow CloneMathRow(MathRow source)
    {
        var row = new MathRow();
        foreach (var element in source.Elements)
        {
            row.Elements.Add(CloneMathElement(element));
        }

        return row;
    }

    private static MathMatrix CloneMathMatrix(MathMatrix source)
    {
        var clone = new MathMatrix();
        foreach (var row in source.Rows)
        {
            var list = new List<MathElement>(row.Count);
            foreach (var element in row)
            {
                list.Add(CloneMathElement(element));
            }

            clone.Rows.Add(list);
        }

        return clone;
    }

    private static ShapeTextBox? CloneShapeTextBox(ShapeTextBox? source)
    {
        if (source is null)
        {
            return null;
        }

        var clone = new ShapeTextBox();
        clone.Properties.Padding = source.Properties.Padding;
        clone.Properties.VerticalAlignment = source.Properties.VerticalAlignment;
        clone.Properties.AutoFit = source.Properties.AutoFit;
        clone.Properties.HorizontalOverflow = source.Properties.HorizontalOverflow;
        clone.Properties.VerticalOverflow = source.Properties.VerticalOverflow;
        clone.Properties.TextDirection = source.Properties.TextDirection;
        CopyBlocks(source.Blocks, clone.Blocks);
        return clone;
    }

    private static EmbeddedObjectInfo? CloneEmbeddedObject(EmbeddedObjectInfo? source)
    {
        if (source is null)
        {
            return null;
        }

        return new EmbeddedObjectInfo
        {
            RelationshipId = source.RelationshipId,
            ContentType = source.ContentType,
            TargetUri = source.TargetUri,
            Data = CloneBytes(source.Data),
            ProgId = source.ProgId,
            ClassId = source.ClassId,
            ObjectId = source.ObjectId,
            IsLinked = source.IsLinked,
            UpdateMode = source.UpdateMode
        };
    }

    private static DiagramInfo? CloneDiagram(DiagramInfo? source)
    {
        if (source is null)
        {
            return null;
        }

        return new DiagramInfo
        {
            DataRelationshipId = source.DataRelationshipId,
            LayoutRelationshipId = source.LayoutRelationshipId,
            QuickStyleRelationshipId = source.QuickStyleRelationshipId,
            ColorStyleRelationshipId = source.ColorStyleRelationshipId,
            DataPart = CloneBytes(source.DataPart),
            LayoutPart = CloneBytes(source.LayoutPart),
            QuickStylePart = CloneBytes(source.QuickStylePart),
            ColorStylePart = CloneBytes(source.ColorStylePart)
        };
    }

    private static ChartModel? CloneChartModel(ChartModel? source)
    {
        if (source is null)
        {
            return null;
        }

        var clone = new ChartModel
        {
            Type = source.Type,
            Stacking = source.Stacking,
            BarDirection = source.BarDirection,
            RadarStyle = source.RadarStyle,
            DoughnutHoleSize = source.DoughnutHoleSize,
            Title = source.Title,
            TitleTextStyle = CloneChartTextStyle(source.TitleTextStyle),
            TitlePosition = source.TitlePosition,
            PaletteName = source.PaletteName,
            ChartAreaStyle = CloneChartStyle(source.ChartAreaStyle),
            PlotAreaStyle = CloneChartStyle(source.PlotAreaStyle),
            Legend = CloneChartLegend(source.Legend),
            DataLabels = CloneChartDataLabels(source.DataLabels)
        };

        foreach (var axis in source.Axes)
        {
            clone.Axes.Add(CloneChartAxis(axis));
        }

        foreach (var series in source.Series)
        {
            clone.Series.Add(CloneChartSeries(series));
        }

        foreach (var root in source.HierarchyRoots)
        {
            clone.HierarchyRoots.Add(CloneChartHierarchyNode(root));
        }

        return clone;
    }

    private static ChartSeries CloneChartSeries(ChartSeries source)
    {
        var clone = new ChartSeries
        {
            Name = source.Name,
            Style = CloneChartStyle(source.Style),
            DataLabels = CloneChartDataLabels(source.DataLabels),
            UseSmoothedLine = source.UseSmoothedLine
        };

        foreach (var point in source.Points)
        {
            clone.Points.Add(CloneChartPoint(point));
        }

        return clone;
    }

    private static ChartPoint CloneChartPoint(ChartPoint source)
    {
        return new ChartPoint
        {
            Category = source.Category,
            Value = source.Value,
            XValue = source.XValue,
            Size = source.Size,
            Style = CloneChartStyle(source.Style),
            DataLabel = CloneChartDataLabels(source.DataLabel)
        };
    }

    private static ChartHierarchyNode CloneChartHierarchyNode(ChartHierarchyNode source)
    {
        var clone = new ChartHierarchyNode
        {
            Key = source.Key,
            Label = source.Label,
            Value = source.Value,
            Style = CloneChartStyle(source.Style),
            DataLabel = CloneChartDataLabels(source.DataLabel)
        };

        foreach (var child in source.Children)
        {
            clone.Children.Add(CloneChartHierarchyNode(child));
        }

        return clone;
    }

    private static ChartAxis CloneChartAxis(ChartAxis source)
    {
        return new ChartAxis
        {
            AxisId = source.AxisId,
            CrossAxisId = source.CrossAxisId,
            Kind = source.Kind,
            Position = source.Position,
            IsVisible = source.IsVisible,
            Minimum = source.Minimum,
            Maximum = source.Maximum,
            MajorUnit = source.MajorUnit,
            MinorUnit = source.MinorUnit,
            MajorTickMark = source.MajorTickMark,
            MinorTickMark = source.MinorTickMark,
            TickLabelPosition = source.TickLabelPosition,
            NumberFormat = source.NumberFormat,
            Title = source.Title,
            LineStyle = CloneChartLine(source.LineStyle),
            MajorGridlineStyle = CloneChartLine(source.MajorGridlineStyle),
            MinorGridlineStyle = CloneChartLine(source.MinorGridlineStyle),
            LabelTextStyle = CloneChartTextStyle(source.LabelTextStyle),
            TitleTextStyle = CloneChartTextStyle(source.TitleTextStyle)
        };
    }

    private static ChartLegend? CloneChartLegend(ChartLegend? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartLegend
        {
            IsVisible = source.IsVisible,
            Position = source.Position,
            Overlay = source.Overlay,
            TextStyle = CloneChartTextStyle(source.TextStyle)
        };
    }

    private static ChartDataLabelSettings? CloneChartDataLabels(ChartDataLabelSettings? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartDataLabelSettings
        {
            IsHidden = source.IsHidden,
            ShowValue = source.ShowValue,
            ShowCategoryName = source.ShowCategoryName,
            ShowSeriesName = source.ShowSeriesName,
            ShowPercent = source.ShowPercent,
            ShowBubbleSize = source.ShowBubbleSize,
            ShowLegendKey = source.ShowLegendKey,
            ShowLeaderLines = source.ShowLeaderLines,
            Position = source.Position,
            NumberFormat = source.NumberFormat,
            TextStyle = CloneChartTextStyle(source.TextStyle),
            ShapeStyle = CloneChartStyle(source.ShapeStyle)
        };
    }

    private static ChartTextStyle? CloneChartTextStyle(ChartTextStyle? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartTextStyle
        {
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            Color = source.Color,
            Bold = source.Bold,
            Italic = source.Italic
        };
    }

    private static ChartStyle? CloneChartStyle(ChartStyle? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartStyle
        {
            Fill = CloneChartFill(source.Fill),
            Line = CloneChartLine(source.Line),
            Effects = CloneChartEffects(source.Effects)
        };
    }

    private static ChartFillStyle? CloneChartFill(ChartFillStyle? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartFillStyle
        {
            IsNone = source.IsNone,
            Color = source.Color
        };
    }

    private static ChartLineStyle? CloneChartLine(ChartLineStyle? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartLineStyle
        {
            IsNone = source.IsNone,
            Color = source.Color,
            Width = source.Width,
            Style = source.Style
        };
    }

    private static ChartEffectStyle? CloneChartEffects(ChartEffectStyle? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartEffectStyle
        {
            Shadow = CloneChartShadow(source.Shadow)
        };
    }

    private static ChartShadowEffect? CloneChartShadow(ChartShadowEffect? source)
    {
        if (source is null)
        {
            return null;
        }

        return new ChartShadowEffect
        {
            BlurRadius = source.BlurRadius,
            Distance = source.Distance,
            Direction = source.Direction,
            Color = source.Color
        };
    }

    public static MetadataContainer CloneMetadataContainer(MetadataContainer source)
    {
        var element = CloneMetadataElement(source.Element);
        var clone = new MetadataContainer(element);
        foreach (var propertyElement in source.PropertyElements)
        {
            clone.PropertyElements.Add(CloneMetadataElement(propertyElement));
        }

        return clone;
    }

    private static MetadataElement CloneMetadataElement(MetadataElement source)
    {
        var clone = new MetadataElement(source.Prefix, source.LocalName, source.NamespaceUri)
        {
            Text = source.Text
        };

        clone.Attributes.AddRange(source.Attributes);
        foreach (var child in source.Children)
        {
            clone.Children.Add(CloneMetadataElement(child));
        }

        return clone;
    }

    public static FloatingObject CloneFloatingObject(FloatingObject source)
    {
        var clone = new FloatingObject(CloneInline(source.Content));
        CopyFloatingAnchor(source.Anchor, clone.Anchor);
        return clone;
    }

    private static void CopyFloatingAnchor(FloatingAnchor source, FloatingAnchor target)
    {
        target.HorizontalReference = source.HorizontalReference;
        target.VerticalReference = source.VerticalReference;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;
        target.OffsetX = source.OffsetX;
        target.OffsetY = source.OffsetY;
        target.WrapStyle = source.WrapStyle;
        target.WrapSide = source.WrapSide;
        target.WrapPolygon = CloneWrapPolygon(source.WrapPolygon);
        target.BehindText = source.BehindText;
        target.AllowOverlap = source.AllowOverlap;
        target.ZOrder = source.ZOrder;
        target.Distance = source.Distance;
        target.AnchorOffset = source.AnchorOffset;
    }

    private static FloatingWrapPolygon? CloneWrapPolygon(FloatingWrapPolygon? source)
    {
        if (source is null)
        {
            return null;
        }

        var points = source.Points.ToArray();
        return new FloatingWrapPolygon(points);
    }

    public static FootnoteDefinition CloneFootnoteDefinition(FootnoteDefinition source)
    {
        var clone = new FootnoteDefinition(source.Id);
        CopyBlocks(source.Blocks, clone.Blocks);
        return clone;
    }

    public static EndnoteDefinition CloneEndnoteDefinition(EndnoteDefinition source)
    {
        var clone = new EndnoteDefinition(source.Id);
        CopyBlocks(source.Blocks, clone.Blocks);
        return clone;
    }

    public static CommentDefinition CloneCommentDefinition(CommentDefinition source)
    {
        var clone = new CommentDefinition(source.Id)
        {
            Author = source.Author,
            Initials = source.Initials,
            Date = source.Date,
            ParentId = source.ParentId,
            ThreadId = source.ThreadId,
            IsResolved = source.IsResolved,
            ResolvedBy = source.ResolvedBy,
            ResolvedDate = source.ResolvedDate
        };

        CopyBlocks(source.Blocks, clone.Blocks);
        return clone;
    }

    private static void CopyNotes<T>(Dictionary<int, T> source, Dictionary<int, T> target, Func<T, T> clone)
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = clone(pair.Value);
        }
    }

    private static void CopyNoteSeparators(NoteSeparatorDefinition source, NoteSeparatorDefinition target)
    {
        target.SeparatorBlocks.Clear();
        target.ContinuationSeparatorBlocks.Clear();
        CopyBlocks(source.SeparatorBlocks, target.SeparatorBlocks);
        CopyBlocks(source.ContinuationSeparatorBlocks, target.ContinuationSeparatorBlocks);
    }

    private static void CopyListDefinitions(Dictionary<int, ListDefinition> source, Dictionary<int, ListDefinition> target)
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value.Clone();
        }
    }

    private static void CopyDocumentFonts(DocumentFonts source, DocumentFonts target)
    {
        target.FontTable.Clear();
        foreach (var pair in source.FontTable)
        {
            target.FontTable[pair.Key] = CloneFontDefinition(pair.Value);
        }

        target.Theme.Clear();
        foreach (var pair in source.Theme.Entries)
        {
            target.Theme.Set(pair.Key, pair.Value);
        }
    }

    private static DocumentFontDefinition CloneFontDefinition(DocumentFontDefinition source)
    {
        var clone = new DocumentFontDefinition(source.Name)
        {
            AltName = source.AltName,
            Charset = source.Charset,
            Family = source.Family,
            Pitch = source.Pitch,
            Panose1 = source.Panose1,
            Regular = CloneEmbeddedFont(source.Regular),
            Bold = CloneEmbeddedFont(source.Bold),
            Italic = CloneEmbeddedFont(source.Italic),
            BoldItalic = CloneEmbeddedFont(source.BoldItalic)
        };

        return clone;
    }

    private static EmbeddedFontData? CloneEmbeddedFont(EmbeddedFontData? source)
    {
        if (source is null)
        {
            return null;
        }

        return new EmbeddedFontData(CloneBytes(source.Data) ?? Array.Empty<byte>(), source.ContentType, source.FontKey);
    }

    private static void CopyDocumentStyles(DocumentStyles source, DocumentStyles target)
    {
        target.ParagraphStyles.Clear();
        foreach (var pair in source.ParagraphStyles)
        {
            target.ParagraphStyles[pair.Key] = CloneParagraphStyleDefinition(pair.Value);
        }

        target.CharacterStyles.Clear();
        foreach (var pair in source.CharacterStyles)
        {
            target.CharacterStyles[pair.Key] = CloneCharacterStyleDefinition(pair.Value);
        }

        target.TableStyles.Clear();
        foreach (var pair in source.TableStyles)
        {
            target.TableStyles[pair.Key] = CloneTableStyleDefinition(pair.Value);
        }

        target.DefaultParagraphStyleId = source.DefaultParagraphStyleId;
        target.DefaultCharacterStyleId = source.DefaultCharacterStyleId;
        target.DefaultTableStyleId = source.DefaultTableStyleId;
    }

    private static ParagraphStyleDefinition CloneParagraphStyleDefinition(ParagraphStyleDefinition source)
    {
        var clone = new ParagraphStyleDefinition(source.Id)
        {
            Name = source.Name,
            BasedOnId = source.BasedOnId,
            NextStyleId = source.NextStyleId,
            LinkedStyleId = source.LinkedStyleId,
            ListId = source.ListId,
            ListLevel = source.ListLevel,
            UiPriority = source.UiPriority,
            QuickStyle = source.QuickStyle,
            SemiHidden = source.SemiHidden,
            UnhideWhenUsed = source.UnhideWhenUsed,
            AutoRedefine = source.AutoRedefine,
            Hidden = source.Hidden,
            Locked = source.Locked,
            PrimaryStyle = source.PrimaryStyle,
            CustomStyle = source.CustomStyle
        };

        CopyParagraphStyleProperties(source.ParagraphProperties, clone.ParagraphProperties);
        CopyTextStyleProperties(source.RunProperties, clone.RunProperties);
        return clone;
    }

    private static CharacterStyleDefinition CloneCharacterStyleDefinition(CharacterStyleDefinition source)
    {
        var clone = new CharacterStyleDefinition(source.Id)
        {
            Name = source.Name,
            BasedOnId = source.BasedOnId,
            NextStyleId = source.NextStyleId,
            LinkedStyleId = source.LinkedStyleId,
            UiPriority = source.UiPriority,
            QuickStyle = source.QuickStyle,
            SemiHidden = source.SemiHidden,
            UnhideWhenUsed = source.UnhideWhenUsed,
            AutoRedefine = source.AutoRedefine,
            Hidden = source.Hidden,
            Locked = source.Locked,
            PrimaryStyle = source.PrimaryStyle,
            CustomStyle = source.CustomStyle
        };

        CopyTextStyleProperties(source.RunProperties, clone.RunProperties);
        return clone;
    }

    private static TableStyleDefinition CloneTableStyleDefinition(TableStyleDefinition source)
    {
        var clone = new TableStyleDefinition(source.Id)
        {
            Name = source.Name,
            BasedOnId = source.BasedOnId,
            NextStyleId = source.NextStyleId,
            LinkedStyleId = source.LinkedStyleId,
            UiPriority = source.UiPriority,
            QuickStyle = source.QuickStyle,
            SemiHidden = source.SemiHidden,
            UnhideWhenUsed = source.UnhideWhenUsed,
            AutoRedefine = source.AutoRedefine,
            Hidden = source.Hidden,
            Locked = source.Locked,
            PrimaryStyle = source.PrimaryStyle,
            CustomStyle = source.CustomStyle
        };

        CopyTableProperties(source.TableProperties, clone.TableProperties);
        CopyTableCellProperties(source.CellProperties, clone.CellProperties);
        foreach (var pair in source.Conditions)
        {
            clone.Conditions[pair.Key] = CloneTableStyleCondition(pair.Value);
        }

        return clone;
    }

    private static TableStyleConditionProperties CloneTableStyleCondition(TableStyleConditionProperties source)
    {
        var clone = new TableStyleConditionProperties();
        CopyTableProperties(source.TableProperties, clone.TableProperties);
        CopyTableCellProperties(source.CellProperties, clone.CellProperties);
        return clone;
    }

    private static void CopyThemeColors(DocumentThemeColorMap source, DocumentThemeColorMap target)
    {
        target.Clear();
        foreach (var pair in source.Overrides)
        {
            target.Set(pair.Key, pair.Value);
        }
    }

    private static void CopyRevisions(DocumentRevisions source, DocumentRevisions target)
    {
        target.Clear();
        foreach (var revision in source.Timeline)
        {
            target.AddOrUpdate(revision.Clone());
        }
    }

    private static void CopyTextStyle(TextStyle source, TextStyle target)
    {
        target.FontFamily = source.FontFamily;
        target.FontFamilyAscii = source.FontFamilyAscii;
        target.FontFamilyHighAnsi = source.FontFamilyHighAnsi;
        target.FontFamilyEastAsia = source.FontFamilyEastAsia;
        target.FontFamilyComplexScript = source.FontFamilyComplexScript;
        target.FontSize = source.FontSize;
        target.FontSizeComplexScript = source.FontSizeComplexScript;
        target.FontWeight = source.FontWeight;
        target.FontStyle = source.FontStyle;
        target.Color = source.Color;
        target.ThemeColor = source.ThemeColor;
        target.ThemeTint = source.ThemeTint;
        target.ThemeShade = source.ThemeShade;
        target.VerticalPosition = source.VerticalPosition;
        target.BaselineOffset = source.BaselineOffset;
        target.LetterSpacing = source.LetterSpacing;
        target.HorizontalScale = source.HorizontalScale;
        target.Kerning = source.Kerning;
        target.Caps = source.Caps;
        target.SmallCaps = source.SmallCaps;
        target.Underline = source.Underline;
        target.UnderlineStyle = source.UnderlineStyle;
        target.UnderlineColor = source.UnderlineColor;
        target.UnderlineThemeColor = source.UnderlineThemeColor;
        target.UnderlineThemeTint = source.UnderlineThemeTint;
        target.UnderlineThemeShade = source.UnderlineThemeShade;
        target.Strikethrough = source.Strikethrough;
        target.HighlightColor = source.HighlightColor;
        target.Hidden = source.Hidden;
        target.ThemeFontAscii = source.ThemeFontAscii;
        target.ThemeFontHighAnsi = source.ThemeFontHighAnsi;
        target.ThemeFontEastAsia = source.ThemeFontEastAsia;
        target.ThemeFontComplexScript = source.ThemeFontComplexScript;
        target.Language = source.Language;
        target.LanguageEastAsia = source.LanguageEastAsia;
        target.LanguageBidi = source.LanguageBidi;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.OpenTypeFeatures = source.OpenTypeFeatures?.Clone();
        target.Effects = source.Effects?.Clone();
    }

    private static void CopyTextStyleProperties(TextStyleProperties source, TextStyleProperties target)
    {
        target.FontFamily = source.FontFamily;
        target.FontFamilyAscii = source.FontFamilyAscii;
        target.FontFamilyHighAnsi = source.FontFamilyHighAnsi;
        target.FontFamilyEastAsia = source.FontFamilyEastAsia;
        target.FontFamilyComplexScript = source.FontFamilyComplexScript;
        target.FontSize = source.FontSize;
        target.FontSizeComplexScript = source.FontSizeComplexScript;
        target.FontWeight = source.FontWeight;
        target.FontStyle = source.FontStyle;
        target.Color = source.Color;
        target.ThemeColor = source.ThemeColor;
        target.ThemeTint = source.ThemeTint;
        target.ThemeShade = source.ThemeShade;
        target.VerticalPosition = source.VerticalPosition;
        target.BaselineOffset = source.BaselineOffset;
        target.LetterSpacing = source.LetterSpacing;
        target.HorizontalScale = source.HorizontalScale;
        target.Kerning = source.Kerning;
        target.Caps = source.Caps;
        target.SmallCaps = source.SmallCaps;
        target.Underline = source.Underline;
        target.UnderlineStyle = source.UnderlineStyle;
        target.UnderlineColor = source.UnderlineColor;
        target.UnderlineThemeColor = source.UnderlineThemeColor;
        target.UnderlineThemeTint = source.UnderlineThemeTint;
        target.UnderlineThemeShade = source.UnderlineThemeShade;
        target.Strikethrough = source.Strikethrough;
        target.HighlightColor = source.HighlightColor;
        target.Hidden = source.Hidden;
        target.ThemeFontAscii = source.ThemeFontAscii;
        target.ThemeFontHighAnsi = source.ThemeFontHighAnsi;
        target.ThemeFontEastAsia = source.ThemeFontEastAsia;
        target.ThemeFontComplexScript = source.ThemeFontComplexScript;
        target.Language = source.Language;
        target.LanguageEastAsia = source.LanguageEastAsia;
        target.LanguageBidi = source.LanguageBidi;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.OpenTypeFeatures = source.OpenTypeFeatures?.Clone();
        target.Effects = source.Effects?.Clone();
    }

    private static void CopyParagraphProperties(ParagraphProperties source, ParagraphProperties target)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.SpacingBeforeLines = source.SpacingBeforeLines;
        target.SpacingAfterLines = source.SpacingAfterLines;
        target.AutoSpacingBefore = source.AutoSpacingBefore;
        target.AutoSpacingAfter = source.AutoSpacingAfter;
        target.LineSpacing = source.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule;
        target.IndentLeft = source.IndentLeft;
        target.IndentRight = source.IndentRight;
        target.FirstLineIndent = source.FirstLineIndent;
        target.KeepWithNext = source.KeepWithNext;
        target.KeepLinesTogether = source.KeepLinesTogether;
        target.WidowControl = source.WidowControl;
        target.PageBreakBefore = source.PageBreakBefore;
        target.ContextualSpacing = source.ContextualSpacing;
        target.Bidi = source.Bidi;
        target.TextDirection = source.TextDirection;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.ShadingColor = source.ShadingColor;
        target.SuppressLineNumbers = source.SuppressLineNumbers;
        target.DropCap = source.DropCap?.Clone();
        target.Frame = source.Frame?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.TabStops.Clear();
        foreach (var tabStop in source.TabStops)
        {
            target.TabStops.Add(tabStop.Clone());
        }
    }

    private static void CopyParagraphStyleProperties(ParagraphStyleProperties source, ParagraphStyleProperties target)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.SpacingBeforeLines = source.SpacingBeforeLines;
        target.SpacingAfterLines = source.SpacingAfterLines;
        target.AutoSpacingBefore = source.AutoSpacingBefore;
        target.AutoSpacingAfter = source.AutoSpacingAfter;
        target.LineSpacing = source.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule;
        target.IndentLeft = source.IndentLeft;
        target.IndentRight = source.IndentRight;
        target.FirstLineIndent = source.FirstLineIndent;
        target.KeepWithNext = source.KeepWithNext;
        target.KeepLinesTogether = source.KeepLinesTogether;
        target.WidowControl = source.WidowControl;
        target.PageBreakBefore = source.PageBreakBefore;
        target.ContextualSpacing = source.ContextualSpacing;
        target.Bidi = source.Bidi;
        target.TextDirection = source.TextDirection;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.ShadingColor = source.ShadingColor;
        target.SuppressLineNumbers = source.SuppressLineNumbers;
        target.DropCap = source.DropCap?.Clone();
        target.Frame = source.Frame?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.TabStops.Clear();
        foreach (var tabStop in source.TabStops)
        {
            target.TabStops.Add(tabStop.Clone());
        }
    }

    private static void CopySectionProperties(SectionProperties source, SectionProperties target)
    {
        target.PageWidth = source.PageWidth;
        target.PageHeight = source.PageHeight;
        target.Orientation = source.Orientation;
        target.MarginLeft = source.MarginLeft;
        target.MarginTop = source.MarginTop;
        target.MarginRight = source.MarginRight;
        target.MarginBottom = source.MarginBottom;
        target.HeaderOffset = source.HeaderOffset;
        target.FooterOffset = source.FooterOffset;
        target.Gutter = source.Gutter;
        target.DifferentFirstPageHeaderFooter = source.DifferentFirstPageHeaderFooter;
        target.ColumnCount = source.ColumnCount;
        target.ColumnGap = source.ColumnGap;
        target.ColumnEqualWidth = source.ColumnEqualWidth;
        target.ColumnSeparator = source.ColumnSeparator;
        target.ColumnWidths.Clear();
        target.ColumnWidths.AddRange(source.ColumnWidths);
        target.ColumnGaps.Clear();
        target.ColumnGaps.AddRange(source.ColumnGaps);
        target.DocGrid = source.DocGrid?.Clone();
        target.PageBackgroundColor = source.PageBackgroundColor;
        target.PageBorders = source.PageBorders?.Clone();
        target.LineNumbering = source.LineNumbering?.Clone();
        target.PageNumbering = source.PageNumbering?.Clone();
        target.Footnotes = source.Footnotes?.Clone();
        target.Endnotes = source.Endnotes?.Clone();
    }

    private static void CopyTableProperties(TableProperties source, TableProperties target)
    {
        target.ColumnWidths.Clear();
        target.ColumnWidths.AddRange(source.ColumnWidths);
        target.Width = source.Width;
        target.WidthUnit = source.WidthUnit;
        target.Indent = source.Indent;
        target.IndentUnit = source.IndentUnit;
        target.Alignment = source.Alignment;
        target.LayoutMode = source.LayoutMode;
        target.CellSpacing = source.CellSpacing;
        target.CellSpacingUnit = source.CellSpacingUnit;
        target.CellPadding = source.CellPadding;
        target.ShadingColor = source.ShadingColor;
        target.Look = source.Look?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.Borders.InsideHorizontal = source.Borders.InsideHorizontal?.Clone();
        target.Borders.InsideVertical = source.Borders.InsideVertical?.Clone();
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
        target.PreferredWidth = source.PreferredWidth;
        target.PreferredWidthUnit = source.PreferredWidthUnit;
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private static byte[]? CloneBytes(byte[]? source)
    {
        if (source is null)
        {
            return null;
        }

        var clone = new byte[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }
}
