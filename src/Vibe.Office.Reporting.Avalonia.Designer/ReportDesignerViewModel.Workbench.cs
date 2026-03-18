using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI;
using System.Reactive;
using Vibe.Office.Reporting;

namespace Vibe.Office.Reporting.Avalonia.Designer;

public sealed partial class ReportDesignerViewModel
{
    private const float DesignerSnapGuideThreshold = 6f;
    private static readonly double[] DesignerZoomPresets = [50d, 75d, 100d, 125d, 150d, 200d];

    private readonly ObservableCollection<ReportDesignerSnapGuideViewModel> _snapGuides = new();
    private readonly ObservableCollection<ReportDesignerInsertToolEntryViewModel> _insertToolEntries = new();
    private ReportDesignerInsertTool _activeInsertTool;
    private ReportDesignerGroupingEntryViewModel? _selectedRowGroupEntry;
    private ReportDesignerGroupingEntryViewModel? _selectedColumnGroupEntry;
    private bool _showAdvancedGroupingMode;
    private ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>? _zoomOptions;
    private ReportDesignerChoiceOptionViewModel? _selectedZoomOption;
    private bool _showRulers = true;
    private double _surfaceZoomFactor = 1d;
    private double _surfaceViewportWidth;
    private double _surfaceViewportHeight;

    /// <summary>
    /// Gets the transient snap guides shown during surface editing.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerSnapGuideViewModel> SnapGuides { get; private set; } = null!;

    /// <summary>
    /// Gets the insert tools available to the workbench.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerInsertToolEntryViewModel> InsertToolEntries { get; private set; } = null!;

    /// <summary>
    /// Gets the active insert tool.
    /// </summary>
    public ReportDesignerInsertTool ActiveInsertTool
    {
        get => _activeInsertTool;
        private set
        {
            if (_activeInsertTool == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _activeInsertTool, value);
            UpdateInsertToolSelectionState();
            this.RaisePropertyChanged(nameof(HasActiveInsertTool));
            this.RaisePropertyChanged(nameof(ActiveInsertToolDisplayText));
        }
    }

    /// <summary>
    /// Gets a value indicating whether an insert tool is active.
    /// </summary>
    public bool HasActiveInsertTool => ActiveInsertTool != ReportDesignerInsertTool.None;

    /// <summary>
    /// Gets the active insert tool label.
    /// </summary>
    public string ActiveInsertToolDisplayText => ActiveInsertTool switch
    {
        ReportDesignerInsertTool.None => "Select",
        ReportDesignerInsertTool.TextBox => "Text Box",
        ReportDesignerInsertTool.Tablix => "Table",
        ReportDesignerInsertTool.Chart => "Chart",
        ReportDesignerInsertTool.Rectangle => "Rectangle",
        ReportDesignerInsertTool.Line => "Line",
        ReportDesignerInsertTool.Image => "Image",
        ReportDesignerInsertTool.Subreport => "Subreport",
        ReportDesignerInsertTool.Template => "Template",
        _ => "Insert"
    };

    /// <summary>
    /// Gets the available zoom presets.
    /// </summary>
    public ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel> ZoomOptions => _zoomOptions!;

