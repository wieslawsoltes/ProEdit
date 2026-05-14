using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Vba.Runtime;

namespace ProEdit.Macros;

public sealed class MacroEngine : IMacroEngine, IMacroDebugEngine
{
    private readonly Document _document;
    private readonly IMacroPayloadCodec _payloadCodec;
    private readonly IMacroCommandFilter _commandFilter;
    private readonly IVbaRuntime? _vbaRuntime;
    private MacroDefinition? _recording;
    private bool _isPlaying;
    private VbaDebugSession? _debugSession;

    public MacroEngine(
        Document document,
        IMacroPayloadCodec? payloadCodec = null,
        IMacroCommandFilter? commandFilter = null,
        IVbaRuntime? vbaRuntime = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _payloadCodec = payloadCodec ?? new JsonMacroPayloadCodec();
        _commandFilter = commandFilter ?? new MacroCommandFilter();
        _vbaRuntime = vbaRuntime;
    }

    public bool IsRecording => _recording is not null;
    public bool IsPlaying => _isPlaying;
    public VbaDebugSession? ActiveDebugSession => _debugSession;
    public bool IsDebugging => _debugSession is not null && _isPlaying;
    public MacroDefinition? ActiveMacro => _recording;
    public IReadOnlyList<MacroDefinition> Macros => _document.Macros.Items;

    public bool StartRecording(string name)
    {
        if (_recording is not null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        _recording = new MacroDefinition
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Language = MacroLanguage.CommandSequence
        };

        return true;
    }

    public MacroDefinition? StopRecording(bool save)
    {
        if (_recording is null)
        {
            return null;
        }

        var macro = _recording;
        _recording = null;

        if (save)
        {
            macro.Name = EnsureUniqueName(macro.Name);
            macro.IsTrusted = true;
            _document.Macros.IsTrusted = true;
            _document.Macros.Items.Add(macro);
        }

        return macro;
    }

    public async ValueTask<MacroRunResult> RunAsync(
        MacroDefinition macro,
        IEditorCommandRouter router,
        RibbonContextSnapshot? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(router);

        if (macro.Language == MacroLanguage.Vba)
        {
            if (_vbaRuntime is null)
            {
                return new MacroRunResult(false, "VBA runtime is unavailable.");
            }

            if (!_document.Macros.IsTrusted && !macro.IsTrusted)
            {
                return new MacroRunResult(false, "Macro execution is blocked.");
            }

            if (string.IsNullOrWhiteSpace(macro.Source))
            {
                return new MacroRunResult(false, "Macro source is empty.");
            }

            _isPlaying = true;
            try
            {
                var result = await _vbaRuntime.ExecuteAsync(macro.Source, macro.Name, null, cancellationToken);
                return new MacroRunResult(result.Success, result.ErrorMessage, result.Diagnostic);
            }
            finally
            {
                _isPlaying = false;
            }
        }

        if (!_document.Macros.IsTrusted && !macro.IsTrusted)
        {
            return new MacroRunResult(false, "Macro execution is blocked.");
        }

        if (macro.Commands.Count == 0)
        {
            return new MacroRunResult(true);
        }

        _isPlaying = true;
        try
        {
            foreach (var command in macro.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_payloadCodec.TryDecode(command.Payload, out var payload))
                {
                    return new MacroRunResult(false, "Unable to decode macro command payload.");
                }

                var executed = await router.ExecuteAsync(command.CommandId, payload, context);
                if (!executed)
                {
                    return new MacroRunResult(false, $"Command '{command.CommandId}' failed.");
                }
            }

            return new MacroRunResult(true);
        }
        finally
        {
            _isPlaying = false;
        }
    }

    public async ValueTask<MacroRunResult> RunDebugAsync(
        MacroDefinition macro,
        IEditorCommandRouter router,
        VbaDebugSession session,
        RibbonContextSnapshot? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(session);

        if (_debugSession is not null && _isPlaying)
        {
            return new MacroRunResult(false, "Debugger is already running.");
        }

        if (macro.Language != MacroLanguage.Vba)
        {
            return new MacroRunResult(false, "Macro is not a VBA macro.");
        }

        if (_vbaRuntime is not IVbaDebugRuntime debugRuntime)
        {
            return new MacroRunResult(false, "VBA debug runtime is unavailable.");
        }

        if (!_document.Macros.IsTrusted && !macro.IsTrusted)
        {
            return new MacroRunResult(false, "Macro execution is blocked.");
        }

        if (string.IsNullOrWhiteSpace(macro.Source))
        {
            return new MacroRunResult(false, "Macro source is empty.");
        }

        _isPlaying = true;
        _debugSession = session;
        try
        {
                var runTask = Task.Run(
                    () => debugRuntime.ExecuteDebugAsync(macro.Source, macro.Name, null, session, cancellationToken).AsTask(),
                cancellationToken);
            var result = await runTask;
            return new MacroRunResult(result.Success, result.ErrorMessage, result.Diagnostic);
        }
        catch (OperationCanceledException)
        {
            return new MacroRunResult(false, "Macro execution canceled.");
        }
        finally
        {
            _isPlaying = false;
            if (ReferenceEquals(_debugSession, session))
            {
                _debugSession = null;
            }
        }
    }

    public bool DeleteMacro(Guid id)
    {
        var items = _document.Macros.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == id)
            {
                items.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public void OnCommandExecuted(string commandId, object? payload, bool recordHistory)
    {
        if (_recording is null || _isPlaying)
        {
            return;
        }

        if (!_commandFilter.IsRecordable(commandId, payload, recordHistory))
        {
            return;
        }

        if (!_payloadCodec.TryEncode(payload, out var encoded))
        {
            return;
        }

        _recording.Commands.Add(new MacroCommand
        {
            CommandId = commandId,
            Payload = encoded
        });
    }

    private string EnsureUniqueName(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = "Macro";
        }

        var items = _document.Macros.Items;
        if (items.Count == 0)
        {
            return trimmed;
        }

        var candidate = trimmed;
        var suffix = 1;
        while (NameExists(items, candidate))
        {
            candidate = $"{trimmed} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private static bool NameExists(List<MacroDefinition> items, string name)
    {
        foreach (var macro in items)
        {
            if (string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
