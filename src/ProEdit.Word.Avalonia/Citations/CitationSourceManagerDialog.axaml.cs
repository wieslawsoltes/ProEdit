using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ProEdit.Documents;

namespace ProEdit.Word.Avalonia;

public partial class CitationSourceManagerDialog : Window
{
    private static readonly string[] SourceTypes =
    {
        "Book",
        "BookSection",
        "JournalArticle",
        "ArticleInAPeriodical",
        "ConferenceProceedings",
        "Report",
        "Thesis",
        "WebSite",
        "DocumentFromInternet",
        "Patent",
        "Case",
        "Interview",
        "SoundRecording",
        "Film",
        "Art",
        "Misc"
    };

    private static readonly string[] StandardFields =
    {
        "Author",
        "Title",
        "Year",
        "Publisher",
        "City",
        "JournalName",
        "Volume",
        "Number",
        "Pages",
        "Url",
        "Doi"
    };

    private readonly CitationSourceCatalog _catalog;
    private readonly ObservableCollection<SourceItem> _sources = new();
    private readonly ListBox _sourcesList;
    private readonly TextBox _tagBox;
    private readonly ComboBox _sourceTypeCombo;
    private readonly StackPanel _standardFieldsPanel;
    private readonly StackPanel _extraFieldsPanel;
    private readonly TextBox _newFieldNameBox;
    private bool _isUpdating;

    public CitationSourceManagerDialog()
        : this(null)
    {
    }

    public CitationSourceManagerDialog(CitationSourceCatalog? catalog)
    {
        InitializeComponent();
        _catalog = catalog?.Clone() ?? new CitationSourceCatalog();
        _sourcesList = this.FindControl<ListBox>("SourcesList")!;
        _tagBox = this.FindControl<TextBox>("TagBox")!;
        _sourceTypeCombo = this.FindControl<ComboBox>("SourceTypeCombo")!;
        _standardFieldsPanel = this.FindControl<StackPanel>("StandardFieldsPanel")!;
        _extraFieldsPanel = this.FindControl<StackPanel>("ExtraFieldsPanel")!;
        _newFieldNameBox = this.FindControl<TextBox>("NewFieldNameBox")!;

        _sourcesList.ItemsSource = _sources;
        _sourcesList.SelectionChanged += (_, _) => BuildFieldEditors();
        _sourceTypeCombo.ItemsSource = SourceTypes;

        RebuildFromCatalog(0);
    }

    private void RebuildFromCatalog(int? selectionIndex)
    {
        _sources.Clear();
        foreach (var source in _catalog.Sources)
        {
            _sources.Add(new SourceItem(source));
        }

        if (_sources.Count > 0)
        {
            var index = Math.Clamp(selectionIndex ?? 0, 0, _sources.Count - 1);
            _sourcesList.SelectedIndex = index;
        }
        else
        {
            _sourcesList.SelectedIndex = -1;
        }

        BuildFieldEditors();
    }

    private void BuildFieldEditors()
    {
        _isUpdating = true;
        _standardFieldsPanel.Children.Clear();
        _extraFieldsPanel.Children.Clear();

        if (_sourcesList.SelectedItem is not SourceItem item)
        {
            _tagBox.Text = string.Empty;
            _sourceTypeCombo.SelectedIndex = -1;
            _isUpdating = false;
            return;
        }

        var source = item.Source;
        _tagBox.Text = source.Tag ?? string.Empty;

        var type = ResolveSourceType(source.SourceType);
        if (!SourceTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            _sourceTypeCombo.ItemsSource = SourceTypes.Concat(new[] { type }).ToArray();
        }

        _sourceTypeCombo.SelectedItem = type;

        foreach (var field in StandardFields)
        {
            var value = source.GetField(field) ?? string.Empty;
            _standardFieldsPanel.Children.Add(CreateFieldRow(field, value, allowRemove: false));
        }

        foreach (var pair in source.Fields)
        {
            if (IsStandardField(pair.Key))
            {
                continue;
            }

            _extraFieldsPanel.Children.Add(CreateFieldRow(pair.Key, pair.Value, allowRemove: true));
        }

        _isUpdating = false;
    }

