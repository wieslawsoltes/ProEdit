using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Vibe.Office.Documents;

namespace Vibe.Word.Avalonia;

public partial class MailMergeRecipientsDialog : Window
{
    private static readonly FilePickerFileType CsvFileType = new("CSV Files")
    {
        Patterns = new[] { "*.csv", "*.txt" }
    };

    private readonly MailMergeData _data;
    private readonly ObservableCollection<RecordItem> _records = new();
    private readonly ObservableCollection<string> _fieldNames = new();
    private readonly ListBox _recordsList;
    private readonly StackPanel _fieldsPanel;
    private readonly TextBox _newFieldNameBox;
    private readonly ComboBox _removeFieldCombo;

    public MailMergeRecipientsDialog()
        : this(null)
    {
    }

    public MailMergeRecipientsDialog(MailMergeData? data)
    {
        InitializeComponent();
        _data = data?.Clone() ?? new MailMergeData();
        _recordsList = this.FindControl<ListBox>("RecordsList")!;
        _fieldsPanel = this.FindControl<StackPanel>("FieldsPanel")!;
        _newFieldNameBox = this.FindControl<TextBox>("NewFieldNameBox")!;
        _removeFieldCombo = this.FindControl<ComboBox>("RemoveFieldCombo")!;

        _recordsList.ItemsSource = _records;
        _recordsList.SelectionChanged += (_, _) => BuildFieldEditors();
        _removeFieldCombo.ItemsSource = _fieldNames;

        RebuildFromData(0);
    }

    private void RebuildFromData(int? selectionIndex)
    {
        _fieldNames.Clear();
        _records.Clear();

        foreach (var field in _data.FieldNames)
        {
            _fieldNames.Add(field);
        }

        if (_data.Records.Count == 0)
        {
            _data.Records.Add(BuildEmptyRecord(_data.FieldNames));
        }

        foreach (var record in _data.Records)
        {
            _records.Add(new RecordItem(record));
        }

        UpdateRecordIndices();
        if (_records.Count > 0)
        {
            var index = Math.Clamp(selectionIndex ?? 0, 0, _records.Count - 1);
            _recordsList.SelectedIndex = index;
        }
        else
        {
            _recordsList.SelectedIndex = -1;
        }

        BuildFieldEditors();
    }

    private void UpdateRecordIndices()
    {
        for (var i = 0; i < _records.Count; i++)
        {
            _records[i].Index = i + 1;
        }
    }

    private void BuildFieldEditors()
    {
        _fieldsPanel.Children.Clear();
        if (_recordsList.SelectedItem is not RecordItem recordItem)
        {
            _fieldsPanel.Children.Add(CreateHintText("Select a record to edit fields."));
            return;
        }

        if (_fieldNames.Count == 0)
        {
            _fieldsPanel.Children.Add(CreateHintText("Add a field to begin."));
            return;
        }

        foreach (var field in _fieldNames)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("140,*"),
                ColumnSpacing = 8
            };

