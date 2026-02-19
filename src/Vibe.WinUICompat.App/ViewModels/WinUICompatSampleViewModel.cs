using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vibe.Office.FlowDocument.IO;
using Vibe.Office.WinUICompat.Converters;
using Vibe.Office.WinUICompat.Documents;
using Vibe.Office.WinUICompat.Text;
using Vibe.WinUICompat.App.Services;
using CompatThickness = Vibe.Office.WinUICompat.Documents.Thickness;
namespace Vibe.WinUICompat.App.ViewModels;

public sealed class WinUICompatSampleViewModel : INotifyPropertyChanged
{
    private readonly IFlowDocumentFileConversionService _conversionService;
    private readonly IFlowDocumentFilePickerService _pickerService;
    private readonly CompatFlowDocumentConverter _converter = new();
    private readonly RichTextDocument _xamlSampleDocument;
    private readonly RichTextDocument _codeSampleDocument;
    private readonly RichTextDocument _editableSampleDocument;

    private RichEditTextDocument _editableDocument;
    private string _inputText;
    private string _statusMessage = "Ready";
    private string? _editableDocumentPath;
    private bool _isProofingEnabled = true;
    private bool _isSpellingEnabled = true;
    private bool _isGrammarEnabled;
    private bool _isStyleEnabled;