    private static bool IsStandardField(string field)
    {
        foreach (var standard in StandardFields)
        {
            if (string.Equals(standard, field, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveSourceType(string? sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
        {
            return SourceTypes[0];
        }

        foreach (var type in SourceTypes)
        {
            if (string.Equals(type, sourceType, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return sourceType;
    }

    private Control CreateFieldRow(string field, string value, bool allowRemove)
    {
        var columnSpec = allowRemove ? "120,*,Auto" : "120,*";
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(columnSpec),
            ColumnSpacing = 8
        };

        var label = new TextBlock
        {
            Text = field,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };
        grid.Children.Add(label);

        var textBox = new TextBox
        {
            Text = value ?? string.Empty,
            Tag = field
        };
        textBox.TextChanged += OnFieldValueChanged;
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);

        if (allowRemove)
        {
            var button = new Button
            {
                Content = "Remove",
                Tag = field
            };
            button.Click += OnRemoveFieldClick;
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }

        return grid;
    }

    private void OnTagChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        if (_sourcesList.SelectedItem is not SourceItem item)
        {
            return;
        }

        item.Source.Tag = _tagBox.Text?.Trim() ?? string.Empty;
        item.Refresh();
    }

    private void OnSourceTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        if (_sourcesList.SelectedItem is not SourceItem item)
        {
            return;
        }

        if (_sourceTypeCombo.SelectedItem is string type)
        {
            item.Source.SourceType = type;
            item.Refresh();
        }
    }

    private void OnFieldValueChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating || sender is not TextBox textBox || textBox.Tag is not string field)
        {
            return;
        }

        if (_sourcesList.SelectedItem is not SourceItem item)
        {
            return;
        }

        item.Source.SetField(field, textBox.Text);
        item.Refresh();
    }

    private void OnRemoveFieldClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string field)
        {
            return;
        }

        if (_sourcesList.SelectedItem is not SourceItem item)
        {
            return;
        }

        item.Source.SetField(field, null);
        item.Refresh();
        BuildFieldEditors();
    }

    private void OnAddSourceClick(object? sender, RoutedEventArgs e)
    {
        var source = new CitationSource
        {
            Tag = _catalog.GenerateUniqueTag("Source"),
            SourceType = SourceTypes[0]
        };

        _catalog.Sources.Add(source);
        _sources.Add(new SourceItem(source));
        _sourcesList.SelectedIndex = _sources.Count - 1;
    }

    private void OnRemoveSourceClick(object? sender, RoutedEventArgs e)
    {
        if (_sourcesList.SelectedItem is not SourceItem item)
        {
            return;
        }

        var index = _sources.IndexOf(item);
        if (index < 0 || index >= _catalog.Sources.Count)
        {
            return;
        }

        _catalog.Sources.RemoveAt(index);
        _sources.RemoveAt(index);
        var nextIndex = Math.Clamp(index, 0, _sources.Count - 1);
        _sourcesList.SelectedIndex = _sources.Count == 0 ? -1 : nextIndex;
    }

    private void OnAddFieldClick(object? sender, RoutedEventArgs e)
    {
        if (_sourcesList.SelectedItem is not SourceItem item)
        {
            return;
        }

        var fieldName = _newFieldNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        if (IsStandardField(fieldName)
            || fieldName.Equals("Tag", StringComparison.OrdinalIgnoreCase)
            || fieldName.Equals("SourceType", StringComparison.OrdinalIgnoreCase)
            || fieldName.Equals("Guid", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        item.Source.SetField(fieldName, string.Empty);
        _newFieldNameBox.Text = string.Empty;
        BuildFieldEditors();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _catalog.EnsureUniqueTags();
        Close(_catalog);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private sealed class SourceItem : INotifyPropertyChanged
    {
        private string _displayName;
        public CitationSource Source { get; }

        public SourceItem(CitationSource source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _displayName = BuildDisplayName(source);
        }

        public string DisplayName
        {
            get => _displayName;
            private set
            {
                if (_displayName == value)
                {
                    return;
                }

                _displayName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Refresh()
        {
            DisplayName = BuildDisplayName(Source);
        }

        private static string BuildDisplayName(CitationSource source)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(source.Tag))
            {
                parts.Add(source.Tag);
            }

            var author = source.GetField("Author");
            if (!string.IsNullOrWhiteSpace(author))
            {
                parts.Add(author);
            }

            var title = source.GetField("Title");
            if (!string.IsNullOrWhiteSpace(title))
            {
                parts.Add(title);
            }

            return parts.Count == 0 ? "Source" : string.Join(" - ", parts);
        }
    }
}
