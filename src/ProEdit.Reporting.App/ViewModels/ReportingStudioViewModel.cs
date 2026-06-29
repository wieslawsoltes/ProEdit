using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using ReactiveUI;
using System.Reactive;
using ProEdit.Reporting;
using ProEdit.Reporting.Avalonia;
using ProEdit.Reporting.Avalonia.Designer;
using ProEdit.Reporting.Avalonia.Viewer;
using ProEdit.Reporting.Data;
using ProEdit.Reporting.App.Services;

namespace ProEdit.Reporting.App.ViewModels;

internal sealed partial class ReportingStudioViewModel : ReactiveObject, IDisposable
{
    private readonly IReportingStudioFilePickerService _filePickerService;
    private readonly ReportingStudioDocumentService _documentService;
    private readonly ReportDataConnectorCatalog _connectorCatalog;
    private readonly ObservableCollection<ReportDiagnostic> _diagnostics = [];
    private readonly IDisposable _viewerTabSubscription;
    private ReportDesignerViewModel _designerViewModel;
    private string? _currentPath;
    private ReportingStudioDocumentKind _documentKind;
    private string _statusMessage = "Ready";
    private bool _isBusy;
    private int _selectedWorkspaceTabIndex;
    private int _selectedRibbonTabIndex;

    public ReportingStudioViewModel(
        IReportingStudioFilePickerService filePickerService,
        ReportingStudioDocumentService documentService)
    {
        _filePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _connectorCatalog = documentService.ConnectorCatalog;

        Diagnostics = new ReadOnlyObservableCollection<ReportDiagnostic>(_diagnostics);
        Connectors = _connectorCatalog.ListConnectors();

        var sampleWorkspace = _documentService.CreateSampleWorkspace();
        _documentKind = sampleWorkspace.DocumentKind;
        _currentPath = sampleWorkspace.Path;
        _designerViewModel = CreateDesignerViewModel(sampleWorkspace.Source);
        AttachDesigner(_designerViewModel);
        ApplyDiagnostics(sampleWorkspace.Diagnostics);
        _statusMessage = "Bundled sample loaded.";

        var canOperate = this.WhenAnyValue(static vm => vm.IsBusy)
            .Select(static isBusy => !isBusy)
            .ObserveOn(RxSchedulers.MainThreadScheduler);
        var canOperateWithWorkspace = this.WhenAnyValue(
            static vm => vm.IsBusy,
            static vm => vm.HasWorkspace,
            static (isBusy, hasWorkspace) => !isBusy && hasWorkspace);
        canOperateWithWorkspace = canOperateWithWorkspace.ObserveOn(RxSchedulers.MainThreadScheduler);

        NewSampleCommand = ReactiveCommand.CreateFromTask(NewSampleAsync, canOperate, outputScheduler: RxSchedulers.MainThreadScheduler);
        OpenTemplateCommand = ReactiveCommand.CreateFromTask(OpenTemplateAsync, canOperate, outputScheduler: RxSchedulers.MainThreadScheduler);
        SaveTemplateCommand = ReactiveCommand.CreateFromTask(SaveTemplateAsync, canOperateWithWorkspace, outputScheduler: RxSchedulers.MainThreadScheduler);
        SaveTemplateAsCommand = ReactiveCommand.CreateFromTask(SaveTemplateAsAsync, canOperateWithWorkspace, outputScheduler: RxSchedulers.MainThreadScheduler);
        ImportRdlCommand = ReactiveCommand.CreateFromTask(ImportRdlAsync, canOperate, outputScheduler: RxSchedulers.MainThreadScheduler);
        ExportRdlCommand = ReactiveCommand.CreateFromTask(ExportRdlAsync, canOperateWithWorkspace, outputScheduler: RxSchedulers.MainThreadScheduler);
        RefreshPreviewCommand = ReactiveCommand.CreateFromTask(RefreshPreviewAsync, canOperateWithWorkspace, outputScheduler: RxSchedulers.MainThreadScheduler);
        RunReportCommand = ReactiveCommand.CreateFromTask(RunReportAsync, canOperateWithWorkspace, outputScheduler: RxSchedulers.MainThreadScheduler);
        ShowDesignCommand = ReactiveCommand.Create(() => { CurrentMode = ReportingStudioMode.Design; }, outputScheduler: RxSchedulers.MainThreadScheduler);
        ShowRunCommand = ReactiveCommand.Create(() => { CurrentMode = ReportingStudioMode.Run; }, outputScheduler: RxSchedulers.MainThreadScheduler);
        InitializeLayoutShell();

        _viewerTabSubscription = this.WhenAnyValue(static vm => vm.SelectedWorkspaceTabIndex)
            .Skip(1)
            .Where(static index => index == 1)
            .Subscribe(index => _ = EnsureViewerCurrentAsync());
    }