    /// <summary>
    /// Gets or sets the selected zoom preset.
    /// </summary>
    public ReportDesignerChoiceOptionViewModel? SelectedZoomOption
    {
        get => _selectedZoomOption;
        set
        {
            if (ReferenceEquals(_selectedZoomOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedZoomOption, value);
            if (value is null)
            {
                return;
            }

            if (double.TryParse(value.Value, CultureInfo.InvariantCulture, out var zoomPercent))
            {
                SetSurfaceZoomFactor(Math.Clamp(zoomPercent / 100d, 0.25d, 4d), "Updated design-surface zoom.");
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether rulers are visible around the design surface.
    /// </summary>
    public bool ShowRulers
    {
        get => _showRulers;
        set
        {
            if (_showRulers == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showRulers, value);
            UpdateViewStateStatus(value ? "Displayed design rulers." : "Hid design rulers.");
        }
    }

    /// <summary>
    /// Gets the active surface zoom factor.
    /// </summary>
    public double SurfaceZoomFactor => _surfaceZoomFactor;

    /// <summary>
    /// Gets the scaled surface width used by the zoomed design surface.
    /// </summary>
    public double SurfaceScaledWidth => SurfaceWidth * SurfaceZoomFactor;

    /// <summary>
    /// Gets the scaled surface height used by the zoomed design surface.
    /// </summary>
    public double SurfaceScaledHeight => SurfaceHeight * SurfaceZoomFactor;

    /// <summary>
    /// Gets the design-surface zoom text.
    /// </summary>
    public string SurfaceZoomDisplayText => $"{Math.Round(SurfaceZoomFactor * 100d, MidpointRounding.AwayFromZero):0}%";

    /// <summary>
    /// Gets the current selection bounds summary.
    /// </summary>
    public string SurfaceSelectionSummaryText => SelectedCanvasItem is { } canvasItem
        ? $"X {canvasItem.Left:0.#}, Y {canvasItem.Top:0.#}, W {canvasItem.Width:0.#}, H {canvasItem.Height:0.#}"
        : $"Page {SurfaceDisplayText}";

    /// <summary>
    /// Gets or sets the selected row-group entry.
    /// </summary>
    public ReportDesignerGroupingEntryViewModel? SelectedRowGroupEntry
    {
        get => _selectedRowGroupEntry;
        set
        {
            if (ReferenceEquals(_selectedRowGroupEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedRowGroupEntry, value);
            for (var index = 0; index < _rowGroupEntries.Count; index++)
            {
                _rowGroupEntries[index].IsSelected = ReferenceEquals(_rowGroupEntries[index], value);
            }

            if (value is not null && _selectedColumnGroupEntry is not null)
            {
                this.RaiseAndSetIfChanged(ref _selectedColumnGroupEntry, null);
                for (var index = 0; index < _columnGroupEntries.Count; index++)
                {
                    _columnGroupEntries[index].IsSelected = false;
                }
            }

            if (!_suppressSelectionSynchronization)
            {
                SelectTarget((object?)(value?.SelectionTarget) ?? GetSelectedTablix());
            }

            RaiseGroupingCapabilityPropertiesChanged();
        }
    }

    /// <summary>
    /// Gets or sets the selected column-group entry.
    /// </summary>
    public ReportDesignerGroupingEntryViewModel? SelectedColumnGroupEntry
    {
        get => _selectedColumnGroupEntry;
        set
        {
            if (ReferenceEquals(_selectedColumnGroupEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedColumnGroupEntry, value);
            for (var index = 0; index < _columnGroupEntries.Count; index++)
            {
                _columnGroupEntries[index].IsSelected = ReferenceEquals(_columnGroupEntries[index], value);
            }

            if (value is not null && _selectedRowGroupEntry is not null)
            {
                this.RaiseAndSetIfChanged(ref _selectedRowGroupEntry, null);
                for (var index = 0; index < _rowGroupEntries.Count; index++)
                {
                    _rowGroupEntries[index].IsSelected = false;
                }
            }

            if (!_suppressSelectionSynchronization)
            {
                SelectTarget((object?)(value?.SelectionTarget) ?? GetSelectedTablix());
            }

            RaiseGroupingCapabilityPropertiesChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the grouping pane shows static and dynamic members.
    /// </summary>
    public bool ShowAdvancedGroupingMode
    {
        get => _showAdvancedGroupingMode;
        set
        {
            if (_showAdvancedGroupingMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showAdvancedGroupingMode, value);
            BuildGroupingEntries();
            this.RaisePropertyChanged(nameof(GroupingStatusText));
            UpdateViewStateStatus(value
                ? "Advanced grouping mode enabled."
                : "Advanced grouping mode disabled.");
        }
    }

    /// <summary>
    /// Gets a value indicating whether a row group can be added from the current selection.
    /// </summary>
    public bool CanAddRowGroup => GetSelectedTablix() is not null && TryGetSelectedDataField(out _, out _, out _) is not null;

    /// <summary>
    /// Gets a value indicating whether a column group can be added from the current selection.
    /// </summary>
    public bool CanAddColumnGroup => GetSelectedTablix() is not null && TryGetSelectedDataField(out _, out _, out _) is not null;

    /// <summary>
    /// Gets a value indicating whether a parent row group can be added for the current selection.
    /// </summary>
    public bool CanAddParentRowGroup => CanAddRowGroup;

    /// <summary>
    /// Gets a value indicating whether a child row group can be added for the current selection.
    /// </summary>
    public bool CanAddChildRowGroup => CanAddRowGroup && SelectedRowGroupEntry?.Member is not null;

    /// <summary>
    /// Gets a value indicating whether an adjacent row group can be added for the current selection.
    /// </summary>
    public bool CanAddAdjacentRowGroup => CanAddRowGroup && SelectedRowGroupEntry?.Member is not null;

    /// <summary>
    /// Gets a value indicating whether a parent column group can be added for the current selection.
    /// </summary>
    public bool CanAddParentColumnGroup => CanAddColumnGroup;

    /// <summary>
    /// Gets a value indicating whether a child column group can be added for the current selection.
    /// </summary>
    public bool CanAddChildColumnGroup => CanAddColumnGroup && SelectedColumnGroupEntry?.Member is not null;

    /// <summary>
    /// Gets a value indicating whether an adjacent column group can be added for the current selection.
    /// </summary>
    public bool CanAddAdjacentColumnGroup => CanAddColumnGroup && SelectedColumnGroupEntry?.Member is not null;

    /// <summary>
    /// Gets a value indicating whether a total row can be added to the selected tablix.
    /// </summary>
    public bool CanAddRowTotal => GetSelectedTablix() is not null;

    /// <summary>
    /// Gets a value indicating whether a total column can be added to the selected tablix.
    /// </summary>
    public bool CanAddColumnTotal => GetSelectedTablix() is not null;

    /// <summary>
    /// Gets a value indicating whether the selected group can be removed.
    /// </summary>
    public bool CanRemoveSelectedGroup => (SelectedRowGroupEntry?.Member ?? SelectedColumnGroupEntry?.Member)?.Kind == ReportTablixMemberKind.Group;

    /// <summary>
    /// Gets the activate text-box insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertTextBoxCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the activate table insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertTablixCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the activate chart insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertChartCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the activate rectangle insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertRectangleCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the activate line insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertLineCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the activate image insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertImageCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the activate subreport insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertSubreportCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the activate template insert command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BeginInsertTemplateCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the cancel insert-mode command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelInsertToolCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the duplicate command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> DuplicateSelectedCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the bring-forward command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BringSelectedForwardCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the send-backward command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> SendSelectedBackwardCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the bring-to-front command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> BringSelectedToFrontCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the send-to-back command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> SendSelectedToBackCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-row-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddRowGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the remove-row-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> RemoveSelectedGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-column-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddColumnGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-parent-row-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddParentRowGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-child-row-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddChildRowGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-adjacent-row-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddAdjacentRowGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-parent-column-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddParentColumnGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-child-column-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddChildColumnGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-adjacent-column-group command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddAdjacentColumnGroupCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-row-total command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddRowTotalCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the add-column-total command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddColumnTotalCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the zoom-in command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the zoom-out command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the actual-size command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ActualSizeCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the fit-page command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> FitPageCommand { get; private set; } = null!;

    private void InitializeWorkbench()
    {
        SnapGuides = new ReadOnlyObservableCollection<ReportDesignerSnapGuideViewModel>(_snapGuides);
        InsertToolEntries = new ReadOnlyObservableCollection<ReportDesignerInsertToolEntryViewModel>(_insertToolEntries);
        _zoomOptions = new ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>(
            new ObservableCollection<ReportDesignerChoiceOptionViewModel>(
                DesignerZoomPresets.Select(static preset =>
                    new ReportDesignerChoiceOptionViewModel(
                        preset.ToString(CultureInfo.InvariantCulture),
                        preset.ToString("0", CultureInfo.InvariantCulture) + "%"))));

        BeginInsertTextBoxCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.TextBox));
        BeginInsertTablixCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.Tablix));
        BeginInsertChartCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.Chart));
        BeginInsertRectangleCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.Rectangle));
        BeginInsertLineCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.Line));
        BeginInsertImageCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.Image));
        BeginInsertSubreportCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.Subreport));
        BeginInsertTemplateCommand = ReactiveCommand.Create(() => SelectInsertTool(ReportDesignerInsertTool.Template));
        CancelInsertToolCommand = ReactiveCommand.Create(CancelInsertTool);
        DuplicateSelectedCommand = ReactiveCommand.Create(DuplicateSelectedItem);
        BringSelectedForwardCommand = ReactiveCommand.Create(BringSelectedForward);
        SendSelectedBackwardCommand = ReactiveCommand.Create(SendSelectedBackward);
        BringSelectedToFrontCommand = ReactiveCommand.Create(BringSelectedToFront);
        SendSelectedToBackCommand = ReactiveCommand.Create(SendSelectedToBack);
        AddRowGroupCommand = ReactiveCommand.Create(AddSelectedRowGroup);
        AddColumnGroupCommand = ReactiveCommand.Create(AddSelectedColumnGroup);
        AddParentRowGroupCommand = ReactiveCommand.Create(AddSelectedParentRowGroup);
        AddChildRowGroupCommand = ReactiveCommand.Create(AddSelectedChildRowGroup);
        AddAdjacentRowGroupCommand = ReactiveCommand.Create(AddSelectedAdjacentRowGroup);
        AddParentColumnGroupCommand = ReactiveCommand.Create(AddSelectedParentColumnGroup);
        AddChildColumnGroupCommand = ReactiveCommand.Create(AddSelectedChildColumnGroup);
        AddAdjacentColumnGroupCommand = ReactiveCommand.Create(AddSelectedAdjacentColumnGroup);
        AddRowTotalCommand = ReactiveCommand.Create(AddSelectedRowTotal);
        AddColumnTotalCommand = ReactiveCommand.Create(AddSelectedColumnTotal);
        RemoveSelectedGroupCommand = ReactiveCommand.Create(RemoveSelectedRowGroup);
        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        ActualSizeCommand = ReactiveCommand.Create(ResetZoomToActualSize);
        FitPageCommand = ReactiveCommand.Create(FitPageToViewport);

        BuildInsertToolEntries();
        SyncSelectedZoomOption();
    }

    internal void SelectCurrentSectionFromSurface()
    {
        SelectTarget(EnsureSelectedSection());
    }

    internal bool TryCommitInsertToolPlacement(double startX, double startY, double endX, double endY)
    {
        if (!HasActiveInsertTool)
        {
            return false;
        }

        var insertedToolLabel = ActiveInsertToolDisplayText;
        var section = EnsureSelectedSection();
        var bounds = CreateInsertBounds(ActiveInsertTool, section, startX, startY, endX, endY);
        var item = CreateInsertToolItem(ActiveInsertTool, section, bounds);
        if (item is null)
        {
            return false;
        }

        item.ZIndex = section.BodyItems.Count == 0
            ? 0
            : section.BodyItems.Max(static candidate => candidate.ZIndex) + 1;
        section.BodyItems.Add(item);
        CancelInsertTool();
        RebuildDesignerState(item);
        SelectedCenterTabIndex = 0;
        MarkDirty($"Inserted {insertedToolLabel.ToLowerInvariant()}.");
        return true;
    }

    internal void CancelInsertTool()
    {
        if (!HasActiveInsertTool)
        {
            ClearSnapGuides();
            return;
        }

        ActiveInsertTool = ReportDesignerInsertTool.None;
        ClearSnapGuides();
        StatusMessage = "Insert mode cancelled.";
    }

    internal bool HandleSurfaceDelete()
    {
        if (_selectedTarget is not ReportItem)
        {
            return false;
        }

        RemoveSelected();
        return true;
    }

    internal bool HandleSurfaceDuplicate()
    {
        if (_selectedTarget is not ReportItem)
        {
            return false;
        }

        DuplicateSelectedItem();
        return true;
    }

    internal bool HandleSurfaceNudge(double deltaX, double deltaY)
    {
        if (SelectedCanvasItem is not { } canvasItem)
        {
            return false;
        }

        if (!TryMoveSurfaceItemByDelta(canvasItem, deltaX, deltaY))
        {
            return false;
        }

        CompleteSurfaceInteraction(canvasItem);
        return true;
    }

    internal bool HandleSurfaceResize(double deltaWidth, double deltaHeight)
    {
        if (SelectedCanvasItem is not { } canvasItem || canvasItem.IsReadOnly || canvasItem.Item is not { } item)
        {
            return false;
        }

        EnsurePreviewMarkedDirtyForSurfaceEdit($"Resizing {canvasItem.Label}.");

        var bounds = item.Bounds;
        var width = SnapAndClamp(bounds.Width + (float)deltaWidth, DesignerMinItemWidth, GetMaxWidth(item, bounds.X));
        var height = SnapAndClamp(bounds.Height + (float)deltaHeight, DesignerMinItemHeight, GetMaxHeight(item, bounds.Y));
        if (Math.Abs(width - bounds.Width) < float.Epsilon && Math.Abs(height - bounds.Height) < float.Epsilon)
        {
            return false;
        }

        item.Bounds = bounds with
        {
            Width = width,
            Height = height
        };

        canvasItem.Width = width;
        canvasItem.Height = height;
        CompleteSurfaceInteraction(canvasItem);
        return true;
    }

    internal bool HandleBringSelectedForward()
    {
        if (_selectedTarget is not ReportItem)
        {
            return false;
        }

        BringSelectedForward();
        return true;
    }

    internal bool HandleSendSelectedBackward()
    {
        if (_selectedTarget is not ReportItem)
        {
            return false;
        }

        SendSelectedBackward();
        return true;
    }

    internal bool CanAcceptGroupingDrop(ReportDesignerGroupingDropTarget dropTarget)
    {
        return dropTarget is ReportDesignerGroupingDropTarget.RowGroups or ReportDesignerGroupingDropTarget.ColumnGroups
            && GetSelectedTablix() is not null;
    }

    internal bool TryApplyGroupingDrop(ReportDesignerDataFieldDragPayload payload, ReportDesignerGroupingDropTarget dropTarget)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return dropTarget switch
        {
            ReportDesignerGroupingDropTarget.RowGroups => TryAddParentGroup(payload.DataSet, payload.FieldName, ReportDesignerTablixHierarchyAxis.Row),
            ReportDesignerGroupingDropTarget.ColumnGroups => TryAddParentGroup(payload.DataSet, payload.FieldName, ReportDesignerTablixHierarchyAxis.Column),
            _ => false
        };
    }

    internal void ClearSnapGuides()
    {
        if (_snapGuides.Count == 0)
        {
            return;
        }

        _snapGuides.Clear();
    }

    private void BuildInsertToolEntries()
    {
        _insertToolEntries.Clear();
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.TextBox, "Text Box", "Add a textbox and place it on the design surface.", SelectInsertTool));
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.Tablix, "Table", "Add a tablix-backed table region.", SelectInsertTool));
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.Chart, "Chart", "Add a chart region.", SelectInsertTool));
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.Rectangle, "Rectangle", "Add a nested layout container.", SelectInsertTool));
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.Line, "Line", "Add a line decoration.", SelectInsertTool));
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.Image, "Image", "Add an image placeholder.", SelectInsertTool));
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.Subreport, "Subreport", "Add a referenced subreport placeholder.", SelectInsertTool));
        _insertToolEntries.Add(new ReportDesignerInsertToolEntryViewModel(ReportDesignerInsertTool.Template, "Template", "Add a document-template item.", SelectInsertTool));
        UpdateInsertToolSelectionState();
    }

    internal void UpdateSurfaceViewport(double viewportWidth, double viewportHeight)
    {
        _surfaceViewportWidth = Math.Max(0d, viewportWidth);
        _surfaceViewportHeight = Math.Max(0d, viewportHeight);
    }

    private void UpdateInsertToolSelectionState()
    {
        for (var index = 0; index < _insertToolEntries.Count; index++)
        {
            _insertToolEntries[index].IsActive = _insertToolEntries[index].Tool == ActiveInsertTool;
        }
    }

    private void SelectInsertTool(ReportDesignerInsertTool tool)
    {
        ActiveInsertTool = ActiveInsertTool == tool ? ReportDesignerInsertTool.None : tool;
        ClearSnapGuides();
        StatusMessage = HasActiveInsertTool
            ? $"Insert mode: {ActiveInsertToolDisplayText}. Click or drag on the design surface to place the item."
            : "Selection mode active.";
    }

    private void ZoomIn()
    {
        SetSurfaceZoomFactor(GetNearestZoomPreset(stepDirection: +1), "Zoomed in on the design surface.");
    }

    private void ZoomOut()
    {
        SetSurfaceZoomFactor(GetNearestZoomPreset(stepDirection: -1), "Zoomed out on the design surface.");
    }

    private void ResetZoomToActualSize()
    {
        SetSurfaceZoomFactor(1d, "Restored the design surface to actual size.");
    }

    private void FitPageToViewport()
    {
        var availableWidth = Math.Max(0d, _surfaceViewportWidth - 80d);
        var availableHeight = Math.Max(0d, _surfaceViewportHeight - 80d);
        if (availableWidth <= 0d || availableHeight <= 0d)
        {
            return;
        }

        var widthScale = availableWidth / Math.Max(1d, SurfaceWidth);
        var heightScale = availableHeight / Math.Max(1d, SurfaceHeight);
        SetSurfaceZoomFactor(Math.Clamp(Math.Min(widthScale, heightScale), 0.25d, 4d), "Fit the full page in the design viewport.");
    }

    private double GetNearestZoomPreset(int stepDirection)
    {
        var currentPercent = SurfaceZoomFactor * 100d;
        if (stepDirection > 0)
        {
            return DesignerZoomPresets.FirstOrDefault(preset => preset > currentPercent, DesignerZoomPresets[^1]) / 100d;
        }

        for (var index = DesignerZoomPresets.Length - 1; index >= 0; index--)
        {
            if (DesignerZoomPresets[index] < currentPercent)
            {
                return DesignerZoomPresets[index] / 100d;
            }
        }

        return DesignerZoomPresets[0] / 100d;
    }

    private void SetSurfaceZoomFactor(double zoomFactor, string statusMessage)
    {
        var normalizedZoom = Math.Clamp(zoomFactor, 0.25d, 4d);
        if (Math.Abs(_surfaceZoomFactor - normalizedZoom) < 0.0001d)
        {
            return;
        }

        _surfaceZoomFactor = normalizedZoom;
        SyncSelectedZoomOption();
        RebuildRulerTicks();
        this.RaisePropertyChanged(nameof(SurfaceZoomFactor));
        this.RaisePropertyChanged(nameof(SurfaceScaledWidth));
        this.RaisePropertyChanged(nameof(SurfaceScaledHeight));
        this.RaisePropertyChanged(nameof(SurfaceZoomDisplayText));
        UpdateViewStateStatus(statusMessage);
    }

    private void SyncSelectedZoomOption()
    {
        if (_zoomOptions is null)
        {
            return;
        }

        var zoomPercentText = Math.Round(SurfaceZoomFactor * 100d, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
        _selectedZoomOption = _zoomOptions.FirstOrDefault(option =>
            string.Equals(option.Value, zoomPercentText, StringComparison.Ordinal));
        this.RaisePropertyChanged(nameof(SelectedZoomOption));
    }

    private void DuplicateSelectedItem()
    {
        if (_selectedTarget is not ReportItem item || !TryGetOwningCollection(item, out var siblings))
        {
            return;
        }

        var clone = ReportDesignerItemCloner.Clone(item);
        AssignDuplicatedIds(clone);
        clone.Bounds = OffsetBoundsForDuplicate(item.Bounds, clone, item);

        var index = siblings.IndexOf(item);
        if (index < 0)
        {
            siblings.Add(clone);
        }
        else
        {
            siblings.Insert(index + 1, clone);
        }

        NormalizeSiblingZOrder(siblings);
        RebuildDesignerState(clone);
        MarkDirty($"Duplicated {DescribeTarget(clone).title}.");
    }

    private void BringSelectedForward()
    {
        ReorderSelectedItem(+1, "Brought selection forward.");
    }

    private void SendSelectedBackward()
    {
        ReorderSelectedItem(-1, "Sent selection backward.");
    }

    private void BringSelectedToFront()
    {
        ReorderSelectedItem(int.MaxValue, "Brought selection to front.");
    }

    private void SendSelectedToBack()
    {
        ReorderSelectedItem(int.MinValue, "Sent selection to back.");
    }

    private void ReorderSelectedItem(int delta, string message)
    {
        if (_selectedTarget is not ReportItem item || !TryGetOwningCollection(item, out var siblings))
        {
            return;
        }

        var currentIndex = siblings.IndexOf(item);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = delta switch
        {
            int.MaxValue => siblings.Count - 1,
            int.MinValue => 0,
            _ => Math.Clamp(currentIndex + delta, 0, siblings.Count - 1)
        };

        if (targetIndex == currentIndex)
        {
            return;
        }

        siblings.RemoveAt(currentIndex);
        siblings.Insert(targetIndex, item);
        NormalizeSiblingZOrder(siblings);
        RebuildDesignerState(item);
        MarkDirty(message);
    }

    private bool TryAddParentGroup(ReportDataSetDefinition dataSet, string fieldName, ReportDesignerTablixHierarchyAxis axis)
    {
        var tablix = GetSelectedTablix();
        if (tablix is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        BindTablixToDataSet(tablix, dataSet);
        EnsureTablixMembersInitialized(tablix, axis);

        var members = GetGroupingMembers(tablix, axis);
        var targetMember = GetSelectedGroupingMember(axis);
        if (targetMember is null)
        {
            targetMember = axis == ReportDesignerTablixHierarchyAxis.Column
                ? FindDefaultColumnGroupingTarget(members)
                : FindDefaultGroupingTarget(members);
        }
        else if (axis == ReportDesignerTablixHierarchyAxis.Column && targetMember.Kind == ReportTablixMemberKind.Static)
        {
            targetMember = FindFirstLeafMember([targetMember]) ?? targetMember;
        }

        if (targetMember is null)
        {
            return false;
        }

        var newMember = CreateWrappedGroupMember(tablix, axis, targetMember, fieldName);
        if (!TryReplaceMember(members, targetMember, _ => [newMember]))
        {
            return false;
        }

        FinalizeGroupingChange(
            tablix,
            axis,
            newMember,
            $"Added parent {axis.ToString().ToLowerInvariant()} group for '{fieldName}'.");
        return true;
    }

    private bool TryAddChildGroup(ReportDataSetDefinition dataSet, string fieldName, ReportDesignerTablixHierarchyAxis axis)
    {
        var tablix = GetSelectedTablix();
        if (tablix is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        BindTablixToDataSet(tablix, dataSet);
        EnsureTablixMembersInitialized(tablix, axis);

        var members = GetGroupingMembers(tablix, axis);
        var selectedMember = GetSelectedGroupingMember(axis);
        if (selectedMember is null)
        {
            return TryAddParentGroup(dataSet, fieldName, axis);
        }

        if (selectedMember.Kind == ReportTablixMemberKind.Group)
        {
            var childTarget = axis == ReportDesignerTablixHierarchyAxis.Column
                ? FindDefaultColumnGroupingTarget(selectedMember.Members)
                : FindDefaultGroupingTarget(selectedMember.Members);
            childTarget ??= FindFirstLeafMember(selectedMember.Members);

            if (childTarget is null)
            {
                childTarget = CreateFallbackLeafMember(tablix, axis);
                selectedMember.Members.Add(childTarget);
            }

            var newMember = CreateWrappedGroupMember(tablix, axis, childTarget, fieldName);
            if (!TryReplaceMember(selectedMember.Members, childTarget, _ => [newMember]))
            {
                return false;
            }

            FinalizeGroupingChange(
                tablix,
                axis,
                newMember,
                $"Added child {axis.ToString().ToLowerInvariant()} group for '{fieldName}'.");
            return true;
        }

        var wrappedMember = CreateWrappedGroupMember(tablix, axis, selectedMember, fieldName);
        if (!TryReplaceMember(members, selectedMember, _ => [wrappedMember]))
        {
            return false;
        }

        FinalizeGroupingChange(
            tablix,
            axis,
            wrappedMember,
            $"Added child {axis.ToString().ToLowerInvariant()} group for '{fieldName}'.");
        return true;
    }

    private bool TryAddAdjacentGroup(ReportDataSetDefinition dataSet, string fieldName, ReportDesignerTablixHierarchyAxis axis)
    {
        var tablix = GetSelectedTablix();
        if (tablix is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        BindTablixToDataSet(tablix, dataSet);
        EnsureTablixMembersInitialized(tablix, axis);

        var members = GetGroupingMembers(tablix, axis);
        var selectedMember = GetSelectedGroupingMember(axis);
        if (selectedMember is null)
        {
            return TryAddParentGroup(dataSet, fieldName, axis);
        }

        var prototype = FindFirstLeafMember([selectedMember]);
        if (prototype is null)
        {
            prototype = CreateFallbackLeafMember(tablix, axis);
        }
        else
        {
            prototype = CloneMemberDefinition(prototype);
            AssignUniqueMemberIds(tablix, prototype);
        }

        var newMember = CreateWrappedGroupMember(tablix, axis, prototype, fieldName);
        if (!TryInsertAdjacentMember(members, selectedMember, newMember))
        {
            return false;
        }

        FinalizeGroupingChange(
            tablix,
            axis,
            newMember,
            $"Added adjacent {axis.ToString().ToLowerInvariant()} group for '{fieldName}'.");
        return true;
    }

    private void AddSelectedRowGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddParentGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Row);
    }

    private void AddSelectedParentRowGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddParentGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Row);
    }

    private void AddSelectedChildRowGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddChildGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Row);
    }

    private void AddSelectedAdjacentRowGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddAdjacentGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Row);
    }

    private void AddSelectedColumnGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddParentGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Column);
    }

    private void AddSelectedParentColumnGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddParentGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Column);
    }

    private void AddSelectedChildColumnGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddChildGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Column);
    }

    private void AddSelectedAdjacentColumnGroup()
    {
        if (TryGetSelectedDataField(out var dataSet, out var fieldName, out _) is null || dataSet is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        TryAddAdjacentGroup(dataSet, fieldName, ReportDesignerTablixHierarchyAxis.Column);
    }

    private void AddSelectedRowTotal()
    {
        var tablix = GetSelectedTablix();
        if (tablix is null)
        {
            return;
        }

        AddSummaryRow(tablix);
    }

    private void AddSelectedColumnTotal()
    {
        var tablix = GetSelectedTablix();
        if (tablix is null)
        {
            return;
        }

        AddSummaryColumn(tablix);
    }

    private void RemoveSelectedRowGroup()
    {
        var tablix = GetSelectedTablix();
        var selectedEntry = SelectedRowGroupEntry?.Member is not null
            ? SelectedRowGroupEntry
            : SelectedColumnGroupEntry;
        var member = selectedEntry?.Member;
        if (tablix is null || member is null || member.Kind != ReportTablixMemberKind.Group)
        {
            return;
        }

        var members = selectedEntry?.Axis == ReportDesignerTablixHierarchyAxis.Column
            ? tablix.ColumnMembers
            : tablix.RowMembers;
        if (!TryReplaceMember(
                members,
                member,
                target =>
                {
                    if (target.Members.Count == 0)
                    {
                        return
                        [
                            new ReportTablixMemberDefinition
                            {
                                Id = target.Id + "_details",
                                Kind = selectedEntry?.Axis == ReportDesignerTablixHierarchyAxis.Column
                                    ? ReportTablixMemberKind.Static
                                    : ReportTablixMemberKind.Details,
                                RowDefinitionIndex = target.RowDefinitionIndex,
                                ColumnDefinitionIndex = target.ColumnDefinitionIndex
                            }
                        ];
                    }

                    return target.Members;
                }))
        {
            return;
        }

        RebuildDesignerState(tablix);
        MarkDirty($"Removed {selectedEntry?.Axis?.ToString().ToLowerInvariant() ?? "row"} group '{member.GroupName ?? member.Id}'.");
    }

    private void AddSummaryRow(TablixItem tablix)
    {
        var dataSet = ResolveTablixDataSet(tablix);
        if (tablix.Columns.Count == 0)
        {
            return;
        }

        EnsureSummaryRow(tablix, dataSet);

        RebuildDesignerState(tablix);
        SelectedCenterTabIndex = 0;
        MarkDirty("Added tablix total row.");
    }

    private void AddSummaryColumn(TablixItem tablix)
    {
        var dataSet = ResolveTablixDataSet(tablix);
        var totalColumnIndex = tablix.Columns.Count;
        tablix.Columns.Add(new ReportTablixColumnDefinition
        {
            Id = CreateUniqueId("totalColumn", tablix.Columns.Select(static column => column.Id)),
            Width = tablix.Columns.Count == 0 ? 120f : Math.Max(96f, tablix.Columns[^1].Width)
        });

        var numericField = dataSet?.ExpectedFields.FirstOrDefault(field => IsNumericField(field.DataType));
        var summaryRowIndex = FindSummaryRowIndex(tablix);
        for (var rowIndex = 0; rowIndex < tablix.Rows.Count; rowIndex++)
        {
            var row = tablix.Rows[rowIndex];
            while (row.Cells.Count < totalColumnIndex)
            {
                row.Cells.Add(new ReportTablixCellDefinition());
            }

            row.Cells.Add(CreateSummaryColumnCell(row.IsHeader));
        }

        if (tablix.Rows.Count == 0)
        {
            tablix.Rows.Add(new ReportTablixRowDefinition
            {
                Id = "header",
                IsHeader = true,
                Cells =
                {
                    new ReportTablixCellDefinition { Text = "Total" }
                }
            });
        }

        var (summaryRow, resolvedSummaryRowIndex) = EnsureSummaryRow(tablix, dataSet);
        summaryRowIndex = resolvedSummaryRowIndex;
        while (summaryRow.Cells.Count < tablix.Columns.Count)
        {
            summaryRow.Cells.Add(new ReportTablixCellDefinition());
        }

        if (summaryRow.Cells.Count > 0 && string.IsNullOrWhiteSpace(summaryRow.Cells[0].Text))
        {
            summaryRow.Cells[0] = new ReportTablixCellDefinition
            {
                Text = "Total"
            };
        }

        summaryRow.Cells[totalColumnIndex] = CreateSummaryAggregateCell(numericField);

        EnsureTablixColumnMembersInitialized(tablix);
        tablix.ColumnMembers.Add(new ReportTablixMemberDefinition
        {
            Id = CreateUniqueId("totalColumnMember", EnumerateTablixMemberIds(tablix)),
            Kind = ReportTablixMemberKind.Static,
            ColumnDefinitionIndex = totalColumnIndex
        });

        RebuildDesignerState(tablix);
        SelectedCenterTabIndex = 0;
        MarkDirty("Added tablix total column.");
    }

    private List<ReportTablixCellDefinition> BuildSummaryRowCells(TablixItem tablix, ReportDataSetDefinition? dataSet)
    {
        var cells = new List<ReportTablixCellDefinition>(tablix.Columns.Count);
        var detailRow = tablix.Rows.FirstOrDefault(static row => !row.IsHeader);
        for (var columnIndex = 0; columnIndex < tablix.Columns.Count; columnIndex++)
        {
            var detailCell = detailRow is not null && columnIndex < detailRow.Cells.Count
                ? detailRow.Cells[columnIndex]
                : null;
            var fieldName = TryExtractBoundFieldName(detailCell?.ValueExpression);
            var field = fieldName is null
                ? null
                : dataSet?.ExpectedFields.FirstOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.OrdinalIgnoreCase));

            if (columnIndex == 0)
            {
                cells.Add(new ReportTablixCellDefinition
                {
                    Text = "Total"
                });
                continue;
            }

            if (field is not null && IsNumericField(field.DataType))
            {
                cells.Add(new ReportTablixCellDefinition
                {
                    ValueExpression = $"Sum(Fields.{field.Name})",
                    FormatString = detailCell?.FormatString
                });
                continue;
            }

            cells.Add(new ReportTablixCellDefinition
            {
                Text = string.Empty
            });
        }

        return cells;
    }

    private static ReportTablixCellDefinition CreateSummaryColumnCell(bool isHeader)
    {
        if (isHeader)
        {
            return new ReportTablixCellDefinition
            {
                Text = "Total"
            };
        }

        return new ReportTablixCellDefinition
        {
            Text = string.Empty
        };
    }

    private static ReportTablixCellDefinition CreateSummaryAggregateCell(ReportFieldDefinition? numericField)
    {
        if (numericField is null)
        {
            return new ReportTablixCellDefinition
            {
                Text = string.Empty
            };
        }

        return new ReportTablixCellDefinition
        {
            ValueExpression = $"Sum(Fields.{numericField.Name})"
        };
    }

    private ReportItem? CreateInsertToolItem(ReportDesignerInsertTool tool, ReportSection section, ReportItemBounds bounds)
    {
        return tool switch
        {
            ReportDesignerInsertTool.TextBox => new TextItem
            {
                Id = CreateUniqueId("text", EnumerateItemIds()),
                Name = "Text Box",
                StaticText = "Text",
                Bounds = bounds
            },
            ReportDesignerInsertTool.Tablix => CreateInsertTablix(section, bounds),
            ReportDesignerInsertTool.Chart => CreateDefaultChart(bounds),
            ReportDesignerInsertTool.Rectangle => new ContainerItem
            {
                Id = CreateUniqueId("rectangle", EnumerateItemIds()),
                Name = "Rectangle",
                Bounds = bounds
            },
            ReportDesignerInsertTool.Line => new LineItem
            {
                Id = CreateUniqueId("line", EnumerateItemIds()),
                Name = "Line",
                Bounds = bounds,
                X2 = bounds.X + bounds.Width,
                Y2 = bounds.Y + Math.Max(1f, bounds.Height)
            },
            ReportDesignerInsertTool.Image => new ImageItem
            {
                Id = CreateUniqueId("image", EnumerateItemIds()),
                Name = "Image",
                SourceKind = ReportImageSourceKind.Uri,
                ValueExpression = "https://via.placeholder.com/640x360.png",
                MimeType = "image/png",
                Bounds = bounds
            },
            ReportDesignerInsertTool.Subreport => CreateDefaultSubreport(bounds),
            ReportDesignerInsertTool.Template => CreateDefaultTemplateItem(bounds),
            _ => null
        };
    }

    private ChartItem CreateDefaultChart(ReportItemBounds bounds)
    {
        var chart = new ChartItem
        {
            Id = CreateUniqueId("chart", EnumerateItemIds()),
            Name = "Chart",
            DataSetId = EnsureDataSetForGallery(),
            CategoryExpression = "Fields.Category",
            TitleExpression = "'Chart'",
            Bounds = bounds
        };
        chart.Series.Add(new ReportChartSeriesDefinition
        {
            NameExpression = "'Series'",
            ValueExpression = "Fields.Value"
        });
        return chart;
    }

    private SubreportItem CreateDefaultSubreport(ReportItemBounds bounds)
    {
        var reportReferenceId = Source.ReferencedReports.Keys.FirstOrDefault()
            ?? "subreport";
        return new SubreportItem
        {
            Id = CreateUniqueId("subreport", EnumerateItemIds()),
            Name = "Subreport",
            ReportReferenceId = reportReferenceId,
            Bounds = bounds
        };
    }

    private DocumentTemplateItem CreateDefaultTemplateItem(ReportItemBounds bounds)
    {
        var templateDefinition = EnsureSharedTemplate("narrative", ReportDocumentTemplateFormat.Markdown, "# Narrative\n\n{{Title}}");
        var item = new DocumentTemplateItem
        {
            Id = CreateUniqueId("templateItem", EnumerateItemIds()),
            Name = "Narrative Block",
            TemplateId = templateDefinition.Id,
            TemplateFormat = templateDefinition.Format,
            Bounds = bounds
        };
        item.Bindings["Title"] = "Parameters.Title";
        return item;
    }

    private TablixItem CreateInsertTablix(ReportSection section, ReportItemBounds bounds)
    {
        var tablix = CreateDefaultTablix(
            CreateUniqueId("tablix", EnumerateItemIds()),
            EnsureDataSetForGallery(),
            section);
        tablix.Bounds = bounds;
        return tablix;
    }

    private static ReportItemBounds CreateInsertBounds(
        ReportDesignerInsertTool tool,
        ReportSection section,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var width = Math.Abs(endX - startX);
        var height = Math.Abs(endY - startY);
        var left = Math.Min(startX, endX);
        var top = Math.Min(startY, endY);

        var defaultSize = GetDefaultInsertSize(tool);
        if (width < 8d && height < 8d)
        {
            width = defaultSize.width;
            height = defaultSize.height;
        }
        else
        {
            width = Math.Max(defaultSize.minimumWidth, width);
            height = Math.Max(defaultSize.minimumHeight, height);
        }

        left = Math.Clamp(left, 0d, Math.Max(0d, section.PageSettings.Width - width));
        top = Math.Clamp(top, 0d, Math.Max(0d, section.PageSettings.Height - height));
        return new ReportItemBounds((float)left, (float)top, (float)width, (float)height);
    }

    private static (double width, double height, double minimumWidth, double minimumHeight) GetDefaultInsertSize(ReportDesignerInsertTool tool)
    {
        return tool switch
        {
            ReportDesignerInsertTool.TextBox => (220d, 40d, 120d, 32d),
            ReportDesignerInsertTool.Tablix => (480d, 180d, 240d, 120d),
            ReportDesignerInsertTool.Chart => (360d, 220d, 220d, 160d),
            ReportDesignerInsertTool.Rectangle => (360d, 200d, 96d, 64d),
            ReportDesignerInsertTool.Line => (220d, 2d, 48d, 1d),
            ReportDesignerInsertTool.Image => (220d, 140d, 96d, 72d),
            ReportDesignerInsertTool.Subreport => (360d, 220d, 180d, 96d),
            ReportDesignerInsertTool.Template => (420d, 180d, 220d, 96d),
            _ => (220d, 40d, 120d, 32d)
        };
    }

    private static ReportItemBounds OffsetBoundsForDuplicate(ReportItemBounds bounds, ReportItem clone, ReportItem original)
    {
        var width = Math.Max(1f, bounds.Width);
        var height = Math.Max(1f, bounds.Height);
        return bounds with
        {
            X = bounds.X + 24f,
            Y = bounds.Y + 24f,
            Width = width,
            Height = height
        };
    }

    private void AssignDuplicatedIds(ReportItem item)
    {
        item.Id = CreateUniqueId(item.Id, EnumerateItemIds());
        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            item.Name = item.Name + " Copy";
        }

        switch (item)
        {
            case ContainerItem containerItem:
                for (var index = 0; index < containerItem.Items.Count; index++)
                {
                    AssignDuplicatedIds(containerItem.Items[index]);
                }

                break;
            case TablixItem tablixItem:
                for (var columnIndex = 0; columnIndex < tablixItem.Columns.Count; columnIndex++)
                {
                    tablixItem.Columns[columnIndex].Id = CreateUniqueId(
                        tablixItem.Columns[columnIndex].Id,
                        tablixItem.Columns.Select(static candidate => candidate.Id));
                }

                for (var rowIndex = 0; rowIndex < tablixItem.Rows.Count; rowIndex++)
                {
                    tablixItem.Rows[rowIndex].Id = CreateUniqueId(
                        tablixItem.Rows[rowIndex].Id,
                        tablixItem.Rows.Select(static candidate => candidate.Id));
                }

                for (var memberIndex = 0; memberIndex < tablixItem.RowMembers.Count; memberIndex++)
                {
                    AssignDuplicatedIds(tablixItem.RowMembers[memberIndex]);
                }

                for (var memberIndex = 0; memberIndex < tablixItem.ColumnMembers.Count; memberIndex++)
                {
                    AssignDuplicatedIds(tablixItem.ColumnMembers[memberIndex]);
                }

                break;
        }
    }

    private void AssignDuplicatedIds(ReportTablixMemberDefinition member)
    {
        member.Id = member.Id + "_copy";
        for (var index = 0; index < member.Members.Count; index++)
        {
            AssignDuplicatedIds(member.Members[index]);
        }
    }

    private bool TryGetOwningCollection(ReportItem item, out List<ReportItem> siblings)
    {
        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is not null)
        {
            siblings = parentContainer.Items;
            return true;
        }

        if (_itemSectionMap.TryGetValue(item, out var ownerSection))
        {
            if (ownerSection.BodyItems.Contains(item))
            {
                siblings = ownerSection.BodyItems;
                return true;
            }

            if (ownerSection.HeaderItems.Contains(item))
            {
                siblings = ownerSection.HeaderItems;
                return true;
            }

            if (ownerSection.FooterItems.Contains(item))
            {
                siblings = ownerSection.FooterItems;
                return true;
            }
        }

        siblings = [];
        return false;
    }

    private static void NormalizeSiblingZOrder(IList<ReportItem> siblings)
    {
        for (var index = 0; index < siblings.Count; index++)
        {
            siblings[index].ZIndex = index;
        }
    }

    private void EnsureTablixRowMembersInitialized(TablixItem tablix)
    {
        if (tablix.RowMembers.Count > 0)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < tablix.Rows.Count; rowIndex++)
        {
            var row = tablix.Rows[rowIndex];
            tablix.RowMembers.Add(new ReportTablixMemberDefinition
            {
                Id = $"{row.Id}_member",
                Kind = row.IsHeader ? ReportTablixMemberKind.Static : ReportTablixMemberKind.Details,
                RowDefinitionIndex = rowIndex
            });
        }
    }

    private void EnsureTablixMembersInitialized(TablixItem tablix, ReportDesignerTablixHierarchyAxis axis)
    {
        if (axis == ReportDesignerTablixHierarchyAxis.Column)
        {
            EnsureTablixColumnMembersInitialized(tablix);
        }
        else
        {
            EnsureTablixRowMembersInitialized(tablix);
        }
    }

    private void EnsureTablixColumnMembersInitialized(TablixItem tablix)
    {
        if (tablix.ColumnMembers.Count > 0)
        {
            return;
        }

        for (var columnIndex = 0; columnIndex < tablix.Columns.Count; columnIndex++)
        {
            var column = tablix.Columns[columnIndex];
            tablix.ColumnMembers.Add(new ReportTablixMemberDefinition
            {
                Id = $"{column.Id}_member",
                Kind = ReportTablixMemberKind.Static,
                ColumnDefinitionIndex = columnIndex
            });
        }
    }

    private static List<ReportTablixMemberDefinition> GetGroupingMembers(TablixItem tablix, ReportDesignerTablixHierarchyAxis axis)
    {
        return axis == ReportDesignerTablixHierarchyAxis.Column
            ? tablix.ColumnMembers
            : tablix.RowMembers;
    }

    private ReportTablixMemberDefinition? GetSelectedGroupingMember(ReportDesignerTablixHierarchyAxis axis)
    {
        return axis == ReportDesignerTablixHierarchyAxis.Column
            ? SelectedColumnGroupEntry?.Member
            : SelectedRowGroupEntry?.Member;
    }

    private void BindTablixToDataSet(TablixItem tablix, ReportDataSetDefinition dataSet)
    {
        if (!string.Equals(tablix.DataSetId, dataSet.Id, StringComparison.OrdinalIgnoreCase))
        {
            tablix.DataSetId = dataSet.Id;
        }
    }

    private static ReportTablixMemberDefinition? FindDefaultGroupingTarget(IReadOnlyList<ReportTablixMemberDefinition> members)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var candidate = members[index];
            if (candidate.Kind != ReportTablixMemberKind.Static)
            {
                return candidate;
            }

            var nestedCandidate = FindDefaultGroupingTarget(candidate.Members);
            if (nestedCandidate is not null)
            {
                return nestedCandidate;
            }
        }

        return null;
    }

    private static bool TryInsertAdjacentMember(
        IList<ReportTablixMemberDefinition> members,
        ReportTablixMemberDefinition target,
        ReportTablixMemberDefinition adjacentMember)
    {
        for (var index = 0; index < members.Count; index++)
        {
            if (ReferenceEquals(members[index], target))
            {
                members.Insert(index + 1, adjacentMember);
                return true;
            }

            if (TryInsertAdjacentMember(members[index].Members, target, adjacentMember))
            {
                return true;
            }
        }

        return false;
    }

    private static ReportTablixMemberDefinition? FindDefaultColumnGroupingTarget(IReadOnlyList<ReportTablixMemberDefinition> members)
    {
        return FindDefaultGroupingTarget(members) ?? FindFirstLeafMember(members);
    }

    private static ReportTablixMemberDefinition? FindFirstLeafMember(IReadOnlyList<ReportTablixMemberDefinition> members)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var candidate = members[index];
            if (candidate.Members.Count == 0)
            {
                return candidate;
            }

            var nestedCandidate = FindFirstLeafMember(candidate.Members);
            if (nestedCandidate is not null)
            {
                return nestedCandidate;
            }
        }

        return null;
    }

    private (ReportTablixRowDefinition Row, int RowIndex) EnsureSummaryRow(TablixItem tablix, ReportDataSetDefinition? dataSet)
    {
        var existingIndex = FindSummaryRowIndex(tablix);
        if (existingIndex >= 0)
        {
            return (tablix.Rows[existingIndex], existingIndex);
        }

        var rowIndex = tablix.Rows.Count;
        var totalRow = new ReportTablixRowDefinition
        {
            Id = CreateUniqueId("totalRow", tablix.Rows.Select(static row => row.Id)),
            Cells = BuildSummaryRowCells(tablix, dataSet)
        };
        tablix.Rows.Add(totalRow);

        EnsureTablixRowMembersInitialized(tablix);
        tablix.RowMembers.Add(new ReportTablixMemberDefinition
        {
            Id = CreateUniqueId("totalRowMember", EnumerateTablixMemberIds(tablix)),
            Kind = ReportTablixMemberKind.Static,
            RowDefinitionIndex = rowIndex,
            KeepWithGroup = "After",
            RepeatOnNewPage = false
        });

        return (totalRow, rowIndex);
    }

    private static int FindSummaryRowIndex(TablixItem tablix)
    {
        var staticRowIndexes = new HashSet<int>();
        CollectStaticRowIndexes(tablix.RowMembers, staticRowIndexes);
        for (var rowIndex = tablix.Rows.Count - 1; rowIndex >= 0; rowIndex--)
        {
            if (!tablix.Rows[rowIndex].IsHeader && staticRowIndexes.Contains(rowIndex))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static void CollectStaticRowIndexes(
        IReadOnlyList<ReportTablixMemberDefinition> members,
        ISet<int> rowIndexes)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            if (member.Kind == ReportTablixMemberKind.Static && member.RowDefinitionIndex.HasValue)
            {
                rowIndexes.Add(member.RowDefinitionIndex.Value);
            }

            CollectStaticRowIndexes(member.Members, rowIndexes);
        }
    }

    private ReportTablixMemberDefinition CreateWrappedGroupMember(
        TablixItem tablix,
        ReportDesignerTablixHierarchyAxis axis,
        ReportTablixMemberDefinition existingMember,
        string fieldName)
    {
        return axis == ReportDesignerTablixHierarchyAxis.Column
            ? CreateWrappedColumnGroupMember(tablix, existingMember, fieldName)
            : CreateWrappedRowGroupMember(tablix, existingMember, fieldName);
    }

    private ReportTablixMemberDefinition CreateWrappedRowGroupMember(
        TablixItem tablix,
        ReportTablixMemberDefinition existingMember,
        string fieldName)
    {
        return new ReportTablixMemberDefinition
        {
            Id = CreateUniqueId($"{fieldName}Group", EnumerateTablixMemberIds(tablix)),
            Kind = ReportTablixMemberKind.Group,
            GroupName = fieldName + " Group",
            GroupExpression = CreateGroupingFieldExpression(fieldName),
            Members =
            {
                existingMember
            }
        };
    }

    private ReportTablixMemberDefinition CreateWrappedColumnGroupMember(
        TablixItem tablix,
        ReportTablixMemberDefinition existingMember,
        string fieldName)
    {
        return new ReportTablixMemberDefinition
        {
            Id = CreateUniqueId($"{fieldName}ColumnGroup", EnumerateTablixMemberIds(tablix)),
            Kind = ReportTablixMemberKind.Group,
            GroupName = fieldName + " Column Group",
            GroupExpression = CreateGroupingFieldExpression(fieldName),
            Members =
            {
                existingMember
            }
        };
    }

    private ReportTablixMemberDefinition CreateFallbackLeafMember(TablixItem tablix, ReportDesignerTablixHierarchyAxis axis)
    {
        if (axis == ReportDesignerTablixHierarchyAxis.Column)
        {
            var columnIndex = tablix.Columns.Count == 0 ? 0 : tablix.Columns.Count - 1;
            return new ReportTablixMemberDefinition
            {
                Id = CreateUniqueId("columnLeaf", EnumerateTablixMemberIds(tablix)),
                Kind = ReportTablixMemberKind.Static,
                ColumnDefinitionIndex = columnIndex
            };
        }

        var detailRowIndex = tablix.Rows.FindIndex(static row => !row.IsHeader);
        if (detailRowIndex < 0)
        {
            detailRowIndex = Math.Max(0, tablix.Rows.Count - 1);
        }

        return new ReportTablixMemberDefinition
        {
            Id = CreateUniqueId("rowLeaf", EnumerateTablixMemberIds(tablix)),
            Kind = ReportTablixMemberKind.Details,
            RowDefinitionIndex = detailRowIndex
        };
    }

    private static ReportTablixMemberDefinition CloneMemberDefinition(ReportTablixMemberDefinition member)
    {
        return new ReportTablixMemberDefinition
        {
            Id = member.Id,
            Kind = member.Kind,
            GroupName = member.GroupName,
            GroupExpression = member.GroupExpression,
            SortExpression = member.SortExpression,
            SortDirection = member.SortDirection,
            VisibilityExpression = member.VisibilityExpression,
            ToggleItemId = member.ToggleItemId,
            RepeatOnNewPage = member.RepeatOnNewPage,
            KeepWithGroup = member.KeepWithGroup,
            PageBreak = member.PageBreak is null
                ? null
                : new ReportPageBreakDefinition
                {
                    DisabledExpression = member.PageBreak.DisabledExpression,
                    Location = member.PageBreak.Location
                },
            RowDefinitionIndex = member.RowDefinitionIndex,
            ColumnDefinitionIndex = member.ColumnDefinitionIndex,
            Members = member.Members.Select(CloneMemberDefinition).ToList()
        };
    }

    private IEnumerable<string> EnumerateTablixMemberIds(TablixItem tablix)
    {
        foreach (var member in tablix.RowMembers)
        {
            foreach (var id in EnumerateMemberIds(member))
            {
                yield return id;
            }
        }

        foreach (var member in tablix.ColumnMembers)
        {
            foreach (var id in EnumerateMemberIds(member))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> EnumerateMemberIds(ReportTablixMemberDefinition member)
    {
        yield return member.Id;
        foreach (var child in member.Members)
        {
            foreach (var id in EnumerateMemberIds(child))
            {
                yield return id;
            }
        }
    }

    private void AssignUniqueMemberIds(TablixItem tablix, ReportTablixMemberDefinition member)
    {
        var used = new HashSet<string>(
            EnumerateTablixMemberIds(tablix).Where(static value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
        AssignUniqueMemberIds(member, used);
    }

    private void AssignUniqueMemberIds(ReportTablixMemberDefinition member, ISet<string> usedIds)
    {
        var baseId = string.IsNullOrWhiteSpace(member.Id) ? "member" : member.Id;
        member.Id = CreateUniqueId(baseId, usedIds);
        usedIds.Add(member.Id);
        for (var index = 0; index < member.Members.Count; index++)
        {
            AssignUniqueMemberIds(member.Members[index], usedIds);
        }
    }

    private static string CreateGroupingFieldExpression(string fieldName)
    {
        return $"Fields.{fieldName}";
    }

    private void FinalizeGroupingChange(
        TablixItem tablix,
        ReportDesignerTablixHierarchyAxis axis,
        ReportTablixMemberDefinition selectedMember,
        string message)
    {
        RebuildDesignerState(tablix);
        SelectedCenterTabIndex = 0;
        if (axis == ReportDesignerTablixHierarchyAxis.Column)
        {
            SelectedColumnGroupEntry = _columnGroupEntries.FirstOrDefault(entry => ReferenceEquals(entry.Member, selectedMember));
        }
        else
        {
            SelectedRowGroupEntry = _rowGroupEntries.FirstOrDefault(entry => ReferenceEquals(entry.Member, selectedMember));
        }

        MarkDirty(message);
    }

    private ReportDataSetDefinition? ResolveTablixDataSet(TablixItem tablix)
    {
        return ReportDefinition.DataSets.FirstOrDefault(
            candidate => string.Equals(candidate.Id, tablix.DataSetId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryExtractBoundFieldName(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        const string marker = "Fields.";
        var markerIndex = expression.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        markerIndex += marker.Length;
        var endIndex = markerIndex;
        while (endIndex < expression.Length)
        {
            var current = expression[endIndex];
            if (!(char.IsLetterOrDigit(current) || current == '_'))
            {
                break;
            }

            endIndex++;
        }

        return endIndex > markerIndex ? expression[markerIndex..endIndex] : null;
    }

    private static bool TryReplaceMember(
        IList<ReportTablixMemberDefinition> members,
        ReportTablixMemberDefinition target,
        Func<ReportTablixMemberDefinition, IEnumerable<ReportTablixMemberDefinition>> replacementFactory)
    {
        for (var index = 0; index < members.Count; index++)
        {
            if (ReferenceEquals(members[index], target))
            {
                members.RemoveAt(index);
                var replacements = replacementFactory(target).ToList();
                for (var replacementIndex = 0; replacementIndex < replacements.Count; replacementIndex++)
                {
                    members.Insert(index + replacementIndex, replacements[replacementIndex]);
                }

                return true;
            }

            if (TryReplaceMember(members[index].Members, target, replacementFactory))
            {
                return true;
            }
        }

        return false;
    }

    internal void ApplySnapGuides(IReadOnlyList<ReportDesignerSnapGuideViewModel> guides)
    {
        _snapGuides.Clear();
        for (var index = 0; index < guides.Count; index++)
        {
            _snapGuides.Add(guides[index]);
        }
    }

    internal float SnapHorizontalMove(ReportItem item, float left, float width, List<ReportDesignerSnapGuideViewModel> guides)
    {
        var bestDelta = DesignerSnapGuideThreshold + 1f;
        var snappedLeft = left;
        var bestGuide = default(ReportDesignerSnapGuideViewModel?);
        foreach (var guide in EnumerateVerticalSnapTargets(item))
        {
            EvaluateHorizontalCandidate(guide, left, width, ref bestDelta, ref snappedLeft, ref bestGuide);
        }

        if (bestGuide is not null)
        {
            guides.Add(bestGuide);
        }

        return snappedLeft;
    }

    internal float SnapVerticalMove(ReportItem item, float top, float height, List<ReportDesignerSnapGuideViewModel> guides)
    {
        var bestDelta = DesignerSnapGuideThreshold + 1f;
        var snappedTop = top;
        var bestGuide = default(ReportDesignerSnapGuideViewModel?);
        foreach (var guide in EnumerateHorizontalSnapTargets(item))
        {
            EvaluateVerticalCandidate(guide, top, height, ref bestDelta, ref snappedTop, ref bestGuide);
        }

        if (bestGuide is not null)
        {
            guides.Add(bestGuide);
        }

        return snappedTop;
    }

    internal float SnapHorizontalEdge(ReportItem item, float edge, bool isLeftEdge, List<ReportDesignerSnapGuideViewModel> guides)
    {
        var bestDelta = DesignerSnapGuideThreshold + 1f;
        var snapped = edge;
        var bestGuide = default(ReportDesignerSnapGuideViewModel?);
        foreach (var guide in EnumerateVerticalSnapTargets(item))
        {
            var delta = Math.Abs(edge - guide);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                snapped = guide;
                bestGuide = new ReportDesignerSnapGuideViewModel(isHorizontal: false, guide, SurfaceHeight);
            }
        }

        if (bestGuide is not null && bestDelta <= DesignerSnapGuideThreshold)
        {
            guides.Add(bestGuide);
            return snapped;
        }

        return edge;
    }

    internal float SnapVerticalEdge(ReportItem item, float edge, bool isTopEdge, List<ReportDesignerSnapGuideViewModel> guides)
    {
        var bestDelta = DesignerSnapGuideThreshold + 1f;
        var snapped = edge;
        var bestGuide = default(ReportDesignerSnapGuideViewModel?);
        foreach (var guide in EnumerateHorizontalSnapTargets(item))
        {
            var delta = Math.Abs(edge - guide);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                snapped = guide;
                bestGuide = new ReportDesignerSnapGuideViewModel(isHorizontal: true, guide, SurfaceWidth);
            }
        }

        if (bestGuide is not null && bestDelta <= DesignerSnapGuideThreshold)
        {
            guides.Add(bestGuide);
            return snapped;
        }

        return edge;
    }

    private void EvaluateHorizontalCandidate(
        float guide,
        float left,
        float width,
        ref float bestDelta,
        ref float snappedLeft,
        ref ReportDesignerSnapGuideViewModel? bestGuide)
    {
        EvaluateHorizontalAnchor(guide, left, ref bestDelta, ref snappedLeft, ref bestGuide);
        EvaluateHorizontalAnchor(guide, left + width, ref bestDelta, ref snappedLeft, ref bestGuide, -width);
        EvaluateHorizontalAnchor(guide, left + (width / 2f), ref bestDelta, ref snappedLeft, ref bestGuide, -(width / 2f));
    }

    private void EvaluateVerticalCandidate(
        float guide,
        float top,
        float height,
        ref float bestDelta,
        ref float snappedTop,
        ref ReportDesignerSnapGuideViewModel? bestGuide)
    {
        EvaluateVerticalAnchor(guide, top, ref bestDelta, ref snappedTop, ref bestGuide);
        EvaluateVerticalAnchor(guide, top + height, ref bestDelta, ref snappedTop, ref bestGuide, -height);
        EvaluateVerticalAnchor(guide, top + (height / 2f), ref bestDelta, ref snappedTop, ref bestGuide, -(height / 2f));
    }

    private void EvaluateHorizontalAnchor(
        float guide,
        float anchor,
        ref float bestDelta,
        ref float snappedLeft,
        ref ReportDesignerSnapGuideViewModel? bestGuide,
        float adjust = 0f)
    {
        var delta = Math.Abs(anchor - guide);
        if (delta > DesignerSnapGuideThreshold || delta >= bestDelta)
        {
            return;
        }

        bestDelta = delta;
        snappedLeft = guide + adjust;
        bestGuide = new ReportDesignerSnapGuideViewModel(isHorizontal: false, guide, SurfaceHeight);
    }

    private void EvaluateVerticalAnchor(
        float guide,
        float anchor,
        ref float bestDelta,
        ref float snappedTop,
        ref ReportDesignerSnapGuideViewModel? bestGuide,
        float adjust = 0f)
    {
        var delta = Math.Abs(anchor - guide);
        if (delta > DesignerSnapGuideThreshold || delta >= bestDelta)
        {
            return;
        }

        bestDelta = delta;
        snappedTop = guide + adjust;
        bestGuide = new ReportDesignerSnapGuideViewModel(isHorizontal: true, guide, SurfaceWidth);
    }

    private IEnumerable<float> EnumerateVerticalSnapTargets(ReportItem item)
    {
        var (left, right) = GetSnapHostHorizontalBounds(item);
        yield return left;
        yield return right;
            yield return left + ((right - left) / 2f);

        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is null)
        {
            yield return (float)SurfaceMarginLeft;
            yield return (float)(SurfaceWidth - SurfaceMarginRight);
        }

        foreach (var peer in EnumerateSnapPeers(item))
        {
            yield return (float)peer.Left;
            yield return (float)(peer.Left + peer.Width);
            yield return (float)(peer.Left + (peer.Width / 2d));
        }
    }

    private IEnumerable<float> EnumerateHorizontalSnapTargets(ReportItem item)
    {
        var (top, bottom) = GetSnapHostVerticalBounds(item);
        yield return top;
        yield return bottom;
        yield return top + ((bottom - top) / 2f);

        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is null)
        {
            yield return (float)SurfaceMarginTop;
            yield return (float)(SurfaceHeight - SurfaceMarginBottom);
        }

        foreach (var peer in EnumerateSnapPeers(item))
        {
            yield return (float)peer.Top;
            yield return (float)(peer.Top + peer.Height);
            yield return (float)(peer.Top + (peer.Height / 2d));
        }
    }

    private (float left, float right) GetSnapHostHorizontalBounds(ReportItem item)
    {
        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is not null && _itemCanvasMap.TryGetValue(parentContainer, out var parentCanvas))
        {
            return ((float)parentCanvas.Left, (float)(parentCanvas.Left + parentCanvas.Width));
        }

        return (0f, (float)SurfaceWidth);
    }

    private (float top, float bottom) GetSnapHostVerticalBounds(ReportItem item)
    {
        if (_itemContainerMap.TryGetValue(item, out var parentContainer) && parentContainer is not null && _itemCanvasMap.TryGetValue(parentContainer, out var parentCanvas))
        {
            return ((float)parentCanvas.Top, (float)(parentCanvas.Top + parentCanvas.Height));
        }

        return (0f, (float)SurfaceHeight);
    }

    private IEnumerable<ReportDesignerCanvasItemViewModel> EnumerateSnapPeers(ReportItem item)
    {
        foreach (var canvasItem in _canvasItems)
        {
            if (ReferenceEquals(canvasItem.Item, item) || canvasItem.IsReadOnly)
            {
                continue;
            }

            if (_itemContainerMap.TryGetValue(canvasItem.Item, out var candidateParent) != _itemContainerMap.TryGetValue(item, out var itemParent))
            {
                continue;
            }

            if (!ReferenceEquals(candidateParent, itemParent))
            {
                continue;
            }

            yield return canvasItem;
        }
    }
}
