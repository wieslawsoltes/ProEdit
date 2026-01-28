using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorMailingsCommandMap
{
    private const float EnvelopeWidth = 912f;
    private const float EnvelopeHeight = 396f;
    private const int DefaultLabelRows = 10;
    private const int DefaultLabelColumns = 3;
    private const float DefaultLabelPadding = 6f;
    private static readonly DocColor MergeFieldHighlightColor = new DocColor(255, 255, 153);

    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;
    private readonly EditorServices _services;
    private bool _highlightMergeFields;
    private bool _previewResults;
    private int _previewRecordIndex;
    private string? _mergeType;

    private enum MailMergeFieldKind
    {
        MergeField,
        AddressBlock,
        GreetingLine,
        Rule
    }

    public EditorMailingsCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session, EditorServices services)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Register()
    {
        _router.RegisterAction(EditorMailingsCommandIds.Create.Envelopes, (_, __) => InsertEnvelope(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorMailingsCommandIds.Create.Labels, (_, __) => InsertLabels(), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorMailingsCommandIds.StartMailMerge.Start, (_, payload) => StartMailMerge(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorMailingsCommandIds.StartMailMerge.SelectRecipients, (_, __) => SelectRecipients(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.StartMailMerge.EditRecipients, (_, __) => EditRecipients(), (context, _) => HasParagraphs(context), isUndoable: false);

        _router.RegisterAction(EditorMailingsCommandIds.WriteInsert.HighlightMergeFields, (_, __) => ToggleHighlightMergeFields(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.WriteInsert.AddressBlock, (_, __) => InsertAddressBlock(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorMailingsCommandIds.WriteInsert.GreetingLine, (_, __) => InsertGreetingLine(), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorMailingsCommandIds.WriteInsert.InsertMergeField, (_, payload) => InsertMergeField(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorMailingsCommandIds.WriteInsert.Rules, (_, payload) => InsertRule(payload), (context, _) => HasParagraphs(context));
        _router.RegisterAction(EditorMailingsCommandIds.WriteInsert.MatchFields, (_, __) => MatchFields(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.WriteInsert.UpdateLabels, (_, __) => UpdateLabels(), (context, _) => HasParagraphs(context));

        _router.RegisterAction(EditorMailingsCommandIds.PreviewResults.Toggle, (_, __) => TogglePreviewResults(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.PreviewResults.FirstRecord, (_, __) => SetPreviewRecord(0), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.PreviewResults.PreviousRecord, (_, __) => MovePreviewRecord(-1), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.PreviewResults.NextRecord, (_, __) => MovePreviewRecord(1), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.PreviewResults.LastRecord, (_, __) => SetPreviewRecordToLast(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.PreviewResults.FindRecipient, (_, __) => FindRecipient(), (context, _) => HasParagraphs(context), isUndoable: false);
        _router.RegisterAction(EditorMailingsCommandIds.PreviewResults.CheckErrors, (_, __) => CheckErrors(), (context, _) => HasParagraphs(context), isUndoable: false);

        _router.RegisterAction(EditorMailingsCommandIds.Finish.FinishAndMerge, (_, payload) => FinishAndMerge(payload), (context, _) => HasParagraphs(context));
    }

    private bool HasParagraphs(RibbonContextSnapshot? context)
    {
        if (context.HasValue && context.Value.Selection.Kind == EditorSelectionKind.FloatingObject)
        {
            return false;
        }

        return _session.Document.ParagraphCount > 0;
    }

    private void InsertEnvelope()
    {
        ApplyToSections(properties =>
        {
            properties.PageWidth = EnvelopeWidth;
            properties.PageHeight = EnvelopeHeight;
            properties.Orientation = PageOrientation.Landscape;
        });

        _session.InsertParagraphBreak();
        _session.InsertText("Envelope".AsSpan());
        _session.InsertParagraphBreak();
        InsertAddressBlock();
        _session.RefreshLayout();
    }

    private void InsertLabels()
    {
        var table = new TableBlock();
        table.Properties.CellPadding = DocThickness.Uniform(DefaultLabelPadding);
        for (var rowIndex = 0; rowIndex < DefaultLabelRows; rowIndex++)
        {
            var row = new TableRow();
            for (var columnIndex = 0; columnIndex < DefaultLabelColumns; columnIndex++)
            {
                var cell = new TableCell();
                cell.Paragraphs.Add(new ParagraphBlock("Label"));
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        _session.InsertBlock(table);
        _session.RefreshLayout();
    }

    private void StartMailMerge(object? payload)
    {
        var type = payload as string;
        _mergeType = string.IsNullOrWhiteSpace(type) ? "Letters" : type.Trim();
    }

    private void SelectRecipients()
    {
        _ = EditRecipientsAsync();
    }

    private void EditRecipients()
    {
        _ = EditRecipientsAsync();
    }

    private async Task EditRecipientsAsync()
    {
        if (!_services.TryGet<IMailMergeSourceManager>(out var manager))
        {
            return;
        }

        try
        {
            var snapshot = _session.Document.MailMergeData?.Clone();
            var updated = await manager.EditRecipientsAsync(snapshot);
            if (updated is null)
            {
                return;
            }

            _session.Document.MailMergeData = updated;
            EnsurePreviewEnabled();
            ClampPreviewRecordIndex();
            UpdateMergeFieldResults();
            _session.RefreshLayout();
        }
        catch
        {
            // Swallow to keep mail merge UI failures from crashing the editor.
        }
    }

    private void ToggleHighlightMergeFields()
    {
        _highlightMergeFields = !_highlightMergeFields;
        UpdateMergeFieldResults();
        _session.RefreshLayout();
    }

    private void InsertAddressBlock()
    {
        InsertField("ADDRESSBLOCK", ResolveFieldDisplay(MailMergeFieldKind.AddressBlock, null));
    }

    private void InsertGreetingLine()
    {
        InsertField("GREETINGLINE", ResolveFieldDisplay(MailMergeFieldKind.GreetingLine, null));
    }

    private void InsertMergeField(object? payload)
    {
        var name = payload as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "MergeField";
        }

        name = name.Trim();
        var instruction = $"MERGEFIELD \"{EscapeFieldText(name)}\"";
        InsertField(instruction, ResolveFieldDisplay(MailMergeFieldKind.MergeField, name));
    }

    private void InsertRule(object? payload)
    {
        var rule = payload as string;
        var instruction = rule switch
        {
            "NextRecord" => "NEXT",
            "SkipRecord" => "SKIPIF",
            _ => "IF"
        };

        InsertField(instruction, ResolveFieldDisplay(MailMergeFieldKind.Rule, instruction));
    }

    private void MatchFields()
    {
        // Placeholder for a future recipient field mapping UI.
    }

    private void UpdateLabels()
    {
        if (!TryGetTableAtCaret(out var table))
        {
            return;
        }

        if (table.Rows.Count == 0 || table.Rows[0].Cells.Count == 0)
        {
            return;
        }

        var templateTexts = ExtractCellParagraphTexts(table.Rows[0].Cells[0]);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                if (rowIndex == 0 && columnIndex == 0)
                {
                    continue;
                }

                var cell = row.Cells[columnIndex];
                cell.Paragraphs.Clear();
                foreach (var text in templateTexts)
                {
                    cell.Paragraphs.Add(new ParagraphBlock(text));
                }
            }
        }

        _session.RefreshLayout();
    }

    private void TogglePreviewResults()
    {
        _previewResults = !_previewResults;
        if (_previewResults)
        {
            EnsurePreviewEnabled();
        }

        UpdateMergeFieldResults();
        _session.RefreshLayout();
    }

    private void SetPreviewRecord(int recordIndex)
    {
        EnsurePreviewEnabled();
        var maxIndex = GetPreviewRecordCount() - 1;
        _previewRecordIndex = Math.Clamp(recordIndex, 0, Math.Max(0, maxIndex));
        UpdateMergeFieldResults();
        _session.RefreshLayout();
    }

    private void SetPreviewRecordToLast()
    {
        EnsurePreviewEnabled();
        var maxIndex = GetPreviewRecordCount() - 1;
        _previewRecordIndex = Math.Clamp(maxIndex, 0, Math.Max(0, maxIndex));
        UpdateMergeFieldResults();
        _session.RefreshLayout();
    }

    private void MovePreviewRecord(int delta)
    {
        EnsurePreviewEnabled();
        var maxIndex = GetPreviewRecordCount() - 1;
        _previewRecordIndex = Math.Clamp(_previewRecordIndex + delta, 0, Math.Max(0, maxIndex));
        UpdateMergeFieldResults();
        _session.RefreshLayout();
    }

    private void FindRecipient()
    {
        SetPreviewRecord(0);
    }

    private void CheckErrors()
    {
        // Placeholder for mail merge validation.
    }

    private void FinishAndMerge(object? payload)
    {
        var mode = payload as string;
        if (string.Equals(mode, "PrintDocuments", StringComparison.OrdinalIgnoreCase))
        {
            _ = _router.ExecuteAsync(EditorReferencesCommandIds.Fields.UpdateAll, recordHistory: false).GetAwaiter().GetResult();
        }

        var message = string.IsNullOrWhiteSpace(mode)
            ? "Finish and Merge"
            : string.Format(CultureInfo.InvariantCulture, "Finish and Merge: {0}", mode);
        _session.InsertParagraphBreak();
        _session.InsertText(message.AsSpan());
        _session.RefreshLayout();
    }

    private void EnsurePreviewEnabled()
    {
        if (_previewResults)
        {
            ClampPreviewRecordIndex();
            return;
        }

        _previewResults = true;
        _previewRecordIndex = 0;
    }

    private void ClampPreviewRecordIndex()
    {
        var maxIndex = GetPreviewRecordCount() - 1;
        _previewRecordIndex = Math.Clamp(_previewRecordIndex, 0, Math.Max(0, maxIndex));
    }

    private int GetPreviewRecordCount()
    {
        var data = _session.Document.MailMergeData;
        var count = data?.Records.Count ?? 0;
        return Math.Max(1, count);
    }

    private void InsertField(string instruction, string displayText)
    {
        var startInline = new FieldStartInline(instruction)
        {
            Definition = FieldInstructionParser.Parse(instruction),
            IsDirty = true
        };

        var inlines = new Inline[]
        {
            startInline,
            new FieldSeparatorInline(),
            BuildResultRun(displayText),
            new FieldEndInline()
        };

        _session.InsertInlines(inlines);
    }

    private RunInline BuildResultRun(string text)
    {
        if (!_highlightMergeFields)
        {
            return new RunInline(text);
        }

        var style = new TextStyleProperties
        {
            HighlightColor = MergeFieldHighlightColor
        };
        return new RunInline(text, style);
    }

    private void UpdateMergeFieldResults()
    {
        foreach (var paragraph in EnumerateAllParagraphs(_session.Document))
        {
            var inlines = paragraph.Inlines;
            if (inlines.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < inlines.Count; i++)
            {
                if (inlines[i] is not FieldStartInline start)
                {
                    continue;
                }

                if (!TryGetMailMergeField(start, out var kind, out var name))
                {
                    continue;
                }

                if (!TryFindFieldRange(inlines, i + 1, out var separatorIndex, out var endIndex))
                {
                    continue;
                }

                var displayText = ResolveFieldDisplay(kind, name);
                inlines.RemoveRange(separatorIndex + 1, endIndex - separatorIndex - 1);
                inlines.Insert(separatorIndex + 1, BuildResultRun(displayText));
                i = separatorIndex + 2;
            }

            paragraph.Text = DocumentEditHelpers.GetParagraphText(paragraph);
        }
    }

    private static bool TryGetMailMergeField(FieldStartInline start, out MailMergeFieldKind kind, out string? name)
    {
        name = null;
        kind = default;
        start.Definition ??= FieldInstructionParser.Parse(start.Instruction);
        var definition = start.Definition;
        if (definition is null)
        {
            return false;
        }

        if (definition.Name.Equals("MERGEFIELD", StringComparison.OrdinalIgnoreCase))
        {
            kind = MailMergeFieldKind.MergeField;
            if (definition.Arguments.Count > 0)
            {
                name = definition.Arguments[0].Value;
            }

            return true;
        }

        if (definition.Name.Equals("ADDRESSBLOCK", StringComparison.OrdinalIgnoreCase))
        {
            kind = MailMergeFieldKind.AddressBlock;
            return true;
        }

        if (definition.Name.Equals("GREETINGLINE", StringComparison.OrdinalIgnoreCase))
        {
            kind = MailMergeFieldKind.GreetingLine;
            return true;
        }

        if (definition.Name.Equals("IF", StringComparison.OrdinalIgnoreCase)
            || definition.Name.Equals("NEXT", StringComparison.OrdinalIgnoreCase)
            || definition.Name.Equals("SKIPIF", StringComparison.OrdinalIgnoreCase))
        {
            kind = MailMergeFieldKind.Rule;
            name = definition.Name;
            return true;
        }

        return false;
    }

    private static bool TryFindFieldRange(
        IReadOnlyList<Inline> inlines,
        int startIndex,
        out int separatorIndex,
        out int endIndex)
    {
        separatorIndex = -1;
        endIndex = -1;
        for (var i = Math.Max(0, startIndex); i < inlines.Count; i++)
        {
            if (inlines[i] is FieldSeparatorInline && separatorIndex < 0)
            {
                separatorIndex = i;
                continue;
            }

            if (inlines[i] is FieldEndInline)
            {
                endIndex = i;
                break;
            }
        }

        return separatorIndex >= 0 && endIndex > separatorIndex;
    }

    private string ResolveFieldDisplay(MailMergeFieldKind kind, string? fieldName)
    {
        if (_previewResults)
        {
            return kind switch
            {
                MailMergeFieldKind.MergeField => ResolvePreviewValue(fieldName),
                MailMergeFieldKind.AddressBlock => ResolveAddressBlock(),
                MailMergeFieldKind.GreetingLine => ResolveGreetingLine(),
                MailMergeFieldKind.Rule => "<<Rule>>",
                _ => "<<MergeField>>"
            };
        }

        return kind switch
        {
            MailMergeFieldKind.MergeField => BuildPlaceholder(fieldName),
            MailMergeFieldKind.AddressBlock => "<<Address Block>>",
            MailMergeFieldKind.GreetingLine => "<<Greeting Line>>",
            MailMergeFieldKind.Rule => "<<Rule>>",
            _ => "<<MergeField>>"
        };
    }

    private string ResolvePreviewValue(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return "<<MergeField>>";
        }

        if (TryGetPreviewRecord(out var record))
        {
            return record.TryGetValue(fieldName, out var value) ? value ?? string.Empty : string.Empty;
        }

        return BuildPlaceholder(fieldName);
    }

    private string ResolveAddressBlock()
    {
        if (!TryGetPreviewRecord(out var record))
        {
            return "<<Address Block>>";
        }

        var firstName = ResolveRecordValue(record, "FirstName", "First Name");
        var lastName = ResolveRecordValue(record, "LastName", "Last Name");
        var company = ResolveRecordValue(record, "Company");
        var address = ResolveRecordValue(record, "Address");
        var city = ResolveRecordValue(record, "City");
        var state = ResolveRecordValue(record, "State");
        var zip = ResolveRecordValue(record, "Zip", "PostalCode", "Postal Code");
        var country = ResolveRecordValue(record, "Country");

        var name = JoinNonEmpty(" ", firstName, lastName);
        var line1 = JoinNonEmpty(", ", company, address);
        var line2 = JoinNonEmpty(" ", city, state, zip);
        var line3 = JoinNonEmpty(", ", line2, country);

        return JoinNonEmpty(", ", name, line1, line3);
    }

    private string ResolveGreetingLine()
    {
        if (!TryGetPreviewRecord(out var record))
        {
            return "<<Greeting Line>>";
        }

        var title = ResolveRecordValue(record, "Title");
        var firstName = ResolveRecordValue(record, "FirstName", "First Name");
        var lastName = ResolveRecordValue(record, "LastName", "Last Name");
        var name = JoinNonEmpty(" ", title, firstName, lastName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Dear,";
        }

        return string.Format(CultureInfo.InvariantCulture, "Dear {0},", name);
    }

    private bool TryGetPreviewRecord(out MailMergeRecord record)
    {
        record = null!;
        var data = _session.Document.MailMergeData;
        if (data is null || data.Records.Count == 0)
        {
            return false;
        }

        var index = Math.Clamp(_previewRecordIndex, 0, data.Records.Count - 1);
        record = data.Records[index];
        return true;
    }

    private static string ResolveRecordValue(MailMergeRecord record, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (record.TryGetValue(key, out var value))
            {
                return value ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string JoinNonEmpty(string separator, params string[] values)
    {
        if (values.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append(value.Trim());
        }

        return builder.ToString();
    }

    private static string BuildPlaceholder(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return "<<MergeField>>";
        }

        return string.Format(CultureInfo.InvariantCulture, "<<{0}>>", fieldName.Trim());
    }

    private static string EscapeFieldText(string value)
    {
        return value.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    private static IEnumerable<ParagraphBlock> EnumerateAllParagraphs(Document document)
    {
        foreach (var paragraph in EnumerateParagraphs(document.Blocks))
        {
            yield return paragraph;
        }

        foreach (var paragraph in EnumerateParagraphs(document.Header.Blocks))
        {
            yield return paragraph;
        }

        foreach (var paragraph in EnumerateParagraphs(document.Footer.Blocks))
        {
            yield return paragraph;
        }

        foreach (var paragraph in EnumerateParagraphs(document.FirstHeader.Blocks))
        {
            yield return paragraph;
        }

        foreach (var paragraph in EnumerateParagraphs(document.FirstFooter.Blocks))
        {
            yield return paragraph;
        }

        foreach (var paragraph in EnumerateParagraphs(document.EvenHeader.Blocks))
        {
            yield return paragraph;
        }

        foreach (var paragraph in EnumerateParagraphs(document.EvenFooter.Blocks))
        {
            yield return paragraph;
        }
    }

    private static IEnumerable<ParagraphBlock> EnumerateParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    yield return paragraph;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                yield return paragraph;
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static string[] ExtractCellParagraphTexts(TableCell cell)
    {
        if (cell.Paragraphs.Count == 0)
        {
            return Array.Empty<string>();
        }

        var texts = new string[cell.Paragraphs.Count];
        for (var i = 0; i < cell.Paragraphs.Count; i++)
        {
            texts[i] = DocumentEditHelpers.GetParagraphText(cell.Paragraphs[i]);
        }

        return texts;
    }

    private bool TryGetTableAtCaret(out TableBlock table)
    {
        table = null!;
        if (_session.Document.ParagraphCount == 0)
        {
            return false;
        }

        var paragraphIndex = Math.Clamp(_session.Caret.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var location = _session.Document.GetParagraphLocation(paragraphIndex);
        if (!location.IsInTable || location.Table is null)
        {
            return false;
        }

        table = location.Table;
        return true;
    }

    private void ApplyToSections(Action<SectionProperties> action)
    {
        action(_session.Document.SectionProperties);
        foreach (var section in _session.Document.Sections)
        {
            action(section.Properties);
        }
    }
}
