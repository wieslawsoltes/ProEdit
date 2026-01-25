using System;
using System.Threading.Tasks;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public sealed class MainWindowMacroManagerService : IMacroManagerService
{
    private readonly Func<Task> _openManager;
    private readonly Func<Task> _toggleRecording;
    private readonly Func<Task> _openVbaEditor;
    private readonly Func<Task> _startDebug;

    public MainWindowMacroManagerService(
        Func<Task> openManager,
        Func<Task> toggleRecording,
        Func<Task> openVbaEditor,
        Func<Task> startDebug)
    {
        _openManager = openManager ?? throw new ArgumentNullException(nameof(openManager));
        _toggleRecording = toggleRecording ?? throw new ArgumentNullException(nameof(toggleRecording));
        _openVbaEditor = openVbaEditor ?? throw new ArgumentNullException(nameof(openVbaEditor));
        _startDebug = startDebug ?? throw new ArgumentNullException(nameof(startDebug));
    }

    public ValueTask OpenMacroManagerAsync() => new ValueTask(_openManager());

    public ValueTask ToggleRecordMacroAsync() => new ValueTask(_toggleRecording());

    public ValueTask OpenVbaEditorAsync() => new ValueTask(_openVbaEditor());

    public ValueTask StartDebugAsync() => new ValueTask(_startDebug());
}
