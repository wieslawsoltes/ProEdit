using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using Vibe.Office.FlowDocument;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.FlowDocument.App.ViewModels;

public sealed class FlowDocumentSampleViewModel : ReactiveObject
{
    private double _zoomFactor = 1d;

    public FlowDocumentSampleViewModel()
    {
        Document = BuildSampleDocument();
    }

    public FlowDocumentModel Document { get; }

    public double ZoomFactor
    {
        get => _zoomFactor;
        set => this.RaiseAndSetIfChanged(ref _zoomFactor, value);
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
        var item1 = new ListItem();
        item1.Blocks.Add(new Paragraph("Numbered list item"));
        list.ListItems.Add(item1);
        var item2 = new ListItem();
        item2.Blocks.Add(new Paragraph("Nested bullets"));
        var nestedList = new List { MarkerStyle = FlowListMarkerStyle.Disc };
        var nestedItem = new ListItem();
        nestedItem.Blocks.Add(new Paragraph("Bullet item"));
        nestedList.ListItems.Add(nestedItem);
        item2.Blocks.Add(nestedList);
        list.ListItems.Add(item2);
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
