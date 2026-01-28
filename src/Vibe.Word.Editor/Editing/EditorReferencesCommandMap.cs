using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorReferencesCommandMap
{
    private const string TocTagPrefix = "TOC";
    private const string TocTitleText = "Table of Contents";
    private const int ReferenceSingleLineSpacingTwips = 240;
    private const string TofTagPrefix = "TOF";
    private const string TofTitleText = "Table of Figures";
    private const string IndexTagPrefix = "INDEX";
    private const string IndexTitleText = "Index";
    private const string AuthoritiesTagPrefix = "TOA";
    private const string AuthoritiesTitleText = "Table of Authorities";
    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private readonly EditorServices _services;
    private int _contentControlCounter;
    private int _footnoteCounter;
    private int _endnoteCounter;
    private int _citationCounter;
    private readonly Dictionary<string, int> _captionCounters = new(StringComparer.OrdinalIgnoreCase);

    public EditorReferencesCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _contentControlCounter = FindNextContentControlId(session.Document);
        _footnoteCounter = FindNextId(session.Document.Footnotes.Keys);
        _endnoteCounter = FindNextId(session.Document.Endnotes.Keys);
        _citationCounter = 1;
    }

    public void Register()
    {
        _router.RegisterAction(EditorReferencesCommandIds.TableOfContents.Insert, (_, payload) => InsertTableOfContents(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.TableOfContents.Update, (_, payload) => UpdateTableOfContents(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.TableOfContents.AddText, (_, payload) => ApplyTocTextLevel(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Notes.InsertFootnote, (_, __) => InsertFootnote(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Notes.InsertEndnote, (_, __) => InsertEndnote(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Notes.NextFootnote, (_, __) => NavigateFootnote(1), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Notes.ShowNotes, (_, __) => NavigateFootnote(0), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Captions.InsertCaption, (_, payload) => InsertCaption(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Citations.InsertCitation, (_, payload) => InsertCitation(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Citations.Bibliography, (_, __) => InsertBibliography(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Citations.ManageSources, (_, __) => ManageSources(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Citations.Style, (_, payload) => SetCitationStyle(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.TableOfFigures.Insert, (_, payload) => InsertTableOfFigures(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.TableOfFigures.Update, (_, payload) => UpdateTableOfFigures(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Index.MarkEntry, (_, payload) => MarkIndexEntry(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Index.InsertIndex, (_, payload) => InsertIndex(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.TableOfAuthorities.MarkCitation, (_, payload) => MarkAuthorityEntry(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.TableOfAuthorities.InsertTable, (_, payload) => InsertTableOfAuthorities(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorReferencesCommandIds.Fields.UpdateCurrent, (_, __) => UpdateFields(FieldUpdateScope.Current, false), (context, _) => HasFields(context));
        _router.RegisterAction(EditorReferencesCommandIds.Fields.UpdateAll, (_, __) => UpdateFields(FieldUpdateScope.Document, false), (context, _) => HasFields(context));
        _router.RegisterAction(EditorReferencesCommandIds.Fields.UpdatePageNumbers, (_, __) => UpdateFields(FieldUpdateScope.Document, true), (context, _) => HasFields(context));
        _router.RegisterAction(EditorReferencesCommandIds.Fields.Lock, (_, __) => SetFieldLocks(true), (context, _) => HasFields(context));
        _router.RegisterAction(EditorReferencesCommandIds.Fields.Unlock, (_, __) => SetFieldLocks(false), (context, _) => HasFields(context));
    }

    private bool HasParagraphs(RibbonContextSnapshot? context)
    {
        if (context.HasValue && context.Value.Selection.Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

        return _session.Document.ParagraphCount > 0;
    }

    private bool HasFields(RibbonContextSnapshot? context)
    {
        if (!HasParagraphs(context))
        {
            return false;
        }

        foreach (var (paragraph, _) in EnumerateBodyParagraphs())
        {
            if (HasFields(paragraph))
            {
                return true;
            }
        }

        return false;
    }

    private void InsertTableOfContents(object? payload)
    {
        var request = NormalizeTocRequest(payload);
        var contentWidth = ResolveTocContentWidth(_session.Caret);
        var paragraphs = BuildTocParagraphs(request, _session.Layout, contentWidth);
        var properties = new ContentControlProperties
        {
            Id = _contentControlCounter++,
            Kind = ContentControlKind.Block,
            Tag = BuildTocTag(request),
            Alias = TocTitleText
        };

        var blocks = new List<Block>(paragraphs.Count + 2)
        {
            new ContentControlStartBlock(properties)
        };
        blocks.AddRange(paragraphs);
        blocks.Add(new ContentControlEndBlock(properties.Id));

        InsertBlocksAtCaret(blocks, refreshLayout: false);
        _session.RefreshLayout();
        UpdateTableOfContentsById(properties.Id, request);
    }

    private void UpdateTableOfContents(object? payload)
    {
        var request = NormalizeTocRequest(payload);
        UpdateTableOfContentsInternal(request);
    }

    private void InsertFootnote()
    {
        var id = _footnoteCounter++;
        var definition = new FootnoteDefinition(id);
        definition.Blocks.Add(new ParagraphBlock("Footnote text"));
        _session.Document.Footnotes[id] = definition;
        _session.InsertInline(new FootnoteReferenceInline(id));
    }

    private void InsertEndnote()
    {
        var id = _endnoteCounter++;
        var definition = new EndnoteDefinition(id);
        definition.Blocks.Add(new ParagraphBlock("Endnote text"));
        _session.Document.Endnotes[id] = definition;
        _session.InsertInline(new EndnoteReferenceInline(id));
    }

    private void InsertCaption(object? payload)
    {
        var request = payload is EditorCaptionInsertRequest provided ? provided : default;
        var label = string.IsNullOrWhiteSpace(request.Label) ? "Figure" : request.Label.Trim();
        var title = string.IsNullOrWhiteSpace(request.Title) ? "Caption" : request.Title.Trim();
        var index = GetNextCaptionIndex(label);
        var instruction = $"SEQ {label}";

        var inlines = new Inline[]
        {
            new RunInline($"{label} "),
            new FieldStartInline(instruction)
            {
                Definition = FieldInstructionParser.Parse(instruction),
                IsDirty = true
            },
            new FieldSeparatorInline(),
            new RunInline(index.ToString(CultureInfo.InvariantCulture)),
            new FieldEndInline(),
            new RunInline(" - "),
            new RunInline(title)
        };

        _session.InsertInlines(inlines);
    }

    private void ApplyTocTextLevel(object? payload)
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return;
        }

        var level = payload is int provided ? provided : 1;
        level = Math.Clamp(level, 1, 9);
        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var styleId = $"Heading {level}";
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            paragraph.StyleId = styleId;
        }

        _session.RefreshLayout();
    }

    private void NavigateFootnote(int direction)
    {
        var anchors = CollectNoteAnchors(NoteKind.Footnote);
        if (anchors.Count == 0)
        {
            return;
        }

        anchors.Sort(CompareAnchors);
        var caret = _session.Caret;
        var nextIndex = FindAnchorIndex(anchors, caret, direction);
        if (nextIndex < 0)
        {
            return;
        }

        var anchor = anchors[nextIndex];
        _session.SetSelection(new TextRange(anchor.Position, anchor.Position));
    }

    private void InsertCitation(object? payload)
    {
        if (payload is string key && !string.IsNullOrWhiteSpace(key))
        {
            InsertCitationWithKey(key.Trim());
            return;
        }

        if (TryGetCitationSourceManager(out var manager))
        {
            _ = InsertCitationWithPickerAsync(manager, payload);
            return;
        }

        var fallback = ResolveEntryText(payload, "Citation", _citationCounter++);
        InsertCitationWithKey(fallback);
    }

    private void InsertBibliography()
    {
        var instruction = "BIBLIOGRAPHY";
        var startInline = new FieldStartInline(instruction)
        {
            Definition = FieldInstructionParser.Parse(instruction),
            IsDirty = true
        };

        var display = BuildBibliographyDisplay();
        var inlines = new Inline[]
        {
            startInline,
            new FieldSeparatorInline(),
            new RunInline(display),
            new FieldEndInline()
        };

        _session.InsertInlines(inlines);
    }

    private void ManageSources()
    {
        if (!TryGetCitationSourceManager(out var manager))
        {
            return;
        }

        _ = ManageSourcesAsync(manager);
    }

    private void InsertCitationWithKey(string key)
    {
        var instruction = $"CITATION \"{EscapeFieldText(key)}\"";
        var startInline = new FieldStartInline(instruction)
        {
            Definition = FieldInstructionParser.Parse(instruction),
            IsDirty = true
        };

        var display = BuildCitationDisplay(key);
        var inlines = new Inline[]
        {
            startInline,
            new FieldSeparatorInline(),
            new RunInline(display),
            new FieldEndInline()
        };

        _session.InsertInlines(inlines);
    }

    private async Task InsertCitationWithPickerAsync(ICitationSourceManager manager, object? payload)
    {
        try
        {
            var selected = await manager.PickSourceAsync(_session.Document.CitationSources);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                InsertCitationWithKey(selected.Trim());
                return;
            }

            var fallback = ResolveEntryText(payload, "Citation", _citationCounter++);
            InsertCitationWithKey(fallback);
        }
        catch (Exception)
        {
            // Swallow dialog exceptions to avoid unobserved task failures.
            var fallback = ResolveEntryText(payload, "Citation", _citationCounter++);
            InsertCitationWithKey(fallback);
        }
    }

    private async Task ManageSourcesAsync(ICitationSourceManager manager)
    {
        try
        {
            var updated = await manager.EditSourcesAsync(_session.Document.CitationSources.Clone());
            if (updated is null)
            {
                return;
            }

            var target = _session.Document.CitationSources;
            target.Sources.Clear();
            foreach (var source in updated.Sources)
            {
                target.Sources.Add(source.Clone());
            }

            target.EnsureUniqueTags();
            UpdateFields(FieldUpdateScope.Document, pageNumbersOnly: false);
        }
        catch (Exception)
        {
            // Swallow dialog exceptions to avoid unobserved task failures.
        }
    }

    private bool TryGetCitationSourceManager(out ICitationSourceManager manager)
    {
        return _services.TryGet(out manager);
    }

    private string BuildCitationDisplay(string key)
    {
        var source = _session.Document.CitationSources.FindByTag(key);
        if (source is null)
        {
            return key;
        }

        var author = source.GetField("Author");
        var year = source.GetField("Year");
        var title = source.GetField("Title");
        if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(year))
        {
            return string.Format(CultureInfo.CurrentCulture, "{0}, {1}", author, year);
        }

        if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(title))
        {
            return string.Format(CultureInfo.CurrentCulture, "{0}, {1}", author, title);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return key;
    }

    private string BuildBibliographyDisplay()
    {
        var sources = _session.Document.CitationSources.Sources;
        if (sources.Count == 0)
        {
            return "Bibliography";
        }

        var entries = new List<string>(sources.Count);
        foreach (var source in sources)
        {
            var author = source.GetField("Author");
            var year = source.GetField("Year");
            var title = source.GetField("Title");
            if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(year))
            {
                entries.Add(string.Format(CultureInfo.CurrentCulture, "{0} ({1})", author, year));
            }
            else if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(title))
            {
                entries.Add(string.Format(CultureInfo.CurrentCulture, "{0}. {1}.", author, title));
            }
            else if (!string.IsNullOrWhiteSpace(title))
            {
                entries.Add(title);
            }
            else if (!string.IsNullOrWhiteSpace(source.Tag))
            {
                entries.Add(source.Tag);
            }
        }

        return entries.Count == 0 ? "Bibliography" : string.Join("; ", entries);
    }

    private void SetCitationStyle(object? payload)
    {
        if (payload is not string style || string.IsNullOrWhiteSpace(style))
        {
            return;
        }

        _session.Document.CitationStyle = style.Trim();
    }

    private void InsertTableOfFigures(object? payload)
    {
        var label = payload as string;
        label = string.IsNullOrWhiteSpace(label) ? "Figure" : label.Trim();
        var contentWidth = ResolveTocContentWidth(_session.Caret);
        var paragraphs = BuildTableOfFiguresParagraphs(label, _session.Layout, contentWidth);
        var properties = new ContentControlProperties
        {
            Id = _contentControlCounter++,
            Kind = ContentControlKind.Block,
            Tag = BuildTofTag(label),
            Alias = TofTitleText
        };

        var blocks = new List<Block>(paragraphs.Count + 2)
        {
            new ContentControlStartBlock(properties)
        };
        blocks.AddRange(paragraphs);
        blocks.Add(new ContentControlEndBlock(properties.Id));

        InsertBlocksAtCaret(blocks, refreshLayout: false);
        _session.RefreshLayout();
        UpdateTableOfFiguresById(properties.Id, label);
    }

    private void UpdateTableOfFigures(object? payload)
    {
        var label = payload as string;
        label = string.IsNullOrWhiteSpace(label) ? "Figure" : label.Trim();
        UpdateTableOfFiguresInternal(label);
    }

    private void MarkIndexEntry(object? payload)
    {
        var entry = ResolveEntryText(payload, "Index Entry", 0);
        var instruction = $"XE \"{EscapeFieldText(entry)}\"";
        var startInline = new FieldStartInline(instruction)
        {
            Definition = FieldInstructionParser.Parse(instruction),
            IsDirty = true
        };

        var inlines = new Inline[]
        {
            startInline,
            new FieldEndInline()
        };

        _session.InsertInlines(inlines);
    }

    private void InsertIndex(object? payload)
    {
        var contentWidth = ResolveTocContentWidth(_session.Caret);
        var paragraphs = BuildIndexParagraphs(_session.Layout, contentWidth);
        var properties = new ContentControlProperties
        {
            Id = _contentControlCounter++,
            Kind = ContentControlKind.Block,
            Tag = IndexTagPrefix,
            Alias = IndexTitleText
        };

        var blocks = new List<Block>(paragraphs.Count + 2)
        {
            new ContentControlStartBlock(properties)
        };
        blocks.AddRange(paragraphs);
        blocks.Add(new ContentControlEndBlock(properties.Id));

        InsertBlocksAtCaret(blocks, refreshLayout: false);
        _session.RefreshLayout();
    }

    private void MarkAuthorityEntry(object? payload)
    {
        var entry = ResolveEntryText(payload, "Authority", 0);
        var instruction = $"TA \"{EscapeFieldText(entry)}\"";
        var startInline = new FieldStartInline(instruction)
        {
            Definition = FieldInstructionParser.Parse(instruction),
            IsDirty = true
        };

        var inlines = new Inline[]
        {
            startInline,
            new FieldEndInline()
        };

        _session.InsertInlines(inlines);
    }

    private void InsertTableOfAuthorities(object? payload)
    {
        var contentWidth = ResolveTocContentWidth(_session.Caret);
        var paragraphs = BuildAuthorityParagraphs(_session.Layout, contentWidth);
        var properties = new ContentControlProperties
        {
            Id = _contentControlCounter++,
            Kind = ContentControlKind.Block,
            Tag = AuthoritiesTagPrefix,
            Alias = AuthoritiesTitleText
        };

        var blocks = new List<Block>(paragraphs.Count + 2)
        {
            new ContentControlStartBlock(properties)
        };
        blocks.AddRange(paragraphs);
        blocks.Add(new ContentControlEndBlock(properties.Id));

        InsertBlocksAtCaret(blocks, refreshLayout: false);
        _session.RefreshLayout();
    }

    private void UpdateTableOfContentsInternal(EditorTocInsertRequest fallback, int? tocId = null)
    {
        var blocks = _session.Document.Blocks;
        var updated = false;

        for (var i = 0; i < blocks.Count;)
        {
            if (blocks[i] is ContentControlStartBlock start
                && IsTocTag(start.Properties.Tag)
                && (!tocId.HasValue || start.Properties.Id == tocId))
            {
                if (TryReplaceTocContent(start, i, fallback, out var endIndex))
                {
                    updated = true;
                    i = endIndex + 1;
                    if (tocId.HasValue)
                    {
                        break;
                    }

                    continue;
                }
            }

            i++;
        }

        if (updated)
        {
            _session.RefreshLayout();
        }
    }

    private void UpdateTableOfContentsById(int? tocId, EditorTocInsertRequest fallback)
    {
        UpdateTableOfContentsInternal(fallback, tocId);
    }

    private bool TryReplaceTocContent(
        ContentControlStartBlock start,
        int startIndex,
        EditorTocInsertRequest fallback,
        out int endIndex)
    {
        endIndex = -1;
        var blocks = _session.Document.Blocks;
        var end = FindMatchingContentControlEndBlock(blocks, startIndex + 1, start.Properties.Id);
        if (end < 0)
        {
            return false;
        }

        var request = ParseTocTag(start.Properties.Tag, fallback);
        var contentWidth = ResolveTocContentWidth(_session.Caret);
        var paragraphs = BuildTocParagraphs(request, _session.Layout, contentWidth);
        blocks.RemoveRange(startIndex + 1, end - startIndex - 1);
        blocks.InsertRange(startIndex + 1, paragraphs);
        endIndex = startIndex + 1 + paragraphs.Count;
        start.Properties.Tag = BuildTocTag(request);
        return true;
    }

    private EditorTocInsertRequest NormalizeTocRequest(object? payload)
    {
        var request = payload is EditorTocInsertRequest provided ? provided : new EditorTocInsertRequest(3);
        var maxLevel = Math.Clamp(request.MaxLevel, 1, 9);
        return new EditorTocInsertRequest(maxLevel, request.UseHyperlinks, request.ShowPageNumbers);
    }

    private static string BuildTocTag(EditorTocInsertRequest request)
    {
        var maxLevel = Math.Clamp(request.MaxLevel, 1, 9);
        var hyperlinks = request.UseHyperlinks ? 1 : 0;
        var pageNumbers = request.ShowPageNumbers ? 1 : 0;
        return $"{TocTagPrefix};Levels={maxLevel};Hyperlinks={hyperlinks};PageNumbers={pageNumbers}";
    }

    private static EditorTocInsertRequest ParseTocTag(string? tag, EditorTocInsertRequest fallback)
    {
        if (!IsTocTag(tag))
        {
            return fallback;
        }

        var maxLevel = fallback.MaxLevel;
        var useHyperlinks = fallback.UseHyperlinks;
        var showPageNumbers = fallback.ShowPageNumbers;
        var parts = tag!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.StartsWith("Levels=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part.AsSpan("Levels=".Length), out var parsed))
                {
                    maxLevel = Math.Clamp(parsed, 1, 9);
                }

                continue;
            }

            if (part.StartsWith("Hyperlinks=", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseBoolFlag(part.AsSpan("Hyperlinks=".Length), out var parsed))
                {
                    useHyperlinks = parsed;
                }

                continue;
            }

            if (part.StartsWith("PageNumbers=", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseBoolFlag(part.AsSpan("PageNumbers=".Length), out var parsed))
                {
                    showPageNumbers = parsed;
                }
            }
        }

        return new EditorTocInsertRequest(maxLevel, useHyperlinks, showPageNumbers);
    }

    private static bool TryParseBoolFlag(ReadOnlySpan<char> value, out bool result)
    {
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool IsTocTag(string? tag)
    {
        return !string.IsNullOrWhiteSpace(tag)
               && tag.TrimStart().StartsWith(TocTagPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private List<ParagraphBlock> BuildTocParagraphs(EditorTocInsertRequest request, DocumentLayout layout, float contentWidth)
    {
        var paragraphs = new List<ParagraphBlock>();
        var titleParagraph = new ParagraphBlock(TocTitleText);
        if (TryResolveTocHeadingStyle(out var tocHeadingStyleId, out _))
        {
            titleParagraph.StyleId = tocHeadingStyleId;
        }

        paragraphs.Add(titleParagraph);

        var headings = CollectHeadingEntries(request.MaxLevel);
        if (headings.Count == 0)
        {
            return paragraphs;
        }

        var indentStep = MathF.Max(0f, _session.LayoutSettings.ListIndent);
        foreach (var heading in headings)
        {
            var level = Math.Clamp(heading.Level, 1, 9);
            var indentLeft = indentStep * Math.Max(0, level - 1);
            var paragraph = new ParagraphBlock();
            if (TryResolveTocEntryStyle(level, out var tocStyleId, out var tocStyle))
            {
                paragraph.StyleId = tocStyleId;
            }
            else
            {
                ApplyReferenceEntryFallback(paragraph, indentLeft);
            }

            if (request.ShowPageNumbers)
            {
                var hasStyleTabs = tocStyle?.ParagraphProperties.TabStops.Count > 0;
                if (!hasStyleTabs)
                {
                    AddReferenceTabStop(paragraph, contentWidth, indentLeft);
                }
            }

            paragraph.Inlines.Add(new RunInline(heading.Text));
            if (request.ShowPageNumbers)
            {
                var pageNumber = ResolvePageNumber(layout, heading.ParagraphIndex);
                paragraph.Inlines.Add(new RunInline("\t"));
                paragraph.Inlines.Add(new RunInline(pageNumber.ToString(CultureInfo.InvariantCulture)));
            }

            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    private bool TryResolveTocHeadingStyle(out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle("TOCHeading", "TOC Heading", out styleId, out style);
    }

    private bool TryResolveTocEntryStyle(int level, out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle($"TOC{level}", $"TOC {level}", out styleId, out style);
    }

    private bool TryResolveTofHeadingStyle(out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle("TOFHeading", "TOF Heading", out styleId, out style)
               || TryResolveParagraphStyle("TableOfFiguresHeading", "Table of Figures Heading", out styleId, out style);
    }

    private bool TryResolveTofEntryStyle(out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle("TOF", "TOF", out styleId, out style)
               || TryResolveParagraphStyle("TOF1", "TOF 1", out styleId, out style)
               || TryResolveParagraphStyle("TableOfFigures", "Table of Figures", out styleId, out style);
    }

    private bool TryResolveIndexHeadingStyle(out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle("IndexHeading", "Index Heading", out styleId, out style);
    }

    private bool TryResolveIndexEntryStyle(out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle("Index1", "Index 1", out styleId, out style)
               || TryResolveParagraphStyle("Index", "Index", out styleId, out style);
    }

    private bool TryResolveAuthoritiesHeadingStyle(out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle("TOAHeading", "TOA Heading", out styleId, out style)
               || TryResolveParagraphStyle("TableOfAuthoritiesHeading", "Table of Authorities Heading", out styleId, out style);
    }

    private bool TryResolveAuthoritiesEntryStyle(out string? styleId, out ParagraphStyleDefinition? style)
    {
        return TryResolveParagraphStyle("TOA", "TOA", out styleId, out style)
               || TryResolveParagraphStyle("TOA1", "TOA 1", out styleId, out style)
               || TryResolveParagraphStyle("TableOfAuthorities", "Table of Authorities", out styleId, out style);
    }

    private bool TryResolveParagraphStyle(
        string idCandidate,
        string nameCandidate,
        out string? styleId,
        out ParagraphStyleDefinition? style)
    {
        var styles = _session.Document.Styles.ParagraphStyles;
        if (styles.TryGetValue(idCandidate, out style))
        {
            styleId = idCandidate;
            return true;
        }

        if (styles.TryGetValue(nameCandidate, out style))
        {
            styleId = nameCandidate;
            return true;
        }

        foreach (var pair in styles)
        {
            var name = pair.Value.Name;
            if (string.Equals(name, nameCandidate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, idCandidate, StringComparison.OrdinalIgnoreCase))
            {
                styleId = pair.Key;
                style = pair.Value;
                return true;
            }
        }

        styleId = null;
        style = null;
        return false;
    }

    private static void ApplyReferenceEntryFallback(ParagraphBlock paragraph, float indentLeft)
    {
        if (indentLeft > 0f)
        {
            paragraph.Properties.IndentLeft = indentLeft;
        }

        paragraph.Properties.LineSpacing = ReferenceSingleLineSpacingTwips;
        paragraph.Properties.LineSpacingRule = DocLineSpacingRule.Auto;
        paragraph.Properties.SpacingBefore = 0f;
        paragraph.Properties.SpacingAfter = 0f;
    }

    private static void AddReferenceTabStop(ParagraphBlock paragraph, float contentWidth, float indentLeft)
    {
        var tabStopPosition = MathF.Max(0f, contentWidth - indentLeft);
        paragraph.Properties.TabStops.Add(new TabStopDefinition(tabStopPosition)
        {
            Alignment = TabAlignment.Right,
            Leader = TabLeader.Dot
        });
    }

    private List<TocHeadingEntry> CollectHeadingEntries(int maxLevel)
    {
        var entries = new List<TocHeadingEntry>();
        var document = _session.Document;
        var paragraphIndex = 0;
        var tocStack = new Stack<int?>();

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ContentControlStartBlock start when IsTocTag(start.Properties.Tag):
                    tocStack.Push(start.Properties.Id);
                    break;
                case ContentControlEndBlock end:
                    if (tocStack.Count > 0 && (!tocStack.Peek().HasValue || tocStack.Peek() == end.Id))
                    {
                        tocStack.Pop();
                    }

                    break;
                case ParagraphBlock paragraph:
                    AppendHeading(entries, document, paragraph, paragraphIndex, maxLevel, tocStack.Count > 0);
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellParagraph in cell.Paragraphs)
                            {
                                AppendHeading(entries, document, cellParagraph, paragraphIndex, maxLevel, tocStack.Count > 0);
                                paragraphIndex++;
                            }
                        }
                    }

                    break;
            }
        }

        return entries;
    }

    private void AppendHeading(
        List<TocHeadingEntry> entries,
        Document document,
        ParagraphBlock paragraph,
        int paragraphIndex,
        int maxLevel,
        bool insideToc)
    {
        if (insideToc)
        {
            return;
        }

        if (!TryGetHeadingLevel(document, paragraph, out var level) || level > maxLevel)
        {
            return;
        }

        var text = DocumentEditHelpers.GetParagraphText(paragraph).Trim();
        if (text.Length == 0)
        {
            return;
        }

        entries.Add(new TocHeadingEntry(paragraphIndex, level, text));
    }

    private float ResolveTocContentWidth(TextPosition position)
    {
        var layout = _session.Layout;
        if (layout.Pages.Count == 0)
        {
            return _session.LayoutSettings.ContentWidth;
        }

        var lineIndex = EditorSelectionService.FindLineIndexForPosition(layout, position, out _);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        if (pageIndex >= 0 && pageIndex < layout.Pages.Count)
        {
            return layout.Pages[pageIndex].ContentBounds.Width;
        }

        return layout.Pages[0].ContentBounds.Width;
    }

    private static int ResolvePageNumber(DocumentLayout layout, int paragraphIndex)
    {
        if (!layout.ParagraphLineRanges.TryGetValue(paragraphIndex, out var range) || range.Count == 0)
        {
            return 1;
        }

        var lineIndex = Math.Clamp(range.Start, 0, Math.Max(0, layout.LineIndex.LineCount - 1));
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        return pageIndex < 0 ? 1 : pageIndex + 1;
    }

    private static bool TryGetHeadingLevel(Document document, ParagraphBlock paragraph, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(paragraph.StyleId))
        {
            return false;
        }

        if (TryParseHeadingLevel(paragraph.StyleId, out level))
        {
            return true;
        }

        if (document.Styles.ParagraphStyles.TryGetValue(paragraph.StyleId, out var style))
        {
            return TryParseHeadingLevel(style.Name, out level);
        }

        return false;
    }

    private static bool TryParseHeadingLevel(string? value, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();
        if (!span.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "Heading".Length;
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }

        if (index >= span.Length || !char.IsDigit(span[index]))
        {
            return false;
        }

        var parsed = 0;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            parsed = (parsed * 10) + (span[index] - '0');
            index++;
        }

        if (parsed <= 0 || parsed > 9)
        {
            return false;
        }

        level = parsed;
        return true;
    }

    private void InsertBlocksAtCaret(IReadOnlyList<Block> blocks, bool refreshLayout)
    {
        if (blocks is null || blocks.Count == 0)
        {
            return;
        }

        if (!_session.Selection.IsEmpty)
        {
            _session.DeleteForward();
        }

        var document = _session.Document;
        if (document.ParagraphCount == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        var location = document.GetParagraphLocation(_session.Caret.ParagraphIndex);
        if (location.IsInTable)
        {
            InsertBlocksAfterTable(location, blocks, refreshLayout);
            return;
        }

        var paragraph = location.Paragraph;
        var offset = _session.Caret.Offset;
        var nextParagraph = new ParagraphBlock(string.Empty, paragraph.ListInfo?.Clone())
        {
            StyleId = paragraph.StyleId
        };
        CopyParagraphProperties(paragraph.Properties, nextParagraph.Properties);

        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            var splitOffset = Math.Clamp(offset, 0, text.Length);
            paragraph.Text = text.Substring(0, splitOffset);
            nextParagraph.Text = text.Substring(splitOffset);
        }
        else
        {
            SplitInlinesAtOffset(paragraph, offset, out var before, out var after);
            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(before);
            NormalizeInlines(paragraph);

            nextParagraph.Inlines.AddRange(after);
            NormalizeInlines(nextParagraph);
        }

        SplitFloatingAnchors(paragraph, nextParagraph, offset);
        var insertIndex = Math.Clamp(location.BlockIndex + 1, 0, document.Blocks.Count);
        document.Blocks.InsertRange(insertIndex, blocks);
        document.Blocks.Insert(insertIndex + blocks.Count, nextParagraph);

        var paragraphIndex = FindParagraphIndex(nextParagraph);
        _session.SetSelection(new TextRange(new TextPosition(paragraphIndex, 0), new TextPosition(paragraphIndex, 0)));

        if (refreshLayout)
        {
            _session.RefreshLayout();
        }
    }

    private void InsertBlocksAfterTable(ParagraphLocation location, IReadOnlyList<Block> blocks, bool refreshLayout)
    {
        var document = _session.Document;
        var insertIndex = Math.Clamp(location.BlockIndex + 1, 0, document.Blocks.Count);
        document.Blocks.InsertRange(insertIndex, blocks);

        var paragraph = FindFirstParagraphAfterIndex(document, insertIndex + blocks.Count);
        if (paragraph is null)
        {
            paragraph = new ParagraphBlock();
            document.Blocks.Insert(insertIndex + blocks.Count, paragraph);
        }

        var paragraphIndex = FindParagraphIndex(paragraph);
        _session.SetSelection(new TextRange(new TextPosition(paragraphIndex, 0), new TextPosition(paragraphIndex, 0)));

        if (refreshLayout)
        {
            _session.RefreshLayout();
        }
    }

    private static ParagraphBlock? FindFirstParagraphAfterIndex(Document document, int blockIndex)
    {
        for (var i = Math.Max(0, blockIndex); i < document.Blocks.Count; i++)
        {
            switch (document.Blocks[i])
            {
                case ParagraphBlock paragraph:
                    return paragraph;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            if (cell.Paragraphs.Count > 0)
                            {
                                return cell.Paragraphs[0];
                            }
                        }
                    }

                    break;
            }
        }

        return null;
    }

    private int FindParagraphIndex(ParagraphBlock paragraph)
    {
        var count = 0;
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock candidate:
                    if (ReferenceEquals(candidate, paragraph))
                    {
                        return count;
                    }

                    count++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var candidate in cell.Paragraphs)
                            {
                                if (ReferenceEquals(candidate, paragraph))
                                {
                                    return count;
                                }

                                count++;
                            }
                        }
                    }

                    break;
            }
        }

        return Math.Clamp(_session.Caret.ParagraphIndex, 0, Math.Max(0, _session.Document.ParagraphCount - 1));
    }

    private static void SplitInlinesAtOffset(
        ParagraphBlock paragraph,
        int offset,
        out List<Inline> before,
        out List<Inline> after)
    {
        before = new List<Inline>();
        after = new List<Inline>();

        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
        var splitOffset = Math.Clamp(offset, 0, length);
        var position = 0;

        foreach (var inline in paragraph.Inlines)
        {
            var inlineLength = DocumentEditHelpers.GetInlineLength(inline);
            var end = position + inlineLength;
            if (splitOffset <= position)
            {
                after.Add(inline);
            }
            else if (splitOffset >= end)
            {
                before.Add(inline);
            }
            else if (inline is RunInline run)
            {
                var runLength = run.Text.Length;
                var splitIndex = Math.Clamp(splitOffset - position, 0, runLength);
                if (splitIndex > 0)
                {
                    before.Add(new RunInline(run.Text.SliceBuffer(0, splitIndex), run.Style) { StyleId = run.StyleId });
                }

                var afterLength = runLength - splitIndex;
                if (afterLength > 0)
                {
                    after.Add(new RunInline(run.Text.SliceBuffer(splitIndex, afterLength), run.Style) { StyleId = run.StyleId });
                }
            }
            else
            {
                before.Add(inline);
            }

            position = end;
        }
    }

    private static void SplitFloatingAnchors(ParagraphBlock source, ParagraphBlock target, int splitOffset)
    {
        if (source.FloatingObjects.Count == 0)
        {
            return;
        }

        for (var i = source.FloatingObjects.Count - 1; i >= 0; i--)
        {
            var floating = source.FloatingObjects[i];
            if (floating.Anchor.AnchorOffset is not { } anchorOffset)
            {
                continue;
            }

            if (anchorOffset >= splitOffset)
            {
                floating.Anchor.AnchorOffset = Math.Max(0, anchorOffset - splitOffset);
                source.FloatingObjects.RemoveAt(i);
                target.FloatingObjects.Add(floating);
            }
        }
    }

    private static void NormalizeInlines(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Text = string.Empty;
            return;
        }

        var normalized = new List<Inline>(paragraph.Inlines.Count);
        RunInline? lastRun = null;

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                if (run.Text.Length == 0)
                {
                    continue;
                }

                if (lastRun is not null && AreRunsMergeable(lastRun, run))
                {
                    lastRun.Text.Append(run.Text);
                }
                else
                {
                    normalized.Add(run);
                    lastRun = run;
                }
            }
            else
            {
                normalized.Add(inline);
                lastRun = null;
            }
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(normalized);
        paragraph.Text = DocumentEditHelpers.GetParagraphText(paragraph);
    }

    private static bool AreRunsMergeable(RunInline left, RunInline right)
    {
        if (!string.Equals(left.StyleId, right.StyleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (left.Style is null && right.Style is null)
        {
            return true;
        }

        if (left.Style is null || right.Style is null)
        {
            return false;
        }

        return AreTextStylesEquivalent(left.Style, right.Style);
    }

    private static bool AreTextStylesEquivalent(TextStyleProperties left, TextStyleProperties right)
    {
        return left.IsEquivalentTo(right);
    }

    private static void CopyParagraphProperties(ParagraphProperties source, ParagraphProperties target)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
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
        target.ShadingColor = source.ShadingColor;
        if (source.Borders.HasAny)
        {
            target.Borders.Top = source.Borders.Top?.Clone();
            target.Borders.Bottom = source.Borders.Bottom?.Clone();
            target.Borders.Left = source.Borders.Left?.Clone();
            target.Borders.Right = source.Borders.Right?.Clone();
        }
        target.TabStops.Clear();
        foreach (var tabStop in source.TabStops)
        {
            target.TabStops.Add(tabStop.Clone());
        }
    }

    private static int FindMatchingContentControlEndBlock(
        IReadOnlyList<Block> blocks,
        int startIndex,
        int? id)
    {
        for (var i = Math.Max(0, startIndex); i < blocks.Count; i++)
        {
            if (blocks[i] is ContentControlEndBlock end
                && (!id.HasValue || end.Id == id))
            {
                return i;
            }
        }

        return -1;
    }

    private readonly record struct TocHeadingEntry(int ParagraphIndex, int Level, string Text);

    private int GetNextCaptionIndex(string label)
    {
        if (_captionCounters.TryGetValue(label, out var current))
        {
            current++;
            _captionCounters[label] = current;
            return current;
        }

        _captionCounters[label] = 1;
        return 1;
    }

    private static int FindNextId(IEnumerable<int> ids)
    {
        var max = 0;
        foreach (var id in ids)
        {
            if (id > max)
            {
                max = id;
            }
        }

        return max + 1;
    }

    private static int FindNextContentControlId(Document document)
    {
        var maxId = 0;
        ScanContentControls(document.Blocks, ref maxId);
        ScanContentControls(document.Header.Blocks, ref maxId);
        ScanContentControls(document.Footer.Blocks, ref maxId);
        ScanContentControls(document.FirstHeader.Blocks, ref maxId);
        ScanContentControls(document.FirstFooter.Blocks, ref maxId);
        ScanContentControls(document.EvenHeader.Blocks, ref maxId);
        ScanContentControls(document.EvenFooter.Blocks, ref maxId);
        return maxId + 1;
    }

    private static void ScanContentControls(IEnumerable<Block> blocks, ref int maxId)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ContentControlStartBlock contentStart when contentStart.Properties.Id.HasValue:
                    maxId = Math.Max(maxId, contentStart.Properties.Id.Value);
                    break;
                case ParagraphBlock paragraph:
                    ScanContentControls(paragraph, ref maxId);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            if (cell.ContentControl?.Id is int cellId)
                            {
                                maxId = Math.Max(maxId, cellId);
                            }

                            foreach (var paragraph in cell.Paragraphs)
                            {
                                ScanContentControls(paragraph, ref maxId);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static void ScanContentControls(ParagraphBlock paragraph, ref int maxId)
    {
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is ContentControlStartInline start && start.Properties.Id.HasValue)
            {
                maxId = Math.Max(maxId, start.Properties.Id.Value);
            }
        }
    }

    private static string EscapeFieldText(string value)
    {
        return value.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    private string ResolveEntryText(object? payload, string fallbackPrefix, int fallbackIndex)
    {
        if (payload is string provided && !string.IsNullOrWhiteSpace(provided))
        {
            return provided.Trim();
        }

        var selectionText = BuildSelectionText(_session.Selection, 120);
        if (!string.IsNullOrWhiteSpace(selectionText))
        {
            return selectionText.Trim();
        }

        return fallbackIndex > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} {1}", fallbackPrefix, fallbackIndex)
            : fallbackPrefix;
    }

    private string BuildSelectionText(TextRange range, int maxLength)
    {
        var selection = range.Normalize();
        var paragraphs = GetParagraphs();
        if (paragraphs.Count == 0 || selection.IsEmpty)
        {
            return string.Empty;
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphs.Count - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphs.Count - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var builder = new StringBuilder();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? selection.Start.Offset : 0;
            var endOffset = i == endIndex ? selection.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (endOffset > startOffset)
            {
                AppendParagraphSlice(builder, paragraph, startOffset, endOffset);
            }

            if (builder.Length >= maxLength)
            {
                break;
            }

            if (i < endIndex)
            {
                builder.Append(' ');
            }
        }

        if (builder.Length > maxLength)
        {
            return builder.ToString(0, maxLength);
        }

        return builder.ToString();
    }

    private IReadOnlyList<ParagraphBlock> GetParagraphs()
    {
        var paragraphs = _session.Layout.Paragraphs;
        if (paragraphs.Count > 0)
        {
            return paragraphs;
        }

        return DocumentEditHelpers.BuildParagraphList(_session.Document);
    }

    private static void AppendParagraphSlice(StringBuilder builder, ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            AppendStringSlice(builder, text, startOffset, endOffset - startOffset);
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
            case FootnoteReferenceInline footnote:
                AppendStringSlice(builder, footnote.Id.ToString(CultureInfo.InvariantCulture), start, length);
                break;
            case EndnoteReferenceInline endnote:
                AppendStringSlice(builder, endnote.Id.ToString(CultureInfo.InvariantCulture), start, length);
                break;
            case CommentReferenceInline comment:
                AppendStringSlice(builder, comment.Id.ToString(CultureInfo.InvariantCulture), start, length);
                break;
            default:
                break;
        }
    }

    private static void AppendStringSlice(StringBuilder builder, string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return;
        }

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        if (length == 0)
        {
            return;
        }

        builder.Append(text.AsSpan(start, length));
    }

    private List<NoteAnchor> CollectNoteAnchors(NoteKind kind)
    {
        var anchors = new List<NoteAnchor>();
        var paragraphIndex = 0;
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    AddNoteAnchors(anchors, paragraph, paragraphIndex++, kind);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                AddNoteAnchors(anchors, paragraph, paragraphIndex++, kind);
                            }
                        }
                    }

                    break;
            }
        }

        return anchors;
    }

    private static void AddNoteAnchors(List<NoteAnchor> anchors, ParagraphBlock paragraph, int paragraphIndex, NoteKind kind)
    {
        var position = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (kind == NoteKind.Footnote && inline is FootnoteReferenceInline footnote)
            {
                anchors.Add(new NoteAnchor(footnote.Id, new TextPosition(paragraphIndex, position)));
            }
            else if (kind == NoteKind.Endnote && inline is EndnoteReferenceInline endnote)
            {
                anchors.Add(new NoteAnchor(endnote.Id, new TextPosition(paragraphIndex, position)));
            }

            position += DocumentEditHelpers.GetInlineLength(inline);
        }
    }

    private static int CompareAnchors(NoteAnchor left, NoteAnchor right)
    {
        var paragraphComparison = left.Position.ParagraphIndex.CompareTo(right.Position.ParagraphIndex);
        if (paragraphComparison != 0)
        {
            return paragraphComparison;
        }

        return left.Position.Offset.CompareTo(right.Position.Offset);
    }

    private static int FindAnchorIndex(List<NoteAnchor> anchors, TextPosition caret, int direction)
    {
        if (anchors.Count == 0)
        {
            return -1;
        }

        if (direction >= 0)
        {
            for (var i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                if (ComparePosition(anchor.Position, caret) > 0 || direction == 0)
                {
                    return i;
                }
            }

            return 0;
        }

        for (var i = anchors.Count - 1; i >= 0; i--)
        {
            var anchor = anchors[i];
            if (ComparePosition(anchor.Position, caret) < 0)
            {
                return i;
            }
        }

        return anchors.Count - 1;
    }

    private static int ComparePosition(TextPosition left, TextPosition right)
    {
        if (left.ParagraphIndex != right.ParagraphIndex)
        {
            return left.ParagraphIndex.CompareTo(right.ParagraphIndex);
        }

        return left.Offset.CompareTo(right.Offset);
    }

    private static string BuildTofTag(string label)
    {
        return $"{TofTagPrefix};Label={label}";
    }

    private void UpdateTableOfFiguresInternal(string label, int? controlId = null)
    {
        var blocks = _session.Document.Blocks;
        var updated = false;

        for (var i = 0; i < blocks.Count;)
        {
            if (blocks[i] is ContentControlStartBlock start
                && IsTofTag(start.Properties.Tag)
                && (!controlId.HasValue || start.Properties.Id == controlId))
            {
                if (TryReplaceTofContent(start, i, label, out var endIndex))
                {
                    updated = true;
                    i = endIndex + 1;
                    if (controlId.HasValue)
                    {
                        break;
                    }

                    continue;
                }
            }

            i++;
        }

        if (updated)
        {
            _session.RefreshLayout();
        }
    }

    private void UpdateTableOfFiguresById(int? controlId, string label)
    {
        UpdateTableOfFiguresInternal(label, controlId);
    }

    private bool TryReplaceTofContent(ContentControlStartBlock start, int startIndex, string label, out int endIndex)
    {
        endIndex = -1;
        var blocks = _session.Document.Blocks;
        var end = FindMatchingContentControlEndBlock(blocks, startIndex + 1, start.Properties.Id);
        if (end < 0)
        {
            return false;
        }

        var contentWidth = ResolveTocContentWidth(_session.Caret);
        var paragraphs = BuildTableOfFiguresParagraphs(label, _session.Layout, contentWidth);
        blocks.RemoveRange(startIndex + 1, end - startIndex - 1);
        blocks.InsertRange(startIndex + 1, paragraphs);
        endIndex = startIndex + 1 + paragraphs.Count;
        start.Properties.Tag = BuildTofTag(label);
        return true;
    }

    private static bool IsTofTag(string? tag)
    {
        return !string.IsNullOrWhiteSpace(tag)
               && tag.TrimStart().StartsWith(TofTagPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private List<ParagraphBlock> BuildTableOfFiguresParagraphs(string label, DocumentLayout layout, float contentWidth)
    {
        var paragraphs = new List<ParagraphBlock>();
        var titleParagraph = new ParagraphBlock(TofTitleText);
        if (TryResolveTofHeadingStyle(out var headingStyleId, out _))
        {
            titleParagraph.StyleId = headingStyleId;
        }

        paragraphs.Add(titleParagraph);

        var captions = CollectCaptionEntries(label);
        if (captions.Count == 0)
        {
            return paragraphs;
        }

        TryResolveTofEntryStyle(out var entryStyleId, out var entryStyle);
        foreach (var entry in captions)
        {
            var paragraph = new ParagraphBlock();
            if (!string.IsNullOrWhiteSpace(entryStyleId))
            {
                paragraph.StyleId = entryStyleId;
            }
            else
            {
                ApplyReferenceEntryFallback(paragraph, 0f);
            }

            var hasStyleTabs = entryStyle?.ParagraphProperties.TabStops.Count > 0;
            if (!hasStyleTabs)
            {
                AddReferenceTabStop(paragraph, contentWidth, 0f);
            }

            paragraph.Inlines.Add(new RunInline(entry.Text));
            var pageNumber = ResolvePageNumber(layout, entry.ParagraphIndex);
            paragraph.Inlines.Add(new RunInline("\t"));
            paragraph.Inlines.Add(new RunInline(pageNumber.ToString(CultureInfo.InvariantCulture)));
            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    private List<IndexEntry> CollectCaptionEntries(string label)
    {
        var entries = new List<IndexEntry>();
        var paragraphIndex = 0;
        var controlStack = new Stack<int?>();

        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ContentControlStartBlock start when IsIndexContentControl(start.Properties.Tag):
                    controlStack.Push(start.Properties.Id);
                    break;
                case ContentControlEndBlock end:
                    if (controlStack.Count > 0 && (!controlStack.Peek().HasValue || controlStack.Peek() == end.Id))
                    {
                        controlStack.Pop();
                    }

                    break;
                case ParagraphBlock paragraph:
                    AppendCaptionEntry(entries, paragraph, paragraphIndex, label, controlStack.Count > 0);
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellParagraph in cell.Paragraphs)
                            {
                                AppendCaptionEntry(entries, cellParagraph, paragraphIndex, label, controlStack.Count > 0);
                                paragraphIndex++;
                            }
                        }
                    }

                    break;
            }
        }

        return entries;
    }

    private void AppendCaptionEntry(List<IndexEntry> entries, ParagraphBlock paragraph, int paragraphIndex, string label, bool insideControl)
    {
        if (insideControl)
        {
            return;
        }

        if (!TryGetCaptionLabel(paragraph, out var captionLabel))
        {
            return;
        }

        if (!string.Equals(captionLabel, label, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var text = DocumentEditHelpers.GetParagraphText(paragraph).Trim();
        if (text.Length == 0)
        {
            return;
        }

        entries.Add(new IndexEntry(text, paragraphIndex));
    }

    private static bool TryGetCaptionLabel(ParagraphBlock paragraph, out string label)
    {
        label = string.Empty;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is FieldStartInline fieldStart && TryParseSequenceLabel(fieldStart.Instruction, out var parsed))
            {
                label = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseSequenceLabel(string? instruction, out string label)
    {
        label = string.Empty;
        var definition = FieldInstructionParser.Parse(instruction);
        if (definition is null || !definition.Name.Equals("SEQ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (definition.Arguments.Count == 0)
        {
            return false;
        }

        label = definition.Arguments[0].Value;
        return !string.IsNullOrWhiteSpace(label);
    }

    private List<ParagraphBlock> BuildIndexParagraphs(DocumentLayout layout, float contentWidth)
    {
        var paragraphs = new List<ParagraphBlock>();
        var titleParagraph = new ParagraphBlock(IndexTitleText);
        if (TryResolveIndexHeadingStyle(out var headingStyleId, out _))
        {
            titleParagraph.StyleId = headingStyleId;
        }

        paragraphs.Add(titleParagraph);

        var entries = CollectFieldEntries("XE");
        if (entries.Count == 0)
        {
            return paragraphs;
        }

        TryResolveIndexEntryStyle(out var entryStyleId, out var entryStyle);
        foreach (var entry in BuildIndexGroups(entries, layout, contentWidth, entryStyleId, entryStyle))
        {
            paragraphs.Add(entry);
        }

        return paragraphs;
    }

    private List<ParagraphBlock> BuildAuthorityParagraphs(DocumentLayout layout, float contentWidth)
    {
        var paragraphs = new List<ParagraphBlock>();
        var titleParagraph = new ParagraphBlock(AuthoritiesTitleText);
        if (TryResolveAuthoritiesHeadingStyle(out var headingStyleId, out _))
        {
            titleParagraph.StyleId = headingStyleId;
        }

        paragraphs.Add(titleParagraph);

        var entries = CollectFieldEntries("TA");
        if (entries.Count == 0)
        {
            return paragraphs;
        }

        TryResolveAuthoritiesEntryStyle(out var entryStyleId, out var entryStyle);
        foreach (var entry in BuildIndexGroups(entries, layout, contentWidth, entryStyleId, entryStyle))
        {
            paragraphs.Add(entry);
        }

        return paragraphs;
    }

    private List<IndexEntry> CollectFieldEntries(string fieldName)
    {
        var entries = new List<IndexEntry>();
        var paragraphIndex = 0;
        var controlStack = new Stack<int?>();

        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ContentControlStartBlock start when IsIndexContentControl(start.Properties.Tag):
                    controlStack.Push(start.Properties.Id);
                    break;
                case ContentControlEndBlock end:
                    if (controlStack.Count > 0 && (!controlStack.Peek().HasValue || controlStack.Peek() == end.Id))
                    {
                        controlStack.Pop();
                    }

                    break;
                case ParagraphBlock paragraph:
                    AppendFieldEntry(entries, paragraph, paragraphIndex, fieldName, controlStack.Count > 0);
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellParagraph in cell.Paragraphs)
                            {
                                AppendFieldEntry(entries, cellParagraph, paragraphIndex, fieldName, controlStack.Count > 0);
                                paragraphIndex++;
                            }
                        }
                    }

                    break;
            }
        }

        return entries;
    }

    private static bool IsIndexContentControl(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var trimmed = tag.TrimStart();
        return trimmed.StartsWith(TocTagPrefix, StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith(TofTagPrefix, StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith(IndexTagPrefix, StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith(AuthoritiesTagPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void AppendFieldEntry(List<IndexEntry> entries, ParagraphBlock paragraph, int paragraphIndex, string fieldName, bool insideControl)
    {
        if (insideControl)
        {
            return;
        }

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is FieldStartInline fieldStart
                && TryParseFieldEntry(fieldStart.Instruction, fieldName, out var entryText))
            {
                entries.Add(new IndexEntry(entryText, paragraphIndex));
            }
        }
    }

    private static bool TryParseFieldEntry(string? instruction, string fieldName, out string entry)
    {
        entry = string.Empty;
        var definition = FieldInstructionParser.Parse(instruction);
        if (definition is null || !definition.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (definition.Arguments.Count == 0)
        {
            return false;
        }

        entry = definition.Arguments[0].Value;
        return !string.IsNullOrWhiteSpace(entry);
    }

    private IEnumerable<ParagraphBlock> BuildIndexGroups(
        List<IndexEntry> entries,
        DocumentLayout layout,
        float contentWidth,
        string? entryStyleId,
        ParagraphStyleDefinition? entryStyle)
    {
        var grouped = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!grouped.TryGetValue(entry.Text, out var list))
            {
                list = new List<int>();
                grouped[entry.Text] = list;
            }

            list.Add(entry.ParagraphIndex);
        }

        var orderedKeys = grouped.Keys.ToList();
        orderedKeys.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var key in orderedKeys)
        {
            var pageNumbers = grouped[key];
            pageNumbers.Sort();
            var uniquePages = new List<int>(pageNumbers.Count);
            var lastPage = -1;
            foreach (var page in pageNumbers)
            {
                var resolved = ResolvePageNumber(layout, page);
                if (resolved != lastPage)
                {
                    uniquePages.Add(resolved);
                    lastPage = resolved;
                }
            }

            var paragraph = new ParagraphBlock();
            if (!string.IsNullOrWhiteSpace(entryStyleId))
            {
                paragraph.StyleId = entryStyleId;
            }
            else
            {
                ApplyReferenceEntryFallback(paragraph, 0f);
            }

            var hasStyleTabs = entryStyle?.ParagraphProperties.TabStops.Count > 0;
            if (!hasStyleTabs)
            {
                AddReferenceTabStop(paragraph, contentWidth, 0f);
            }

            paragraph.Inlines.Add(new RunInline(key));
            paragraph.Inlines.Add(new RunInline("\t"));
            paragraph.Inlines.Add(new RunInline(string.Join(", ", uniquePages)));
            yield return paragraph;
        }
    }

    private void UpdateFields(FieldUpdateScope scope, bool pageNumbersOnly)
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return;
        }

        var selection = _session.Selection.Normalize();
        if (scope == FieldUpdateScope.Current && selection.IsEmpty)
        {
            if (TryUpdateFieldAtCaret(pageNumbersOnly))
            {
                _session.RefreshLayout();
            }

            return;
        }

        TextRange? range = scope == FieldUpdateScope.Document ? null : selection;
        if (UpdateFieldsInDocument(range, pageNumbersOnly))
        {
            _session.RefreshLayout();
        }
    }

    private bool UpdateFieldsInDocument(TextRange? range, bool pageNumbersOnly)
    {
        var updated = false;
        foreach (var (paragraph, paragraphIndex) in EnumerateBodyParagraphs())
        {
            var spans = BuildFieldSpans(paragraph);
            if (spans.Count == 0)
            {
                continue;
            }

            for (var i = spans.Count - 1; i >= 0; i--)
            {
                var span = spans[i];
                if (range.HasValue && !IntersectsRange(paragraphIndex, span, range.Value))
                {
                    continue;
                }

                if (TryBuildFieldResult(span, paragraph, paragraphIndex, pageNumbersOnly, out var resultInline))
                {
                    ReplaceFieldResult(paragraph, span, resultInline);
                    updated = true;
                }
            }
        }

        return updated;
    }

    private bool TryUpdateFieldAtCaret(bool pageNumbersOnly)
    {
        var caret = _session.Caret;
        if (caret.ParagraphIndex < 0 || caret.ParagraphIndex >= _session.Document.ParagraphCount)
        {
            return false;
        }

        var paragraph = _session.Document.GetParagraph(caret.ParagraphIndex);
        var spans = BuildFieldSpans(paragraph);
        if (spans.Count == 0)
        {
            return false;
        }

        foreach (var span in spans)
        {
            if (caret.Offset >= span.StartOffset && caret.Offset <= span.EndOffset)
            {
                if (TryBuildFieldResult(span, paragraph, caret.ParagraphIndex, pageNumbersOnly, out var resultInline))
                {
                    ReplaceFieldResult(paragraph, span, resultInline);
                    return true;
                }

                return false;
            }
        }

        return false;
    }

    private void SetFieldLocks(bool locked)
    {
        if (_session.Document.ParagraphCount == 0)
        {
            return;
        }

        var selection = _session.Selection.Normalize();
        var hasSelection = !selection.IsEmpty;
        var applied = false;

        foreach (var (paragraph, paragraphIndex) in EnumerateBodyParagraphs())
        {
            var spans = BuildFieldSpans(paragraph);
            if (spans.Count == 0)
            {
                continue;
            }

            foreach (var span in spans)
            {
                if (hasSelection && !IntersectsRange(paragraphIndex, span, selection))
                {
                    continue;
                }

                span.Start.IsLocked = locked;
                applied = true;
            }
        }

        if (!applied && !hasSelection)
        {
            var caret = _session.Caret;
            if (caret.ParagraphIndex >= 0 && caret.ParagraphIndex < _session.Document.ParagraphCount)
            {
                var paragraph = _session.Document.GetParagraph(caret.ParagraphIndex);
                var spans = BuildFieldSpans(paragraph);
                foreach (var span in spans)
                {
                    if (caret.Offset >= span.StartOffset && caret.Offset <= span.EndOffset)
                    {
                        span.Start.IsLocked = locked;
                        applied = true;
                        break;
                    }
                }
            }
        }

        if (applied)
        {
            _session.RefreshLayout();
        }
    }

    private bool TryBuildFieldResult(
        FieldSpan span,
        ParagraphBlock paragraph,
        int paragraphIndex,
        bool pageNumbersOnly,
        out Inline? resultInline)
    {
        resultInline = null;
        var fieldStart = span.Start;
        if (fieldStart.IsLocked)
        {
            return false;
        }

        var definition = fieldStart.Definition ??= FieldInstructionParser.Parse(fieldStart.Instruction);
        if (definition is null)
        {
            return false;
        }

        var kind = definition.Kind;
        if (pageNumbersOnly && kind is not FieldKind.Page and not FieldKind.NumPages)
        {
            return false;
        }

        string? text = kind switch
        {
            FieldKind.Page => ResolveFieldPageNumber(paragraphIndex, span.StartOffset),
            FieldKind.NumPages => _session.Layout.Pages.Count.ToString(CultureInfo.InvariantCulture),
            FieldKind.Date => ResolveFieldDateTime(definition, false),
            FieldKind.Time => ResolveFieldDateTime(definition, true),
            FieldKind.Citation => ResolveCitationFieldText(definition),
            FieldKind.Bibliography => BuildBibliographyDisplay(),
            _ => null
        };

        if (text is null)
        {
            return false;
        }

        var (style, styleId) = ResolveFieldResultStyle(paragraph, span);
        var run = new RunInline(text, style)
        {
            StyleId = styleId
        };

        fieldStart.IsDirty = false;
        resultInline = run;
        return true;
    }

    private string ResolveFieldPageNumber(int paragraphIndex, int startOffset)
    {
        var position = new TextPosition(paragraphIndex, startOffset);
        var lineIndex = EditorSelectionService.FindLineIndexForPosition(_session.Layout, position, out _);
        var pageIndex = _session.Layout.LineIndex.GetPageForLine(lineIndex);
        if (pageIndex < 0)
        {
            pageIndex = 0;
        }

        return (pageIndex + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveFieldDateTime(FieldDefinition definition, bool timeOnly)
    {
        var format = ResolveFieldFormat(definition);
        if (string.IsNullOrWhiteSpace(format))
        {
            format = timeOnly ? "t" : "d";
        }

        return DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
    }

    private string? ResolveCitationFieldText(FieldDefinition definition)
    {
        if (definition.Arguments.Count == 0)
        {
            return null;
        }

        var tag = definition.Arguments[0].Value;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        return BuildCitationDisplay(tag.Trim());
    }

    private static string? ResolveFieldFormat(FieldDefinition definition)
    {
        foreach (var fieldSwitch in definition.Switches)
        {
            if (string.Equals(fieldSwitch.Name, "\\@", StringComparison.OrdinalIgnoreCase))
            {
                return fieldSwitch.Value;
            }
        }

        return null;
    }

    private static (TextStyleProperties? Style, string? StyleId) ResolveFieldResultStyle(ParagraphBlock paragraph, FieldSpan span)
    {
        var inlines = paragraph.Inlines;
        var resultStart = span.SeparatorIndex >= 0 ? span.SeparatorIndex + 1 : span.StartIndex + 1;
        var resultEnd = span.EndIndex;
        for (var i = resultStart; i < resultEnd && i < inlines.Count; i++)
        {
            if (inlines[i] is RunInline run)
            {
                return (run.Style?.Clone(), run.StyleId);
            }
        }

        return (null, null);
    }

    private static void ReplaceFieldResult(ParagraphBlock paragraph, FieldSpan span, Inline? resultInline)
    {
        var inlines = paragraph.Inlines;
        var separatorIndex = span.SeparatorIndex;
        var endIndex = span.EndIndex;
        if (separatorIndex < 0)
        {
            separatorIndex = span.StartIndex + 1;
            inlines.Insert(separatorIndex, new FieldSeparatorInline());
            endIndex++;
        }

        var resultStart = separatorIndex + 1;
        var removeCount = Math.Max(0, endIndex - resultStart);
        if (removeCount > 0 && resultStart < inlines.Count)
        {
            inlines.RemoveRange(resultStart, Math.Min(removeCount, inlines.Count - resultStart));
        }

        if (resultInline is not null)
        {
            inlines.Insert(resultStart, resultInline);
        }
    }

    private static bool IntersectsRange(int paragraphIndex, FieldSpan span, TextRange selection)
    {
        var fieldStart = new TextPosition(paragraphIndex, span.StartOffset);
        var fieldEnd = new TextPosition(paragraphIndex, span.EndOffset);
        var start = selection.Normalize().Start;
        var end = selection.Normalize().End;

        if (ComparePositions(fieldEnd, start) < 0)
        {
            return false;
        }

        if (ComparePositions(fieldStart, end) > 0)
        {
            return false;
        }

        return true;
    }

    private static int ComparePositions(TextPosition left, TextPosition right)
    {
        var paragraphCompare = left.ParagraphIndex.CompareTo(right.ParagraphIndex);
        if (paragraphCompare != 0)
        {
            return paragraphCompare;
        }

        return left.Offset.CompareTo(right.Offset);
    }

    private static bool HasFields(ParagraphBlock paragraph)
    {
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is FieldStartInline)
            {
                return true;
            }
        }

        return false;
    }

    private static List<FieldSpan> BuildFieldSpans(ParagraphBlock paragraph)
    {
        var spans = new List<FieldSpan>();
        if (paragraph.Inlines.Count == 0)
        {
            return spans;
        }

        var inlines = paragraph.Inlines;
        var offsets = new int[inlines.Count];
        var offset = 0;
        for (var i = 0; i < inlines.Count; i++)
        {
            offsets[i] = offset;
            offset += DocumentEditHelpers.GetInlineLength(inlines[i]);
        }

        for (var i = 0; i < inlines.Count; i++)
        {
            if (inlines[i] is not FieldStartInline start)
            {
                continue;
            }

            var separatorIndex = -1;
            var endIndex = -1;
            for (var j = i + 1; j < inlines.Count; j++)
            {
                if (inlines[j] is FieldSeparatorInline && separatorIndex < 0)
                {
                    separatorIndex = j;
                    continue;
                }

                if (inlines[j] is FieldEndInline)
                {
                    endIndex = j;
                    break;
                }
            }

            if (endIndex <= i)
            {
                continue;
            }

            spans.Add(new FieldSpan(start, i, separatorIndex, endIndex, offsets[i], offsets[endIndex]));
            i = endIndex;
        }

        return spans;
    }

    private IEnumerable<(ParagraphBlock Paragraph, int ParagraphIndex)> EnumerateBodyParagraphs()
    {
        var paragraphIndex = 0;
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    yield return (paragraph, paragraphIndex++);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                yield return (paragraph, paragraphIndex++);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private enum FieldUpdateScope
    {
        Current,
        Document
    }

    private readonly record struct FieldSpan(
        FieldStartInline Start,
        int StartIndex,
        int SeparatorIndex,
        int EndIndex,
        int StartOffset,
        int EndOffset);

    private enum NoteKind
    {
        Footnote,
        Endnote
    }

    private readonly record struct NoteAnchor(int Id, TextPosition Position);

    private readonly record struct IndexEntry(string Text, int ParagraphIndex);
}
