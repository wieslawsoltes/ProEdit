using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Macros;
using Vibe.Office.Vba;
using Vibe.Office.Vba.Runtime;

namespace Vibe.Word.Avalonia;

public partial class VbaToolingWindow : Window
{
    private Document _document = null!;
    private IMacroEngine _macroEngine = null!;
    private IMacroDebugEngine? _macroDebugEngine;
    private IEditorCommandRouter _router = null!;
    private Func<RibbonContextSnapshot?> _snapshotProvider = null!;

    private readonly ObservableCollection<VbaModuleInfo> _modules = new();
    private readonly ObservableCollection<string> _procedures = new();
    private readonly ObservableCollection<MacroDefinition> _recordings = new();
    private readonly ObservableCollection<VbaProjectReference> _references = new();
    private readonly ObservableCollection<VbaBreakpoint> _breakpoints = new();
    private readonly ObservableCollection<string> _locals = new();
    private readonly ObservableCollection<string> _callStack = new();

    private DispatcherTimer _procedureRefreshTimer = null!;

    private ListBox _modulesList = null!;
    private ListBox _proceduresList = null!;
    private ListBox _recordingsList = null!;
    private ListBox _referencesList = null!;
    private ListBox _localsList = null!;
    private ListBox _callStackList = null!;
    private ListBox _breakpointsList = null!;
    private TextBox _sourceEditor = null!;
    private TextBox _breakpointLineBox = null!;
    private TextBox _immediateInput = null!;
    private TextBox _immediateOutput = null!;

    private VbaModuleInfo? _currentModule;
    private VbaDebugSession? _debugSession;
    private bool _suppressTextChanged;
    private bool _isDirty;

    public VbaToolingWindow()
    {
        var document = new Document();
        Initialize(document, new MacroEngine(document), new NullEditorCommandRouter(), () => null);
    }

    public VbaToolingWindow(
        Document document,
        IMacroEngine macroEngine,
        IEditorCommandRouter router,
        Func<RibbonContextSnapshot?> snapshotProvider)
    {
        Initialize(document, macroEngine, router, snapshotProvider);
    }

