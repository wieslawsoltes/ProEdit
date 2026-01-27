using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Macros;
using Vibe.Office.Vba;
using Vibe.Office.Vba.Runtime;

namespace Vibe.Word.App;

public partial class MacroManagerDialog : Window
{
    private static readonly FilePickerFileType VbaModuleFileType = new("VBA Modules")
    {
        Patterns = new[] { "*.bas" },
        MimeTypes = new[] { "text/plain" }
    };

    private Document _document = null!;
    private IMacroEngine _macroEngine = null!;
    private IEditorCommandRouter _router = null!;
    private Func<RibbonContextSnapshot?> _snapshotProvider = null!;
    private readonly ObservableCollection<VbaModuleInfo> _modules = new();

    private ListBox _modulesList = null!;
    private ListBox _proceduresList = null!;
    private TextBox _sourceEditor = null!;
    private Button _saveButton = null!;
    private DispatcherTimer _procedureRefreshTimer = null!;
    private VbaModuleInfo? _currentModule;
    private bool _isDirty;
    private bool _suppressTextChanged;

    public MacroManagerDialog()
    {
        var document = new Document();
        Initialize(document, new MacroEngine(document), new NullEditorCommandRouter(), () => null);
    }

    public MacroManagerDialog(
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
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));

        InitializeComponent();

        _modulesList = this.FindControl<ListBox>("ModulesList")!;
        _proceduresList = this.FindControl<ListBox>("ProceduresList")!;
        _sourceEditor = this.FindControl<TextBox>("SourceEditor")!;
        _saveButton = this.FindControl<Button>("SaveButton")!;
        _saveButton.IsEnabled = false;

        _procedureRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _procedureRefreshTimer.Tick += OnProcedureRefreshTick;

        foreach (var module in _document.Macros.VbaModules)
        {
            _modules.Add(module);
        }

        _modulesList.ItemsSource = _modules;
        if (_modules.Count > 0)
        {
            _modulesList.SelectedIndex = 0;
        }
    }

    private void OnModuleSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        _procedureRefreshTimer.Stop();
        RefreshProceduresFromEditor();
        _currentModule = _modulesList.SelectedItem as VbaModuleInfo;
        if (_currentModule is null)
        {
            _suppressTextChanged = true;
            _sourceEditor.Text = string.Empty;
            _suppressTextChanged = false;
            _proceduresList.ItemsSource = null;
            return;
        }

        _suppressTextChanged = true;
        _sourceEditor.Text = _currentModule.Source ?? string.Empty;
        _suppressTextChanged = false;
        _proceduresList.ItemsSource = _currentModule.Procedures;
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

    private async void OnNewModuleClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("New Module", "Module name:", ResolveUniqueModuleName());
        var name = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var uniqueName = ResolveUniqueModuleName(name.Trim(), null);
        var module = new VbaModuleInfo
        {
            Name = uniqueName,
            StreamName = uniqueName,
            Source = $"Sub {ResolveUniqueProcedureName("Macro", null)}(){Environment.NewLine}{Environment.NewLine}End Sub{Environment.NewLine}"
        };

        _document.Macros.VbaModules.Add(module);
        _modules.Add(module);
        _modulesList.SelectedItem = module;
        SetDirtyState(true);
        await SaveModulesAsync();
    }

    private async void OnDeleteModuleClick(object? sender, RoutedEventArgs e)
    {
        if (_currentModule is null)
        {
            return;
        }

        _document.Macros.VbaModules.Remove(_currentModule);
        _modules.Remove(_currentModule);
        _currentModule = null;
        _suppressTextChanged = true;
        _sourceEditor.Text = string.Empty;
        _suppressTextChanged = false;
        _proceduresList.ItemsSource = null;
        SetDirtyState(true);
        await SaveModulesAsync();
    }

    private async void OnRenameModuleClick(object? sender, RoutedEventArgs e)
    {
        if (_currentModule is null)
        {
            return;
        }

        var dialog = new TextInputDialog("Rename Module", "Module name:", _currentModule.Name);
        var name = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmed = name.Trim();
        var uniqueName = ResolveUniqueModuleName(trimmed, _currentModule);
        var oldName = _currentModule.Name;
        _currentModule.Name = uniqueName;
        if (string.IsNullOrWhiteSpace(_currentModule.StreamName)
            || string.Equals(_currentModule.StreamName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            _currentModule.StreamName = uniqueName;
        }

        RefreshModulesList();
        SetDirtyState(true);
        await SaveModulesAsync();
    }

    private async void OnImportModuleClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { VbaModuleFileType }
        });

        if (result.Count == 0)
        {
            return;
        }

        var path = result[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var source = await File.ReadAllTextAsync(path);
        var baseName = Path.GetFileNameWithoutExtension(path);
        var uniqueName = ResolveUniqueModuleName(baseName, null);
        var module = new VbaModuleInfo
        {
            Name = uniqueName,
            StreamName = uniqueName,
            Source = source
        };

        VbaMacroUtilities.UpdateModuleProcedures(module, module.Source);
        _document.Macros.VbaModules.Add(module);
        _modules.Add(module);
        _modulesList.SelectedItem = module;
        SetDirtyState(true);
        await SaveModulesAsync();
    }

    private async void OnExportModuleClick(object? sender, RoutedEventArgs e)
    {
        if (_currentModule is null || StorageProvider is null)
        {
            return;
        }

        CommitEditorText();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "bas",
            FileTypeChoices = new[] { VbaModuleFileType },
            SuggestedFileName = _currentModule.Name
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await File.WriteAllTextAsync(path, _currentModule.Source ?? string.Empty);
    }

    private async void OnNewProcedureClick(object? sender, RoutedEventArgs e)
    {
        if (_currentModule is null)
        {
            return;
        }

        var dialog = new TextInputDialog("New Macro", "Macro name:", ResolveUniqueProcedureName("Macro", _currentModule));
        var name = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        CommitEditorText();
        var template = $"{Environment.NewLine}Sub {name.Trim()}(){Environment.NewLine}{Environment.NewLine}End Sub{Environment.NewLine}";
        _sourceEditor.Text = (_currentModule.Source ?? string.Empty) + template;
        _currentModule.Source = _sourceEditor.Text;
        SetDirtyState(true);
        await SaveModulesAsync();
    }

    private async void OnRunClick(object? sender, RoutedEventArgs e)
    {
        await RunSelectedProcedureAsync();
    }

    private async void OnProcedureDoubleTapped(object? sender, RoutedEventArgs e)
    {
        await RunSelectedProcedureAsync();
    }

    private async Task RunSelectedProcedureAsync()
    {
        if (_currentModule is null)
        {
            return;
        }

        await SaveModulesAsync();

        var procedure = _proceduresList.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(procedure))
        {
            return;
        }

        if (!await EnsureMacrosTrustedAsync())
        {
            return;
        }

        MacroDefinition? target = null;
        foreach (var macro in _document.Macros.Items)
        {
            if (macro.Language == MacroLanguage.Vba
                && string.Equals(macro.Name, procedure, StringComparison.OrdinalIgnoreCase))
            {
                target = macro;
                break;
            }
        }

        if (target is null)
        {
            return;
        }

        var snapshot = _snapshotProvider.Invoke();
        var runResult = await _macroEngine.RunAsync(target, _router, snapshot);
        if (!runResult.Success)
        {
            var details = BuildMacroDiagnosticsMessage(runResult);
            var diagnosticsDialog = new MacroDiagnosticsDialog("Macro Error", details);
            await diagnosticsDialog.ShowDialog(this);
        }
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

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await SaveModulesAsync();
    }

    private async void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_isDirty)
        {
            await SaveModulesAsync();
        }

        _procedureRefreshTimer.Stop();
        Close();
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
        _proceduresList.ItemsSource = null;
        _proceduresList.ItemsSource = _currentModule.Procedures;
    }

    private void RefreshModulesList()
    {
        var selected = _currentModule;
        _modulesList.ItemsSource = null;
        _modulesList.ItemsSource = _modules;
        if (selected is not null)
        {
            _modulesList.SelectedItem = selected;
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
            _proceduresList.ItemsSource = null;
            _proceduresList.ItemsSource = _currentModule.Procedures;
        }

        SetDirtyState(false);
        await Task.CompletedTask;
    }

    private void SetDirtyState(bool isDirty)
    {
        if (_isDirty == isDirty)
        {
            return;
        }

        _isDirty = isDirty;
        UpdateSaveButton();
    }

    private void UpdateSaveButton()
    {
        _saveButton.IsEnabled = _isDirty;
        Title = _isDirty ? "Macros *" : "Macros";
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

    private string ResolveUniqueModuleName(string? baseName = null, VbaModuleInfo? ignore = null)
    {
        var trimmed = string.IsNullOrWhiteSpace(baseName) ? "Module" : baseName.Trim();
        var candidate = trimmed;
        var suffix = 1;
        while (ModuleExists(candidate, ignore))
        {
            candidate = $"{trimmed}{suffix}";
            suffix++;
        }

        return candidate;
    }

    private string ResolveUniqueProcedureName(string baseName, VbaModuleInfo? module)
    {
        var suffix = 1;
        while (true)
        {
            var name = $"{baseName}{suffix}";
            if (module is null || !ProcedureExists(module, name))
            {
                return name;
            }

            suffix++;
        }
    }

    private bool ModuleExists(string name, VbaModuleInfo? ignore)
    {
        foreach (var module in _document.Macros.VbaModules)
        {
            if (ignore is not null && ReferenceEquals(module, ignore))
            {
                continue;
            }

            if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ProcedureExists(VbaModuleInfo module, string name)
    {
        foreach (var procedure in module.Procedures)
        {
            if (string.Equals(procedure, name, StringComparison.OrdinalIgnoreCase))
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
