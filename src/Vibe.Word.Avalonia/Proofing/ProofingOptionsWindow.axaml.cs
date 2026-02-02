using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vibe.Office.Editing;

namespace Vibe.Word.Avalonia;

public partial class ProofingOptionsWindow : Window
{
    private readonly ListBox _languageList;
    private readonly TextBox _languageInput;
    private readonly Button _addLanguageButton;
    private readonly Button _removeLanguageButton;
    private readonly ListBox _profilesList;
    private readonly Button _addProfileButton;
    private readonly Button _removeProfileButton;
    private readonly TextBox _profileNameBox;
    private readonly ComboBox _spellEngineCombo;
    private readonly ComboBox _grammarEngineCombo;
    private readonly ComboBox _styleEngineCombo;
    private readonly ListBox _engineList;
    private readonly ListBox _engineSettingsList;
    private readonly Button _addSettingButton;
    private readonly Button _removeSettingButton;
    private readonly ListBox _pluginList;
    private readonly Button _addPluginButton;
    private readonly Button _removePluginButton;
    private readonly Button _okButton;
    private readonly Button _cancelButton;
    private readonly ProofingOptions _options;
    private readonly ObservableCollection<ProfileItem> _profiles = new();
    private readonly ObservableCollection<LanguageProfileItem> _languages = new();
    private readonly ObservableCollection<EngineItem> _engines = new();
    private readonly ObservableCollection<EngineSettingItem> _engineSettings = new();
    private readonly ObservableCollection<string> _plugins = new();
    private readonly Dictionary<string, List<EngineSettingItem>> _engineSettingsById = new(StringComparer.OrdinalIgnoreCase);
    private ProfileItem? _selectedProfile;
    private EngineItem? _selectedEngine;

