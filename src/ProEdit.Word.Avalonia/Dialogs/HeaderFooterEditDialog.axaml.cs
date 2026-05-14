using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

public partial class HeaderFooterEditDialog : Window
{
    private readonly DocumentView _editorView;
    private readonly TextBlock _titleBlock;

    public HeaderFooterEditDialog()
        : this("Edit Header/Footer", new Document())
    {
    }

    public HeaderFooterEditDialog(string title, Document document)
    {
        InitializeComponent();
        Title = title;
        _editorView = this.FindControl<DocumentView>("EditorView")!;
        _titleBlock = this.FindControl<TextBlock>("HeaderFooterTitle")!;
        _titleBlock.Text = title;
        _editorView.LoadDocument(document);
        _editorView.Focus();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        var blocks = CloneBlocks(_editorView.Document.Blocks);
        var result = new EditorHeaderFooterUpdateRequest(blocks, _editorView.Document);
        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static List<Block> CloneBlocks(IReadOnlyList<Block> source)
    {
        var result = new List<Block>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            result.Add(DocumentClone.CloneBlock(source[i]));
        }

        return result;
    }
}
