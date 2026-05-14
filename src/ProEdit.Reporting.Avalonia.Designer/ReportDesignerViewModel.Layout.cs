using System.Reactive;
using ReactiveUI;
using ProEdit.Reporting.Avalonia;

namespace ProEdit.Reporting.Avalonia.Designer;

public sealed partial class ReportDesignerViewModel
{
    private const double DesignerDrawerWidth = 304d;
    private const double DesignerInspectorWidth = 336d;
    private const double DesignerInspectorMinWidth = 280d;
    private const double DesignerInspectorMaxWidth = 640d;

    private PaneVisibilityState _contextTrayState = PaneVisibilityState.Closed;
    private PaneVisibilityState _leftDrawerState = PaneVisibilityState.Closed;
    private PaneVisibilityState _rightDrawerState = PaneVisibilityState.Closed;
    private DesignerContextPaneKind _activeContextPaneKind = DesignerContextPaneKind.None;
    private DesignerPaneKind _activeLeftPaneKind = DesignerPaneKind.ReportData;
    private DesignerPaneKind _activeRightPaneKind = DesignerPaneKind.Properties;
    private double _inspectorDrawerWidth = DesignerInspectorWidth;
    private int _selectedReportDataPaneTabIndex;
    private bool _showSurfacePreviewBackground = true;

    /// <summary>
    /// Gets or sets the left drawer visibility state.
    /// </summary>
    public PaneVisibilityState LeftDrawerState
    {
        get => _leftDrawerState;
        set
        {
            if (_leftDrawerState == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _leftDrawerState, value);
            RaiseLayoutStatePropertiesChanged();
        }
    }

    /// <summary>
    /// Gets or sets the right drawer visibility state.
    /// </summary>
    public PaneVisibilityState RightDrawerState
    {
        get => _rightDrawerState;
        set
        {
            if (_rightDrawerState == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _rightDrawerState, value);
            RaiseLayoutStatePropertiesChanged();
        }
    }

    /// <summary>
    /// Gets or sets the contextual bottom-tray visibility state.
    /// </summary>
    public PaneVisibilityState ContextTrayState
    {
        get => _contextTrayState;
        set
        {
            if (_contextTrayState == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _contextTrayState, value);
            RaiseLayoutStatePropertiesChanged();
        }
    }

    /// <summary>
    /// Gets the currently focused left drawer pane kind.
    /// </summary>
    public DesignerPaneKind ActiveLeftPaneKind
    {
        get => _activeLeftPaneKind;
        private set
        {
            if (_activeLeftPaneKind == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _activeLeftPaneKind, value);
        }
    }

    /// <summary>
    /// Gets the currently focused right drawer pane kind.
    /// </summary>
    public DesignerPaneKind ActiveRightPaneKind
    {
        get => _activeRightPaneKind;
        private set
        {
            if (_activeRightPaneKind == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _activeRightPaneKind, value);
        }
    }

    /// <summary>
    /// Gets the active contextual tray pane kind.
    /// </summary>
    public DesignerContextPaneKind ActiveContextPaneKind
    {
        get => _activeContextPaneKind;
        private set
        {
            if (_activeContextPaneKind == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _activeContextPaneKind, value);
            RaiseLayoutStatePropertiesChanged();
        }
    }

    /// <summary>
    /// Gets or sets the selected tab in the report-data drawer.
    /// </summary>
    public int SelectedReportDataPaneTabIndex
    {
        get => _selectedReportDataPaneTabIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (_selectedReportDataPaneTabIndex == clamped)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedReportDataPaneTabIndex, clamped);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the live preview background is shown behind the design surface.
    /// </summary>
    public bool ShowSurfacePreviewBackground
    {
        get => _showSurfacePreviewBackground;
        set
        {
            if (_showSurfacePreviewBackground == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showSurfacePreviewBackground, value);
            this.RaisePropertyChanged(nameof(HasVisibleSurfacePreview));
            UpdateViewStateStatus(value
                ? "Displayed the preview background on the design surface."
                : "Hid the preview background on the design surface.");
        }
    }

