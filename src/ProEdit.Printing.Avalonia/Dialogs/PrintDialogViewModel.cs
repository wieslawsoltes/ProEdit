using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using ProEdit.Printing;

namespace ProEdit.Printing.Avalonia;

public sealed partial class PrintDialogViewModel : ReactiveObject
{
    private const double ThumbnailBaseWidth = 200;
    private const double ThumbnailBaseHeight = 260;

    private readonly IPrintService _printService;
    private readonly IPrintDocumentInfo _documentInfo;
    private readonly ObservableCollection<PrinterInfo> _printers = new();
    private readonly ObservableCollection<PrintPreviewItem> _previewPages = new();
    private readonly Dictionary<int, PrintPreviewPage> _previewCache = new();
    private IReadOnlyList<int> _printablePageIndices = Array.Empty<int>();
    private IReadOnlyList<PrintPageRange> _customRanges = Array.Empty<PrintPageRange>();
    private IReadOnlyList<PrintPageRange> _selectionRanges = Array.Empty<PrintPageRange>();
    private int _currentPrintableIndex;
    private bool _initialized;
    private bool _suppressPageNumberSync;
    private float _currentPreviewDpi;
    private double _thumbnailBaseWidth = ThumbnailBaseWidth;
    private double _thumbnailBaseHeight = ThumbnailBaseHeight;