            var label = new TextBlock
            {
                Text = field,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
            row.Children.Add(label);

            var value = recordItem.Record.TryGetValue(field, out var stored) ? stored : string.Empty;
            var textBox = new TextBox
            {
                Text = value,
                Tag = field
            };
            textBox.TextChanged += OnFieldValueChanged;
            Grid.SetColumn(textBox, 1);
            row.Children.Add(textBox);

            _fieldsPanel.Children.Add(row);
        }
    }

    private static TextBlock CreateHintText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#707070"))
        };
    }

    private void OnFieldValueChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.Tag is not string field)
        {
            return;
        }

        if (_recordsList.SelectedItem is not RecordItem recordItem)
        {
            return;
        }

        recordItem.Record.Fields[field] = textBox.Text ?? string.Empty;
    }

    private async void OnLoadCsvClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { CsvFileType }
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

        try
        {
            var data = LoadCsv(path);
            ApplyData(data);
            RebuildFromData(0);
        }
        catch
        {
            // Ignore CSV errors for now.
        }
    }

    private void OnAddRecordClick(object? sender, RoutedEventArgs e)
    {
        var record = BuildEmptyRecord(_data.FieldNames);
        _data.Records.Add(record);
        RebuildFromData(_data.Records.Count - 1);
    }

    private void OnRemoveRecordClick(object? sender, RoutedEventArgs e)
    {
        if (_recordsList.SelectedItem is not RecordItem recordItem)
        {
            return;
        }

        var index = _records.IndexOf(recordItem);
        if (index < 0 || index >= _data.Records.Count)
        {
            return;
        }

        _data.Records.RemoveAt(index);
        var nextIndex = Math.Clamp(index, 0, Math.Max(0, _data.Records.Count - 1));
        RebuildFromData(nextIndex);
    }

    private void OnAddFieldClick(object? sender, RoutedEventArgs e)
    {
        var name = _newFieldNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = EnsureUniqueFieldName(name, _data.FieldNames);
        _data.FieldNames.Add(name);
        foreach (var record in _data.Records)
        {
            record.Fields[name] = string.Empty;
        }

        _newFieldNameBox.Text = string.Empty;
        RebuildFromData(_recordsList.SelectedIndex);
    }

    private void OnRemoveFieldClick(object? sender, RoutedEventArgs e)
    {
        if (_removeFieldCombo.SelectedItem is not string fieldName)
        {
            return;
        }

        var index = _data.FieldNames.FindIndex(name => name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        _data.FieldNames.RemoveAt(index);
        foreach (var record in _data.Records)
        {
            record.Fields.Remove(fieldName);
        }

        RebuildFromData(_recordsList.SelectedIndex);
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(_data.Clone());
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyData(MailMergeData data)
    {
        _data.FieldNames.Clear();
        _data.Records.Clear();

        foreach (var field in data.FieldNames)
        {
            _data.FieldNames.Add(field);
        }

        foreach (var record in data.Records)
        {
            _data.Records.Add(record);
        }
    }

    private static MailMergeRecord BuildEmptyRecord(IReadOnlyList<string> fieldNames)
    {
        var record = new MailMergeRecord();
        foreach (var field in fieldNames)
        {
            record.Fields[field] = string.Empty;
        }

        return record;
    }

    private static MailMergeData LoadCsv(string path)
    {
        var data = new MailMergeData();
        using var reader = new StreamReader(path, true);
        string? line;
        var headerRead = false;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line.AsSpan());
            if (!headerRead)
            {
                AddFieldNames(data, values);
                headerRead = true;
                continue;
            }

            var record = new MailMergeRecord();
            for (var i = 0; i < data.FieldNames.Count; i++)
            {
                var value = i < values.Count ? values[i] : string.Empty;
                record.Fields[data.FieldNames[i]] = value;
            }

            data.Records.Add(record);
        }

        if (headerRead && data.Records.Count == 0)
        {
            data.Records.Add(BuildEmptyRecord(data.FieldNames));
        }

        return data;
    }

    private static void AddFieldNames(MailMergeData data, List<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++)
        {
            var name = values[i].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Field{i + 1}";
            }

            name = EnsureUniqueFieldName(name, seen);
            data.FieldNames.Add(name);
            seen.Add(name);
        }
    }

    private static string EnsureUniqueFieldName(string name, IEnumerable<string> existing)
    {
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return EnsureUniqueFieldName(name, seen);
    }

    private static string EnsureUniqueFieldName(string name, HashSet<string> seen)
    {
        var baseName = name;
        var suffix = 1;
        while (seen.Contains(name))
        {
            suffix++;
            name = string.Format("{0}{1}", baseName, suffix);
        }

        return name;
    }

    private static List<string> ParseCsvLine(ReadOnlySpan<char> line)
    {
        var values = new List<string>();
        if (line.IsEmpty)
        {
            return values;
        }

        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case ',':
                    values.Add(builder.ToString());
                    builder.Clear();
                    break;
                case '"':
                    inQuotes = true;
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        values.Add(builder.ToString());
        return values;
    }

    private sealed class RecordItem : INotifyPropertyChanged
    {
        private int _index;

        public MailMergeRecord Record { get; }

        public int Index
        {
            get => _index;
            set
            {
                if (_index == value)
                {
                    return;
                }

                _index = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }

        public string DisplayName => $"Record {Index}";

        public event PropertyChangedEventHandler? PropertyChanged;

        public RecordItem(MailMergeRecord record)
        {
            Record = record;
        }
    }
}