    /// <summary>
    /// Gets a value indicating whether the left drawer is visible.
    /// </summary>
    public bool IsLeftDrawerVisible => LeftDrawerState != PaneVisibilityState.Closed;

    /// <summary>
    /// Gets a value indicating whether the right drawer is visible.
    /// </summary>
    public bool IsRightDrawerVisible => RightDrawerState != PaneVisibilityState.Closed;

    /// <summary>
    /// Gets a value indicating whether the bottom contextual tray is visible.
    /// </summary>
    public bool IsContextTrayVisible => ContextTrayState != PaneVisibilityState.Closed && ActiveContextPaneKind != DesignerContextPaneKind.None;

    /// <summary>
    /// Gets a value indicating whether the left drawer is pinned.
    /// </summary>
    public bool IsLeftDrawerPinned => LeftDrawerState == PaneVisibilityState.Pinned;

    /// <summary>
    /// Gets a value indicating whether the right drawer is pinned.
    /// </summary>
    public bool IsRightDrawerPinned => RightDrawerState == PaneVisibilityState.Pinned;

    /// <summary>
    /// Gets a value indicating whether the context tray is pinned.
    /// </summary>
    public bool IsContextTrayPinned => ContextTrayState == PaneVisibilityState.Pinned;

    /// <summary>
    /// Gets a value indicating whether the surface preview image should be shown.
    /// </summary>
    public bool HasVisibleSurfacePreview => ShowSurfacePreviewBackground && HasCurrentSurfacePreview;

    /// <summary>
    /// Gets a value indicating whether the grouping tray should be shown.
    /// </summary>
    public bool ShowGroupingTray => IsContextTrayVisible && ActiveContextPaneKind == DesignerContextPaneKind.Grouping && ShowGroupingPane;

    /// <summary>
    /// Gets a value indicating whether the chart-data tray should be shown.
    /// </summary>
    public bool ShowChartDataTray => IsContextTrayVisible && ActiveContextPaneKind == DesignerContextPaneKind.ChartData && ShowChartDataPane;

    /// <summary>
    /// Gets a value indicating whether the parameter-layout tray should be shown.
    /// </summary>
    public bool ShowParameterLayoutTray => IsContextTrayVisible && ActiveContextPaneKind == DesignerContextPaneKind.ParameterLayout && ShowParameterLayoutPane;

    /// <summary>
    /// Gets the left drawer width.
    /// </summary>
    public double LeftDrawerWidth => IsLeftDrawerVisible ? DesignerDrawerWidth : 0d;

    /// <summary>
    /// Gets the right drawer width.
    /// </summary>
    public double RightDrawerWidth => IsRightDrawerVisible ? InspectorDrawerWidth : 0d;

