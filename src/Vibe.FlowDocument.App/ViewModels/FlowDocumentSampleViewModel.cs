using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive;
using Vibe.FlowDocument.App.Services;
using Vibe.Office.FlowDocument;
using Vibe.Office.FlowDocument.IO;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.FlowDocument.App.ViewModels;

public sealed class FlowDocumentSampleViewModel : ReactiveObject
{
    private readonly IFlowDocumentFileConversionService _conversionService;
    private readonly IFlowDocumentFilePickerService _pickerService;
    private double _zoomFactor = 1d;
    private FlowDocumentModel _editableDocument;
    private string _statusMessage = "Ready";
    private string? _editableDocumentPath;

    public FlowDocumentSampleViewModel(
        IFlowDocumentFileConversionService conversionService,
        IFlowDocumentFilePickerService pickerService)
    {
        _conversionService = conversionService ?? throw new ArgumentNullException(nameof(conversionService));
        _pickerService = pickerService ?? throw new ArgumentNullException(nameof(pickerService));

        Document = BuildSampleDocument();
        _editableDocument = BuildEditableDocument();

        OpenRichTextDocumentCommand = ReactiveCommand.CreateFromTask(
            OpenRichTextDocumentAsync,
            outputScheduler: RxApp.MainThreadScheduler);
        SaveRichTextDocumentCommand = ReactiveCommand.CreateFromTask(
            SaveRichTextDocumentAsync,
            outputScheduler: RxApp.MainThreadScheduler);
    }

    public FlowDocumentModel Document { get; }

    public FlowDocumentModel EditableDocument
    {
        get => _editableDocument;
        private set => this.RaiseAndSetIfChanged(ref _editableDocument, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> OpenRichTextDocumentCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveRichTextDocumentCommand { get; }

    public double ZoomFactor
    {
        get => _zoomFactor;
        set => this.RaiseAndSetIfChanged(ref _zoomFactor, value);
    }

    private async Task OpenRichTextDocumentAsync()
    {
        var path = await _pickerService.PickOpenPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            StatusMessage = $"Loading {Path.GetFileName(path)}...";
            EditableDocument = await _conversionService.LoadAsync(path);
            _editableDocumentPath = path;
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
            return;
        }

        path = EnsureKnownExtension(path);

        try
        {
            StatusMessage = $"Saving {Path.GetFileName(path)}...";
            await _conversionService.SaveAsync(EditableDocument, path);
            _editableDocumentPath = path;
            StatusMessage = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
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

    private static FlowDocumentModel BuildEditableDocument()
    {
        var document = new FlowDocumentModel
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new FlowThickness(72, 72, 72, 72),
            ColumnWidth = 360,
            ColumnGap = 24
        };

        var title = new Paragraph
        {
            FontSize = 24,
            FontWeight = FlowFontWeight.Bold,
            Margin = new FlowThickness(0, 0, 0, 8)
        };
        title.Inlines.Add(new Run("RichTextBox Smoke Sample"));
        document.Blocks.Add(title);

        var intro = new Paragraph
        {
            Margin = new FlowThickness(0, 0, 0, 8)
        };
        intro.Inlines.Add(new Run("Use Open to load "));
        var openFormats = new Bold();
        openFormats.Inlines.Add(new Run(".docx, .md, .pdf"));
        intro.Inlines.Add(openFormats);
        intro.Inlines.Add(new Run(" files as FlowDocument and Save As to export "));
        var saveFormats = new Bold();
        saveFormats.Inlines.Add(new Run(".docx, .md, .pdf/.pdx"));
        intro.Inlines.Add(saveFormats);
        intro.Inlines.Add(new Run("."));
        document.Blocks.Add(intro);

        var list = new List { MarkerStyle = FlowListMarkerStyle.Disc };
        var item1 = new ListItem();
        item1.Blocks.Add(new Paragraph("Type to modify the document."));
        list.ListItems.Add(item1);
        var item2 = new ListItem();
        item2.Blocks.Add(new Paragraph("Select text with pointer or keyboard."));
        list.ListItems.Add(item2);
        var item3 = new ListItem();
        item3.Blocks.Add(new Paragraph("Use undo/redo shortcuts supported by the editor infrastructure."));
        list.ListItems.Add(item3);
        document.Blocks.Add(list);

        return document;
    }

    private static FlowDocumentModel BuildSampleDocument()
    {
        var document = new FlowDocumentModel
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new FlowThickness(72, 72, 72, 72)
        };

        var intro = new Paragraph();
        intro.Inlines.Add(new Run("This sample renders a "));
        var bold = new Bold();
        bold.Inlines.Add(new Run("FlowDocument"));
        intro.Inlines.Add(bold);
        intro.Inlines.Add(new Run(" with "));
        var italic = new Italic();
        italic.Inlines.Add(new Run("VibeOffice"));
        intro.Inlines.Add(italic);
        intro.Inlines.Add(new Run(" layout + Skia rendering."));
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
                Width = 120,
                Height = 26
            }
        });
        document.Blocks.Add(linkParagraph);

        var list = new List { MarkerStyle = FlowListMarkerStyle.Decimal, StartIndex = 1 };
        var listItem1 = new ListItem();
        listItem1.Blocks.Add(new Paragraph("Numbered list item"));
        list.ListItems.Add(listItem1);
        var listItem2 = new ListItem();
        listItem2.Blocks.Add(new Paragraph("Nested bullets"));
        var nestedList = new List { MarkerStyle = FlowListMarkerStyle.Disc };
        var nestedItem = new ListItem();
        nestedItem.Blocks.Add(new Paragraph("Bullet item"));
        nestedList.ListItems.Add(nestedItem);
        listItem2.Blocks.Add(nestedList);
        list.ListItems.Add(listItem2);
        document.Blocks.Add(list);

        var table = new Table();
        var group = new TableRowGroup();
        var row1 = new TableRow();
        var cell1 = new TableCell { RowSpan = 2 };
        cell1.Blocks.Add(new Paragraph("Row span 2"));
        row1.Cells.Add(cell1);
        var cell2 = new TableCell();
        cell2.Blocks.Add(new Paragraph("Row 1, Col 2"));
        row1.Cells.Add(cell2);
        group.Rows.Add(row1);
        var row2 = new TableRow();
        var cell3 = new TableCell();
        cell3.Blocks.Add(new Paragraph("Row 2, Col 2"));
        row2.Cells.Add(cell3);
        group.Rows.Add(row2);
        table.RowGroups.Add(group);
        document.Blocks.Add(table);

        var anchoredParagraph = new Paragraph();
        anchoredParagraph.Inlines.Add(new Run("Anchored blocks inside inline flow: "));

        var figure = new Figure { Width = 240, Height = 120 };
        figure.Blocks.Add(new Paragraph("Figure content rendered via floating shape text box."));
        anchoredParagraph.Inlines.Add(figure);
        anchoredParagraph.Inlines.Add(new Run("  "));

        var floater = new Floater { Width = 220 };
        floater.Blocks.Add(new Paragraph("Floater content with wrapping."));
        anchoredParagraph.Inlines.Add(floater);
        document.Blocks.Add(anchoredParagraph);

        document.Blocks.Add(new BlockUIContainer
        {
            Child = new Border
            {
                Width = 340,
                Height = 90,
                Padding = new Avalonia.Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(226, 236, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(123, 142, 199)),
                BorderThickness = new Avalonia.Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "Block UI Container", FontWeight = FontWeight.SemiBold },
                        new Button { Content = "Action", Width = 96, Height = 28 }
                    }
                }
            }
        });

        return document;
    }
}
