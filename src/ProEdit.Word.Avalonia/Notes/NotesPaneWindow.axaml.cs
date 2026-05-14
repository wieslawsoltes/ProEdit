using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

public partial class NotesPaneWindow : Window
{
    private readonly ObservableCollection<NoteEntry> _footnotes = new();
    private readonly ObservableCollection<NoteEntry> _endnotes = new();
    private readonly Dictionary<int, TextPosition> _footnoteAnchors = new();
    private readonly Dictionary<int, TextPosition> _endnoteAnchors = new();
    private readonly DispatcherTimer _refreshTimer;

    private DocumentView? _view;
    private Document? _document;
    private NoteEntry? _currentNote;
    private bool _suppressEditorChange;
    private bool _isDirty;

    private readonly ListBox _footnotesList;
    private readonly ListBox _endnotesList;
    private readonly TextBox _noteEditor;
    private readonly TextBlock _noteTitle;
    private readonly Button _applyButton;

    public NotesPaneWindow()
    {
        InitializeComponent();

        _footnotesList = this.FindControl<ListBox>("FootnotesList")!;
        _endnotesList = this.FindControl<ListBox>("EndnotesList")!;
        _noteEditor = this.FindControl<TextBox>("NoteEditor")!;
        _noteTitle = this.FindControl<TextBlock>("NoteTitle")!;
        _applyButton = this.FindControl<Button>("ApplyButton")!;

        _footnotesList.ItemsSource = _footnotes;
        _endnotesList.ItemsSource = _endnotes;
        _noteEditor.TextChanged += OnNoteEditorTextChanged;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    public NotesPaneWindow(DocumentView view)
        : this()
    {
        SetDocumentView(view);
    }

    public void SetDocumentView(DocumentView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!ReferenceEquals(_view, view))
        {
            DetachView();
            _view = view;
            _view.EditorStateChanged += OnEditorStateChanged;
        }

        _document = _view.Document;
        RefreshNotes(preserveSelection: false, preserveEditorText: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachView();
        _refreshTimer.Stop();
        base.OnClosed(e);
    }

    private void DetachView()
    {
        if (_view is not null)
        {
            _view.EditorStateChanged -= OnEditorStateChanged;
        }

        _view = null;
        _document = null;
    }

    private void OnEditorStateChanged(object? sender, EventArgs e)
    {
        if (_view is not null)
        {
            _document = _view.Document;
        }

        if (_isDirty)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshNotes(preserveSelection: true, preserveEditorText: true);
    }

    private void OnFootnoteSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressEditorChange)
        {
            return;
        }

