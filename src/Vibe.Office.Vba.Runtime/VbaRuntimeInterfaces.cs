using Vibe.Office.Vba;

namespace Vibe.Office.Vba.Runtime;

public interface IVbaHost
{
    bool TryInvokeMember(string name, IReadOnlyList<VbaValue> arguments, out VbaValue result);
    bool TryGetMember(string name, out VbaValue result);
    bool TrySetMember(string name, VbaValue value);
}

public interface IVbaRuntime
{
    ValueTask<VbaRunResult> ExecuteAsync(
        string source,
        string? entryPoint,
        IReadOnlyList<VbaValue>? arguments = null,
        CancellationToken cancellationToken = default);
}

public interface IVbaDebugRuntime : IVbaRuntime
{
    ValueTask<VbaRunResult> ExecuteDebugAsync(
        string source,
        string? entryPoint,
        IReadOnlyList<VbaValue>? arguments,
        VbaDebugSession session,
        CancellationToken cancellationToken = default);
}

public sealed record VbaStackFrame(string ProcedureName, VbaSourceSpan? Span);

public sealed record VbaDiagnostic(string Message, VbaSourceSpan? Span, IReadOnlyList<VbaStackFrame> CallStack);

public readonly record struct VbaRunResult(
    bool Success,
    string? ErrorMessage = null,
    VbaDiagnostic? Diagnostic = null,
    VbaValue ReturnValue = default);
