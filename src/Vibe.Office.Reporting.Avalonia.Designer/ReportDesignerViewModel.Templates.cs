using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using ReactiveUI;
using Vibe.Office.Reporting;

namespace Vibe.Office.Reporting.Avalonia.Designer;

public sealed partial class ReportDesignerViewModel
{
    private const string EmbeddedTemplateReferenceValue = "__embedded__";

    private readonly ObservableCollection<ReportDesignerTemplateBindingEntryViewModel> _templateBindingEntries = new();
    private readonly ObservableCollection<ReportDesignerTemplatePlaceholderEntryViewModel> _templatePlaceholderEntries = new();
    private readonly ObservableCollection<ReportDesignerChoiceOptionViewModel> _templateReferenceOptions = new();
    private readonly IReadOnlyList<ReportDesignerChoiceOptionViewModel> _templateFormatOptions =
        CreateEnumOptions<ReportDocumentTemplateFormat>();
    private readonly IReadOnlyList<ReportDesignerChoiceOptionViewModel> _templateStorageModeOptions =
    [
        new ReportDesignerChoiceOptionViewModel("embedded", "Embedded"),
        new ReportDesignerChoiceOptionViewModel("external", "External")
    ];

    private ReportDesignerTemplateBindingEntryViewModel? _selectedTemplateBindingEntry;
    private ReportDesignerTemplatePlaceholderEntryViewModel? _selectedTemplatePlaceholderEntry;
    private ReportDesignerChoiceOptionViewModel? _selectedTemplateFormatOption;
    private ReportDesignerChoiceOptionViewModel? _selectedTemplateStorageModeOption;
    private ReportDesignerChoiceOptionViewModel? _selectedTemplateReferenceOption;
    private bool _isTemplatePreviewBusy;
    private bool _suppressTemplateWorkspaceUpdates;
    private string _selectedTemplateContentText = string.Empty;
    private string _selectedTemplateDefinitionIdText = string.Empty;
    private string _selectedTemplateSourceText = string.Empty;
    private string _templatePreviewText = string.Empty;
    private string _templateWorkspaceStatusMessage = "Select a template item or shared template to edit content, bindings, and preview.";

    public ReadOnlyObservableCollection<ReportDesignerTemplateBindingEntryViewModel> TemplateBindingEntries { get; private set; } = null!;

    public ReadOnlyObservableCollection<ReportDesignerTemplatePlaceholderEntryViewModel> TemplatePlaceholderEntries { get; private set; } = null!;

    public ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel> TemplateReferenceOptions { get; private set; } = null!;

    public IReadOnlyList<ReportDesignerChoiceOptionViewModel> TemplateFormatOptions => _templateFormatOptions;

    public IReadOnlyList<ReportDesignerChoiceOptionViewModel> TemplateStorageModeOptions => _templateStorageModeOptions;

    public bool HasSelectedTemplateItem => GetSelectedTemplateItemForEditing() is not null;

    public bool HasSelectedSharedTemplate => GetSelectedSharedTemplateDefinition() is not null;

    public bool HasActiveTemplateContentTarget => GetActiveTemplateContentTarget() is not null;

    public bool ShowTemplateWorkspacePlaceholder => !HasActiveTemplateContentTarget;

    public bool ShowTemplateContentEditor => GetActiveTemplateContentTarget() switch
    {
        DocumentTemplateItem => true,
        ReportSharedTemplateDefinition { IsEmbedded: true } => true,
        _ => false
    };

    public bool ShowTemplateSourceEditor => GetActiveTemplateContentTarget() is ReportSharedTemplateDefinition;

    public bool ShowTemplateReferenceEditor => HasSelectedTemplateItem;

    public bool ShowTemplateBindingEditor => HasSelectedTemplateItem;

    public bool ShowTemplateStorageModeEditor => GetActiveTemplateContentTarget() is ReportSharedTemplateDefinition;

    public bool CanEmbedActiveTemplateForEditing => GetActiveTemplateContentTarget() is ReportSharedTemplateDefinition { IsEmbedded: false };

    public bool CanRefreshTemplatePreview => HasActiveTemplateContentTarget && !IsTemplatePreviewBusy;

    public bool CanInsertSelectedTemplatePlaceholder => SelectedTemplatePlaceholderEntry is not null && HasActiveTemplateContentTarget;

    public bool CanUseSelectedDataNodeInTemplate => HasActiveTemplateContentTarget && CreatePlaceholderFromSelectedDataNode() is not null;

