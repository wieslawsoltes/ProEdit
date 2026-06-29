using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ProEdit.Word.Avalonia;

public partial class RibbonQuickAccessDialog : Window
{
    private readonly RibbonQuickAccessDialogViewModel _viewModel;
    private RibbonQuickAccessCandidate? _dragCandidate;
    private PointerPressedEventArgs? _dragStartArgs;
    private Point _dragStartPoint;
    private bool _isDragging;

    private static readonly DataFormat<string> DragFormat =
        DataFormat.CreateStringApplicationFormat("proedit.qat-item");

    public RibbonQuickAccessDialog()
        : this(Array.Empty<RibbonQuickAccessCandidate>())
    {
    }

    public RibbonQuickAccessDialog(IEnumerable<RibbonQuickAccessCandidate> candidates)
    {
        InitializeComponent();
        _viewModel = new RibbonQuickAccessDialogViewModel(candidates);
        DataContext = _viewModel;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var selected = _viewModel.Candidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Id)
            .ToArray();
        Close(selected);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnCandidatePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (!e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var candidate = GetCandidateFromEventSource(e.Source);
        if (candidate is null)
        {
            return;
        }

        _dragCandidate = candidate;
        _dragStartArgs = e;
        _dragStartPoint = e.GetPosition(listBox);
    }

    private async void OnCandidatePointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (_dragCandidate is null)
        {
            return;
        }

        if (_dragStartArgs is null)
        {
            _dragCandidate = null;
            return;
        }

        if (!_isDragging && !e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = null;
            _dragStartArgs = null;
            return;
        }

        if (_isDragging)
        {
            return;
        }

        var position = e.GetPosition(listBox);
        if (Math.Abs(position.X - _dragStartPoint.X) < 4 && Math.Abs(position.Y - _dragStartPoint.Y) < 4)
        {
            return;
        }

        _isDragging = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormat, _dragCandidate.Id));
        await DragDrop.DoDragDropAsync(_dragStartArgs, data, DragDropEffects.Move);
        _isDragging = false;
        _dragCandidate = null;
        _dragStartArgs = null;
    }

    private void OnCandidatePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragCandidate = null;
        _dragStartArgs = null;
        _isDragging = false;
    }

    private void OnCandidateDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DragFormat))
        {
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnCandidateDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (!e.DataTransfer.Contains(DragFormat))
        {
            return;
        }

        var id = e.DataTransfer.TryGetValue(DragFormat);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var candidate = _viewModel.Candidates.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return;
        }

        var target = GetCandidateFromEventSource(e.Source);
        if (ReferenceEquals(target, candidate))
        {
            return;
        }

        var sourceIndex = _viewModel.Candidates.IndexOf(candidate);
        if (sourceIndex < 0)
        {
            return;
        }

        var targetIndex = target is null
            ? _viewModel.Candidates.Count - 1
            : _viewModel.Candidates.IndexOf(target);

        if (targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        _viewModel.Candidates.Move(sourceIndex, targetIndex);
        listBox.SelectedItem = candidate;
        e.Handled = true;
    }

    private static RibbonQuickAccessCandidate? GetCandidateFromEventSource(object? source)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        var container = visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        return container?.DataContext as RibbonQuickAccessCandidate;
    }
}
