using ProEdit.Vba;

namespace ProEdit.Vba.Runtime;

public enum VbaDebugStepMode
{
    None,
    StepIn,
    StepOver,
    StepOut
}

public sealed record VbaBreakpoint(int Line, string? ProcedureName = null)
{
    public string Display => string.IsNullOrWhiteSpace(ProcedureName)
        ? $"Line {Line}"
        : $"{ProcedureName} (Line {Line})";
}

public sealed record VbaDebugLocation(string ProcedureName, VbaSourceSpan Span);

public sealed record VbaDebugState(
    VbaDebugLocation Location,
    IReadOnlyList<VbaStackFrame> CallStack,
    IReadOnlyDictionary<string, VbaValue> Locals);

public sealed class VbaDebugSession
{
    private readonly object _lock = new();
    private readonly List<VbaBreakpoint> _breakpoints = new();
    private readonly ManualResetEventSlim _resumeSignal = new(false);
    private VbaDebugStepMode _stepMode = VbaDebugStepMode.None;
    private int _stepDepth = 1;
    private int _currentDepth = 1;
    private bool _breakRequested;
    private bool _stopRequested;
    private bool _hasStopped;
    private VbaDebugLocation? _resumeLocation;
    private Func<string, VbaValue>? _immediateEvaluator;

    public bool IsPaused { get; private set; }
    public VbaDebugState? CurrentState { get; private set; }

    public event EventHandler<VbaDebugState>? Paused;
    public event EventHandler? Resumed;
    public event EventHandler? Stopped;

    public IReadOnlyList<VbaBreakpoint> Breakpoints => _breakpoints;

    public void AddBreakpoint(VbaBreakpoint breakpoint)
    {
        if (breakpoint.Line <= 0)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var existing in _breakpoints)
            {
                if (MatchesBreakpoint(existing, breakpoint))
                {
                    return;
                }
            }

            _breakpoints.Add(breakpoint);
        }
    }

    public void RemoveBreakpoint(VbaBreakpoint breakpoint)
    {
        lock (_lock)
        {
            for (var i = _breakpoints.Count - 1; i >= 0; i--)
            {
                if (MatchesBreakpoint(_breakpoints[i], breakpoint))
                {
                    _breakpoints.RemoveAt(i);
                }
            }
        }
    }

    public void ClearBreakpoints()
    {
        lock (_lock)
        {
            _breakpoints.Clear();
        }
    }

    public void Continue()
    {
        Resume(VbaDebugStepMode.None, null);
    }

    public void Break()
    {
        lock (_lock)
        {
            _breakRequested = true;
        }
    }

    public void StepIn()
    {
        Resume(VbaDebugStepMode.StepIn, null);
    }

    public void StepOver()
    {
        Resume(VbaDebugStepMode.StepOver, _currentDepth);
    }

    public void StepOut()
    {
        Resume(VbaDebugStepMode.StepOut, _currentDepth);
    }

    public void Stop()
    {
        lock (_lock)
        {
            _stopRequested = true;
        }

        _resumeSignal.Set();
    }

    public bool TryEvaluateImmediate(string expression, out VbaValue result, out string? errorMessage)
    {
        result = VbaValue.Empty;
        errorMessage = null;

        Func<string, VbaValue>? evaluator;
        lock (_lock)
        {
            evaluator = _immediateEvaluator;
        }

        if (!IsPaused || evaluator is null)
        {
            return false;
        }

        try
        {
            result = evaluator(expression ?? string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    internal bool IsStopRequested
    {
        get
        {
            lock (_lock)
            {
                return _stopRequested;
            }
        }
    }

    internal bool ShouldPause(string procedureName, VbaSourceSpan span, int callDepth)
    {
        var location = new VbaDebugLocation(procedureName, span);

        lock (_lock)
        {
            if (_resumeLocation is not null && IsSameLocation(_resumeLocation, location))
            {
                _resumeLocation = null;
                return false;
            }

            if (_breakRequested)
            {
                _breakRequested = false;
                return true;
            }

            if (MatchesBreakpoint(location))
            {
                return true;
            }

            switch (_stepMode)
            {
                case VbaDebugStepMode.StepIn:
                    return true;
                case VbaDebugStepMode.StepOver:
                    return callDepth <= _stepDepth;
                case VbaDebugStepMode.StepOut:
                    return callDepth < _stepDepth;
                default:
                    return false;
            }
        }
    }

    internal void Pause(
        VbaDebugState state,
        int callDepth,
        Func<string, VbaValue> evaluator,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _stepMode = VbaDebugStepMode.None;
            _currentDepth = Math.Max(1, callDepth);
            _immediateEvaluator = evaluator;
            CurrentState = state;
            IsPaused = true;
            _resumeSignal.Reset();
        }

        Paused?.Invoke(this, state);
        _resumeSignal.Wait(cancellationToken);
    }

    internal void NotifyStopped()
    {
        lock (_lock)
        {
            if (_hasStopped)
            {
                return;
            }

            _hasStopped = true;
            _stopRequested = true;
            IsPaused = false;
            _immediateEvaluator = null;
        }

        _resumeSignal.Set();
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void Resume(VbaDebugStepMode mode, int? depth)
    {
        bool raiseEvent;
        lock (_lock)
        {
            _stepMode = mode;
            if (depth.HasValue)
            {
                _stepDepth = Math.Max(1, depth.Value);
            }

            _resumeLocation = CurrentState?.Location;
            raiseEvent = IsPaused;
            if (IsPaused)
            {
                IsPaused = false;
                _resumeSignal.Set();
            }
        }

        if (raiseEvent)
        {
            Resumed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool MatchesBreakpoint(VbaDebugLocation location)
    {
        foreach (var breakpoint in _breakpoints)
        {
            if (breakpoint.Line != location.Span.Line)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(breakpoint.ProcedureName)
                || string.Equals(breakpoint.ProcedureName, location.ProcedureName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesBreakpoint(VbaBreakpoint existing, VbaBreakpoint candidate)
    {
        if (existing.Line != candidate.Line)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(existing.ProcedureName)
            && string.IsNullOrWhiteSpace(candidate.ProcedureName))
        {
            return true;
        }

        return string.Equals(existing.ProcedureName, candidate.ProcedureName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameLocation(VbaDebugLocation left, VbaDebugLocation right)
    {
        return left.Span.Line == right.Span.Line
               && left.Span.Column == right.Span.Column
               && string.Equals(left.ProcedureName, right.ProcedureName, StringComparison.OrdinalIgnoreCase);
    }
}