    public bool CanRemoveSelectedTemplateBinding => SelectedTemplateBindingEntry is not null;

    public bool CanCreateTemplateItemFromSelectedTemplate => GetSelectedSharedTemplateDefinition() is not null;

    public bool CanPromoteSelectedTemplateItemToShared => GetSelectedTemplateItemForEditing() is { } item
        && string.IsNullOrWhiteSpace(item.TemplateId)
        && !string.IsNullOrWhiteSpace(item.EmbeddedContent);

    public bool CanDetachSelectedTemplateItem => GetSelectedTemplateItemForEditing() is { } item
        && !string.IsNullOrWhiteSpace(item.TemplateId);

    public bool CanEditReferencedSharedTemplate => ResolveTemplateReference(GetSelectedTemplateItemForEditing()) is not null;

    public string TemplateWorkspaceTitle => GetActiveTemplateContentTarget() switch
    {
        ReportSharedTemplateDefinition template => $"Shared Template · {template.Id}",
        DocumentTemplateItem item => string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name,
        _ => "Template Workspace"
    };

    public string TemplateWorkspaceSubtitle => GetSelectedTemplateItemForEditing() switch
    {
        { TemplateId: { Length: > 0 } templateId } => $"Editing a document-template item linked to shared template '{templateId}'.",
        DocumentTemplateItem => "Editing embedded template content and bindings for the selected report item.",
        _ when GetSelectedSharedTemplateDefinition() is not null => "Editing reusable shared template content used by document-template items.",
        _ => "Select a document-template item or shared template to start editing."
    };