    public ReadOnlyObservableCollection<ReportDiagnostic> Diagnostics { get; }

    public IReadOnlyList<ReportDataConnectorDefinition> Connectors { get; }

    public ReportDesignerViewModel DesignerViewModel
    {
        get => _designerViewModel;
        private set => this.RaiseAndSetIfChanged(ref _designerViewModel, value);
    }

    public ReportViewerViewModel ViewerViewModel => DesignerViewModel.PreviewViewModel;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public string DesignerStatusMessage => string.IsNullOrWhiteSpace(DesignerViewModel.StatusMessage)
        ? "Designer ready."
        : DesignerViewModel.StatusMessage!;

    public string ViewerStatusMessage => string.IsNullOrWhiteSpace(ViewerViewModel.StatusMessage)
        ? "Viewer ready."
        : ViewerViewModel.StatusMessage!;

    public string ActiveDocumentMode => _documentKind switch
    {
        ReportingStudioDocumentKind.Sample => "Bundled Sample",
        ReportingStudioDocumentKind.NativeTemplate => "Native Template",
        ReportingStudioDocumentKind.ImportedRdl => "Imported RDL",
        _ => "Workspace"
    };

    public string CurrentPathDisplay => string.IsNullOrWhiteSpace(_currentPath)
        ? "Bundled workspace (unsaved)"
        : _currentPath!;

    public string WindowTitle
    {
        get
        {
            var reportName = string.IsNullOrWhiteSpace(DesignerViewModel.ReportDefinition.Name)
                ? "Untitled Report"
                : DesignerViewModel.ReportDefinition.Name;
            return $"{reportName} | ProEdit Reporting Studio";
        }
    }

    public string PreviewStateText
    {
        get
        {
            if (DesignerViewModel.IsPreviewDirty)
            {
                return "Preview dirty";
            }

            return ViewerViewModel.CurrentSnapshot is null
                ? "Preview pending"
                : "Preview current";
        }
    }

    public string WorkspaceSummaryText
    {
        get
        {
            var report = DesignerViewModel.ReportDefinition;
            return $"{report.Name} · {report.Sections.Count} section(s) · {report.DataSets.Count} dataset(s)";
        }
    }

    public string DiagnosticsSummaryText => Diagnostics.Count == 0
        ? "No diagnostics"
        : $"{Diagnostics.Count} diagnostic(s)";

    public string ConnectorsSummaryText => $"{Connectors.Count} connector(s)";

    public bool HasWorkspace => DesignerViewModel is not null;