    public PrintDialogViewModel(IPrintService printService, IPrintDocumentInfo documentInfo)
    {
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _documentInfo = documentInfo ?? throw new ArgumentNullException(nameof(documentInfo));

        Printers = new ReadOnlyObservableCollection<PrinterInfo>(_printers);
        PreviewPages = new ReadOnlyObservableCollection<PrintPreviewItem>(_previewPages);
        SelectedPreviewItems = new ObservableCollection<PrintPreviewItem>();
        SelectedPreviewItems.CollectionChanged += OnSelectedPreviewItemsChanged;

        RangeOptions = BuildRangeOptions();
        DuplexOptions = new[]
        {
            PrintDuplexMode.Default,
            PrintDuplexMode.OneSided,
            PrintDuplexMode.TwoSidedLongEdge,
            PrintDuplexMode.TwoSidedShortEdge
        };
        ColorOptions = new[] { PrintColorMode.Color, PrintColorMode.Grayscale };
        OrientationOptions = new[] { PrintOrientationMode.Auto, PrintOrientationMode.Portrait, PrintOrientationMode.Landscape };
        ScalingOptions = new[] { PrintScalingMode.FitToPage, PrintScalingMode.ActualSize, PrintScalingMode.Custom };
        OutputOptions = new[] { PrintOutputKind.Printer, PrintOutputKind.Pdf };
        PresetOptions = new[]
        {
            new PrintPresetOption("Default Settings"),
            new PrintPresetOption("High Quality"),
            new PrintPresetOption("Draft")
        };
        ZoomOptions = new[]
        {
            new PrintZoomOption("50%", 0.5f),
            new PrintZoomOption("75%", 0.75f),
            new PrintZoomOption("100%", 1f),
            new PrintZoomOption("125%", 1.25f),
            new PrintZoomOption("150%", 1.5f),
            new PrintZoomOption("200%", 2f)
        };

        PaperSizeOptions = BuildPaperSizeOptions(documentInfo.DefaultPaperSize);

        RangeKind = RangeOptions.FirstOrDefault();
        Scaling = PrintScalingMode.FitToPage;
        ColorMode = PrintColorMode.Color;
        Duplex = PrintDuplexMode.Default;
        Orientation = ResolveDefaultOrientation(documentInfo.DefaultPaperSize);
        OutputKind = PrintOutputKind.Printer;
        Copies = 1;
        Collate = true;
        CustomScale = 1f;
        SelectedPreset = PresetOptions.FirstOrDefault();
        SelectedZoomOption = ZoomOptions.FirstOrDefault(option => Math.Abs(option.Scale - 1f) < 0.001f) ?? ZoomOptions.FirstOrDefault();
        PreviewZoom = SelectedZoomOption?.Scale ?? 1f;
        _currentPreviewDpi = ResolvePreviewDpi(0);
        UpdateThumbnailBaseSize(0);
        UpdateThumbnailScale();
        UpdateThumbnailScale();
        PageNumberText = "1";
        PageDisplayText = "Page 0 of 0";
        RangeStartText = "1";
        RangeEndText = "1";
        AllPagesLabel = "All Pages";
        PrimaryActionText = "Print";
        OutputPath = string.Empty;
        SelectedPaperSize = ResolveDefaultPaperSize(documentInfo.DefaultPaperSize, PaperSizeOptions);
        IsSelectionRangeAvailable = true;
        PreviewSelectionMode = PreviewSelectionMode.Single;

        BrowseOutputPath = new Interaction<Unit, string?>();

        var canPrint = this.WhenAnyValue(
            vm => vm.HasRangeError,
            vm => vm.IsBusy,
            vm => vm.OutputKind,
            vm => vm.OutputPath,
            (hasRangeError, isBusy, outputKind, outputPath) =>
                !hasRangeError
                && !isBusy
                && (outputKind != PrintOutputKind.Pdf || !string.IsNullOrWhiteSpace(outputPath)));

        PrintCommand = ReactiveCommand.CreateFromTask(PrintAsync, canPrint);
        CancelCommand = ReactiveCommand.Create(() => RequestClose?.Invoke(false));
        BrowseOutputCommand = ReactiveCommand.CreateFromTask(BrowseOutputAsync);
        RefreshPreviewCommand = ReactiveCommand.CreateFromTask(RefreshPreviewAsync);
        NextPageCommand = ReactiveCommand.CreateFromTask(() => NavigatePreviewAsync(1),
            this.WhenAnyValue(vm => vm.CanNavigateNext));
        PreviousPageCommand = ReactiveCommand.CreateFromTask(() => NavigatePreviewAsync(-1),
            this.WhenAnyValue(vm => vm.CanNavigatePrevious));

        this.WhenAnyValue(vm => vm.RangeKind)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                UpdateRangeVisibility();
                UpdateSelectionMode();
                ValidateCustomRange();
                QueuePreviewRefresh();
            });

        this.WhenAnyValue(vm => vm.RangeStartText, vm => vm.RangeEndText)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.TaskpoolScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                ValidateCustomRange();
                QueuePreviewRefresh();
            });

        this.WhenAnyValue(vm => vm.Scaling)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                UpdateScalingVisibility();
                QueuePreviewRefresh();
            });

        this.WhenAnyValue(vm => vm.Orientation)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => QueuePreviewRefresh());

        this.WhenAnyValue(vm => vm.ColorMode)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => QueuePreviewRefresh());

        this.WhenAnyValue(vm => vm.CustomScale)
            .Throttle(TimeSpan.FromMilliseconds(250), RxApp.TaskpoolScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => QueuePreviewRefresh());

        this.WhenAnyValue(vm => vm.SelectedPaperSize)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => QueuePreviewRefresh());

        this.WhenAnyValue(vm => vm.OutputKind)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateOutputVisibility());

        this.WhenAnyValue(vm => vm.SelectedZoomOption)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(option =>
            {
                if (option is null)
                {
                    return;
                }

                PreviewZoom = option.Scale;
                UpdateThumbnailScale();
                UpdatePreviewDimensions(SelectedPreviewPage);
            });

        this.WhenAnyValue(vm => vm.SelectedPreviewPage)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnSelectedPreviewPageChanged);

        this.WhenAnyValue(vm => vm.PageNumberText)
            .Throttle(TimeSpan.FromMilliseconds(200), RxApp.TaskpoolScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(value =>
            {
                if (_suppressPageNumberSync)
                {
                    return;
                }

                if (!int.TryParse(value, out var pageNumber))
                {
                    return;
                }

                QueueNavigateToPage(pageNumber);
            });

        UpdateRangeVisibility();
        UpdateScalingVisibility();
        UpdateOutputVisibility();
    }

    public ReadOnlyObservableCollection<PrinterInfo> Printers { get; }
    public ReadOnlyObservableCollection<PrintPreviewItem> PreviewPages { get; }
    public IReadOnlyList<PrintRangeKind> RangeOptions { get; }
    public IReadOnlyList<PrintDuplexMode> DuplexOptions { get; }
    public IReadOnlyList<PrintColorMode> ColorOptions { get; }
    public IReadOnlyList<PrintOrientationMode> OrientationOptions { get; }
    public IReadOnlyList<PrintScalingMode> ScalingOptions { get; }
    public IReadOnlyList<PrintOutputKind> OutputOptions { get; }
    public IReadOnlyList<PrintPresetOption> PresetOptions { get; }
    public IReadOnlyList<PrintZoomOption> ZoomOptions { get; }
    public IReadOnlyList<PrintPaperSizeOption> PaperSizeOptions { get; }

    public Interaction<Unit, string?> BrowseOutputPath { get; }

    [Reactive] public partial PrinterInfo? SelectedPrinter { get; set; }
    [Reactive] public partial PrintPresetOption? SelectedPreset { get; set; }
    [Reactive] public partial PrintZoomOption? SelectedZoomOption { get; set; }
    [Reactive] public partial PrintPaperSizeOption? SelectedPaperSize { get; set; }
    [Reactive] public partial int Copies { get; set; }
    [Reactive] public partial bool Collate { get; set; }
    [Reactive] public partial PrintRangeKind RangeKind { get; set; }
    [Reactive] public partial string RangeStartText { get; set; }
    [Reactive] public partial string RangeEndText { get; set; }
    [Reactive] public partial PrintDuplexMode Duplex { get; set; }
    [Reactive] public partial PrintColorMode ColorMode { get; set; }
    [Reactive] public partial PrintOrientationMode Orientation { get; set; }
    [Reactive] public partial PrintScalingMode Scaling { get; set; }
    [Reactive] public partial float CustomScale { get; set; }
    [Reactive] public partial PrintOutputKind OutputKind { get; set; }
    [Reactive] public partial string OutputPath { get; set; }
    [Reactive] public partial bool IsBusy { get; set; }
    [Reactive] public partial string? StatusMessage { get; set; }
    [Reactive] public partial bool HasRangeError { get; set; }
    [Reactive] public partial string? RangeErrorMessage { get; set; }
    [Reactive] public partial bool IsCustomRangeVisible { get; set; }
    [Reactive] public partial bool IsCustomScaleVisible { get; set; }
    [Reactive] public partial bool IsPdfOutput { get; set; }
    [Reactive] public partial bool CanNavigatePrevious { get; set; }
    [Reactive] public partial bool CanNavigateNext { get; set; }
    [Reactive] public partial int TotalPages { get; set; }
    [Reactive] public partial PrintPreviewItem? SelectedPreviewPage { get; set; }
    [Reactive] public partial string PageDisplayText { get; set; }
    [Reactive] public partial float PreviewZoom { get; set; }
    [Reactive] public partial string PageNumberText { get; set; }
    [Reactive] public partial double PreviewWidth { get; set; }
    [Reactive] public partial double PreviewHeight { get; set; }
    [Reactive] public partial double PreviewThumbnailWidth { get; set; }
    [Reactive] public partial double PreviewThumbnailHeight { get; set; }
    [Reactive] public partial bool IsSelectionRangeAvailable { get; set; }
    [Reactive] public partial string AllPagesLabel { get; set; }
    [Reactive] public partial string PrimaryActionText { get; set; }
    [Reactive] public partial PreviewSelectionMode PreviewSelectionMode { get; set; }

    public ReactiveCommand<Unit, Unit> PrintCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseOutputCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshPreviewCommand { get; }
    public ReactiveCommand<Unit, Unit> NextPageCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }

    public event Action<bool>? RequestClose;

    public ObservableCollection<PrintPreviewItem> SelectedPreviewItems { get; }

    public async Task InitializeAsync()
    {
        await LoadPrintersAsync();
        await RefreshPreviewAsync();
        _initialized = true;
    }

    private async Task LoadPrintersAsync()
    {
        var printers = await _printService.GetPrintersAsync();
        await RunOnUiAsync(() =>
        {
            _printers.Clear();
            foreach (var printer in printers)
            {
                _printers.Add(printer);
            }

            SelectedPrinter = _printers.FirstOrDefault(printer => printer.IsDefault) ?? _printers.FirstOrDefault();
        });
    }

    private static IReadOnlyList<PrintRangeKind> BuildRangeOptions()
    {
        return new[] { PrintRangeKind.All, PrintRangeKind.CurrentPage, PrintRangeKind.Selection, PrintRangeKind.CustomPages };
    }

    private static IReadOnlyList<PrintPaperSizeOption> BuildPaperSizeOptions(PrintPaperSize? defaultSize)
    {
        var options = new List<PrintPaperSizeOption>
        {
            new("Letter (8.5 x 11 in)", new PrintPaperSize("Letter", 8.5f * 96f, 11f * 96f)),
            new("A4 (210 x 297 mm)", new PrintPaperSize("A4", 210f / 25.4f * 96f, 297f / 25.4f * 96f))
        };

        if (defaultSize is not null && !options.Any(option => SizesMatch(option.Size, defaultSize)))
        {
            options.Insert(0, new PrintPaperSizeOption("Document", defaultSize));
        }

        return options;
    }

    private static PrintPaperSizeOption? ResolveDefaultPaperSize(PrintPaperSize? defaultSize, IReadOnlyList<PrintPaperSizeOption> options)
    {
        if (defaultSize is null)
        {
            return options.FirstOrDefault();
        }

        return options.FirstOrDefault(option => SizesMatch(option.Size, defaultSize)) ?? options.FirstOrDefault();
    }

    private static bool SizesMatch(PrintPaperSize left, PrintPaperSize right)
    {
        const float tolerance = 0.5f;
        return MathF.Abs(left.Width - right.Width) <= tolerance
               && MathF.Abs(left.Height - right.Height) <= tolerance;
    }

    private async Task RefreshPreviewAsync()
    {
        await RunOnUiAsync(() =>
        {
            IsBusy = true;
            StatusMessage = "Rendering preview...";
        });

        try
        {
            var settings = BuildSettingsSnapshot();
            var previewSettings = settings.Clone();
            previewSettings.RangeKind = PrintRangeKind.All;
            previewSettings.CustomRanges = Array.Empty<PrintPageRange>();

            PrintPreviewResult? result = null;
            var dpi = _currentPreviewDpi;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                result = await _printService.BuildPreviewAsync(new PrintPreviewRequest(_documentInfo, previewSettings)
                {
                    Dpi = dpi
                });

                var desiredDpi = ResolvePreviewDpi(result.TotalPages);
                if (Math.Abs(desiredDpi - dpi) <= 0.1f)
                {
                    break;
                }

                dpi = desiredDpi;
            }

            if (result is null)
            {
                throw new InvalidOperationException("Unable to build print preview.");
            }

            _currentPreviewDpi = dpi;

            await RunOnUiAsync(() =>
            {
                TotalPages = result.TotalPages;
                AllPagesLabel = BuildAllPagesLabel(result.TotalPages);
                UpdateThumbnailBaseSize(result.TotalPages);
                _printablePageIndices = result.PrintablePageIndices.Count == 0
                    ? Array.Empty<int>()
                    : result.PrintablePageIndices;
                if (_printablePageIndices.Count > 0)
                {
                    _currentPrintableIndex = ResolvePrintableIndex(_documentInfo.CurrentPageIndex) ?? 0;
                    _currentPrintableIndex = Math.Clamp(_currentPrintableIndex, 0, _printablePageIndices.Count - 1);
                }
                else
                {
                    _currentPrintableIndex = 0;
                }

                UpdatePreviewCache(result.Pages);
                UpdatePreviewList(result.Pages);
                UpdateSelectedPreviewPage();
                UpdateNavigationState();
                StatusMessage = null;
            });

            await UpdateIncludedPagesAsync(settings, result.TotalPages);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => StatusMessage = ex.Message);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false);
        }
    }

    private async Task NavigatePreviewAsync(int delta)
    {
        if (_printablePageIndices.Count == 0)
        {
            return;
        }

        _currentPrintableIndex = Math.Clamp(_currentPrintableIndex + delta, 0, _printablePageIndices.Count - 1);
        UpdateNavigationState();

        var targetPageIndex = _printablePageIndices[_currentPrintableIndex];
        if (_previewCache.TryGetValue(targetPageIndex, out var cached))
        {
            SetSelectedPreview(cached);
            return;
        }

        await RefreshPreviewAsync();
    }

    private async Task BrowseOutputAsync()
    {
        var path = await BrowseOutputPath.Handle(Unit.Default);
        if (!string.IsNullOrWhiteSpace(path))
        {
            OutputPath = path;
        }
    }

    private async Task PrintAsync()
    {
        await RunOnUiAsync(() =>
        {
            IsBusy = true;
            StatusMessage = OutputKind == PrintOutputKind.Pdf ? "Saving..." : "Printing...";
        });

        try
        {
            var settings = BuildSettingsSnapshot();
            settings.PrinterName = SelectedPrinter?.Name;
            settings.OutputPath = OutputPath;
            var result = await _printService.PrintAsync(_documentInfo, settings);
            await RunOnUiAsync(() =>
            {
                if (!result.Succeeded)
                {
                    StatusMessage = result.Message;
                    return;
                }

                StatusMessage = OutputKind == PrintOutputKind.Pdf ? "Saved." : "Print job sent.";
                RequestClose?.Invoke(true);
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => StatusMessage = ex.Message);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false);
        }
    }

    private void ValidateCustomRange()
    {
        if (RangeKind != PrintRangeKind.CustomPages)
        {
            _customRanges = Array.Empty<PrintPageRange>();
            HasRangeError = false;
            RangeErrorMessage = null;
            return;
        }

        if (!TryParsePageNumber(RangeStartText, out var start))
        {
            _customRanges = Array.Empty<PrintPageRange>();
            HasRangeError = true;
            RangeErrorMessage = "Enter a start page.";
            return;
        }

        var end = start;
        if (!string.IsNullOrWhiteSpace(RangeEndText))
        {
            if (!TryParsePageNumber(RangeEndText, out end))
            {
                _customRanges = Array.Empty<PrintPageRange>();
                HasRangeError = true;
                RangeErrorMessage = "Enter a valid end page.";
                return;
            }
        }

        if (end < start)
        {
            _customRanges = Array.Empty<PrintPageRange>();
            HasRangeError = true;
            RangeErrorMessage = "End page must be greater than or equal to start.";
            return;
        }

        _customRanges = new[] { new PrintPageRange(start, end) };
        HasRangeError = false;
        RangeErrorMessage = null;
    }

    private static bool TryParsePageNumber(string? text, out int value)
    {
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }

        return value > 0;
    }

    private PrintSettings BuildSettingsSnapshot()
    {
        var selectionRanges = RangeKind == PrintRangeKind.Selection ? _selectionRanges : Array.Empty<PrintPageRange>();
        return new PrintSettings
        {
            OutputKind = OutputKind,
            PrinterName = SelectedPrinter?.Name,
            OutputPath = OutputPath,
            Copies = Math.Max(1, Copies),
            Collate = Collate,
            RangeKind = RangeKind,
            CustomRanges = RangeKind == PrintRangeKind.CustomPages ? _customRanges : selectionRanges,
            Duplex = Duplex,
            ColorMode = ColorMode,
            Scaling = Scaling,
            CustomScale = CustomScale,
            Orientation = Orientation,
            PaperSize = SelectedPaperSize?.Size
        };
    }

    private void UpdatePreviewCache(IEnumerable<PrintPreviewPage> pages)
    {
        _previewCache.Clear();
        foreach (var page in pages)
        {
            _previewCache[Math.Max(0, page.PageNumber - 1)] = page;
        }
    }

    private void UpdatePreviewList(IEnumerable<PrintPreviewPage> pages)
    {
        _previewPages.Clear();
        foreach (var page in pages.OrderBy(page => page.PageNumber))
        {
            var item = new PrintPreviewItem(page)
            {
                IsIncluded = true
            };
            _previewPages.Add(item);
        }
    }

    private async Task UpdateIncludedPagesAsync(PrintSettings settings, int totalPages)
    {
        if (totalPages <= 0)
        {
            return;
        }

        var included = await ResolveIncludedPageIndicesAsync(settings, totalPages);
        await RunOnUiAsync(() => UpdateIncludedPageStates(included));
    }

    private void UpdateIncludedPageStates(HashSet<int> includedPages)
    {
        foreach (var item in _previewPages)
        {
            item.IsIncluded = includedPages.Contains(item.PageIndex);
        }
    }

    private async Task<HashSet<int>> ResolveIncludedPageIndicesAsync(PrintSettings settings, int totalPages)
    {
        var included = new HashSet<int>();
        if (totalPages <= 0)
        {
            return included;
        }

        if (HasRangeError)
        {
            AddRange(included, 0, totalPages - 1, totalPages);
            return included;
        }

        switch (settings.RangeKind)
        {
            case PrintRangeKind.All:
                AddRange(included, 0, totalPages - 1, totalPages);
                break;
            case PrintRangeKind.CurrentPage:
                AddRange(included, _documentInfo.CurrentPageIndex ?? 0, _documentInfo.CurrentPageIndex ?? 0, totalPages);
                break;
            case PrintRangeKind.CustomPages:
                AddRanges(included, _customRanges, totalPages);
                break;
            case PrintRangeKind.Selection:
                if (_selectionRanges.Count > 0)
                {
                    AddRanges(included, _selectionRanges, totalPages);
                    break;
                }

                if (_documentInfo.HasSelection)
                {
                    var request = new PrintPreviewRequest(_documentInfo, settings)
                    {
                        PageIndices = new[] { ResolveCurrentPrintablePageIndex() },
                        Dpi = _currentPreviewDpi
                    };
                    var result = await _printService.BuildPreviewAsync(request);
                    foreach (var index in result.PrintablePageIndices)
                    {
                        if ((uint)index < (uint)totalPages)
                        {
                            included.Add(index);
                        }
                    }

                    if (included.Count > 0)
                    {
                        break;
                    }
                }

                AddRange(included, 0, totalPages - 1, totalPages);
                break;
            default:
                AddRange(included, 0, totalPages - 1, totalPages);
                break;
        }

        return included;
    }

    private static void AddRanges(HashSet<int> included, IReadOnlyList<PrintPageRange> ranges, int totalPages)
    {
        if (ranges.Count == 0)
        {
            return;
        }

        foreach (var range in ranges)
        {
            AddRange(included, range.Start - 1, range.End - 1, totalPages);
        }
    }

    private static void AddRange(HashSet<int> included, int start, int end, int totalPages)
    {
        if (totalPages <= 0)
        {
            return;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        start = Math.Clamp(start, 0, totalPages - 1);
        end = Math.Clamp(end, 0, totalPages - 1);
        for (var i = start; i <= end; i++)
        {
            included.Add(i);
        }
    }

    private void UpdateSelectedPreviewPage()
    {
        var targetPageIndex = ResolveCurrentPrintablePageIndex();
        if (_previewCache.TryGetValue(targetPageIndex, out var preview))
        {
            SetSelectedPreview(preview);
            return;
        }

        SelectedPreviewPage = null;
        PageDisplayText = $"Page 0 of {TotalPages}";
    }

    private void SetSelectedPreview(PrintPreviewPage preview)
    {
        var item = _previewPages.FirstOrDefault(candidate => candidate.Page.PageNumber == preview.PageNumber)
                   ?? new PrintPreviewItem(preview);
        SelectedPreviewPage = item;
        PageDisplayText = $"Page {preview.PageNumber} of {TotalPages}";
        _suppressPageNumberSync = true;
        PageNumberText = preview.PageNumber.ToString(CultureInfo.InvariantCulture);
        _suppressPageNumberSync = false;
        UpdatePreviewDimensions(item);
    }

    private int ResolveCurrentPrintablePageIndex()
    {
        if (_printablePageIndices.Count == 0)
        {
            return _documentInfo.CurrentPageIndex ?? 0;
        }

        _currentPrintableIndex = Math.Clamp(_currentPrintableIndex, 0, _printablePageIndices.Count - 1);
        return _printablePageIndices[_currentPrintableIndex];
    }

    private int? ResolvePrintableIndex(int? pageIndex)
    {
        if (pageIndex is null)
        {
            return null;
        }

        for (var i = 0; i < _printablePageIndices.Count; i++)
        {
            if (_printablePageIndices[i] == pageIndex.Value)
            {
                return i;
            }
        }

        return null;
    }

    private void UpdateNavigationState()
    {
        CanNavigatePrevious = _printablePageIndices.Count > 0 && _currentPrintableIndex > 0;
        CanNavigateNext = _printablePageIndices.Count > 0 && _currentPrintableIndex < _printablePageIndices.Count - 1;
    }

    private void UpdateRangeVisibility()
    {
        IsCustomRangeVisible = RangeKind == PrintRangeKind.CustomPages;
    }

    private void UpdateSelectionMode()
    {
        PreviewSelectionMode = RangeKind == PrintRangeKind.Selection
            ? PreviewSelectionMode.Multiple
            : PreviewSelectionMode.Single;

        if (PreviewSelectionMode == PreviewSelectionMode.Single)
        {
            SelectedPreviewItems.Clear();
            if (SelectedPreviewPage is not null)
            {
                SelectedPreviewItems.Add(SelectedPreviewPage);
            }
        }
        else
        {
            if (SelectedPreviewPage is not null && !SelectedPreviewItems.Contains(SelectedPreviewPage))
            {
                SelectedPreviewItems.Add(SelectedPreviewPage);
            }
        }

        if (RangeKind == PrintRangeKind.Selection)
        {
            UpdateSelectionRangesFromPreview();
        }
    }

    private void OnSelectedPreviewItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (RangeKind != PrintRangeKind.Selection)
        {
            return;
        }

        UpdateSelectionRangesFromPreview();
        _ = UpdateIncludedPagesAsync(BuildSettingsSnapshot(), TotalPages);
    }

    private void UpdateSelectionRangesFromPreview()
    {
        if (RangeKind != PrintRangeKind.Selection)
        {
            _selectionRanges = Array.Empty<PrintPageRange>();
            return;
        }

        var selected = SelectedPreviewItems
            .Select(item => item.PageIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        if (selected.Length == 0)
        {
            _selectionRanges = Array.Empty<PrintPageRange>();
            return;
        }

        var ranges = new List<PrintPageRange>();
        var rangeStart = selected[0];
        var rangeEnd = selected[0];
        for (var i = 1; i < selected.Length; i++)
        {
            var value = selected[i];
            if (value == rangeEnd + 1)
            {
                rangeEnd = value;
                continue;
            }

            ranges.Add(new PrintPageRange(rangeStart + 1, rangeEnd + 1));
            rangeStart = value;
            rangeEnd = value;
        }

        ranges.Add(new PrintPageRange(rangeStart + 1, rangeEnd + 1));
        _selectionRanges = ranges;
    }

    private void UpdateScalingVisibility()
    {
        IsCustomScaleVisible = Scaling == PrintScalingMode.Custom;
    }

    private void UpdateOutputVisibility()
    {
        IsPdfOutput = OutputKind == PrintOutputKind.Pdf;
        PrimaryActionText = OutputKind == PrintOutputKind.Pdf ? "Save" : "Print";
    }

    private void QueuePreviewRefresh()
    {
        if (!_initialized)
        {
            return;
        }

        RefreshPreviewCommand.Execute().Subscribe();
    }

    private void QueueNavigateToPage(int pageNumber)
    {
        if (!_initialized || TotalPages == 0)
        {
            return;
        }

        var clamped = Math.Clamp(pageNumber, 1, TotalPages);
        var targetPageIndex = clamped - 1;
        if (_printablePageIndices.Count > 0)
        {
            var index = -1;
            for (var i = 0; i < _printablePageIndices.Count; i++)
            {
                if (_printablePageIndices[i] == targetPageIndex)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                index = 0;
                for (var i = 0; i < _printablePageIndices.Count; i++)
                {
                    if (_printablePageIndices[i] >= targetPageIndex)
                    {
                        index = i;
                        break;
                    }

                    index = i;
                }
            }

            _currentPrintableIndex = Math.Clamp(index, 0, _printablePageIndices.Count - 1);
        }
        else
        {
            _currentPrintableIndex = Math.Clamp(targetPageIndex, 0, Math.Max(0, TotalPages - 1));
        }

        UpdateNavigationState();
        _ = NavigatePreviewAsync(0);
    }

    private void UpdatePreviewDimensions(PrintPreviewItem? preview)
    {
        if (preview is null)
        {
            PreviewWidth = 0;
            PreviewHeight = 0;
            return;
        }

        PreviewWidth = preview.Page.Width * PreviewZoom;
        PreviewHeight = preview.Page.Height * PreviewZoom;
    }

    private void UpdateThumbnailScale()
    {
        PreviewThumbnailWidth = _thumbnailBaseWidth * PreviewZoom;
        PreviewThumbnailHeight = _thumbnailBaseHeight * PreviewZoom;
    }

    private void UpdateThumbnailBaseSize(int totalPages)
    {
        var size = ResolveThumbnailBaseSize(totalPages);
        _thumbnailBaseWidth = size.width;
        _thumbnailBaseHeight = size.height;
        UpdateThumbnailScale();
    }

    private void OnSelectedPreviewPageChanged(PrintPreviewItem? preview)
    {
        UpdatePreviewDimensions(preview);
        if (preview is null || TotalPages == 0)
        {
            if (PreviewSelectionMode == PreviewSelectionMode.Single)
            {
                SelectedPreviewItems.Clear();
            }
            return;
        }

        if (PreviewSelectionMode == PreviewSelectionMode.Single)
        {
            SelectedPreviewItems.Clear();
            SelectedPreviewItems.Add(preview);
        }

        _suppressPageNumberSync = true;
        PageNumberText = preview.Page.PageNumber.ToString(CultureInfo.InvariantCulture);
        _suppressPageNumberSync = false;
        PageDisplayText = $"Page {preview.Page.PageNumber} of {TotalPages}";

        var index = ResolvePrintableIndex(preview.Page.PageNumber - 1);
        if (index.HasValue)
        {
            _currentPrintableIndex = Math.Clamp(index.Value, 0, Math.Max(0, _printablePageIndices.Count - 1));
            UpdateNavigationState();
        }
    }

    private static string BuildAllPagesLabel(int totalPages)
    {
        return totalPages == 1 ? "All 1 Page" : $"All {totalPages} Pages";
    }

    private static float ResolvePreviewDpi(int totalPages)
    {
        if (totalPages <= 0)
        {
            return 32f;
        }

        if (totalPages <= 30)
        {
            return 36f;
        }

        if (totalPages <= 80)
        {
            return 30f;
        }

        if (totalPages <= 150)
        {
            return 24f;
        }

        if (totalPages <= 300)
        {
            return 20f;
        }

        return 16f;
    }

    private static (double width, double height) ResolveThumbnailBaseSize(int totalPages)
    {
        if (totalPages <= 0)
        {
            return (ThumbnailBaseWidth, ThumbnailBaseHeight);
        }

        if (totalPages <= 30)
        {
            return (200, 260);
        }

        if (totalPages <= 80)
        {
            return (185, 245);
        }

        if (totalPages <= 150)
        {
            return (165, 220);
        }

        if (totalPages <= 300)
        {
            return (145, 200);
        }

        return (130, 180);
    }

    private static PrintOrientationMode ResolveDefaultOrientation(PrintPaperSize? size)
    {
        if (size is null)
        {
            return PrintOrientationMode.Portrait;
        }

        return size.Width > size.Height
            ? PrintOrientationMode.Landscape
            : PrintOrientationMode.Portrait;
    }

    private static Task RunOnUiAsync(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        RxApp.MainThreadScheduler.Schedule(Unit.Default, (_, _) =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return Disposable.Empty;
        });
        return tcs.Task;
    }
}

public sealed record PrintPresetOption(string Label);

public sealed record PrintZoomOption(string Label, float Scale);

public sealed record PrintPaperSizeOption(string Label, PrintPaperSize Size);

public enum PreviewSelectionMode
{
    Single,
    Multiple
}

public sealed partial class PrintPreviewItem : ReactiveObject
{
    public PrintPreviewItem(PrintPreviewPage page)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
        PageIndex = Math.Max(0, page.PageNumber - 1);
    }

    public PrintPreviewPage Page { get; }

    public int PageIndex { get; }

    [Reactive] public partial bool IsIncluded { get; set; }
}