    public string TemplateWorkspaceStatusMessage
    {
        get => _templateWorkspaceStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _templateWorkspaceStatusMessage, value ?? string.Empty);
    }

    public string TemplatePreviewText
    {
        get => _templatePreviewText;
        private set => this.RaiseAndSetIfChanged(ref _templatePreviewText, value ?? string.Empty);
    }

    public bool IsTemplatePreviewBusy
    {
        get => _isTemplatePreviewBusy;
        private set => this.RaiseAndSetIfChanged(ref _isTemplatePreviewBusy, value);
    }

    public string SelectedTemplateDefinitionIdText
    {
        get => _selectedTemplateDefinitionIdText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedTemplateDefinitionIdText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplateDefinitionIdText, normalized);
            if (_suppressTemplateWorkspaceUpdates || GetActiveTemplateContentTarget() is not ReportSharedTemplateDefinition template)
            {
                return;
            }

            var oldId = template.Id;
            var newId = NormalizeRequired(normalized, "Template id");
            if (string.Equals(oldId, newId, StringComparison.Ordinal))
            {
                return;
            }

            RenameSharedTemplateAndReferences(template, oldId, newId);
            OnTemplateWorkspaceChanged("Updated shared template id.");
        }
    }

    public string SelectedTemplateSourceText
    {
        get => _selectedTemplateSourceText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedTemplateSourceText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplateSourceText, normalized);
            if (_suppressTemplateWorkspaceUpdates || GetActiveTemplateContentTarget() is not ReportSharedTemplateDefinition template)
            {
                return;
            }

            template.Source = NormalizeOptional(normalized);
            OnTemplateWorkspaceChanged("Updated template source.");
        }
    }

    public string SelectedTemplateContentText
    {
        get => _selectedTemplateContentText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedTemplateContentText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplateContentText, normalized);
            if (_suppressTemplateWorkspaceUpdates)
            {
                return;
            }

            switch (GetActiveTemplateContentTarget())
            {
                case ReportSharedTemplateDefinition template:
                    template.Content = NormalizeOptional(normalized);
                    OnTemplateWorkspaceChanged("Updated shared template content.");
                    break;
                case DocumentTemplateItem item:
                    item.EmbeddedContent = NormalizeOptional(normalized);
                    OnTemplateWorkspaceChanged("Updated embedded template content.");
                    break;
            }
        }
    }

    public ReportDesignerChoiceOptionViewModel? SelectedTemplateFormatOption
    {
        get => _selectedTemplateFormatOption;
        set
        {
            if (ReferenceEquals(_selectedTemplateFormatOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplateFormatOption, value);
            if (_suppressTemplateWorkspaceUpdates || value is null)
            {
                return;
            }

            var format = Enum.Parse<ReportDocumentTemplateFormat>(value.Value, ignoreCase: true);
            switch (GetActiveTemplateContentTarget())
            {
                case ReportSharedTemplateDefinition template:
                    template.Format = format;
                    break;
                case DocumentTemplateItem item:
                    item.TemplateFormat = format;
                    break;
                default:
                    return;
            }

            OnTemplateWorkspaceChanged("Updated template format.");
        }
    }

    public ReportDesignerChoiceOptionViewModel? SelectedTemplateStorageModeOption
    {
        get => _selectedTemplateStorageModeOption;
        set
        {
            if (ReferenceEquals(_selectedTemplateStorageModeOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplateStorageModeOption, value);
            if (_suppressTemplateWorkspaceUpdates || value is null || GetActiveTemplateContentTarget() is not ReportSharedTemplateDefinition template)
            {
                return;
            }

            template.IsEmbedded = string.Equals(value.Value, "embedded", StringComparison.OrdinalIgnoreCase);
            OnTemplateWorkspaceChanged(template.IsEmbedded
                ? "Switched template storage to embedded."
                : "Switched template storage to external source.");
        }
    }

    public ReportDesignerChoiceOptionViewModel? SelectedTemplateReferenceOption
    {
        get => _selectedTemplateReferenceOption;
        set
        {
            if (ReferenceEquals(_selectedTemplateReferenceOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplateReferenceOption, value);
            if (_suppressTemplateWorkspaceUpdates || value is null || GetSelectedTemplateItemForEditing() is not { } item)
            {
                return;
            }

            if (string.Equals(value.Value, EmbeddedTemplateReferenceValue, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(item.TemplateId))
                {
                    if (ResolveTemplateReference(item) is { } template)
                    {
                        item.TemplateFormat = template.Format;
                        if (string.IsNullOrWhiteSpace(item.EmbeddedContent) && template.IsEmbedded)
                        {
                            item.EmbeddedContent = template.Content;
                        }
                    }

                    item.TemplateId = null;
                }
            }
            else
            {
                item.TemplateId = value.Value;
                if (ResolveTemplateReference(item) is { } template)
                {
                    item.TemplateFormat = template.Format;
                }
            }

            OnTemplateWorkspaceChanged("Updated template reference.");
            RefreshTemplateWorkspaceEditors();
        }
    }

    public ReportDesignerTemplateBindingEntryViewModel? SelectedTemplateBindingEntry
    {
        get => _selectedTemplateBindingEntry;
        set
        {
            if (ReferenceEquals(_selectedTemplateBindingEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplateBindingEntry, value);
            this.RaisePropertyChanged(nameof(CanRemoveSelectedTemplateBinding));
        }
    }

    public ReportDesignerTemplatePlaceholderEntryViewModel? SelectedTemplatePlaceholderEntry
    {
        get => _selectedTemplatePlaceholderEntry;
        set
        {
            if (ReferenceEquals(_selectedTemplatePlaceholderEntry, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTemplatePlaceholderEntry, value);
            this.RaisePropertyChanged(nameof(CanInsertSelectedTemplatePlaceholder));
        }
    }

    public ReactiveCommand<Unit, Unit> AddTemplateBindingCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> RemoveSelectedTemplateBindingCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> InsertSelectedTemplatePlaceholderCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> UseSelectedDataNodeInTemplateCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> RefreshTemplatePreviewCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> CreateTemplateItemFromSelectedTemplateCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> PromoteSelectedTemplateItemToSharedCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> DetachSelectedTemplateItemCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> EditReferencedSharedTemplateCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> EmbedActiveTemplateForEditingCommand { get; private set; } = null!;

    private void InitializeTemplateWorkspace()
    {
        TemplateBindingEntries = new ReadOnlyObservableCollection<ReportDesignerTemplateBindingEntryViewModel>(_templateBindingEntries);
        TemplatePlaceholderEntries = new ReadOnlyObservableCollection<ReportDesignerTemplatePlaceholderEntryViewModel>(_templatePlaceholderEntries);
        TemplateReferenceOptions = new ReadOnlyObservableCollection<ReportDesignerChoiceOptionViewModel>(_templateReferenceOptions);

        AddTemplateBindingCommand = DesignerCommandFactory.Create(AddTemplateBinding);
        RemoveSelectedTemplateBindingCommand = DesignerCommandFactory.Create(RemoveSelectedTemplateBinding);
        InsertSelectedTemplatePlaceholderCommand = DesignerCommandFactory.Create(InsertSelectedTemplatePlaceholder);
        UseSelectedDataNodeInTemplateCommand = DesignerCommandFactory.Create(UseSelectedDataNodeInTemplate);
        RefreshTemplatePreviewCommand = DesignerCommandFactory.CreateFromTask(RefreshTemplatePreviewAsync);
        CreateTemplateItemFromSelectedTemplateCommand = DesignerCommandFactory.Create(CreateTemplateItemFromSelectedTemplate);
        PromoteSelectedTemplateItemToSharedCommand = DesignerCommandFactory.Create(PromoteSelectedTemplateItemToShared);
        DetachSelectedTemplateItemCommand = DesignerCommandFactory.Create(DetachSelectedTemplateItem);
        EditReferencedSharedTemplateCommand = DesignerCommandFactory.Create(EditReferencedSharedTemplate);
        EmbedActiveTemplateForEditingCommand = DesignerCommandFactory.Create(EmbedActiveTemplateForEditing);
        RefreshTemplatePlaceholderEntries();
    }

    private void RefreshTemplateWorkspaceEditors()
    {
        _suppressTemplateWorkspaceUpdates = true;
        try
        {
            LoadTemplateWorkspaceEditors();
            RefreshTemplateReferenceOptions();
            RefreshTemplateBindingEntries();
            RefreshTemplatePlaceholderEntries();
        }
        finally
        {
            _suppressTemplateWorkspaceUpdates = false;
        }

        RaiseTemplateWorkspacePropertiesChanged();

        if (!HasActiveTemplateContentTarget)
        {
            TemplatePreviewText = string.Empty;
            TemplateWorkspaceStatusMessage = "Select a template item or shared template to edit content, bindings, and preview.";
            return;
        }

        _ = RefreshTemplatePreviewAsync();
    }

    private void LoadTemplateWorkspaceEditors()
    {
        var activeTarget = GetActiveTemplateContentTarget();
        _selectedTemplateDefinitionIdText = activeTarget is ReportSharedTemplateDefinition templateDefinition
            ? templateDefinition.Id
            : string.Empty;
        _selectedTemplateSourceText = activeTarget is ReportSharedTemplateDefinition sourceTemplate
            ? sourceTemplate.Source ?? string.Empty
            : string.Empty;
        _selectedTemplateContentText = activeTarget switch
        {
            ReportSharedTemplateDefinition template => template.Content ?? string.Empty,
            DocumentTemplateItem item => item.EmbeddedContent ?? string.Empty,
            _ => string.Empty
        };

        _selectedTemplateFormatOption = _templateFormatOptions.FirstOrDefault(option =>
            string.Equals(
                option.Value,
                (activeTarget switch
                {
                    ReportSharedTemplateDefinition template => template.Format,
                    DocumentTemplateItem item => item.TemplateFormat,
                    _ => ReportDocumentTemplateFormat.Markdown
                }).ToString(),
                StringComparison.OrdinalIgnoreCase));

        _selectedTemplateStorageModeOption = activeTarget is ReportSharedTemplateDefinition storageTemplate
            ? _templateStorageModeOptions.FirstOrDefault(option =>
                string.Equals(option.Value, storageTemplate.IsEmbedded ? "embedded" : "external", StringComparison.OrdinalIgnoreCase))
            : _templateStorageModeOptions[0];

        this.RaisePropertyChanged(nameof(SelectedTemplateDefinitionIdText));
        this.RaisePropertyChanged(nameof(SelectedTemplateSourceText));
        this.RaisePropertyChanged(nameof(SelectedTemplateContentText));
        this.RaisePropertyChanged(nameof(SelectedTemplateFormatOption));
        this.RaisePropertyChanged(nameof(SelectedTemplateStorageModeOption));
    }

    private void RefreshTemplateReferenceOptions()
    {
        _templateReferenceOptions.Clear();
        _templateReferenceOptions.Add(new ReportDesignerChoiceOptionViewModel(EmbeddedTemplateReferenceValue, "Embedded Content"));
        for (var index = 0; index < ReportDefinition.SharedTemplates.Count; index++)
        {
            var template = ReportDefinition.SharedTemplates[index];
            _templateReferenceOptions.Add(new ReportDesignerChoiceOptionViewModel(template.Id, template.Id));
        }

        _selectedTemplateReferenceOption = GetSelectedTemplateItemForEditing() is { } item
            ? _templateReferenceOptions.FirstOrDefault(option =>
                string.Equals(
                    option.Value,
                    string.IsNullOrWhiteSpace(item.TemplateId) ? EmbeddedTemplateReferenceValue : item.TemplateId,
                    StringComparison.OrdinalIgnoreCase))
            : null;

        this.RaisePropertyChanged(nameof(SelectedTemplateReferenceOption));
    }

    private void RefreshTemplateBindingEntries()
    {
        _templateBindingEntries.Clear();
        if (GetSelectedTemplateItemForEditing() is not { } item)
        {
            SelectedTemplateBindingEntry = null;
            return;
        }

        foreach (var pair in item.Bindings)
        {
            _templateBindingEntries.Add(new ReportDesignerTemplateBindingEntryViewModel(
                pair.Key,
                pair.Value,
                ApplyTemplateBindingEntryChange,
                RemoveTemplateBindingEntry));
        }

        SelectedTemplateBindingEntry = _templateBindingEntries.FirstOrDefault();
    }

    private void RefreshTemplatePlaceholderEntries()
    {
        _templatePlaceholderEntries.Clear();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var parameterIndex = 0; parameterIndex < ReportDefinition.Parameters.Count; parameterIndex++)
        {
            var parameter = ReportDefinition.Parameters[parameterIndex];
            var key = GetUniqueTemplatePlaceholderKey(parameter.Id, usedKeys);
            _templatePlaceholderEntries.Add(new ReportDesignerTemplatePlaceholderEntryViewModel(
                key,
                "{{" + key + "}}",
                string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName,
                $"Parameters.{parameter.Id}",
                "Parameter"));
        }

        for (var dataSetIndex = 0; dataSetIndex < ReportDefinition.DataSets.Count; dataSetIndex++)
        {
            var dataSet = ReportDefinition.DataSets[dataSetIndex];
            AppendTemplateFieldPlaceholders(dataSet.Id, dataSet.ExpectedFields.Select(static field => (field.Name, field.DataType)), usedKeys);
            AppendTemplateFieldPlaceholders(dataSet.Id, dataSet.CalculatedFields.Select(static field => (field.Name, field.DataType)), usedKeys);
        }

        SelectedTemplatePlaceholderEntry = _templatePlaceholderEntries.FirstOrDefault();
    }

    private void AppendTemplateFieldPlaceholders(
        string dataSetId,
        IEnumerable<(string Name, ReportParameterDataType DataType)> fields,
        HashSet<string> usedKeys)
    {
        foreach (var (fieldName, dataType) in fields)
        {
            var key = GetUniqueTemplatePlaceholderKey(fieldName, usedKeys, dataSetId);
            _templatePlaceholderEntries.Add(new ReportDesignerTemplatePlaceholderEntryViewModel(
                key,
                "{{" + key + "}}",
                $"{fieldName} ({dataSetId})",
                CreateReportFieldExpression(dataSetId, fieldName),
                dataType.ToString()));
        }
    }

    private static string GetUniqueTemplatePlaceholderKey(
        string baseName,
        HashSet<string> usedKeys,
        string? prefix = null)
    {
        var candidate = SanitizeTemplatePlaceholderKey(string.IsNullOrWhiteSpace(prefix) ? baseName : $"{prefix}_{baseName}");
        if (!usedKeys.Contains(candidate))
        {
            usedKeys.Add(candidate);
            return candidate;
        }

        candidate = SanitizeTemplatePlaceholderKey(baseName);
        if (!usedKeys.Contains(candidate))
        {
            usedKeys.Add(candidate);
            return candidate;
        }

        var suffix = 2;
        var numbered = candidate;
        while (usedKeys.Contains(numbered))
        {
            numbered = $"{candidate}{suffix.ToString(CultureInfo.InvariantCulture)}";
            suffix++;
        }

        usedKeys.Add(numbered);
        return numbered;
    }

    private static string SanitizeTemplatePlaceholderKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Value";
        }

        Span<char> buffer = stackalloc char[value.Length];
        var position = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            buffer[position++] = char.IsLetterOrDigit(character) || character == '_' ? character : '_';
        }

        var sanitized = new string(buffer[..position]).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Value" : sanitized;
    }

    private void AddTemplateBinding()
    {
        if (GetSelectedTemplateItemForEditing() is not { } item)
        {
            return;
        }

        var baseKey = "Value";
        var existingKeys = item.Bindings.Keys.Concat(_templateBindingEntries.Select(static entry => entry.Key));
        var key = CreateUniqueId(baseKey, existingKeys);
        var entry = new ReportDesignerTemplateBindingEntryViewModel(
            key,
            string.Empty,
            ApplyTemplateBindingEntryChange,
            RemoveTemplateBindingEntry);
        _templateBindingEntries.Add(entry);
        SelectedTemplateBindingEntry = entry;
        ApplyTemplateBindingsFromEntries();
        OnTemplateWorkspaceChanged("Added template binding.");
    }

    private void RemoveSelectedTemplateBinding()
    {
        if (SelectedTemplateBindingEntry is null)
        {
            return;
        }

        RemoveTemplateBindingEntry(SelectedTemplateBindingEntry);
    }

    private void RemoveTemplateBindingEntry(ReportDesignerTemplateBindingEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_templateBindingEntries.Remove(entry))
        {
            return;
        }

        SelectedTemplateBindingEntry = _templateBindingEntries.FirstOrDefault();
        ApplyTemplateBindingsFromEntries();
        OnTemplateWorkspaceChanged("Removed template binding.");
    }

    private void ApplyTemplateBindingEntryChange(ReportDesignerTemplateBindingEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_suppressTemplateWorkspaceUpdates || GetSelectedTemplateItemForEditing() is null)
        {
            return;
        }

        ApplyTemplateBindingsFromEntries();
        OnTemplateWorkspaceChanged("Updated template bindings.");
    }

    private void ApplyTemplateBindingsFromEntries()
    {
        if (GetSelectedTemplateItemForEditing() is not { } item)
        {
            return;
        }

        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _templateBindingEntries.Count; index++)
        {
            var entry = _templateBindingEntries[index];
            var key = entry.Key.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            bindings[key] = entry.Expression.Trim();
        }

        item.Bindings.Clear();
        foreach (var pair in bindings)
        {
            item.Bindings[pair.Key] = pair.Value;
        }
    }

    private void InsertSelectedTemplatePlaceholder()
    {
        if (SelectedTemplatePlaceholderEntry is not { } placeholder)
        {
            return;
        }

        InsertTemplatePlaceholder(placeholder);
    }

    private void UseSelectedDataNodeInTemplate()
    {
        var placeholder = CreatePlaceholderFromSelectedDataNode();
        if (placeholder is null)
        {
            return;
        }

        InsertTemplatePlaceholder(placeholder);
    }

    private void InsertTemplatePlaceholder(ReportDesignerTemplatePlaceholderEntryViewModel placeholder)
    {
        ArgumentNullException.ThrowIfNull(placeholder);

        if (GetSelectedTemplateItemForEditing() is { } item)
        {
            ApplyTemplateBindingAndPlaceholder(item, placeholder.Key, placeholder.Expression, insertToken: false);
            RefreshTemplateBindingEntries();
        }

        var currentContent = SelectedTemplateContentText;
        var separator = string.IsNullOrWhiteSpace(currentContent)
            ? string.Empty
            : (currentContent.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? string.Empty : Environment.NewLine);
        SelectedTemplateContentText = currentContent + separator + placeholder.Token;
        OnTemplateWorkspaceChanged($"Inserted placeholder '{placeholder.Token}'.");
    }

    internal void ApplyTemplateBindingAndPlaceholder(
        DocumentTemplateItem item,
        string key,
        string expression,
        bool insertToken = true)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        item.Bindings[key] = expression ?? string.Empty;
        if (!insertToken)
        {
            return;
        }

        var token = "{{" + key + "}}";
        if (ResolveTemplateReference(item) is { } sharedTemplate)
        {
            sharedTemplate.Content = AppendTemplateToken(sharedTemplate.Content, token);
            return;
        }

        item.EmbeddedContent = AppendTemplateToken(item.EmbeddedContent, token);
    }

    private ReportDesignerTemplatePlaceholderEntryViewModel? CreatePlaceholderFromSelectedDataNode()
    {
        var selectedDataNode = SelectedDataNode;
        if (selectedDataNode is null)
        {
            return null;
        }

        return selectedDataNode switch
        {
            { Kind: ReportDesignerDataNodeKind.Parameter, Target: ReportParameterDefinition parameter } =>
                new ReportDesignerTemplatePlaceholderEntryViewModel(
                    SanitizeTemplatePlaceholderKey(parameter.Id),
                    "{{" + SanitizeTemplatePlaceholderKey(parameter.Id) + "}}",
                    string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName,
                    $"Parameters.{parameter.Id}",
                    "Parameter"),
            { Kind: ReportDesignerDataNodeKind.QueryField, Target: ReportFieldDefinition field, SelectionTarget: ReportDataSetDefinition dataSet } =>
                new ReportDesignerTemplatePlaceholderEntryViewModel(
                    SanitizeTemplatePlaceholderKey(field.Name),
                    "{{" + SanitizeTemplatePlaceholderKey(field.Name) + "}}",
                    $"{field.Name} ({dataSet.Id})",
                    CreateReportFieldExpression(dataSet.Id, field.Name),
                    field.DataType.ToString()),
            { Kind: ReportDesignerDataNodeKind.CalculatedField, Target: ReportCalculatedFieldDefinition calculatedField, SelectionTarget: ReportDataSetDefinition dataSet } =>
                new ReportDesignerTemplatePlaceholderEntryViewModel(
                    SanitizeTemplatePlaceholderKey(calculatedField.Name),
                    "{{" + SanitizeTemplatePlaceholderKey(calculatedField.Name) + "}}",
                    $"{calculatedField.Name} ({dataSet.Id})",
                    CreateReportFieldExpression(dataSet.Id, calculatedField.Name),
                    calculatedField.DataType.ToString()),
            _ => null
        };
    }

    private static string AppendTemplateToken(string? content, string token)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return token;
        }

        if (content.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        return content.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? content + token
            : content + Environment.NewLine + token;
    }

    private async Task RefreshTemplatePreviewAsync(CancellationToken cancellationToken = default)
    {
        var activeTarget = GetActiveTemplateContentTarget();
        if (activeTarget is null || IsTemplatePreviewBusy)
        {
            return;
        }

        var content = SelectedTemplateContentText;
        if (string.IsNullOrWhiteSpace(content))
        {
            TemplatePreviewText = string.Empty;
            TemplateWorkspaceStatusMessage = "Template content is empty.";
            return;
        }

        IsTemplatePreviewBusy = true;
        RaiseTemplateWorkspacePropertiesChanged();
        TemplateWorkspaceStatusMessage = "Resolving template preview...";

        try
        {
            var bindings = GetSelectedTemplateItemForEditing()?.Bindings
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var result = await _dataRuntimeService.PreviewTemplateAsync(BuildPreviewSource(), content, bindings, cancellationToken);
            TemplatePreviewText = result.ResolvedContent;
            TemplateWorkspaceStatusMessage = result.Diagnostics.Count == 0
                ? "Template preview is current."
                : result.Diagnostics[0].Message;
        }
        finally
        {
            IsTemplatePreviewBusy = false;
            RaiseTemplateWorkspacePropertiesChanged();
        }
    }

    private void CreateTemplateItemFromSelectedTemplate()
    {
        if (GetSelectedSharedTemplateDefinition() is not { } template)
        {
            return;
        }

        var section = EnsureSelectedSection();
        var item = new DocumentTemplateItem
        {
            Id = CreateUniqueId("templateItem", EnumerateItemIds()),
            Name = template.Id,
            TemplateId = template.Id,
            TemplateFormat = template.Format,
            Bounds = CreateNextBounds(section, 420f, 220f)
        };
        section.BodyItems.Add(item);
        RebuildDesignerState(item);
        SelectedInspectorTabIndex = 4;
        MarkDirty($"Added document-template item from '{template.Id}'.");
    }

    private void PromoteSelectedTemplateItemToShared()
    {
        if (GetSelectedTemplateItemForEditing() is not { } item
            || string.IsNullOrWhiteSpace(item.EmbeddedContent))
        {
            return;
        }

        var template = new ReportSharedTemplateDefinition
        {
            Id = CreateUniqueId(
                string.IsNullOrWhiteSpace(item.Name) ? "template" : SanitizeTemplatePlaceholderKey(item.Name),
                ReportDefinition.SharedTemplates.Select(static template => template.Id)),
            Format = item.TemplateFormat,
            IsEmbedded = true,
            Content = item.EmbeddedContent
        };
        ReportDefinition.SharedTemplates.Add(template);
        item.TemplateId = template.Id;
        item.EmbeddedContent = null;

        RebuildDesignerState(item);
        SelectedInspectorTabIndex = 4;
        MarkDirty($"Promoted template item '{item.Name}' to shared template '{template.Id}'.");
    }

    private void DetachSelectedTemplateItem()
    {
        if (GetSelectedTemplateItemForEditing() is not { } item
            || ResolveTemplateReference(item) is not { } template)
        {
            return;
        }

        if (template.IsEmbedded)
        {
            item.EmbeddedContent = template.Content;
        }
        else if (_dataRuntimeService.TryLoadTemplateSourceText(template.Format, template.Source, out var content, out _))
        {
            item.EmbeddedContent = content;
        }

        item.TemplateFormat = template.Format;
        item.TemplateId = null;
        RebuildDesignerState(item);
        SelectedInspectorTabIndex = 4;
        MarkDirty($"Detached template item '{item.Name}' to embedded content.");
    }

    private void EditReferencedSharedTemplate()
    {
        if (ResolveTemplateReference(GetSelectedTemplateItemForEditing()) is not { } template)
        {
            return;
        }

        SelectedInspectorTabIndex = 4;
        SelectTarget(template);
    }

    private void EmbedActiveTemplateForEditing()
    {
        if (GetActiveTemplateContentTarget() is not ReportSharedTemplateDefinition template)
        {
            return;
        }

        if (_dataRuntimeService.TryLoadTemplateSourceText(template.Format, template.Source, out var content, out var errorMessage))
        {
            template.Content = content;
            template.IsEmbedded = true;
            OnTemplateWorkspaceChanged("Embedded template source for editing.");
            RefreshTemplateWorkspaceEditors();
            return;
        }

        TemplateWorkspaceStatusMessage = errorMessage ?? "Unable to load template source.";
    }

    private void RenameSharedTemplateAndReferences(
        ReportSharedTemplateDefinition template,
        string oldId,
        string newId)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Id = newId;

        foreach (var item in EnumerateItems().OfType<DocumentTemplateItem>())
        {
            if (string.Equals(item.TemplateId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                item.TemplateId = newId;
            }
        }
    }

    private ReportSharedTemplateDefinition? GetSelectedSharedTemplateDefinition()
    {
        return _selectedTarget as ReportSharedTemplateDefinition;
    }

    private DocumentTemplateItem? GetSelectedTemplateItemForEditing()
    {
        return _selectedTarget as DocumentTemplateItem;
    }

    private object? GetActiveTemplateContentTarget()
    {
        if (GetSelectedSharedTemplateDefinition() is { } selectedSharedTemplate)
        {
            return selectedSharedTemplate;
        }

        if (GetSelectedTemplateItemForEditing() is not { } item)
        {
            return null;
        }

        return (object?)ResolveTemplateReference(item) ?? item;
    }

    private ReportSharedTemplateDefinition? ResolveTemplateReference(DocumentTemplateItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.TemplateId))
        {
            return null;
        }

        return ReportDefinition.SharedTemplates.FirstOrDefault(template =>
            string.Equals(template.Id, item.TemplateId, StringComparison.OrdinalIgnoreCase));
    }

    private void RaiseTemplateWorkspacePropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(HasSelectedTemplateItem));
        this.RaisePropertyChanged(nameof(HasSelectedSharedTemplate));
        this.RaisePropertyChanged(nameof(HasActiveTemplateContentTarget));
        this.RaisePropertyChanged(nameof(ShowTemplateWorkspacePlaceholder));
        this.RaisePropertyChanged(nameof(ShowTemplateContentEditor));
        this.RaisePropertyChanged(nameof(ShowTemplateSourceEditor));
        this.RaisePropertyChanged(nameof(ShowTemplateReferenceEditor));
        this.RaisePropertyChanged(nameof(ShowTemplateBindingEditor));
        this.RaisePropertyChanged(nameof(ShowTemplateStorageModeEditor));
        this.RaisePropertyChanged(nameof(CanEmbedActiveTemplateForEditing));
        this.RaisePropertyChanged(nameof(CanRefreshTemplatePreview));
        this.RaisePropertyChanged(nameof(CanInsertSelectedTemplatePlaceholder));
        this.RaisePropertyChanged(nameof(CanUseSelectedDataNodeInTemplate));
        this.RaisePropertyChanged(nameof(CanRemoveSelectedTemplateBinding));
        this.RaisePropertyChanged(nameof(CanCreateTemplateItemFromSelectedTemplate));
        this.RaisePropertyChanged(nameof(CanPromoteSelectedTemplateItemToShared));
        this.RaisePropertyChanged(nameof(CanDetachSelectedTemplateItem));
        this.RaisePropertyChanged(nameof(CanEditReferencedSharedTemplate));
        this.RaisePropertyChanged(nameof(TemplateWorkspaceTitle));
        this.RaisePropertyChanged(nameof(TemplateWorkspaceSubtitle));
    }

    private void OnTemplateWorkspaceChanged(string message)
    {
        MarkDirty(message);
        RefreshLightweightViews();
        RaiseTemplateWorkspacePropertiesChanged();
        _ = RefreshTemplatePreviewAsync();
    }
}