    public bool CanSaveTemplate => HasWorkspace && !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanSaveTemplate));
        }
    }

    public int SelectedWorkspaceTabIndex
    {
        get => _selectedWorkspaceTabIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (_selectedWorkspaceTabIndex == normalized)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedWorkspaceTabIndex, normalized);
            SynchronizeModeFromWorkspaceIndex();
        }
    }

    public int SelectedRibbonTabIndex
    {
        get => _selectedRibbonTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedRibbonTabIndex, Math.Clamp(value, 0, 3));
    }

    public ReactiveCommand<Unit, Unit> NewSampleCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTemplateCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveTemplateCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveTemplateAsCommand { get; }

    public ReactiveCommand<Unit, Unit> ImportRdlCommand { get; }

    public ReactiveCommand<Unit, Unit> ExportRdlCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshPreviewCommand { get; }

    public ReactiveCommand<Unit, Unit> RunReportCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowDesignCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowRunCommand { get; }

    public async Task InitializeAsync(string? initialPath = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
        {
            if (ReportingStudioDocumentService.IsNativeTemplatePath(initialPath))
            {
                await LoadNativeWorkspaceAsync(initialPath, cancellationToken);
                return;
            }

            if (ReportingStudioDocumentService.IsRdlPath(initialPath))
            {
                await LoadImportedWorkspaceAsync(initialPath, cancellationToken);
                return;
            }

            ApplyDiagnostics(
            [
                new ReportDiagnostic(
                    ReportDiagnosticSeverity.Warning,
                    ReportDiagnosticCodes.UnsupportedFeature,
                    $"Startup file '{Path.GetFileName(initialPath)}' is not a supported reporting template.",
                    "$startup")
            ]);
            StatusMessage = "Unsupported startup file. Loaded bundled sample instead.";
        }

        await EnsureViewerCurrentAsync();
    }

    public void Dispose()
    {
        _viewerTabSubscription.Dispose();
        DetachDesigner(DesignerViewModel);
        DesignerViewModel.Dispose();
    }

    private async Task NewSampleAsync()
    {
        await RunBusyOperationAsync(
            async cancellationToken =>
            {
                LoadWorkspace(_documentService.CreateSampleWorkspace(), "Bundled sample loaded.");
                await RefreshPreviewCoreAsync(cancellationToken);
            },
            "Creating sample workspace...");
    }

    private async Task OpenTemplateAsync()
    {
        var path = await _filePickerService.PickOpenTemplatePathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await LoadNativeWorkspaceAsync(path);
    }

    private async Task SaveTemplateAsync()
    {
        if (!HasWorkspace)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentPath) || _documentKind != ReportingStudioDocumentKind.NativeTemplate)
        {
            await SaveTemplateAsAsync();
            return;
        }

        await SaveNativeToPathAsync(_currentPath);
    }

    private async Task SaveTemplateAsAsync()
    {
        if (!HasWorkspace)
        {
            return;
        }

        var path = await _filePickerService.PickSaveTemplatePathAsync(BuildSuggestedBaseFileName());
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await SaveNativeToPathAsync(path);
    }

    private async Task ImportRdlAsync()
    {
        var path = await _filePickerService.PickImportRdlPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await LoadImportedWorkspaceAsync(path);
    }

    private async Task ExportRdlAsync()
    {
        if (!HasWorkspace)
        {
            return;
        }

        var path = await _filePickerService.PickExportRdlPathAsync(BuildSuggestedBaseFileName());
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyOperationAsync(
            async cancellationToken =>
            {
                var result = await _documentService.ExportRdlAsync(DesignerViewModel.ReportDefinition, path, cancellationToken);
                ApplyDiagnostics(result.Diagnostics);
                StatusMessage = result.HasErrors
                    ? $"RDL export failed for {Path.GetFileName(result.Path)}."
                    : (result.Diagnostics.Count > 0
                        ? $"Exported RDL with diagnostics to {result.Path}."
                        : $"Exported RDL to {result.Path}.");
            },
            "Exporting RDL...");
    }

    private async Task RefreshPreviewAsync()
    {
        await RunBusyOperationAsync(
            RefreshPreviewCoreAsync,
            "Refreshing preview...");
    }

    private async Task RunReportAsync()
    {
        await RunBusyOperationAsync(
            async cancellationToken =>
            {
                await RefreshPreviewCoreAsync(cancellationToken);
                SelectedWorkspaceTabIndex = 1;
            },
            "Running report...");
    }

    private async Task LoadNativeWorkspaceAsync(string path, CancellationToken cancellationToken = default)
    {
        await RunBusyOperationAsync(
            async token =>
            {
                var result = await _documentService.OpenNativeAsync(path, token);
                if (result.Workspace is null)
                {
                    ApplyDiagnostics(result.Diagnostics);
                    StatusMessage = $"Template load failed for {Path.GetFileName(path)}.";
                    return;
                }

                LoadWorkspace(
                    result.Workspace,
                    result.HasErrors
                        ? $"Loaded {Path.GetFileName(path)} with diagnostics."
                        : $"Loaded {Path.GetFileName(path)}.");

                await RefreshPreviewCoreAsync(token);
            },
            $"Opening {Path.GetFileName(path)}...",
            cancellationToken);
    }

    private async Task LoadImportedWorkspaceAsync(string path, CancellationToken cancellationToken = default)
    {
        await RunBusyOperationAsync(
            async token =>
            {
                var result = await _documentService.ImportRdlAsync(path, token);
                if (result.Workspace is null)
                {
                    ApplyDiagnostics(result.Diagnostics);
                    StatusMessage = $"RDL import failed for {Path.GetFileName(path)}.";
                    return;
                }

                LoadWorkspace(
                    result.Workspace,
                    result.HasErrors
                        ? $"Imported {Path.GetFileName(path)} with diagnostics."
                        : $"Imported {Path.GetFileName(path)}.");

                await RefreshPreviewCoreAsync(token);
            },
            $"Importing {Path.GetFileName(path)}...",
            cancellationToken);
    }

    private async Task SaveNativeToPathAsync(string path, CancellationToken cancellationToken = default)
    {
        await RunBusyOperationAsync(
            async token =>
            {
                var result = await _documentService.SaveNativeAsync(DesignerViewModel.ReportDefinition, path, token);
                ApplyDiagnostics(result.Diagnostics);
                if (!result.HasErrors)
                {
                    _currentPath = result.Path;
                    _documentKind = ReportingStudioDocumentKind.NativeTemplate;
                    RaiseShellStateProperties();
                }

                StatusMessage = result.HasErrors
                    ? $"Template save failed for {Path.GetFileName(result.Path)}."
                    : (result.Diagnostics.Count > 0
                        ? $"Saved template with diagnostics to {result.Path}."
                        : $"Saved template to {result.Path}.");
            },
            $"Saving {Path.GetFileName(path)}...",
            cancellationToken);
    }

    private async Task RefreshPreviewCoreAsync(CancellationToken cancellationToken = default)
    {
        await DesignerViewModel.RefreshPreviewAsync(cancellationToken);
        StatusMessage = DesignerViewModel.StatusMessage ?? "Preview refreshed.";
        RaiseShellStateProperties();
    }

    private async Task EnsureViewerCurrentAsync()
    {
        if (IsBusy || !HasWorkspace)
        {
            return;
        }

        if (DesignerViewModel.IsPreviewDirty || ViewerViewModel.CurrentSnapshot is null)
        {
            await RefreshPreviewAsync();
        }
    }

    private async Task RunBusyOperationAsync(
        Func<CancellationToken, Task> operation,
        string busyMessage,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = busyMessage;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation canceled.";
        }
        catch (Exception ex)
        {
            ApplyDiagnostics(
            [
                new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.ServiceOperationFailed,
                    ex.Message,
                    "$studio")
            ]);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadWorkspace(ReportingStudioWorkspace workspace, string statusMessage)
    {
        DetachDesigner(DesignerViewModel);
        DesignerViewModel.Dispose();

        _documentKind = workspace.DocumentKind;
        _currentPath = workspace.Path;
        DesignerViewModel = CreateDesignerViewModel(workspace.Source);
        AttachDesigner(DesignerViewModel);
        ApplyDiagnostics(workspace.Diagnostics);
        SelectedWorkspaceTabIndex = 0;
        StatusMessage = statusMessage;
        RaiseShellStateProperties();
    }

    private ReportDesignerViewModel CreateDesignerViewModel(ReportViewerSource source)
    {
        return new ReportDesignerViewModel(source, connectorCatalog: _connectorCatalog);
    }

    private void AttachDesigner(ReportDesignerViewModel viewModel)
    {
        viewModel.PropertyChanged += HandleDesignerPropertyChanged;
        viewModel.PreviewViewModel.PropertyChanged += HandleViewerPropertyChanged;
    }

    private void DetachDesigner(ReportDesignerViewModel viewModel)
    {
        viewModel.PropertyChanged -= HandleDesignerPropertyChanged;
        viewModel.PreviewViewModel.PropertyChanged -= HandleViewerPropertyChanged;
    }

    private void HandleDesignerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReportDesignerViewModel.StatusMessage)
            or nameof(ReportDesignerViewModel.IsPreviewDirty))
        {
            this.RaisePropertyChanged(nameof(DesignerStatusMessage));
            this.RaisePropertyChanged(nameof(PreviewStateText));
        }

        RaiseShellStateProperties();
    }

    private void HandleViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReportViewerViewModel.StatusMessage)
            or nameof(ReportViewerViewModel.CurrentSnapshot))
        {
            this.RaisePropertyChanged(nameof(ViewerStatusMessage));
            this.RaisePropertyChanged(nameof(PreviewStateText));
        }
    }

    private void ApplyDiagnostics(IReadOnlyList<ReportDiagnostic> diagnostics)
    {
        _diagnostics.Clear();
        for (var index = 0; index < diagnostics.Count; index++)
        {
            _diagnostics.Add(diagnostics[index]);
        }

        this.RaisePropertyChanged(nameof(DiagnosticsSummaryText));
    }

    private void RaiseShellStateProperties()
    {
        this.RaisePropertyChanged(nameof(ViewerViewModel));
        this.RaisePropertyChanged(nameof(WindowTitle));
        this.RaisePropertyChanged(nameof(ActiveDocumentMode));
        this.RaisePropertyChanged(nameof(CurrentPathDisplay));
        this.RaisePropertyChanged(nameof(PreviewStateText));
        this.RaisePropertyChanged(nameof(WorkspaceSummaryText));
        this.RaisePropertyChanged(nameof(DiagnosticsSummaryText));
        this.RaisePropertyChanged(nameof(ConnectorsSummaryText));
        this.RaisePropertyChanged(nameof(DesignerStatusMessage));
        this.RaisePropertyChanged(nameof(ViewerStatusMessage));
        this.RaisePropertyChanged(nameof(CanSaveTemplate));
    }

    private string BuildSuggestedBaseFileName()
    {
        var reportName = string.IsNullOrWhiteSpace(DesignerViewModel.ReportDefinition.Name)
            ? "report"
            : DesignerViewModel.ReportDefinition.Name;

        Span<char> invalid = stackalloc char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        foreach (var character in invalid)
        {
            reportName = reportName.Replace(character, '-');
        }

        return reportName.Replace(' ', '-');
    }
}