    private void Initialize(
        Document document,
        IMacroEngine macroEngine,
        IEditorCommandRouter router,
        Func<RibbonContextSnapshot?> snapshotProvider)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _macroEngine = macroEngine ?? throw new ArgumentNullException(nameof(macroEngine));
        _macroDebugEngine = macroEngine as IMacroDebugEngine;
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));

        InitializeComponent();

        _modulesList = this.FindControl<ListBox>("ModulesList")!;
        _proceduresList = this.FindControl<ListBox>("ProceduresList")!;
        _recordingsList = this.FindControl<ListBox>("RecordingsList")!;
        _referencesList = this.FindControl<ListBox>("ReferencesList")!;
        _localsList = this.FindControl<ListBox>("LocalsList")!;
        _callStackList = this.FindControl<ListBox>("CallStackList")!;
        _breakpointsList = this.FindControl<ListBox>("BreakpointsList")!;
        _sourceEditor = this.FindControl<TextBox>("SourceEditor")!;
        _breakpointLineBox = this.FindControl<TextBox>("BreakpointLineBox")!;
        _immediateInput = this.FindControl<TextBox>("ImmediateInput")!;
        _immediateOutput = this.FindControl<TextBox>("ImmediateOutput")!;

        _modulesList.ItemsSource = _modules;
        _proceduresList.ItemsSource = _procedures;
        _recordingsList.ItemsSource = _recordings;
        _referencesList.ItemsSource = _references;
        _breakpointsList.ItemsSource = _breakpoints;
        _localsList.ItemsSource = _locals;
        _callStackList.ItemsSource = _callStack;

        _procedureRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _procedureRefreshTimer.Tick += OnProcedureRefreshTick;

        LoadDocument(_document);
    }

    public void LoadDocument(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        DetachDebugSession();

        _modules.Clear();
        foreach (var module in _document.Macros.VbaModules)
        {
            _modules.Add(module);
        }

        _modulesList.SelectedIndex = _modules.Count > 0 ? 0 : -1;
        RefreshRecordings();
        RefreshReferences();
        SetDirtyState(false);
    }

    public void UpdateContext(
        Document document,
        IMacroEngine macroEngine,
        IEditorCommandRouter router,
        Func<RibbonContextSnapshot?> snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(macroEngine);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        _macroEngine = macroEngine;
        _macroDebugEngine = macroEngine as IMacroDebugEngine;
        _router = router;
        _snapshotProvider = snapshotProvider;

        LoadDocument(document);
    }

    protected override void OnClosed(EventArgs e)
    {
        _procedureRefreshTimer.Stop();
        DetachDebugSession();
        base.OnClosed(e);
    }

    public async Task StartDebugAsync()
    {
        if (_macroDebugEngine is null)
        {
            AppendImmediateOutput("Debug runtime is unavailable.");
            return;
        }

        await SaveModulesAsync();
        var macro = await ResolveMacroAsync();
        if (macro is null)
        {
            return;
        }

        if (!await EnsureMacrosTrustedAsync())
        {
            return;
        }

        var session = new VbaDebugSession();
        ApplyBreakpoints(session);
        AttachDebugSession(session);

        var snapshot = _snapshotProvider.Invoke();
        var result = await _macroDebugEngine.RunDebugAsync(macro, _router, session, snapshot);
        if (!result.Success)
        {
            var details = BuildMacroDiagnosticsMessage(result);
            var diagnosticsDialog = new MacroDiagnosticsDialog("Macro Error", details);
            await diagnosticsDialog.ShowDialog(this);
        }
    }

    private void OnModuleSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        CommitEditorText();
        _procedureRefreshTimer.Stop();
        _currentModule = _modulesList.SelectedItem as VbaModuleInfo;
        if (_currentModule is null)
        {
            _suppressTextChanged = true;
            _sourceEditor.Text = string.Empty;
            _suppressTextChanged = false;
            _procedures.Clear();
            return;
        }

        _suppressTextChanged = true;
        _sourceEditor.Text = _currentModule.Source ?? string.Empty;
        _suppressTextChanged = false;
        UpdateProcedures(_currentModule.Procedures);
    }

    private async void OnProcedureDoubleTapped(object? sender, RoutedEventArgs e)
    {
        await StartDebugAsync();
    }

    private void OnSourceTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged)
        {
            return;
        }

        SetDirtyState(true);
        _procedureRefreshTimer.Stop();
        _procedureRefreshTimer.Start();
    }

    private void OnProcedureRefreshTick(object? sender, EventArgs e)
    {
        _procedureRefreshTimer.Stop();
        RefreshProceduresFromEditor();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await SaveModulesAsync();
    }

    private async void OnValidateClick(object? sender, RoutedEventArgs e)
    {
        if (_currentModule is null)
        {
            return;
        }

        CommitEditorText();
        var source = _currentModule.Source ?? string.Empty;
        try
        {
            VbaCompiler.Compile(source);
            AppendImmediateOutput("Module compiled successfully.");
        }
        catch (VbaParseException ex)
        {
            var span = new VbaSourceSpan(ex.Token.Line, ex.Token.Column);
            var diagnostic = new VbaDiagnostic(ex.Message, span, Array.Empty<VbaStackFrame>());
            var details = BuildMacroDiagnosticsMessage(new MacroRunResult(false, $"Parse error: {ex.Message}", diagnostic));
            var dialog = new MacroDiagnosticsDialog("Macro Compile Error", details);
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            var dialog = new MacroDiagnosticsDialog("Macro Compile Error", ex.Message);
            await dialog.ShowDialog(this);
        }
    }

    private async void OnConvertRecordingClick(object? sender, RoutedEventArgs e)
    {
        var macro = _recordingsList.SelectedItem as MacroDefinition;
        if (macro is null)
        {
            return;
        }

        var generated = MacroRecorderVbaEmitter.EmitVba(macro);
        var module = _currentModule ?? EnsureRecordedModule();
        if (module is null)
        {
            return;
        }

        var existing = module.Source ?? string.Empty;
        if (!string.IsNullOrEmpty(existing) && !existing.EndsWith(Environment.NewLine, StringComparison.Ordinal))
        {
            existing += Environment.NewLine;
        }

        existing += generated + Environment.NewLine;
        module.Source = existing;

        _suppressTextChanged = true;
        _sourceEditor.Text = existing;
        _suppressTextChanged = false;

        SetDirtyState(true);
        RefreshProceduresFromEditor();
        await SaveModulesAsync();
    }

    private async void OnAddReferenceClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("Add Reference", "Reference name:", string.Empty);
        var name = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var reference = new VbaProjectReference
        {
            Name = name.Trim()
        };

        _document.Macros.References.Add(reference);
        _references.Add(reference);
        SetDirtyState(true);
    }

    private void OnRemoveReferenceClick(object? sender, RoutedEventArgs e)
    {
        var reference = _referencesList.SelectedItem as VbaProjectReference;
        if (reference is null)
        {
            return;
        }

        _document.Macros.References.Remove(reference);
        _references.Remove(reference);
        SetDirtyState(true);
    }

    private void OnAddBreakpointClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_breakpointLineBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
        {
            AppendImmediateOutput("Invalid breakpoint line.");
            return;
        }

        var procedure = _proceduresList.SelectedItem as string;
        var breakpoint = new VbaBreakpoint(line, string.IsNullOrWhiteSpace(procedure) ? null : procedure);
        _breakpoints.Add(breakpoint);
        _debugSession?.AddBreakpoint(breakpoint);
    }

    private void OnRemoveBreakpointClick(object? sender, RoutedEventArgs e)
    {
        var breakpoint = _breakpointsList.SelectedItem as VbaBreakpoint;
        if (breakpoint is null)
        {
            return;
        }

        _breakpoints.Remove(breakpoint);
        _debugSession?.RemoveBreakpoint(breakpoint);
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        RequireDebugSession()?.Continue();
    }

    private void OnBreakClick(object? sender, RoutedEventArgs e)
    {
        RequireDebugSession()?.Break();
    }

    private void OnStepInClick(object? sender, RoutedEventArgs e)
    {
        RequireDebugSession()?.StepIn();
    }

    private void OnStepOverClick(object? sender, RoutedEventArgs e)
    {
        RequireDebugSession()?.StepOver();
    }

    private void OnStepOutClick(object? sender, RoutedEventArgs e)
    {
        RequireDebugSession()?.StepOut();
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        RequireDebugSession()?.Stop();
    }

    private async void OnDebugClick(object? sender, RoutedEventArgs e)
    {
        await StartDebugAsync();
    }

    private void OnImmediateExecuteClick(object? sender, RoutedEventArgs e)
    {
        ExecuteImmediate();
    }

    private void OnImmediateKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            e.Handled = true;
            ExecuteImmediate();
        }
    }

    private void ExecuteImmediate()
    {
        var expression = _immediateInput.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        AppendImmediateOutput($"> {expression}");

        if (_debugSession is null || !_debugSession.IsPaused)
        {
            AppendImmediateOutput("Debugger is not paused.");
            return;
        }

        if (_debugSession.TryEvaluateImmediate(expression, out var result, out var errorMessage))
        {
            AppendImmediateOutput(FormatValue(result));
        }
        else if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            AppendImmediateOutput(errorMessage);
        }
        else
        {
            AppendImmediateOutput("Immediate window is unavailable.");
        }
    }

    private void CommitEditorText()
    {
        if (_currentModule is null)
        {
            return;
        }

        _currentModule.Source = _sourceEditor.Text ?? string.Empty;
    }

    private void RefreshProceduresFromEditor()
    {
        if (_currentModule is null)
        {
            return;
        }

        var source = _sourceEditor.Text ?? string.Empty;
        _currentModule.Source = source;
        VbaMacroUtilities.UpdateModuleProcedures(_currentModule, source);
        UpdateProcedures(_currentModule.Procedures);
    }

    private void UpdateProcedures(IEnumerable<string> procedures)
    {
        _procedures.Clear();
        foreach (var name in procedures)
        {
            _procedures.Add(name);
        }
    }

    private async Task SaveModulesAsync()
    {
        CommitEditorText();
        foreach (var module in _document.Macros.VbaModules)
        {
            VbaMacroUtilities.UpdateModuleProcedures(module, module.Source);
        }

        VbaMacroUtilities.SyncVbaDefinitions(_document.Macros);
        if (_currentModule is not null)
        {
            UpdateProcedures(_currentModule.Procedures);
        }

        RefreshRecordings();
        SetDirtyState(false);
        await Task.CompletedTask;
    }

    private void RefreshRecordings()
    {
        _recordings.Clear();
        foreach (var macro in _document.Macros.Items)
        {
            if (macro.Language == MacroLanguage.CommandSequence)
            {
                _recordings.Add(macro);
            }
        }
    }

    private void RefreshReferences()
    {
        _references.Clear();
        foreach (var reference in _document.Macros.References)
        {
            _references.Add(reference);
        }
    }

    private VbaModuleInfo? EnsureRecordedModule()
    {
        var module = _currentModule;
        if (module is not null)
        {
            return module;
        }

        var name = ResolveUniqueModuleName("RecordedMacros");
        module = new VbaModuleInfo
        {
            Name = name,
            StreamName = name,
            Source = string.Empty
        };

        _document.Macros.VbaModules.Add(module);
        _modules.Add(module);
        _modulesList.SelectedItem = module;
        return module;
    }

    private async Task<MacroDefinition?> ResolveMacroAsync()
    {
        var procedure = _proceduresList.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(procedure))
        {
            return FindMacroDefinition(procedure);
        }

        var items = BuildMacroPickerItems();
        if (items.Count == 0)
        {
            AppendImmediateOutput("No VBA macros available.");
            return null;
        }

        var dialog = new PickerDialog("Select Macro", items);
        var result = await dialog.ShowDialog<PickerItem?>(this);
        if (result is null)
        {
            return null;
        }

        return FindMacroDefinition(result.Id);
    }

    private List<PickerItem> BuildMacroPickerItems()
    {
        var items = new List<PickerItem>();
        foreach (var macro in _document.Macros.Items)
        {
            if (macro.Language == MacroLanguage.Vba)
            {
                items.Add(new PickerItem(macro.Name, macro.Name, macro.Description ?? string.Empty));
            }
        }

        return items;
    }

    private MacroDefinition? FindMacroDefinition(string name)
    {
        foreach (var macro in _document.Macros.Items)
        {
            if (macro.Language == MacroLanguage.Vba
                && string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return macro;
            }
        }

        return null;
    }

    private async Task<bool> EnsureMacrosTrustedAsync()
    {
        if (_document.Macros.IsTrusted)
        {
            return true;
        }

        var items = new[]
        {
            new PickerItem("enable", "Enable Macros", "Enable macros for this document.", IconKey: "RibbonIcon.Alert")
        };

        var dialog = new PickerDialog("Macro Security", items);
        var result = await dialog.ShowDialog<PickerItem?>(this);
        if (result is null)
        {
            return false;
        }

        _document.Macros.IsTrusted = true;
        foreach (var macro in _document.Macros.Items)
        {
            macro.IsTrusted = true;
        }

        return true;
    }

    private void AttachDebugSession(VbaDebugSession session)
    {
        DetachDebugSession();
        _debugSession = session;
        _debugSession.Paused += OnDebugPaused;
        _debugSession.Resumed += OnDebugResumed;
        _debugSession.Stopped += OnDebugStopped;
    }

    private void DetachDebugSession()
    {
        if (_debugSession is null)
        {
            return;
        }

        _debugSession.Paused -= OnDebugPaused;
        _debugSession.Resumed -= OnDebugResumed;
        _debugSession.Stopped -= OnDebugStopped;
        _debugSession = null;
    }

    private void OnDebugPaused(object? sender, VbaDebugState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateDebugState(state);
            AppendImmediateOutput("[Paused]");
        });
    }

    private void OnDebugResumed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => AppendImmediateOutput("[Resumed]"));
    }

    private void OnDebugStopped(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _locals.Clear();
            _callStack.Clear();
            AppendImmediateOutput("[Stopped]");
        });
    }

    private void UpdateDebugState(VbaDebugState state)
    {
        _locals.Clear();
        foreach (var (name, value) in state.Locals.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            _locals.Add($"{name} = {FormatValue(value)}");
        }

        _callStack.Clear();
        _callStack.Add($"{state.Location.ProcedureName} (Line {state.Location.Span.Line})");
        foreach (var frame in state.CallStack)
        {
            if (frame.Span.HasValue)
            {
                _callStack.Add($"{frame.ProcedureName} (Line {frame.Span.Value.Line})");
            }
            else
            {
                _callStack.Add(frame.ProcedureName);
            }
        }
    }

    private void ApplyBreakpoints(VbaDebugSession session)
    {
        foreach (var breakpoint in _breakpoints)
        {
            session.AddBreakpoint(breakpoint);
        }
    }

    private VbaDebugSession? RequireDebugSession()
    {
        if (_debugSession is null)
        {
            AppendImmediateOutput("No active debug session.");
        }

        return _debugSession;
    }

    private void SetDirtyState(bool isDirty)
    {
        if (_isDirty == isDirty)
        {
            return;
        }

        _isDirty = isDirty;
        Title = _isDirty ? "VBA Editor *" : "VBA Editor";
    }

    private static string FormatValue(VbaValue value)
    {
        return value.Kind switch
        {
            VbaValueKind.String => $"\"{value.AsString()}\"",
            VbaValueKind.Boolean => value.AsBoolean() ? "True" : "False",
            VbaValueKind.Double => value.AsDouble().ToString(CultureInfo.InvariantCulture),
            VbaValueKind.Object => value.AsObjectPath() ?? "Object",
            VbaValueKind.Array => "Array",
            _ => "Empty"
        };
    }

    private void AppendImmediateOutput(string message)
    {
        if (string.IsNullOrEmpty(_immediateOutput.Text))
        {
            _immediateOutput.Text = message;
        }
        else
        {
            _immediateOutput.Text += Environment.NewLine + message;
        }

        _immediateOutput.CaretIndex = _immediateOutput.Text?.Length ?? 0;
    }

    private string ResolveUniqueModuleName(string baseName)
    {
        var trimmed = string.IsNullOrWhiteSpace(baseName) ? "Module" : baseName.Trim();
        var candidate = trimmed;
        var suffix = 1;
        while (ModuleExists(candidate))
        {
            candidate = $"{trimmed}{suffix}";
            suffix++;
        }

        return candidate;
    }

    private bool ModuleExists(string name)
    {
        foreach (var module in _document.Macros.VbaModules)
        {
            if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildMacroDiagnosticsMessage(MacroRunResult result)
    {
        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            builder.AppendLine(result.ErrorMessage);
        }

        if (result.Diagnostic is not null)
        {
            if (result.Diagnostic.Span.HasValue)
            {
                var span = result.Diagnostic.Span.Value;
                builder.AppendLine($"Line {span.Line}, Column {span.Column}");
            }

            if (result.Diagnostic.CallStack.Count > 0)
            {
                builder.AppendLine("Call stack:");
                foreach (var frame in result.Diagnostic.CallStack)
                {
                    if (frame.Span.HasValue)
                    {
                        var span = frame.Span.Value;
                        builder.AppendLine($"  {frame.ProcedureName} (Line {span.Line}, Column {span.Column})");
                    }
                    else
                    {
                        builder.AppendLine($"  {frame.ProcedureName}");
                    }
                }
            }
        }

        var message = builder.ToString();
        return string.IsNullOrWhiteSpace(message) ? "Macro execution failed." : message.TrimEnd();
    }
}