        SelectNote(_footnotesList.SelectedItem as NoteEntry);
    }

    private void OnEndnoteSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressEditorChange)
        {
            return;
        }

        SelectNote(_endnotesList.SelectedItem as NoteEntry);
    }

    private void OnNoteEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEditorChange || _currentNote is null)
        {
            return;
        }

        SetDirtyState(true);
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        var document = ResolveDocument();
        if (_currentNote is null || document is null)
        {
            return;
        }

        var text = _noteEditor.Text ?? string.Empty;
        CollabGestureToken? gesture = null;
        if (_view is not null && _view.TryGetService<ICollabGestureRecorder>(out var gestureRecorder))
        {
            gesture = gestureRecorder.BeginGesture("note-edit");
        }

        var history = gesture.HasValue ? null : ResolveHistoryService();
        var snapshot = history?.CaptureSnapshot();

        UpdateNoteBlocks(_currentNote, text);
        NormalizeNoteBlocks(_currentNote);
        _view?.RefreshLayout();

        if (gesture.HasValue && _view is not null && _view.TryGetService<ICollabGestureRecorder>(out var endRecorder))
        {
            endRecorder.EndGesture(gesture.Value);
        }
        else if (snapshot is not null)
        {
            history?.RecordSnapshot(snapshot.Value);
        }

        SetDirtyState(false);
        RefreshNotes(preserveSelection: true, preserveEditorText: false);
    }

    private void OnGoToReferenceClick(object? sender, RoutedEventArgs e)
    {
        if (_currentNote is null || _view is null)
        {
            return;
        }

        if (!TryGetAnchor(_currentNote, out var position))
        {
            return;
        }

        _view.GoToPosition(position, ensureVisible: true);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SelectNote(NoteEntry? entry)
    {
        _currentNote = entry;
        _suppressEditorChange = true;
        try
        {
            if (entry is null)
            {
                _noteTitle.Text = "Select a note";
                _noteEditor.Text = string.Empty;
                SetDirtyState(false);
                return;
            }

            _noteTitle.Text = entry.Kind == NoteKind.Footnote
                ? $"Footnote {entry.Id}"
                : $"Endnote {entry.Id}";
            _noteEditor.Text = entry.Text;
            SetDirtyState(false);
        }
        finally
        {
            _suppressEditorChange = false;
        }
    }

    private void RefreshNotes(bool preserveSelection, bool preserveEditorText)
    {
        var document = ResolveDocument();
        if (document is null)
        {
            return;
        }

        var selectedFootnote = preserveSelection ? _footnotesList.SelectedItem as NoteEntry : null;
        var selectedEndnote = preserveSelection ? _endnotesList.SelectedItem as NoteEntry : null;
        var selectedNote = preserveSelection ? _currentNote : null;
        var editorText = preserveEditorText ? _noteEditor.Text ?? string.Empty : string.Empty;

        _footnotes.Clear();
        _endnotes.Clear();
        BuildAnchors();

        foreach (var pair in document.Footnotes)
        {
            var text = BuildNoteText(pair.Value.Blocks);
            _footnotes.Add(new NoteEntry(NoteKind.Footnote, pair.Key, text));
        }

        foreach (var pair in document.Endnotes)
        {
            var text = BuildNoteText(pair.Value.Blocks);
            _endnotes.Add(new NoteEntry(NoteKind.Endnote, pair.Key, text));
        }

        RestoreSelection(selectedFootnote, _footnotesList, NoteKind.Footnote);
        RestoreSelection(selectedEndnote, _endnotesList, NoteKind.Endnote);

        if (selectedNote is not null)
        {
            var currentList = selectedNote.Kind == NoteKind.Footnote ? _footnotesList : _endnotesList;
            var entry = currentList.SelectedItem as NoteEntry;
            if (entry is not null)
            {
                _currentNote = entry;
                if (preserveEditorText)
                {
                    _suppressEditorChange = true;
                    _noteEditor.Text = editorText;
                    _suppressEditorChange = false;
                }
                else
                {
                    SelectNote(entry);
                }

                return;
            }
        }

        if (_currentNote is null && _footnotes.Count > 0)
        {
            _footnotesList.SelectedIndex = 0;
        }
        else if (_currentNote is null && _endnotes.Count > 0)
        {
            _endnotesList.SelectedIndex = 0;
        }
    }

    private void RestoreSelection(NoteEntry? selected, ListBox list, NoteKind kind)
    {
        if (selected is null)
        {
            return;
        }

        var collection = kind == NoteKind.Footnote ? _footnotes : _endnotes;
        for (var i = 0; i < collection.Count; i++)
        {
            if (collection[i].Id == selected.Id)
            {
                list.SelectedIndex = i;
                return;
            }
        }
    }

    private void BuildAnchors()
    {
        _footnoteAnchors.Clear();
        _endnoteAnchors.Clear();

        var document = ResolveDocument();
        if (document is null)
        {
            return;
        }

        var paragraphIndex = 0;
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    AddAnchors(paragraph, paragraphIndex++);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                AddAnchors(paragraph, paragraphIndex++);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private void AddAnchors(ParagraphBlock paragraph, int paragraphIndex)
    {
        if (paragraph.Inlines.Count == 0)
        {
            DocumentEditHelpers.EnsureParagraphInlines(paragraph);
        }

        var offset = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is FootnoteReferenceInline footnote)
            {
                _footnoteAnchors.TryAdd(footnote.Id, new TextPosition(paragraphIndex, offset));
            }
            else if (inline is EndnoteReferenceInline endnote)
            {
                _endnoteAnchors.TryAdd(endnote.Id, new TextPosition(paragraphIndex, offset));
            }

            offset += DocumentEditHelpers.GetInlineLength(inline);
        }
    }

    private bool TryGetAnchor(NoteEntry entry, out TextPosition position)
    {
        if (entry.Kind == NoteKind.Footnote)
        {
            return _footnoteAnchors.TryGetValue(entry.Id, out position);
        }

        return _endnoteAnchors.TryGetValue(entry.Id, out position);
    }

    private void UpdateNoteBlocks(NoteEntry entry, string text)
    {
        var document = ResolveDocument();
        if (document is null)
        {
            return;
        }

        if (entry.Kind == NoteKind.Footnote)
        {
            if (document.Footnotes.TryGetValue(entry.Id, out var note))
            {
                note.Blocks.Clear();
                note.Blocks.AddRange(CreateNoteBlocks(text));
            }
        }
        else
        {
            if (document.Endnotes.TryGetValue(entry.Id, out var note))
            {
                note.Blocks.Clear();
                note.Blocks.AddRange(CreateNoteBlocks(text));
            }
        }
    }

    private void NormalizeNoteBlocks(NoteEntry entry)
    {
        if (_view is null)
        {
            return;
        }

        if (!_view.TryGetService<ITextContainerNormalizer>(out var normalizer))
        {
            return;
        }

        var document = ResolveDocument();
        if (document is null)
        {
            return;
        }

        if (entry.Kind == NoteKind.Footnote)
        {
            if (document.Footnotes.TryGetValue(entry.Id, out var note))
            {
                normalizer.EnsureBlocksInlines(note.Blocks);
            }
        }
        else
        {
            if (document.Endnotes.TryGetValue(entry.Id, out var note))
            {
                normalizer.EnsureBlocksInlines(note.Blocks);
            }
        }
    }

    private Document? ResolveDocument()
    {
        if (_view is not null)
        {
            _document = _view.Document;
        }

        return _document;
    }

    private IEditorHistorySnapshotService? ResolveHistoryService()
    {
        if (_view is null)
        {
            return null;
        }

        return _view.TryGetService<IEditorHistorySnapshotService>(out var history) ? history : null;
    }

    private static List<Block> CreateNoteBlocks(string text)
    {
        var lines = SplitLines(text);
        var blocks = new List<Block>(Math.Max(lines.Count, 1));
        foreach (var line in lines)
        {
            blocks.Add(new ParagraphBlock(line));
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new ParagraphBlock());
        }

        return blocks;
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                lines.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            if (ch == '\n')
            {
                lines.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        lines.Add(builder.ToString());
        return lines;
    }

    private static string BuildNoteText(IReadOnlyList<Block> blocks)
    {
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var first = true;
        foreach (var block in blocks)
        {
            if (block is not ParagraphBlock paragraph)
            {
                continue;
            }

            if (!first)
            {
                builder.AppendLine();
            }

            var text = DocumentEditHelpers.GetParagraphText(paragraph);
            if (text.Length > 0)
            {
                text = text.Replace(DocumentConstants.ObjectReplacementChar, '?');
            }

            builder.Append(text);
            first = false;
        }

        return builder.ToString();
    }

    private void SetDirtyState(bool isDirty)
    {
        if (_isDirty == isDirty)
        {
            return;
        }

        _isDirty = isDirty;
        _applyButton.IsEnabled = _isDirty;
        Title = _isDirty ? "Notes *" : "Notes";
    }

}

public sealed record NoteEntry(NoteKind Kind, int Id, string Text)
{
    public string Display => BuildDisplay(Kind, Id, Text);

    private static string BuildDisplay(NoteKind kind, int id, string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length > 60)
        {
            trimmed = trimmed[..60] + "...";
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = "(empty)";
        }

        var label = kind == NoteKind.Footnote ? "Footnote" : "Endnote";
        return $"{label} {id}: {trimmed}";
    }
}

public enum NoteKind
{
    Footnote,
    Endnote
}