    /// <summary>
    /// Gets or sets the persisted inspector drawer width.
    /// </summary>
    public double InspectorDrawerWidth
    {
        get => _inspectorDrawerWidth;
        set
        {
            var clamped = Math.Clamp(value, DesignerInspectorMinWidth, DesignerInspectorMaxWidth);
            if (Math.Abs(_inspectorDrawerWidth - clamped) < double.Epsilon)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _inspectorDrawerWidth, clamped);
            this.RaisePropertyChanged(nameof(RightDrawerWidth));
        }
    }

    /// <summary>
    /// Gets the open-report-data command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenReportDataPaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-outline command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenOutlinePaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-template-library command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenTemplateLibraryPaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-properties inspector command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenPropertiesInspectorCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-parameters inspector command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenParametersInspectorCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-data inspector command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenDataInspectorCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-expressions inspector command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenExpressionsInspectorCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the close-left-drawer command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CloseLeftDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the pin-left-drawer command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> TogglePinLeftDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the close-right-drawer command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CloseRightDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the pin-right-drawer command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> TogglePinRightDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-grouping-tray command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenGroupingTrayCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-chart-data-tray command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenChartDataTrayCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the open-parameter-layout-tray command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenParameterLayoutTrayCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the close-context-tray command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CloseContextTrayCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the pin-context-tray command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> TogglePinContextTrayCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the toggle-rulers command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleRulersCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the toggle-preview-background command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> TogglePreviewBackgroundCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the reset-layout command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetLayoutCommand { get; private set; } = null!;

    private void InitializeLayoutWorkspace()
    {
        OpenReportDataPaneCommand = DesignerCommandFactory.Create(() => OpenLeftPane(DesignerPaneKind.ReportData, tabIndex: 0));
        OpenOutlinePaneCommand = DesignerCommandFactory.Create(() => OpenLeftPane(DesignerPaneKind.Outline, tabIndex: 1));
        OpenTemplateLibraryPaneCommand = DesignerCommandFactory.Create(() => OpenLeftPane(DesignerPaneKind.Templates, tabIndex: 2));
        OpenPropertiesInspectorCommand = DesignerCommandFactory.Create(() => OpenRightPane(DesignerPaneKind.Properties, tabIndex: 0));
        OpenParametersInspectorCommand = DesignerCommandFactory.Create(() => OpenRightPane(DesignerPaneKind.Parameters, tabIndex: 1));
        OpenDataInspectorCommand = DesignerCommandFactory.Create(() => OpenRightPane(DesignerPaneKind.Data, tabIndex: 2));
        OpenExpressionsInspectorCommand = DesignerCommandFactory.Create(() => OpenRightPane(DesignerPaneKind.Expressions, tabIndex: 3));
        CloseLeftDrawerCommand = DesignerCommandFactory.Create(() => { LeftDrawerState = PaneVisibilityState.Closed; });
        TogglePinLeftDrawerCommand = DesignerCommandFactory.Create(() => TogglePinnedState(isLeftDrawer: true));
        CloseRightDrawerCommand = DesignerCommandFactory.Create(() => { RightDrawerState = PaneVisibilityState.Closed; });
        TogglePinRightDrawerCommand = DesignerCommandFactory.Create(() => TogglePinnedState(isLeftDrawer: false));
        OpenGroupingTrayCommand = DesignerCommandFactory.Create(() => OpenContextTray(DesignerContextPaneKind.Grouping));
        OpenChartDataTrayCommand = DesignerCommandFactory.Create(() => OpenContextTray(DesignerContextPaneKind.ChartData));
        OpenParameterLayoutTrayCommand = DesignerCommandFactory.Create(() => OpenContextTray(DesignerContextPaneKind.ParameterLayout));
        CloseContextTrayCommand = DesignerCommandFactory.Create(() => { ContextTrayState = PaneVisibilityState.Closed; });
        TogglePinContextTrayCommand = DesignerCommandFactory.Create(TogglePinnedContextTray);
        ToggleRulersCommand = DesignerCommandFactory.Create(() => { ShowRulers = !ShowRulers; });
        TogglePreviewBackgroundCommand = DesignerCommandFactory.Create(() => { ShowSurfacePreviewBackground = !ShowSurfacePreviewBackground; });
        ResetLayoutCommand = DesignerCommandFactory.Create(ResetLayoutState);
    }

    private void OpenLeftPane(DesignerPaneKind paneKind, int tabIndex)
    {
        ActiveLeftPaneKind = paneKind;
        SelectedReportDataPaneTabIndex = tabIndex;
        if (LeftDrawerState == PaneVisibilityState.Closed)
        {
            LeftDrawerState = PaneVisibilityState.Open;
        }
    }

    private void OpenRightPane(DesignerPaneKind paneKind, int tabIndex)
    {
        ActiveRightPaneKind = paneKind;
        SelectedInspectorTabIndex = tabIndex;
        if (RightDrawerState == PaneVisibilityState.Closed)
        {
            RightDrawerState = PaneVisibilityState.Open;
        }
    }

    private void OpenContextTray(DesignerContextPaneKind paneKind)
    {
        if (paneKind == DesignerContextPaneKind.None)
        {
            ContextTrayState = PaneVisibilityState.Closed;
            ActiveContextPaneKind = paneKind;
            return;
        }

        if (paneKind == DesignerContextPaneKind.Grouping && !ShowGroupingPane)
        {
            return;
        }

        if (paneKind == DesignerContextPaneKind.ChartData && !ShowChartDataPane)
        {
            return;
        }

        if (paneKind == DesignerContextPaneKind.ParameterLayout && !ShowParameterLayoutPane)
        {
            return;
        }

        ActiveContextPaneKind = paneKind;
        if (ContextTrayState == PaneVisibilityState.Closed)
        {
            ContextTrayState = PaneVisibilityState.Open;
        }
    }

    private void TogglePinnedState(bool isLeftDrawer)
    {
        if (isLeftDrawer)
        {
            LeftDrawerState = LeftDrawerState switch
            {
                PaneVisibilityState.Closed => PaneVisibilityState.Pinned,
                PaneVisibilityState.Open => PaneVisibilityState.Pinned,
                _ => PaneVisibilityState.Open
            };
        }
        else
        {
            RightDrawerState = RightDrawerState switch
            {
                PaneVisibilityState.Closed => PaneVisibilityState.Pinned,
                PaneVisibilityState.Open => PaneVisibilityState.Pinned,
                _ => PaneVisibilityState.Open
            };
        }
    }

    private void TogglePinnedContextTray()
    {
        ContextTrayState = ContextTrayState switch
        {
            PaneVisibilityState.Closed => PaneVisibilityState.Pinned,
            PaneVisibilityState.Open => PaneVisibilityState.Pinned,
            _ => PaneVisibilityState.Open
        };
    }

    private void ResetLayoutState()
    {
        LeftDrawerState = PaneVisibilityState.Closed;
        RightDrawerState = PaneVisibilityState.Closed;
        ContextTrayState = PaneVisibilityState.Closed;
        InspectorDrawerWidth = DesignerInspectorWidth;
        ActiveContextPaneKind = DesignerContextPaneKind.None;
        SelectedReportDataPaneTabIndex = 0;
        SelectedInspectorTabIndex = 0;
        ShowRulers = true;
        ShowSurfacePreviewBackground = true;
        FitPageToViewport();
        UpdateViewStateStatus("Reset the canvas-first design layout.");
    }

    private void SyncContextTrayFromSelection()
    {
        var suggestedKind = DesignerContextPaneKind.None;
        if (ShowChartDataPane)
        {
            suggestedKind = DesignerContextPaneKind.ChartData;
        }
        else if (ShowGroupingPane)
        {
            suggestedKind = DesignerContextPaneKind.Grouping;
        }

        if (suggestedKind == DesignerContextPaneKind.None)
        {
            if (ContextTrayState != PaneVisibilityState.Pinned
                || ActiveContextPaneKind != DesignerContextPaneKind.ParameterLayout)
            {
                ActiveContextPaneKind = DesignerContextPaneKind.None;
                ContextTrayState = PaneVisibilityState.Closed;
            }
        }
        else
        {
            ActiveContextPaneKind = suggestedKind;
            if (ContextTrayState == PaneVisibilityState.Closed)
            {
                ContextTrayState = PaneVisibilityState.Open;
            }
        }
    }

    private void RaiseLayoutStatePropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(IsLeftDrawerVisible));
        this.RaisePropertyChanged(nameof(IsRightDrawerVisible));
        this.RaisePropertyChanged(nameof(IsContextTrayVisible));
        this.RaisePropertyChanged(nameof(IsLeftDrawerPinned));
        this.RaisePropertyChanged(nameof(IsRightDrawerPinned));
        this.RaisePropertyChanged(nameof(IsContextTrayPinned));
        this.RaisePropertyChanged(nameof(ShowGroupingTray));
        this.RaisePropertyChanged(nameof(ShowChartDataTray));
        this.RaisePropertyChanged(nameof(ShowParameterLayoutTray));
        this.RaisePropertyChanged(nameof(LeftDrawerWidth));
        this.RaisePropertyChanged(nameof(RightDrawerWidth));
    }
}