    public ProofingOptionsWindow(
        ProofingOptions options,
        IReadOnlyList<ProofingProfileDefinition> profiles,
        IReadOnlyList<ProofingEngineDescriptor> engines)
    {
        _options = options?.Clone() ?? ProofingOptions.CreateDefault();
        InitializeComponent();

        _languageList = this.FindControl<ListBox>("LanguageList")!;
        _languageInput = this.FindControl<TextBox>("LanguageInput")!;
        _addLanguageButton = this.FindControl<Button>("AddLanguageButton")!;
        _removeLanguageButton = this.FindControl<Button>("RemoveLanguageButton")!;
        _profilesList = this.FindControl<ListBox>("ProfilesList")!;
        _addProfileButton = this.FindControl<Button>("AddProfileButton")!;
        _removeProfileButton = this.FindControl<Button>("RemoveProfileButton")!;
        _profileNameBox = this.FindControl<TextBox>("ProfileNameBox")!;
        _spellEngineCombo = this.FindControl<ComboBox>("SpellEngineCombo")!;
        _grammarEngineCombo = this.FindControl<ComboBox>("GrammarEngineCombo")!;
        _styleEngineCombo = this.FindControl<ComboBox>("StyleEngineCombo")!;
        _engineList = this.FindControl<ListBox>("EngineList")!;
        _engineSettingsList = this.FindControl<ListBox>("EngineSettingsList")!;
        _addSettingButton = this.FindControl<Button>("AddSettingButton")!;
        _removeSettingButton = this.FindControl<Button>("RemoveSettingButton")!;
        _pluginList = this.FindControl<ListBox>("PluginList")!;
        _addPluginButton = this.FindControl<Button>("AddPluginButton")!;
        _removePluginButton = this.FindControl<Button>("RemovePluginButton")!;
        _okButton = this.FindControl<Button>("OkButton")!;
        _cancelButton = this.FindControl<Button>("CancelButton")!;

        _engines.Clear();
        foreach (var engine in engines)
        {
            _engines.Add(new EngineItem(engine.Id, engine.DisplayName, engine.Kind));
        }

        var spellEngines = _engines.Where(item => item.Kind.HasFlag(ProofingEngineKind.Spell)).ToList();
        if (spellEngines.Count == 0)
        {
            spellEngines.Add(new EngineItem("hunspell", "Hunspell (Offline)", ProofingEngineKind.Spell));
        }

        var grammarEngines = BuildOptionalEngineList(ProofingEngineKind.Grammar);
        var styleEngines = BuildOptionalEngineList(ProofingEngineKind.Style);

        foreach (var profile in profiles)
        {
            _profiles.Add(ProfileItem.FromDefinition(profile, spellEngines, grammarEngines, styleEngines));
        }

        if (_profiles.Count == 0)
        {
            var defaults = ProofingOptions.CreateDefault().Profiles;
            foreach (var profile in defaults)
            {
                _profiles.Add(ProfileItem.FromDefinition(profile, spellEngines, grammarEngines, styleEngines));
            }
        }

        foreach (var (language, profileId) in _options.LanguageProfiles)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            var profileItem = _profiles.FirstOrDefault(item => item.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                              ?? _profiles.FirstOrDefault();
            if (profileItem is null)
            {
                continue;
            }

            _languages.Add(new LanguageProfileItem(language.Trim(), _profiles, profileItem));
        }

        if (_languages.Count == 0 && _profiles.Count > 0)
        {
            _languages.Add(new LanguageProfileItem("en-US", _profiles, _profiles[0]));
        }

        foreach (var plugin in _options.PluginAssemblies)
        {
            if (!string.IsNullOrWhiteSpace(plugin))
            {
                _plugins.Add(plugin);
            }
        }

        foreach (var (engineId, settings) in _options.EngineSettings)
        {
            var list = new List<EngineSettingItem>();
            foreach (var (key, value) in settings)
            {
                list.Add(new EngineSettingItem(key, value));
            }

            _engineSettingsById[engineId] = list;
        }

        _languageList.ItemsSource = _languages;
        _profilesList.ItemsSource = _profiles;
        _engineList.ItemsSource = _engines;
        _pluginList.ItemsSource = _plugins;
        _engineSettingsList.ItemsSource = _engineSettings;

        _spellEngineCombo.ItemsSource = spellEngines;
        _grammarEngineCombo.ItemsSource = grammarEngines;
        _styleEngineCombo.ItemsSource = styleEngines;

        _languageList.SelectionChanged += OnLanguageSelectionChanged;
        _profilesList.SelectionChanged += OnProfileSelectionChanged;
        _engineList.SelectionChanged += OnEngineSelectionChanged;

        _addLanguageButton.Click += OnAddLanguageClicked;
        _removeLanguageButton.Click += OnRemoveLanguageClicked;
        _addProfileButton.Click += OnAddProfileClicked;
        _removeProfileButton.Click += OnRemoveProfileClicked;
        _addPluginButton.Click += OnAddPluginClicked;
        _removePluginButton.Click += OnRemovePluginClicked;
        _addSettingButton.Click += OnAddSettingClicked;
        _removeSettingButton.Click += OnRemoveSettingClicked;
        _okButton.Click += OnOkClicked;
        _cancelButton.Click += (_, _) => Close(null);

        _profileNameBox.TextChanged += (_, _) =>
        {
            if (_selectedProfile is not null)
            {
                _selectedProfile.Name = _profileNameBox.Text ?? string.Empty;
            }
        };

        _spellEngineCombo.SelectionChanged += (_, _) =>
        {
            if (_selectedProfile is not null && _spellEngineCombo.SelectedItem is EngineItem engine)
            {
                _selectedProfile.SpellEngine = engine;
            }
        };

        _grammarEngineCombo.SelectionChanged += (_, _) =>
        {
            if (_selectedProfile is not null && _grammarEngineCombo.SelectedItem is EngineItem engine)
            {
                _selectedProfile.GrammarEngine = engine;
            }
        };

        _styleEngineCombo.SelectionChanged += (_, _) =>
        {
            if (_selectedProfile is not null && _styleEngineCombo.SelectedItem is EngineItem engine)
            {
                _selectedProfile.StyleEngine = engine;
            }
        };

        if (_profiles.Count > 0)
        {
            _profilesList.SelectedIndex = 0;
        }

        if (_engines.Count > 0)
        {
            _engineList.SelectedIndex = 0;
        }
    }

    private ObservableCollection<EngineItem> BuildOptionalEngineList(ProofingEngineKind kind)
    {
        var list = new ObservableCollection<EngineItem>
        {
            new EngineItem(string.Empty, "(None)", ProofingEngineKind.None)
        };

        foreach (var engine in _engines.Where(item => item.Kind.HasFlag(kind)))
        {
            list.Add(engine);
        }

        return list;
    }

