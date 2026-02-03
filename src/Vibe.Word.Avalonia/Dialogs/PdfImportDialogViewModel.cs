using System.Collections.Generic;
using System.Reactive;
using ReactiveUI;
using Vibe.Office.Pdf;

namespace Vibe.Word.Avalonia;

public sealed class PdfImportDialogViewModel : ReactiveObject
{
    public event Action<PdfImportOptions?>? RequestClose;

    public PdfImportDialogViewModel()
    {
        PreservationOptions = new[]
        {
            PdfPreservationMode.None,
            PdfPreservationMode.StoreOriginal,
            PdfPreservationMode.Incremental
        };

        ImportMode = PdfImportMode.Reflow;
        PreservationMode = PdfPreservationMode.None;

        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(() => RequestClose?.Invoke(null));
    }

    public IReadOnlyList<PdfPreservationMode> PreservationOptions { get; }

    private PdfImportMode _importMode;
    private PdfPreservationMode _preservationMode;
    private bool _skipDialog;

    public PdfImportMode ImportMode
    {
        get => _importMode;
        set => this.RaiseAndSetIfChanged(ref _importMode, value);
    }

    public PdfPreservationMode PreservationMode
    {
        get => _preservationMode;
        set => this.RaiseAndSetIfChanged(ref _preservationMode, value);
    }

    public bool SkipDialog
    {
        get => _skipDialog;
        set => this.RaiseAndSetIfChanged(ref _skipDialog, value);
    }

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    private void Confirm()
    {
        var options = new PdfImportOptions
        {
            Mode = ImportMode,
            PreservationMode = PreservationMode
        };

        if (options.PreservationMode != PdfPreservationMode.None)
        {
            options.ParserOptions.PreserveSourceBytes = true;
        }

        RequestClose?.Invoke(options);
    }
}
