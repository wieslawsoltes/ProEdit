using System.Reactive;
using ReactiveUI;
using ProEdit.Reporting.Avalonia;

namespace ProEdit.Reporting.Avalonia.Viewer;

public sealed partial class ReportViewerViewModel
{
    private PaneVisibilityState _leftDrawerState = PaneVisibilityState.Closed;
    private bool _hasInitializedLayoutState;
    private bool _isThumbnailTrayOpen;
    private double _viewportHeight;
    private double _viewportWidth;

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
    /// Gets or sets a value indicating whether the thumbnail filmstrip is expanded.
    /// </summary>
    public bool IsThumbnailTrayOpen
    {
        get => _isThumbnailTrayOpen;
        set
        {
            if (_isThumbnailTrayOpen == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isThumbnailTrayOpen, value);
            RaiseLayoutStatePropertiesChanged();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the left drawer is visible.
    /// </summary>
    public bool IsLeftDrawerVisible => LeftDrawerState != PaneVisibilityState.Closed;

    /// <summary>
    /// Gets a value indicating whether the left drawer is pinned.
    /// </summary>
    public bool IsLeftDrawerPinned => LeftDrawerState == PaneVisibilityState.Pinned;

    /// <summary>
    /// Gets a value indicating whether the currently selected viewer pane is parameters.
    /// </summary>
    public bool IsParametersPaneActive => SelectedPaneIndex == (int)ReportViewerPane.Parameters;

    /// <summary>
    /// Gets a value indicating whether the currently selected viewer pane is outline.
    /// </summary>
    public bool IsOutlinePaneActive => SelectedPaneIndex == (int)ReportViewerPane.Outline;

    /// <summary>
    /// Gets a value indicating whether the currently selected viewer pane is search.
    /// </summary>
    public bool IsSearchPaneActive => SelectedPaneIndex == (int)ReportViewerPane.Search;

    /// <summary>
    /// Gets a value indicating whether the currently selected viewer pane is diagnostics.
    /// </summary>
    public bool IsDiagnosticsPaneActive => SelectedPaneIndex == (int)ReportViewerPane.Diagnostics;

    /// <summary>
    /// Gets a value indicating whether the currently selected viewer pane is drillthrough.
    /// </summary>
    public bool IsDrillthroughPaneActive => SelectedPaneIndex == (int)ReportViewerPane.Drillthrough;

    /// <summary>
    /// Gets the parameters pane command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenParametersPaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the outline pane command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenOutlinePaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the search pane command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenSearchPaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the diagnostics pane command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenDiagnosticsPaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the drillthrough pane command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenDrillthroughPaneCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the close-left-drawer command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CloseLeftDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the pin-left-drawer command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> TogglePinLeftDrawerCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the toggle-thumbnails command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleThumbnailsCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the fit-width command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> FitWidthCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the fit-page command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> FitPageCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the actual-size command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ActualSizeCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the zoom-in command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the zoom-out command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the reset-layout command.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetLayoutCommand { get; private set; } = null!;

    internal void InitializeLayoutCommands()
    {
        OpenParametersPaneCommand = ReactiveCommand.Create(() => OpenPane(ReportViewerPane.Parameters), outputScheduler: RxSchedulers.MainThreadScheduler);
        OpenOutlinePaneCommand = ReactiveCommand.Create(() => OpenPane(ReportViewerPane.Outline), outputScheduler: RxSchedulers.MainThreadScheduler);
        OpenSearchPaneCommand = ReactiveCommand.Create(() => OpenPane(ReportViewerPane.Search), outputScheduler: RxSchedulers.MainThreadScheduler);
        OpenDiagnosticsPaneCommand = ReactiveCommand.Create(() => OpenPane(ReportViewerPane.Diagnostics), outputScheduler: RxSchedulers.MainThreadScheduler);
        OpenDrillthroughPaneCommand = ReactiveCommand.Create(() => OpenPane(ReportViewerPane.Drillthrough), outputScheduler: RxSchedulers.MainThreadScheduler);
        CloseLeftDrawerCommand = ReactiveCommand.Create(() => { LeftDrawerState = PaneVisibilityState.Closed; }, outputScheduler: RxSchedulers.MainThreadScheduler);
        TogglePinLeftDrawerCommand = ReactiveCommand.Create(TogglePinLeftDrawer, outputScheduler: RxSchedulers.MainThreadScheduler);
        ToggleThumbnailsCommand = ReactiveCommand.Create(() => { IsThumbnailTrayOpen = !IsThumbnailTrayOpen; }, outputScheduler: RxSchedulers.MainThreadScheduler);
        FitWidthCommand = ReactiveCommand.Create(FitWidthToViewport, outputScheduler: RxSchedulers.MainThreadScheduler);
        FitPageCommand = ReactiveCommand.Create(FitPageToViewport, outputScheduler: RxSchedulers.MainThreadScheduler);
        ActualSizeCommand = ReactiveCommand.Create(() => { ZoomFactor = 1f; }, outputScheduler: RxSchedulers.MainThreadScheduler);
        ZoomInCommand = ReactiveCommand.Create(() => { ZoomFactor = GetNearestZoomPreset(stepDirection: +1); }, outputScheduler: RxSchedulers.MainThreadScheduler);
        ZoomOutCommand = ReactiveCommand.Create(() => { ZoomFactor = GetNearestZoomPreset(stepDirection: -1); }, outputScheduler: RxSchedulers.MainThreadScheduler);
        ResetLayoutCommand = ReactiveCommand.Create(ResetLayoutState, outputScheduler: RxSchedulers.MainThreadScheduler);
    }

    internal void UpdateViewportSize(double width, double height)
    {
        _viewportWidth = Math.Max(0d, width);
        _viewportHeight = Math.Max(0d, height);
    }

    private void ApplyDefaultLayoutState(ReportViewerParameterResolutionResult parameterResolution, ReportViewerState? requestedState)
    {
        if (_hasInitializedLayoutState || requestedState is not null)
        {
            return;
        }

        LeftDrawerState = HasUnresolvedVisibleParameters(parameterResolution)
            ? PaneVisibilityState.Open
            : PaneVisibilityState.Closed;
        if (LeftDrawerState != PaneVisibilityState.Closed)
        {
            SelectedPaneIndex = (int)ReportViewerPane.Parameters;
        }

        IsThumbnailTrayOpen = false;
        _hasInitializedLayoutState = true;
    }

    private static bool HasUnresolvedVisibleParameters(ReportViewerParameterResolutionResult parameterResolution)
    {
        for (var index = 0; index < parameterResolution.Parameters.Count; index++)
        {
            var state = parameterResolution.Parameters[index];
            if (state.Definition.Visibility != ReportParameterVisibility.Visible)
            {
                continue;
            }

            if (state.ResolvedValue is null || state.ResolvedValue.IsNull || state.ResolvedValue.Values.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    private void OpenPane(ReportViewerPane pane)
    {
        SelectedPaneIndex = (int)pane;
        if (LeftDrawerState == PaneVisibilityState.Closed)
        {
            LeftDrawerState = PaneVisibilityState.Open;
        }
    }

    private void TogglePinLeftDrawer()
    {
        LeftDrawerState = LeftDrawerState switch
        {
            PaneVisibilityState.Closed => PaneVisibilityState.Pinned,
            PaneVisibilityState.Open => PaneVisibilityState.Pinned,
            _ => PaneVisibilityState.Open
        };
    }

    private void FitWidthToViewport()
    {
        if (SelectedPage is null)
        {
            return;
        }

        var availableWidth = Math.Max(0d, (_viewportWidth <= 0d ? 1180d : _viewportWidth) - 72d);
        if (availableWidth <= 0d || SelectedPage.Page.Width <= 0d)
        {
            return;
        }

        ZoomFactor = (float)Math.Clamp(availableWidth / SelectedPage.Page.Width, 0.25d, 4d);
    }

    private void FitPageToViewport()
    {
        if (SelectedPage is null)
        {
            return;
        }

        var availableWidth = Math.Max(0d, (_viewportWidth <= 0d ? 1180d : _viewportWidth) - 72d);
        var availableHeight = Math.Max(0d, (_viewportHeight <= 0d ? 820d : _viewportHeight) - 72d);
        if (availableWidth <= 0d || availableHeight <= 0d)
        {
            return;
        }

        var widthScale = availableWidth / Math.Max(1d, SelectedPage.Page.Width);
        var heightScale = availableHeight / Math.Max(1d, SelectedPage.Page.Height);
        ZoomFactor = (float)Math.Clamp(Math.Min(widthScale, heightScale), 0.25d, 4d);
    }

    private float GetNearestZoomPreset(int stepDirection)
    {
        var zoomLevels = ZoomLevels.OrderBy(static value => value).ToArray();
        if (zoomLevels.Length == 0)
        {
            return ZoomFactor;
        }

        if (stepDirection > 0)
        {
            for (var index = 0; index < zoomLevels.Length; index++)
            {
                if (zoomLevels[index] > ZoomFactor)
                {
                    return zoomLevels[index];
                }
            }

            return zoomLevels[^1];
        }

        for (var index = zoomLevels.Length - 1; index >= 0; index--)
        {
            if (zoomLevels[index] < ZoomFactor)
            {
                return zoomLevels[index];
            }
        }

        return zoomLevels[0];
    }

    private void RaiseLayoutStatePropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(IsLeftDrawerVisible));
        this.RaisePropertyChanged(nameof(IsLeftDrawerPinned));
        this.RaisePropertyChanged(nameof(IsParametersPaneActive));
        this.RaisePropertyChanged(nameof(IsOutlinePaneActive));
        this.RaisePropertyChanged(nameof(IsSearchPaneActive));
        this.RaisePropertyChanged(nameof(IsDiagnosticsPaneActive));
        this.RaisePropertyChanged(nameof(IsDrillthroughPaneActive));
    }

    private void ResetLayoutInitialization()
    {
        _hasInitializedLayoutState = false;
    }

    private void ResetLayoutState()
    {
        LeftDrawerState = PaneVisibilityState.Closed;
        IsThumbnailTrayOpen = false;
        SearchQuery = string.Empty;
        if (HasUnresolvedPromptParameters())
        {
            OpenPane(ReportViewerPane.Parameters);
        }
        else
        {
            SelectedPaneIndex = (int)ReportViewerPane.Parameters;
        }

        FitPageToViewport();
        StatusMessage = "Reset the run-preview layout.";
    }

    private bool HasUnresolvedPromptParameters()
    {
        for (var index = 0; index < _parameters.Count; index++)
        {
            if (_parameters[index].IsNull || string.IsNullOrWhiteSpace(_parameters[index].TextValue))
            {
                return true;
            }
        }

        return false;
    }
}