    public WinUICompatSampleViewModel(
        IFlowDocumentFileConversionService conversionService,
        IFlowDocumentFilePickerService pickerService)
    {
        _conversionService = conversionService ?? throw new ArgumentNullException(nameof(conversionService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));

        _xamlSampleDocument = BuildXamlSampleDocument();
        _codeSampleDocument = BuildCodeSampleDocument();
        _editableSampleDocument = BuildEditableDocument();
        _editableDocument = CreateDocument(CloneCompatDocument(_editableSampleDocument));
        _inputText = _editableDocument.GetText();

        OpenRichTextDocumentCommand = new AsyncUiCommand(OpenRichTextDocumentAsync);
        SaveRichTextDocumentCommand = new AsyncUiCommand(SaveRichTextDocumentAsync);
        ApplyTextCommand = new DelegateCommand(ApplyText);
        ResetDocumentCommand = new DelegateCommand(ResetDocument);
        UndoCommand = new DelegateCommand(Undo);
        RedoCommand = new DelegateCommand(Redo);
        SelectAllCommand = new DelegateCommand(SelectAll);
        LoadSampleContentCommand = new DelegateCommand(LoadSampleContent);
        ToggleBoldCommand = new DelegateCommand(ToggleBold);
        ToggleItalicCommand = new DelegateCommand(ToggleItalic);
        ToggleUnderlineCommand = new DelegateCommand(ToggleUnderline);
        ToggleBulletedListCommand = new DelegateCommand(ToggleBulletedList);
        ToggleNumberedListCommand = new DelegateCommand(ToggleNumberedList);
        InsertTableCommand = new DelegateCommand(InsertTable);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RichEditTextDocument EditableDocument
    {
        get => _editableDocument;
        private set
        {
            if (ReferenceEquals(_editableDocument, value))
            {
                return;
            }

            _editableDocument = value;
            OnPropertyChanged();
        }
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (string.Equals(_inputText, value, StringComparison.Ordinal))
            {
                return;
            }

            _inputText = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string CurrentDocumentPath => string.IsNullOrWhiteSpace(_editableDocumentPath) ? "(unsaved)" : _editableDocumentPath;

    public bool IsProofingEnabled
    {
        get => _isProofingEnabled;
        set
        {
            if (_isProofingEnabled == value)
            {
                return;
            }

            _isProofingEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsSpellingEnabled
    {
        get => _isSpellingEnabled;
        set
        {
            if (_isSpellingEnabled == value)
            {
                return;
            }

            _isSpellingEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsGrammarEnabled
    {
        get => _isGrammarEnabled;
        set
        {
            if (_isGrammarEnabled == value)
            {
                return;
            }

            _isGrammarEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsStyleEnabled
    {
        get => _isStyleEnabled;
        set
        {
            if (_isStyleEnabled == value)
            {
                return;
            }

            _isStyleEnabled = value;
            OnPropertyChanged();
        }
    }

    public RichTextDocument XamlSampleDocument => _xamlSampleDocument;

    public RichTextDocument CodeSampleDocument => _codeSampleDocument;

    public ICommand OpenRichTextDocumentCommand { get; }

    public ICommand SaveRichTextDocumentCommand { get; }

    public ICommand ApplyTextCommand { get; }

    public ICommand ResetDocumentCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand LoadSampleContentCommand { get; }

    public ICommand ToggleBoldCommand { get; }

    public ICommand ToggleItalicCommand { get; }

    public ICommand ToggleUnderlineCommand { get; }

    public ICommand ToggleBulletedListCommand { get; }

    public ICommand ToggleNumberedListCommand { get; }

    public ICommand InsertTableCommand { get; }

    private async Task OpenRichTextDocumentAsync()
    {
        var path = await _pickerService.PickOpenPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Open canceled.";
            return;
        }

        try
        {
            StatusMessage = $"Loading {Path.GetFileName(path)}...";
            var loaded = await _conversionService.LoadAsync(path);
            var compat = _converter.FromFlowDocument(loaded);

            var document = new RichEditTextDocument();
            document.SetDocument(compat);
            EditableDocument = document;
            InputText = EditableDocument.GetText();
            _editableDocumentPath = path;
            OnPropertyChanged(nameof(CurrentDocumentPath));
            StatusMessage = $"Loaded {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Open failed: {ex.Message}";
        }
    }

    private async Task SaveRichTextDocumentAsync()
    {
        var suggestedName = string.IsNullOrWhiteSpace(_editableDocumentPath)
            ? "flow-document"
            : Path.GetFileNameWithoutExtension(_editableDocumentPath);

        var path = await _pickerService.PickSavePathAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Save canceled.";
            return;
        }

        path = EnsureKnownExtension(path);

        try
        {
            StatusMessage = $"Saving {Path.GetFileName(path)}...";
            var flow = _converter.ToFlowDocument(CloneCompatDocument(EditableDocument.Document));
            await _conversionService.SaveAsync(flow, path);

            _editableDocumentPath = path;
            OnPropertyChanged(nameof(CurrentDocumentPath));
            StatusMessage = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private void ApplyText()
    {
        EditableDocument.SetText(InputText);
        StatusMessage = $"Applied {EditableDocument.GetText().Length} chars.";
    }

    private void ResetDocument()
    {
        _editableDocumentPath = null;
        OnPropertyChanged(nameof(CurrentDocumentPath));

        EditableDocument = CreateDocument(CloneCompatDocument(_editableSampleDocument));
        InputText = EditableDocument.GetText();
        StatusMessage = "Sample content restored.";
    }

    private void Undo()
    {
        if (EditableDocument.Undo())
        {
            SyncInputFromDocument();
            StatusMessage = $"Undo -> {EditableDocument.GetText().Length} chars.";
            return;
        }

        StatusMessage = "Nothing to undo.";
    }

    private void Redo()
    {
        if (EditableDocument.Redo())
        {
            SyncInputFromDocument();
            StatusMessage = $"Redo -> {EditableDocument.GetText().Length} chars.";
            return;
        }

        StatusMessage = "Nothing to redo.";
    }

    private void SelectAll()
    {
        var text = EditableDocument.GetText();
        EditableDocument.SetSelection(0, text.Length);
        StatusMessage = $"Selected {text.Length} chars.";
    }

    private void LoadSampleContent()
    {
        var sample = CloneCompatDocument(_codeSampleDocument);
        EditableDocument.SetDocument(sample);
        InputText = EditableDocument.GetText();
        StatusMessage = "Loaded rich code sample content into editor.";
    }

    private void ToggleBold()
    {
        if (EditableDocument.ToggleBold())
        {
            SyncInputFromDocument();
            StatusMessage = "Bold toggled.";
        }
    }

    private void ToggleItalic()
    {
        if (EditableDocument.ToggleItalic())
        {
            SyncInputFromDocument();
            StatusMessage = "Italic toggled.";
        }
    }

    private void ToggleUnderline()
    {
        if (EditableDocument.ToggleUnderline())
        {
            SyncInputFromDocument();
            StatusMessage = "Underline toggled.";
        }
    }

    private void ToggleBulletedList()
    {
        if (EditableDocument.ToggleBulletedList())
        {
            SyncInputFromDocument();
            StatusMessage = "Bulleted list toggled.";
        }
    }

    private void ToggleNumberedList()
    {
        if (EditableDocument.ToggleNumberedList())
        {
            SyncInputFromDocument();
            StatusMessage = "Numbered list toggled.";
        }
    }

    private void InsertTable()
    {
        if (EditableDocument.InsertTable(2, 3))
        {
            SyncInputFromDocument();
            StatusMessage = "Inserted 2x3 table.";
        }
    }

    private void SyncInputFromDocument()
    {
        InputText = EditableDocument.GetText();
    }

    private static RichEditTextDocument CreateDocument(string text)
    {
        var document = new RichEditTextDocument();
        document.SetText(text);
        return document;
    }

    private static RichEditTextDocument CreateDocument(RichTextDocument source)
    {
        var document = new RichEditTextDocument();
        document.SetDocument(source);
        return document;
    }

    private RichTextDocument CloneCompatDocument(RichTextDocument source)
    {
        return _converter.FromFlowDocument(_converter.ToFlowDocument(source));
    }

    private static string EnsureKnownExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return path + ".docx";
        }

        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pdx", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path + ".docx";
    }

    private static string ConvertCompatToPlainText(RichTextDocument document)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < document.Blocks.Count; i++)
        {
            AppendBlock(document.Blocks[i], builder);
            if (i < document.Blocks.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void AppendBlock(Block block, StringBuilder builder)
    {
        switch (block)
        {
            case Section section:
                for (var i = 0; i < section.Blocks.Count; i++)
                {
                    AppendBlock(section.Blocks[i], builder);
                    if (i < section.Blocks.Count - 1)
                    {
                        builder.AppendLine();
                    }
                }

                break;
            case Paragraph paragraph:
                AppendParagraph(paragraph, builder);
                break;
            case List list:
                AppendList(list, builder);
                break;
            case Table table:
                AppendTable(table, builder);
                break;
            case BlockUIContainer:
                builder.Append("[Block UI]");
                break;
            default:
                break;
        }
    }

    private static void AppendParagraph(Paragraph paragraph, StringBuilder builder)
    {
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            AppendInline(paragraph.Inlines[i], builder);
        }
    }

    private static void AppendInline(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case Run run:
                builder.Append(run.Text);
                break;
            case Hyperlink hyperlink:
                for (var i = 0; i < hyperlink.Inlines.Count; i++)
                {
                    AppendInline(hyperlink.Inlines[i], builder);
                }

                if (!string.IsNullOrWhiteSpace(hyperlink.NavigateUri))
                {
                    builder.Append(" (").Append(hyperlink.NavigateUri).Append(')');
                }

                break;
            case Span span:
                for (var i = 0; i < span.Inlines.Count; i++)
                {
                    AppendInline(span.Inlines[i], builder);
                }

                break;
            case InlineUIContainer:
                builder.Append("[Inline UI]");
                break;
            case Figure figure:
                builder.Append("[Figure ");
                for (var i = 0; i < figure.Blocks.Count; i++)
                {
                    AppendBlock(figure.Blocks[i], builder);
                    if (i < figure.Blocks.Count - 1)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(']');
                break;
            case Floater floater:
                builder.Append("[Floater ");
                for (var i = 0; i < floater.Blocks.Count; i++)
                {
                    AppendBlock(floater.Blocks[i], builder);
                    if (i < floater.Blocks.Count - 1)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(']');
                break;
            case LineBreak:
                builder.AppendLine();
                break;
            default:
                break;
        }
    }

    private static void AppendList(List list, StringBuilder builder)
    {
        var start = list.StartIndex.GetValueOrDefault(1);
        for (var i = 0; i < list.ListItems.Count; i++)
        {
            var marker = $"{Math.Max(1, start + i)}. ";
            builder.Append(marker);

            var item = list.ListItems[i];
            for (var blockIndex = 0; blockIndex < item.Blocks.Count; blockIndex++)
            {
                AppendBlock(item.Blocks[blockIndex], builder);
                if (blockIndex < item.Blocks.Count - 1)
                {
                    builder.Append(' ');
                }
            }

            if (i < list.ListItems.Count - 1)
            {
                builder.AppendLine();
            }
        }
    }

    private static void AppendTable(Table table, StringBuilder builder)
    {
        for (var i = 0; i < table.RowGroups.Count; i++)
        {
            var rowGroup = table.RowGroups[i];
            for (var rowIndex = 0; rowIndex < rowGroup.Rows.Count; rowIndex++)
            {
                var row = rowGroup.Rows[rowIndex];
                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    if (cellIndex > 0)
                    {
                        builder.Append(" | ");
                    }

                    var cell = row.Cells[cellIndex];
                    for (var blockIndex = 0; blockIndex < cell.Blocks.Count; blockIndex++)
                    {
                        AppendBlock(cell.Blocks[blockIndex], builder);
                        if (blockIndex < cell.Blocks.Count - 1)
                        {
                            builder.Append(' ');
                        }
                    }
                }

                if (rowIndex < rowGroup.Rows.Count - 1 || i < table.RowGroups.Count - 1)
                {
                    builder.AppendLine();
                }
            }
        }
    }

    private static string BuildEditableText()
    {
        return string.Join(
            Environment.NewLine,
            "RichTextBox Smoke Sample",
            string.Empty,
            "Use Open... to load .docx, .md, .pdf/.pdx content.",
            "Use Save As... to export through shared FlowDocument conversion.",
            string.Empty,
            "Type here and press Apply to overwrite the current document.",
            "Undo/Redo uses the same RichEditTextDocument history as the control.");
    }

    private static RichTextDocument BuildEditableDocument()
    {
        var document = new RichTextDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new CompatThickness(72, 72, 72, 72),
            ColumnWidth = 360,
            ColumnGap = 24
        };

        var title = new Paragraph
        {
            TextAlignment = "Center",
            Margin = new CompatThickness(0, 0, 0, 8)
        };
        var boldTitle = new Bold();
        boldTitle.Inlines.Add(new Run("RichTextBox Smoke Sample"));
        title.Inlines.Add(boldTitle);
        document.Blocks.Add(title);

        var intro = new Paragraph
        {
            Margin = new CompatThickness(0, 0, 0, 8)
        };
        intro.Inlines.Add(new Run("Use Open to load "));
        var openFormats = new Bold();
        openFormats.Inlines.Add(new Run(".docx, .md, .pdf/.pdx"));
        intro.Inlines.Add(openFormats);
        intro.Inlines.Add(new Run(" files and Save As to export through the shared conversion pipeline."));
        document.Blocks.Add(intro);

        var inlineUiParagraph = new Paragraph();
        inlineUiParagraph.Inlines.Add(new Run("Inline UI sample: "));
        inlineUiParagraph.Inlines.Add(new InlineUIContainer
        {
            Child = new Button
            {
                Content = "Inline Action",
                MinWidth = 110,
                Height = 28
            }
        });
        inlineUiParagraph.Inlines.Add(new Run(" keeps layout and remains clickable."));
        document.Blocks.Add(inlineUiParagraph);

        var blockUiHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        blockUiHost.Children.Add(new Button
        {
            Content = "Primary",
            MinWidth = 90,
            Height = 32
        });

        var modePicker = new ComboBox
        {
            MinWidth = 130,
            Height = 32
        };
        modePicker.Items.Add("Review");
        modePicker.Items.Add("Edit");
        modePicker.Items.Add("Comment");
        modePicker.SelectedIndex = 0;
        blockUiHost.Children.Add(modePicker);

        document.Blocks.Add(new BlockUIContainer
        {
            Child = blockUiHost
        });

        var list = new List
        {
            MarkerStyle = "Disc",
            Margin = new CompatThickness(0, 8, 0, 0)
        };
        list.ListItems.Add(CreateListItem("Type to modify the document."));
        list.ListItems.Add(CreateListItem("Select text with pointer or keyboard."));
        list.ListItems.Add(CreateListItem("Undo/redo uses the same editor infrastructure."));
        document.Blocks.Add(list);

        return document;
    }

    private static RichTextDocument BuildXamlSampleDocument()
    {
        var document = new RichTextDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new CompatThickness(72, 72, 72, 72),
            ColumnWidth = 360,
            ColumnGap = 32,
            ColumnRuleWidth = 1,
            ColumnRuleBrush = "#D2D8E8"
        };

        var title = new Paragraph
        {
            TextAlignment = "Center",
            Margin = new CompatThickness(0, 0, 0, 8)
        };
        var titleBold = new Bold();
        titleBold.Inlines.Add(new Run("FlowDocument Rich Sample"));
        title.Inlines.Add(titleBold);
        document.Blocks.Add(title);

        var intro = new Paragraph();
        intro.Inlines.Add(new Run("This sample demonstrates "));
        var bold = new Bold();
        bold.Inlines.Add(new Run("inline formatting"));
        intro.Inlines.Add(bold);
        intro.Inlines.Add(new Run(", "));
        var italic = new Italic();
        italic.Inlines.Add(new Run("text styling"));
        intro.Inlines.Add(italic);
        intro.Inlines.Add(new Run(", hyperlinks, floating content, table row spans, and UI containers."));
        document.Blocks.Add(intro);

        var decorated = new Paragraph();
        var underlined = new Underline();
        underlined.Inlines.Add(new Run("Decorated text"));
        decorated.Inlines.Add(underlined);
        decorated.Inlines.Add(new Run(" and a "));
        var hyperlink = new Hyperlink
        {
            NavigateUri = "https://learn.microsoft.com/dotnet/desktop/wpf/advanced/flow-document-overview"
        };
        hyperlink.Inlines.Add(new Run("FlowDocument reference link"));
        decorated.Inlines.Add(hyperlink);
        decorated.Inlines.Add(new Run(" inside the same paragraph."));
        document.Blocks.Add(decorated);

        var inlineUi = new Paragraph();
        inlineUi.Inlines.Add(new Run("Inline UI: "));
        inlineUi.Inlines.Add(new InlineUIContainer
        {
            Child = new Button
            {
                Content = "Inline Chip",
                MinWidth = 92,
                Height = 28
            }
        });
        inlineUi.Inlines.Add(new Run(" followed by regular text."));
        document.Blocks.Add(inlineUi);

        var floating = new Paragraph();

        var figure = new Figure
        {
            Width = 240,
            Height = 120,
            HorizontalAnchor = "PageLeft",
            Background = "#F8FAFF",
            BorderBrush = "#AAB4CC",
            BorderThickness = new CompatThickness(1),
            Padding = new CompatThickness(6)
        };
        figure.Blocks.Add(new Paragraph("Figure: anchored block content rendered through floating conversion."));
        floating.Inlines.Add(figure);
        floating.Inlines.Add(new Run(" "));

        var floater = new Floater
        {
            Width = 220,
            HorizontalAlignment = "Right",
            Background = "#F4F8FF",
            BorderBrush = "#AAB4CC",
            BorderThickness = new CompatThickness(1),
            Padding = new CompatThickness(6)
        };
        floater.Blocks.Add(new Paragraph("Floater: secondary floating block sample."));
        floating.Inlines.Add(floater);
        floating.Inlines.Add(new Run(" Main body text wraps around floating content."));
        document.Blocks.Add(floating);

        document.Blocks.Add(new BlockUIContainer
        {
            Child = CreateReadOnlyBlockUiHost("Action", "Preview", "Inspect")
        });

        var list = new List
        {
            MarkerStyle = "UpperRoman",
            StartIndex = 3,
            Margin = new CompatThickness(0, 8, 0, 8)
        };
        list.ListItems.Add(CreateListItem("List item one"));
        list.ListItems.Add(CreateListItem("List item two"));
        list.ListItems.Add(CreateListItem("List item three"));
        document.Blocks.Add(list);

        var tableTitle = new Paragraph();
        var tableTitleBold = new Bold();
        tableTitleBold.Inlines.Add(new Run("Row-span table sample"));
        tableTitle.Inlines.Add(tableTitleBold);
        document.Blocks.Add(tableTitle);

        var table = new Table { CellSpacing = 3 };
        table.Columns.Add(new TableColumn { Width = 170 });
        table.Columns.Add(new TableColumn { Width = 170 });
        table.Columns.Add(new TableColumn { Width = 170 });

        var group = new TableRowGroup();

        var row1 = new TableRow();
        row1.Cells.Add(CreateTableCell("Group A (row span 2)", rowSpan: 2, highlighted: true));
        row1.Cells.Add(CreateTableCell("Q1"));
        row1.Cells.Add(CreateTableCell("120"));
        group.Rows.Add(row1);

        var row2 = new TableRow();
        row2.Cells.Add(CreateTableCell("Q2"));
        row2.Cells.Add(CreateTableCell("145"));
        group.Rows.Add(row2);

        var row3 = new TableRow();
        row3.Cells.Add(CreateTableCell("Group B", highlighted: true));
        row3.Cells.Add(CreateTableCell("Q3"));
        row3.Cells.Add(CreateTableCell("132"));
        group.Rows.Add(row3);

        table.RowGroups.Add(group);
        document.Blocks.Add(table);

        return document;
    }

    private static RichTextDocument BuildCodeSampleDocument()
    {
        var document = new RichTextDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new CompatThickness(72, 72, 72, 72),
            ColumnWidth = 360,
            ColumnGap = 24
        };

        var intro = new Paragraph();
        intro.Inlines.Add(new Run("This sample mirrors the Avalonia app with "));
        var bold = new Bold();
        bold.Inlines.Add(new Run("custom WinUICompat"));
        intro.Inlines.Add(bold);
        intro.Inlines.Add(new Run(" APIs and "));
        var italic = new Italic();
        italic.Inlines.Add(new Run("Uno"));
        intro.Inlines.Add(italic);
        intro.Inlines.Add(new Run(" hosting."));
        document.Blocks.Add(intro);

        var linkParagraph = new Paragraph();
        var link = new Hyperlink { NavigateUri = "https://example.com" };
        link.Inlines.Add(new Run("Inline hyperlink"));
        linkParagraph.Inlines.Add(link);
        linkParagraph.Inlines.Add(new Run(" and embedded inline UI: "));
        linkParagraph.Inlines.Add(new InlineUIContainer
        {
            Child = new Button
            {
                Content = "Inline UI",
                MinWidth = 84,
                Height = 28
            }
        });
        document.Blocks.Add(linkParagraph);

        var list = new List { MarkerStyle = "Decimal", StartIndex = 1 };
        list.ListItems.Add(CreateListItem("Numbered list item"));

        var nestedItem = new ListItem();
        nestedItem.Blocks.Add(new Paragraph("Nested bullets"));
        var nested = new List { MarkerStyle = "Disc" };
        nested.ListItems.Add(CreateListItem("Bullet item"));
        nestedItem.Blocks.Add(nested);
        list.ListItems.Add(nestedItem);
        document.Blocks.Add(list);

        var table = new Table();
        var tableGroup = new TableRowGroup();
        var tableRow1 = new TableRow();
        tableRow1.Cells.Add(CreateTableCell("Row span 2", rowSpan: 2));
        tableRow1.Cells.Add(CreateTableCell("Row 1, Col 2"));
        tableGroup.Rows.Add(tableRow1);

        var tableRow2 = new TableRow();
        tableRow2.Cells.Add(CreateTableCell("Row 2, Col 2"));
        tableGroup.Rows.Add(tableRow2);
        table.RowGroups.Add(tableGroup);
        document.Blocks.Add(table);

        var floating = new Paragraph();
        var figure = new Figure
        {
            Width = 220,
            Height = 96,
            HorizontalAnchor = "PageLeft",
            Background = "#F8FAFF",
            BorderBrush = "#AAB4CC",
            BorderThickness = new CompatThickness(1),
            Padding = new CompatThickness(6)
        };
        figure.Blocks.Add(new Paragraph("Figure content"));
        floating.Inlines.Add(figure);
        floating.Inlines.Add(new Run(" "));

        var floater = new Floater
        {
            Width = 200,
            HorizontalAlignment = "Right",
            Background = "#F4F8FF",
            BorderBrush = "#AAB4CC",
            BorderThickness = new CompatThickness(1),
            Padding = new CompatThickness(6)
        };
        floater.Blocks.Add(new Paragraph("Floater content"));
        floating.Inlines.Add(floater);
        floating.Inlines.Add(new Run(" Inline flow continues around floating objects."));
        document.Blocks.Add(floating);

        document.Blocks.Add(new BlockUIContainer
        {
            Child = CreateReadOnlyBlockUiHost("Action", "Edit", "Review")
        });

        return document;
    }

    private static ListItem CreateListItem(string text)
    {
        var item = new ListItem();
        item.Blocks.Add(new Paragraph(text));
        return item;
    }

    private static UIElement CreateReadOnlyBlockUiHost(string buttonLabel, params string[] modes)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        panel.Children.Add(new Button
        {
            Content = buttonLabel,
            MinWidth = 96,
            Height = 32
        });

        var picker = new ComboBox
        {
            MinWidth = 126,
            Height = 32
        };

        for (var i = 0; i < modes.Length; i++)
        {
            picker.Items.Add(modes[i]);
        }

        if (picker.Items.Count > 0)
        {
            picker.SelectedIndex = 0;
        }

        panel.Children.Add(picker);
        return panel;
    }

    private static TableCell CreateTableCell(string text, int rowSpan = 1, int columnSpan = 1, bool highlighted = false)
    {
        var cell = new TableCell
        {
            RowSpan = Math.Max(1, rowSpan),
            ColumnSpan = Math.Max(1, columnSpan),
            BorderBrush = "#AAB4CC",
            BorderThickness = new CompatThickness(1),
            Padding = new CompatThickness(6, 4, 6, 4),
            Background = highlighted ? "#EEF3FF" : null
        };
        cell.Blocks.Add(new Paragraph(text));
        return cell;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action _execute;

        public DelegateCommand(Action execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler? CanExecuteChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }
    }

    private sealed class AsyncUiCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private bool _isExecuting;

        public AsyncUiCommand(Func<Task> executeAsync)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting;
        }

        public async void Execute(object? parameter)
        {
            if (_isExecuting)
            {
                return;
            }

            _isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await _executeAsync();
            }
            finally
            {
                _isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
