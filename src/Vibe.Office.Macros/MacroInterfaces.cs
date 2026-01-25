using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Vba.Runtime;

namespace Vibe.Office.Macros;

public interface IMacroEngine : IEditorCommandObserver
{
    bool IsRecording { get; }
    bool IsPlaying { get; }
    MacroDefinition? ActiveMacro { get; }
    IReadOnlyList<MacroDefinition> Macros { get; }

    bool StartRecording(string name);
    MacroDefinition? StopRecording(bool save);
    ValueTask<MacroRunResult> RunAsync(
        MacroDefinition macro,
        IEditorCommandRouter router,
        RibbonContextSnapshot? context = null,
        CancellationToken cancellationToken = default);
    bool DeleteMacro(Guid id);
}

public interface IMacroDebugEngine
{
    VbaDebugSession? ActiveDebugSession { get; }
    bool IsDebugging { get; }
    ValueTask<MacroRunResult> RunDebugAsync(
        MacroDefinition macro,
        IEditorCommandRouter router,
        VbaDebugSession session,
        RibbonContextSnapshot? context = null,
        CancellationToken cancellationToken = default);
}

public readonly record struct MacroRunResult(
    bool Success,
    string? ErrorMessage = null,
    VbaDiagnostic? Diagnostic = null);

public interface IMacroPayloadCodec
{
    bool TryEncode(object? payload, out MacroPayload? encoded);
    bool TryDecode(MacroPayload? payload, out object? decoded);
}

public interface IMacroCommandFilter
{
    bool IsRecordable(string commandId, object? payload, bool recordHistory);
}