    private void OnLanguageSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        _removeLanguageButton.IsEnabled = _languageList.SelectedItem is not null;
    }

    private void OnProfileSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        _selectedProfile = _profilesList.SelectedItem as ProfileItem;
        _removeProfileButton.IsEnabled = _selectedProfile is not null;

        if (_selectedProfile is null)
        {
            _profileNameBox.Text = string.Empty;
            _spellEngineCombo.SelectedItem = null;
            _grammarEngineCombo.SelectedItem = null;
            _styleEngineCombo.SelectedItem = null;
            return;
        }

        _profileNameBox.Text = _selectedProfile.Name;
        _spellEngineCombo.SelectedItem = _selectedProfile.SpellEngine;
        _grammarEngineCombo.SelectedItem = _selectedProfile.GrammarEngine;
        _styleEngineCombo.SelectedItem = _selectedProfile.StyleEngine;
    }

    private void OnEngineSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        SaveEngineSettings();
        _selectedEngine = _engineList.SelectedItem as EngineItem;
        LoadEngineSettings();
    }

    private void OnAddLanguageClicked(object? sender, RoutedEventArgs e)
    {
        var language = _languageInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(language) || _profiles.Count == 0)
        {
            return;
        }

        if (_languages.Any(item => item.Language.Equals(language, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _languages.Add(new LanguageProfileItem(language, _profiles, _profiles[0]));
        _languageInput.Text = string.Empty;
    }

    private void OnRemoveLanguageClicked(object? sender, RoutedEventArgs e)
    {
        if (_languageList.SelectedItem is LanguageProfileItem item)
        {
            _languages.Remove(item);
        }
    }

    private void OnAddProfileClicked(object? sender, RoutedEventArgs e)
    {
        var id = Guid.NewGuid().ToString("N");
        var spellEngine = _profiles.FirstOrDefault()?.SpellEngine
                         ?? _profiles.Select(item => item.SpellEngine).FirstOrDefault()
                         ?? _engines.FirstOrDefault(item => item.Kind.HasFlag(ProofingEngineKind.Spell))
                         ?? new EngineItem("hunspell", "Hunspell (Offline)", ProofingEngineKind.Spell);
        var defaultLanguage = _languages.FirstOrDefault()?.Language ?? "en-US";
        var profile = new ProfileItem(
            id,
            "Custom Profile",
            spellEngine,
            new EngineItem(string.Empty, "(None)", ProofingEngineKind.None),
            new EngineItem(string.Empty, "(None)", ProofingEngineKind.None),
            defaultLanguage);
        _profiles.Add(profile);
        _profilesList.SelectedItem = profile;
    }

    private void OnRemoveProfileClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
        {
            return;
        }

        var profileId = _selectedProfile.Id;
        _profiles.Remove(_selectedProfile);
        _selectedProfile = null;
        if (_profiles.Count > 0)
        {
            _profilesList.SelectedIndex = 0;
        }

        foreach (var language in _languages)
        {
            if (language.SelectedProfile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            {
                language.SelectedProfile = _profiles.FirstOrDefault() ?? language.SelectedProfile;
            }
        }
    }

    private async void OnAddPluginClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var results = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new(".NET Assembly") { Patterns = new List<string> { "*.dll" } }
            }
        });

        var path = results.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_plugins.Contains(path))
        {
            _plugins.Add(path);
        }
    }

    private void OnRemovePluginClicked(object? sender, RoutedEventArgs e)
    {
        if (_pluginList.SelectedItem is string item)
        {
            _plugins.Remove(item);
        }
    }

    private void OnAddSettingClicked(object? sender, RoutedEventArgs e)
    {
        _engineSettings.Add(new EngineSettingItem(string.Empty, string.Empty));
    }

    private void OnRemoveSettingClicked(object? sender, RoutedEventArgs e)
    {
        if (_engineSettingsList.SelectedItem is EngineSettingItem item)
        {
            _engineSettings.Remove(item);
        }
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        SaveEngineSettings();

        var next = _options.Clone();
        next.Profiles = _profiles.Select(profile => profile.ToDefinition()).ToList();
        next.LanguageProfiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in _languages)
        {
            if (!string.IsNullOrWhiteSpace(language.Language))
            {
                next.LanguageProfiles[language.Language] = language.SelectedProfile.Id;
            }
        }

        next.PluginAssemblies = _plugins.ToList();
        next.EngineSettings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (engineId, settings) in _engineSettingsById)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in settings)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                {
                    map[item.Key.Trim()] = item.Value ?? string.Empty;
                }
            }

            if (map.Count > 0)
            {
                next.EngineSettings[engineId] = map;
            }
        }

        Close(next);
    }

    private void SaveEngineSettings()
    {
        if (_selectedEngine is null)
        {
            return;
        }

        var list = new List<EngineSettingItem>();
        foreach (var item in _engineSettings)
        {
            list.Add(item.Clone());
        }

        _engineSettingsById[_selectedEngine.Id] = list;
    }

    private void LoadEngineSettings()
    {
        _engineSettings.Clear();
        if (_selectedEngine is null)
        {
            return;
        }

        if (!_engineSettingsById.TryGetValue(_selectedEngine.Id, out var list))
        {
            return;
        }

        foreach (var item in list)
        {
            _engineSettings.Add(item.Clone());
        }
    }

    private sealed class LanguageProfileItem : NotifyBase
    {
        private string _language;
        private ProfileItem _selectedProfile;

        public string Language
        {
            get => _language;
            set => SetField(ref _language, value);
        }

        public ObservableCollection<ProfileItem> Profiles { get; }

        public ProfileItem SelectedProfile
        {
            get => _selectedProfile;
            set => SetField(ref _selectedProfile, value);
        }

        public LanguageProfileItem(string language, ObservableCollection<ProfileItem> profiles, ProfileItem selected)
        {
            _language = language;
            Profiles = profiles;
            _selectedProfile = selected;
        }
    }

    private sealed class ProfileItem : NotifyBase
    {
        private string _name;
        private EngineItem _spellEngine;
        private EngineItem _grammarEngine;
        private EngineItem _styleEngine;
        private string? _defaultLanguage;

        public string Id { get; }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public EngineItem SpellEngine
        {
            get => _spellEngine;
            set => SetField(ref _spellEngine, value);
        }

        public EngineItem GrammarEngine
        {
            get => _grammarEngine;
            set => SetField(ref _grammarEngine, value);
        }

        public EngineItem StyleEngine
        {
            get => _styleEngine;
            set => SetField(ref _styleEngine, value);
        }

        public string? DefaultLanguage
        {
            get => _defaultLanguage;
            set => SetField(ref _defaultLanguage, value);
        }

        public ProfileItem(
            string id,
            string name,
            EngineItem spellEngine,
            EngineItem grammarEngine,
            EngineItem styleEngine,
            string? defaultLanguage)
        {
            Id = id;
            _name = name;
            _spellEngine = spellEngine;
            _grammarEngine = grammarEngine;
            _styleEngine = styleEngine;
            _defaultLanguage = defaultLanguage;
        }

        public static ProfileItem FromDefinition(
            ProofingProfileDefinition definition,
            IReadOnlyList<EngineItem> spellEngines,
            IReadOnlyList<EngineItem> grammarEngines,
            IReadOnlyList<EngineItem> styleEngines)
        {
            var spell = ResolveEngine(spellEngines, definition.SpellEngineId) ?? spellEngines.FirstOrDefault()!;
            var grammar = ResolveEngine(grammarEngines, definition.GrammarEngineId) ?? grammarEngines.FirstOrDefault()!;
            var style = ResolveEngine(styleEngines, definition.StyleEngineId) ?? styleEngines.FirstOrDefault()!;
            return new ProfileItem(definition.Id, definition.Name, spell, grammar, style, definition.DefaultLanguage);
        }

        public ProofingProfileDefinition ToDefinition()
        {
            return new ProofingProfileDefinition
            {
                Id = Id,
                Name = Name,
                SpellEngineId = SpellEngine.Id,
                GrammarEngineId = string.IsNullOrWhiteSpace(GrammarEngine.Id) ? null : GrammarEngine.Id,
                StyleEngineId = string.IsNullOrWhiteSpace(StyleEngine.Id) ? null : StyleEngine.Id,
                DefaultLanguage = DefaultLanguage
            };
        }

        private static EngineItem? ResolveEngine(IReadOnlyList<EngineItem> engines, string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return engines.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class EngineItem
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ProofingEngineKind Kind { get; }
        public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;

        public EngineItem(string id, string displayName, ProofingEngineKind kind)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
        }

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    private sealed class EngineSettingItem : NotifyBase
    {
        private string _key;
        private string _value;

        public string Key
        {
            get => _key;
            set => SetField(ref _key, value);
        }

        public string Value
        {
            get => _value;
            set => SetField(ref _value, value);
        }

        public EngineSettingItem(string key, string value)
        {
            _key = key;
            _value = value;
        }

        public EngineSettingItem Clone()
        {
            return new EngineSettingItem(Key, Value);
        }
    }

    private abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
