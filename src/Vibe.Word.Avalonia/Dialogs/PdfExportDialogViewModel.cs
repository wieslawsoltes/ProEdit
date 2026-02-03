using System.Reactive;
using ReactiveUI;
using Vibe.Office.Pdf;

namespace Vibe.Word.Avalonia;

public sealed class PdfExportDialogViewModel : ReactiveObject
{
    public event Action<PdfExportOptions?>? RequestClose;

    private PdfExportMode _exportMode;
    private bool _allowPreserveWithChanges;

    public PdfExportDialogViewModel(
        bool hasPreservedData,
        bool hasChanges,
        PdfPreservationMode preservationMode,
        bool supportsIncremental,
        string? incrementalDetails)
    {
        HasPreservedData = hasPreservedData;
        HasChanges = hasChanges;
        PreservationMode = preservationMode;
        SupportsIncremental = supportsIncremental;
        IncrementalDetails = incrementalDetails ?? string.Empty;
        _exportMode = PdfExportMode.Regenerate;

        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(() => RequestClose?.Invoke(null));
    }

    public bool HasPreservedData { get; }

    public bool HasChanges { get; }

    public PdfPreservationMode PreservationMode { get; }

    public bool SupportsIncremental { get; }

    public string IncrementalDetails { get; }

    public bool ShowPreserveUnavailable => !HasPreservedData;

    public bool ShowChangeWarning => HasPreservedData && HasChanges;

    public bool ShowIncrementalWarning => HasPreservedData
        && PreservationMode == PdfPreservationMode.Incremental
        && HasChanges
        && !SupportsIncremental;

    public bool ShowIncrementalDetails => ShowIncrementalWarning && !string.IsNullOrWhiteSpace(IncrementalDetails);

    public bool CanPreserve => HasPreservedData
        && (!HasChanges || AllowPreserveWithChanges);

    public PdfExportMode ExportMode
    {
        get => _exportMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _exportMode, value);
            if (value == PdfExportMode.Preserve && !CanPreserve)
            {
                _exportMode = PdfExportMode.Regenerate;
                this.RaisePropertyChanged(nameof(ExportMode));
            }
        }
    }

    public bool AllowPreserveWithChanges
    {
        get => _allowPreserveWithChanges;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _allowPreserveWithChanges, value))
            {
                this.RaisePropertyChanged(nameof(CanPreserve));
                if (!CanPreserve && ExportMode == PdfExportMode.Preserve)
                {
                    ExportMode = PdfExportMode.Regenerate;
                }
            }
        }
    }

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    private void Confirm()
    {
        var options = new PdfExportOptions
        {
            ExportMode = ExportMode,
            AllowPreserveWithChanges = AllowPreserveWithChanges
        };

        RequestClose?.Invoke(options);
    }
}
