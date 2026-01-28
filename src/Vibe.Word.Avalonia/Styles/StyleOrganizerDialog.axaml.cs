using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.OpenXml;
using Vibe.Office.Primitives;

namespace Vibe.Word.Avalonia;

public partial class StyleOrganizerDialog : Window
{
    private static readonly FilePickerFileType DocxFileType = new("Word Documents")
    {
        Patterns = new[] { "*.docx" }
    };

    private readonly IStyleManagerService _styleService;
    private ListBox? _currentStylesList;
    private ListBox? _sourceStylesList;
    private TextBlock? _sourcePathText;
    private DocumentStyles? _sourceStyles;
    private string? _sourcePath;

    public StyleOrganizerDialog(IStyleManagerService styleService)
    {
        _styleService = styleService ?? throw new ArgumentNullException(nameof(styleService));
        InitializeComponent();
        InitializeControls();
        RefreshCurrentStyles();
    }

    private void InitializeControls()
    {
        _currentStylesList = this.FindControl<ListBox>("CurrentStylesList");
        _sourceStylesList = this.FindControl<ListBox>("SourceStylesList");
        _sourcePathText = this.FindControl<TextBlock>("SourcePathText");

        if (this.FindControl<Button>("LoadSourceButton") is { } loadButton)
        {
            loadButton.Click += OnLoadSourceClick;
        }

        if (this.FindControl<Button>("CopyFromSourceButton") is { } copyFromButton)
        {
            copyFromButton.Click += OnCopyFromSourceClick;
        }

        if (this.FindControl<Button>("CopyToSourceButton") is { } copyToButton)
        {
            copyToButton.Click += OnCopyToSourceClick;
        }

        if (this.FindControl<Button>("ExportButton") is { } exportButton)
        {
            exportButton.Click += OnExportClick;
        }

        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += (_, _) => Close();
        }
    }

    private void RefreshCurrentStyles()
    {
        if (_currentStylesList is null)
        {
            return;
        }

        var styles = _styleService.GetStyles();
        var list = styles.OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _currentStylesList.ItemsSource = list;
    }

    private void RefreshSourceStyles()
    {
        if (_sourceStylesList is null)
        {
            return;
        }

        if (_sourceStyles is null)
        {
            _sourceStylesList.ItemsSource = null;
            return;
        }

        var list = BuildStyleInfoList(_sourceStyles);
        _sourceStylesList.ItemsSource = list;
    }

    private async void OnLoadSourceClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { DocxFileType }
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

        var importer = new DocxImporter();
        var document = importer.Load(path);
        _sourceStyles = document.Styles;
        _sourcePath = path;
        if (_sourcePathText is not null)
        {
            _sourcePathText.Text = Path.GetFileName(path);
        }

        RefreshSourceStyles();
    }

    private void OnCopyFromSourceClick(object? sender, RoutedEventArgs e)
    {
        if (_sourceStyles is null || _sourceStylesList is null)
        {
            return;
        }

        var selected = _sourceStylesList.SelectedItems?.OfType<EditorStyleInfo>().ToList();
        if (selected is null || selected.Count == 0)
        {
            return;
        }

        var importKeys = ExpandDependencies(selected, _sourceStyles);
        foreach (var key in importKeys)
        {
            ImportStyleDefinition(key, _sourceStyles);
        }

        RefreshCurrentStyles();
    }

    private void OnCopyToSourceClick(object? sender, RoutedEventArgs e)
    {
        if (_sourceStyles is null || _currentStylesList is null)
        {
            return;
        }

        var selected = _currentStylesList.SelectedItems?.OfType<EditorStyleInfo>().ToList();
        if (selected is null || selected.Count == 0)
        {
            return;
        }

        var exportKeys = ExpandDependencies(selected, _styleService);
        foreach (var key in exportKeys)
        {
            CopyStyleIntoSource(key, _sourceStyles);
        }

        RefreshSourceStyles();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || _currentStylesList is null)
        {
            return;
        }

        var selected = _currentStylesList.SelectedItems?.OfType<EditorStyleInfo>().ToList();
        if (selected is null || selected.Count == 0)
        {
            return;
        }

        var exportKeys = ExpandDependencies(selected, _styleService);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "docx",
            FileTypeChoices = new[] { DocxFileType },
            SuggestedFileName = "Styles.docx"
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(document.SectionProperties, document.Header, document.Footer, document.FirstHeader, document.FirstFooter, document.EvenHeader, document.EvenFooter));
        document.Blocks.Add(new ParagraphBlock());

        CopyStylesToDocument(document.Styles, exportKeys);

        var exporter = new DocxExporter();
        exporter.Save(document, path);
    }

    private void ImportStyleDefinition(StyleKeyInfo key, DocumentStyles sourceStyles)
    {
        switch (key.Type)
        {
            case EditorStyleType.Paragraph:
                if (sourceStyles.ParagraphStyles.TryGetValue(key.Id, out var paragraph))
                {
                    ImportParagraphStyle(paragraph);
                }

                break;
            case EditorStyleType.Character:
                if (sourceStyles.CharacterStyles.TryGetValue(key.Id, out var character))
                {
                    ImportCharacterStyle(character);
                }

                break;
            case EditorStyleType.Table:
                if (sourceStyles.TableStyles.TryGetValue(key.Id, out var table))
                {
                    ImportTableStyle(table);
                }

                break;
            default:
                break;
        }
    }

    private void ImportParagraphStyle(ParagraphStyleDefinition definition)
    {
        var existing = _styleService.GetParagraphStyleDefinition(definition.Id);
        if (existing is null)
        {
            var options = new EditorStyleCreateOptions(
                EditorStyleType.Paragraph,
                definition.Name ?? definition.Id,
                definition.BasedOnId,
                definition.NextStyleId,
                definition.LinkedStyleId,
                definition.QuickStyle == true,
                definition.AutoRedefine == true,
                definition.RunProperties.Clone(),
                CloneParagraphStyleProperties(definition.ParagraphProperties),
                null,
                null,
                definition.Id);
            _styleService.CreateStyle(options);
        }
        else
        {
            _styleService.RenameStyle(EditorStyleType.Paragraph, definition.Id, definition.Name ?? definition.Id);
            _styleService.SetStyleBasedOn(EditorStyleType.Paragraph, definition.Id, definition.BasedOnId);
            _styleService.SetStyleNext(EditorStyleType.Paragraph, definition.Id, definition.NextStyleId);
            _styleService.UpdateParagraphStyleProperties(definition.Id, definition.RunProperties.Clone(), CloneParagraphStyleProperties(definition.ParagraphProperties));
        }

        ApplyMetadata(EditorStyleType.Paragraph, definition.Id, definition.LinkedStyleId, definition.PrimaryStyle, definition.CustomStyle);
        ApplyFlags(EditorStyleType.Paragraph, definition);
    }

    private void ImportCharacterStyle(CharacterStyleDefinition definition)
    {
        var existing = _styleService.GetCharacterStyleDefinition(definition.Id);
        if (existing is null)
        {
            var options = new EditorStyleCreateOptions(
                EditorStyleType.Character,
                definition.Name ?? definition.Id,
                definition.BasedOnId,
                definition.NextStyleId,
                definition.LinkedStyleId,
                definition.QuickStyle == true,
                definition.AutoRedefine == true,
                definition.RunProperties.Clone(),
                null,
                null,
                null,
                definition.Id);
            _styleService.CreateStyle(options);
        }
        else
        {
            _styleService.RenameStyle(EditorStyleType.Character, definition.Id, definition.Name ?? definition.Id);
            _styleService.SetStyleBasedOn(EditorStyleType.Character, definition.Id, definition.BasedOnId);
            _styleService.SetStyleNext(EditorStyleType.Character, definition.Id, definition.NextStyleId);
            _styleService.UpdateCharacterStyleProperties(definition.Id, definition.RunProperties.Clone());
        }

        ApplyMetadata(EditorStyleType.Character, definition.Id, definition.LinkedStyleId, definition.PrimaryStyle, definition.CustomStyle);
        ApplyFlags(EditorStyleType.Character, definition);
    }

    private void ImportTableStyle(TableStyleDefinition definition)
    {
        var existing = _styleService.GetTableStyleDefinition(definition.Id);
        if (existing is null)
        {
            var options = new EditorStyleCreateOptions(
                EditorStyleType.Table,
                definition.Name ?? definition.Id,
                definition.BasedOnId,
                definition.NextStyleId,
                definition.LinkedStyleId,
                definition.QuickStyle == true,
                definition.AutoRedefine == true,
                null,
                null,
                definition.TableProperties.Clone(),
                definition.CellProperties.Clone(),
                definition.Id);
            _styleService.CreateStyle(options);
        }
        else
        {
            _styleService.RenameStyle(EditorStyleType.Table, definition.Id, definition.Name ?? definition.Id);
            _styleService.SetStyleBasedOn(EditorStyleType.Table, definition.Id, definition.BasedOnId);
            _styleService.SetStyleNext(EditorStyleType.Table, definition.Id, definition.NextStyleId);
            _styleService.UpdateTableStyleProperties(definition.Id, definition.TableProperties.Clone(), definition.CellProperties.Clone());
        }

        ApplyMetadata(EditorStyleType.Table, definition.Id, definition.LinkedStyleId, definition.PrimaryStyle, definition.CustomStyle);
        _styleService.UpdateTableStyleConditions(definition.Id, definition.Conditions);
        ApplyFlags(EditorStyleType.Table, definition);
    }

    private void ApplyMetadata(EditorStyleType type, string styleId, string? linkedStyleId, bool? primaryStyle, bool? customStyle)
    {
        _styleService.SetStyleLinkedStyle(type, styleId, linkedStyleId);
        _styleService.SetStylePrimaryStyle(type, styleId, primaryStyle);
        _styleService.SetStyleCustomStyle(type, styleId, customStyle);
    }

    private void ApplyFlags(EditorStyleType type, ParagraphStyleDefinition definition)
    {
        _styleService.SetStyleQuickStyle(type, definition.Id, definition.QuickStyle);
        _styleService.SetStyleHidden(type, definition.Id, definition.Hidden);
        _styleService.SetStyleSemiHidden(type, definition.Id, definition.SemiHidden);
        _styleService.SetStyleUnhideWhenUsed(type, definition.Id, definition.UnhideWhenUsed);
        _styleService.SetStyleAutoRedefine(type, definition.Id, definition.AutoRedefine);
        _styleService.SetStylePriority(type, definition.Id, definition.UiPriority);
        _styleService.SetStyleLocked(type, definition.Id, definition.Locked);
    }

    private void ApplyFlags(EditorStyleType type, CharacterStyleDefinition definition)
    {
        _styleService.SetStyleQuickStyle(type, definition.Id, definition.QuickStyle);
        _styleService.SetStyleHidden(type, definition.Id, definition.Hidden);
        _styleService.SetStyleSemiHidden(type, definition.Id, definition.SemiHidden);
        _styleService.SetStyleUnhideWhenUsed(type, definition.Id, definition.UnhideWhenUsed);
        _styleService.SetStyleAutoRedefine(type, definition.Id, definition.AutoRedefine);
        _styleService.SetStylePriority(type, definition.Id, definition.UiPriority);
        _styleService.SetStyleLocked(type, definition.Id, definition.Locked);
    }

    private void ApplyFlags(EditorStyleType type, TableStyleDefinition definition)
    {
        _styleService.SetStyleQuickStyle(type, definition.Id, definition.QuickStyle);
        _styleService.SetStyleHidden(type, definition.Id, definition.Hidden);
        _styleService.SetStyleSemiHidden(type, definition.Id, definition.SemiHidden);
        _styleService.SetStyleUnhideWhenUsed(type, definition.Id, definition.UnhideWhenUsed);
        _styleService.SetStyleAutoRedefine(type, definition.Id, definition.AutoRedefine);
        _styleService.SetStylePriority(type, definition.Id, definition.UiPriority);
        _styleService.SetStyleLocked(type, definition.Id, definition.Locked);
    }

    private void CopyStyleIntoSource(StyleKeyInfo key, DocumentStyles targetStyles)
    {
        switch (key.Type)
        {
            case EditorStyleType.Paragraph:
            {
                var definition = _styleService.GetParagraphStyleDefinition(key.Id);
                if (definition is not null)
                {
                    targetStyles.ParagraphStyles[key.Id] = CloneParagraphStyleDefinition(definition);
                }

                break;
            }
            case EditorStyleType.Character:
            {
                var definition = _styleService.GetCharacterStyleDefinition(key.Id);
                if (definition is not null)
                {
                    targetStyles.CharacterStyles[key.Id] = CloneCharacterStyleDefinition(definition);
                }

                break;
            }
            case EditorStyleType.Table:
            {
                var definition = _styleService.GetTableStyleDefinition(key.Id);
                if (definition is not null)
                {
                    targetStyles.TableStyles[key.Id] = CloneTableStyleDefinition(definition);
                }

                break;
            }
            default:
                break;
        }
    }

    private void CopyStylesToDocument(DocumentStyles targetStyles, IReadOnlyList<StyleKeyInfo> keys)
    {
        foreach (var key in keys)
        {
            CopyStyleIntoSource(key, targetStyles);
        }

        targetStyles.DefaultParagraphStyleId = ResolveDefaultStyleId(EditorStyleType.Paragraph, keys);
        targetStyles.DefaultCharacterStyleId = ResolveDefaultStyleId(EditorStyleType.Character, keys);
        targetStyles.DefaultTableStyleId = ResolveDefaultStyleId(EditorStyleType.Table, keys);
    }

    private string? ResolveDefaultStyleId(EditorStyleType type, IReadOnlyList<StyleKeyInfo> keys)
    {
        var defaultStyle = _styleService.GetStyles(type).FirstOrDefault(style => style.IsDefault);
        if (!string.IsNullOrWhiteSpace(defaultStyle.Id)
            && keys.Any(key => key.Type == type && string.Equals(key.Id, defaultStyle.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return defaultStyle.Id;
        }

        var fallback = keys.FirstOrDefault(key => key.Type == type);
        return string.IsNullOrWhiteSpace(fallback.Id) ? null : fallback.Id;
    }

    private static List<EditorStyleInfo> BuildStyleInfoList(DocumentStyles styles)
    {
        var list = new List<EditorStyleInfo>();
        foreach (var pair in styles.ParagraphStyles)
        {
            var definition = pair.Value;
            var name = string.IsNullOrWhiteSpace(definition.Name) ? pair.Key : definition.Name!;
            list.Add(new EditorStyleInfo(
                pair.Key,
                name,
                EditorStyleType.Paragraph,
                string.Equals(pair.Key, styles.DefaultParagraphStyleId, StringComparison.OrdinalIgnoreCase),
                false,
                definition.QuickStyle == true,
                definition.Hidden == true,
                definition.SemiHidden == true,
                definition.UnhideWhenUsed == true,
                definition.Locked == true,
                definition.CustomStyle == true,
                definition.UiPriority));
        }

        foreach (var pair in styles.CharacterStyles)
        {
            var definition = pair.Value;
            var name = string.IsNullOrWhiteSpace(definition.Name) ? pair.Key : definition.Name!;
            list.Add(new EditorStyleInfo(
                pair.Key,
                name,
                EditorStyleType.Character,
                string.Equals(pair.Key, styles.DefaultCharacterStyleId, StringComparison.OrdinalIgnoreCase),
                false,
                definition.QuickStyle == true,
                definition.Hidden == true,
                definition.SemiHidden == true,
                definition.UnhideWhenUsed == true,
                definition.Locked == true,
                definition.CustomStyle == true,
                definition.UiPriority));
        }

        foreach (var pair in styles.TableStyles)
        {
            var definition = pair.Value;
            var name = string.IsNullOrWhiteSpace(definition.Name) ? pair.Key : definition.Name!;
            list.Add(new EditorStyleInfo(
                pair.Key,
                name,
                EditorStyleType.Table,
                string.Equals(pair.Key, styles.DefaultTableStyleId, StringComparison.OrdinalIgnoreCase),
                false,
                definition.QuickStyle == true,
                definition.Hidden == true,
                definition.SemiHidden == true,
                definition.UnhideWhenUsed == true,
                definition.Locked == true,
                definition.CustomStyle == true,
                definition.UiPriority));
        }

        list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static IReadOnlyList<StyleKeyInfo> ExpandDependencies(IEnumerable<EditorStyleInfo> styles, DocumentStyles sourceStyles)
    {
        var result = new List<StyleKeyInfo>();
        var visited = new HashSet<StyleKeyInfo>();
        foreach (var style in styles)
        {
            AddWithDependencies(style.Type, style.Id, sourceStyles, visited, result);
        }

        return result;
    }

    private static IReadOnlyList<StyleKeyInfo> ExpandDependencies(IEnumerable<EditorStyleInfo> styles, IStyleManagerService styleService)
    {
        var result = new List<StyleKeyInfo>();
        var visited = new HashSet<StyleKeyInfo>();
        foreach (var style in styles)
        {
            AddWithDependencies(style.Type, style.Id, styleService, visited, result);
        }

        return result;
    }

    private static void AddWithDependencies(EditorStyleType type, string styleId, DocumentStyles styles, HashSet<StyleKeyInfo> visited, List<StyleKeyInfo> result)
    {
        var key = new StyleKeyInfo(type, styleId);
        if (!visited.Add(key))
        {
            return;
        }

        var definition = GetDefinition(styles, type, styleId);
        if (definition is { } resolved)
        {
            if (!string.IsNullOrWhiteSpace(resolved.BasedOnId))
            {
                AddWithDependencies(type, resolved.BasedOnId, styles, visited, result);
            }

            if (!string.IsNullOrWhiteSpace(resolved.NextStyleId))
            {
                AddWithDependencies(type, resolved.NextStyleId, styles, visited, result);
            }

            if (!string.IsNullOrWhiteSpace(resolved.LinkedStyleId))
            {
                AddLinkedStyleDependency(type, resolved.LinkedStyleId, styles, visited, result);
            }
        }

        result.Add(key);
    }

    private static void AddWithDependencies(EditorStyleType type, string styleId, IStyleManagerService styleService, HashSet<StyleKeyInfo> visited, List<StyleKeyInfo> result)
    {
        var key = new StyleKeyInfo(type, styleId);
        if (!visited.Add(key))
        {
            return;
        }

        StyleDefinitionBase? definition = type switch
        {
            EditorStyleType.Paragraph => styleService.GetParagraphStyleDefinition(styleId) is { } paragraph
                ? new StyleDefinitionBase(paragraph.BasedOnId, paragraph.NextStyleId, paragraph.LinkedStyleId)
                : null,
            EditorStyleType.Character => styleService.GetCharacterStyleDefinition(styleId) is { } character
                ? new StyleDefinitionBase(character.BasedOnId, character.NextStyleId, character.LinkedStyleId)
                : null,
            EditorStyleType.Table => styleService.GetTableStyleDefinition(styleId) is { } table
                ? new StyleDefinitionBase(table.BasedOnId, table.NextStyleId, table.LinkedStyleId)
                : null,
            _ => null
        };

        if (definition is { } resolved)
        {
            if (!string.IsNullOrWhiteSpace(resolved.BasedOnId))
            {
                AddWithDependencies(type, resolved.BasedOnId, styleService, visited, result);
            }

            if (!string.IsNullOrWhiteSpace(resolved.NextStyleId))
            {
                AddWithDependencies(type, resolved.NextStyleId, styleService, visited, result);
            }

            if (!string.IsNullOrWhiteSpace(resolved.LinkedStyleId))
            {
                AddLinkedStyleDependency(type, resolved.LinkedStyleId, styleService, visited, result);
            }
        }

        result.Add(key);
    }

    private static void AddLinkedStyleDependency(
        EditorStyleType sourceType,
        string linkedStyleId,
        DocumentStyles styles,
        HashSet<StyleKeyInfo> visited,
        List<StyleKeyInfo> result)
    {
        if (string.IsNullOrWhiteSpace(linkedStyleId))
        {
            return;
        }

        if (sourceType == EditorStyleType.Paragraph && styles.CharacterStyles.ContainsKey(linkedStyleId))
        {
            AddWithDependencies(EditorStyleType.Character, linkedStyleId, styles, visited, result);
            return;
        }

        if (sourceType == EditorStyleType.Character && styles.ParagraphStyles.ContainsKey(linkedStyleId))
        {
            AddWithDependencies(EditorStyleType.Paragraph, linkedStyleId, styles, visited, result);
            return;
        }

        if (sourceType == EditorStyleType.Table && styles.TableStyles.ContainsKey(linkedStyleId))
        {
            AddWithDependencies(EditorStyleType.Table, linkedStyleId, styles, visited, result);
            return;
        }

        if (styles.ParagraphStyles.ContainsKey(linkedStyleId))
        {
            AddWithDependencies(EditorStyleType.Paragraph, linkedStyleId, styles, visited, result);
            return;
        }

        if (styles.CharacterStyles.ContainsKey(linkedStyleId))
        {
            AddWithDependencies(EditorStyleType.Character, linkedStyleId, styles, visited, result);
            return;
        }

        if (styles.TableStyles.ContainsKey(linkedStyleId))
        {
            AddWithDependencies(EditorStyleType.Table, linkedStyleId, styles, visited, result);
        }
    }

    private static void AddLinkedStyleDependency(
        EditorStyleType sourceType,
        string linkedStyleId,
        IStyleManagerService styleService,
        HashSet<StyleKeyInfo> visited,
        List<StyleKeyInfo> result)
    {
        if (string.IsNullOrWhiteSpace(linkedStyleId))
        {
            return;
        }

        if (sourceType == EditorStyleType.Paragraph && styleService.GetCharacterStyleDefinition(linkedStyleId) is not null)
        {
            AddWithDependencies(EditorStyleType.Character, linkedStyleId, styleService, visited, result);
            return;
        }

        if (sourceType == EditorStyleType.Character && styleService.GetParagraphStyleDefinition(linkedStyleId) is not null)
        {
            AddWithDependencies(EditorStyleType.Paragraph, linkedStyleId, styleService, visited, result);
            return;
        }

        if (sourceType == EditorStyleType.Table && styleService.GetTableStyleDefinition(linkedStyleId) is not null)
        {
            AddWithDependencies(EditorStyleType.Table, linkedStyleId, styleService, visited, result);
            return;
        }

        if (styleService.GetParagraphStyleDefinition(linkedStyleId) is not null)
        {
            AddWithDependencies(EditorStyleType.Paragraph, linkedStyleId, styleService, visited, result);
            return;
        }

        if (styleService.GetCharacterStyleDefinition(linkedStyleId) is not null)
        {
            AddWithDependencies(EditorStyleType.Character, linkedStyleId, styleService, visited, result);
            return;
        }

        if (styleService.GetTableStyleDefinition(linkedStyleId) is not null)
        {
            AddWithDependencies(EditorStyleType.Table, linkedStyleId, styleService, visited, result);
        }
    }

    private static StyleDefinitionBase? GetDefinition(DocumentStyles styles, EditorStyleType type, string styleId)
    {
        return type switch
        {
            EditorStyleType.Paragraph => styles.ParagraphStyles.TryGetValue(styleId, out var paragraph)
                ? new StyleDefinitionBase(paragraph.BasedOnId, paragraph.NextStyleId, paragraph.LinkedStyleId)
                : null,
            EditorStyleType.Character => styles.CharacterStyles.TryGetValue(styleId, out var character)
                ? new StyleDefinitionBase(character.BasedOnId, character.NextStyleId, character.LinkedStyleId)
                : null,
            EditorStyleType.Table => styles.TableStyles.TryGetValue(styleId, out var table)
                ? new StyleDefinitionBase(table.BasedOnId, table.NextStyleId, table.LinkedStyleId)
                : null,
            _ => null
        };
    }

    private static ParagraphStyleDefinition CloneParagraphStyleDefinition(ParagraphStyleDefinition source)
    {
        var clone = new ParagraphStyleDefinition(source.Id)
        {
            Name = source.Name,
            BasedOnId = source.BasedOnId,
            NextStyleId = source.NextStyleId,
            LinkedStyleId = source.LinkedStyleId,
            UiPriority = source.UiPriority,
            QuickStyle = source.QuickStyle,
            SemiHidden = source.SemiHidden,
            UnhideWhenUsed = source.UnhideWhenUsed,
            AutoRedefine = source.AutoRedefine,
            Hidden = source.Hidden,
            Locked = source.Locked,
            PrimaryStyle = source.PrimaryStyle,
            CustomStyle = source.CustomStyle
        };

        ApplyParagraphStyleProperties(clone.ParagraphProperties, source.ParagraphProperties);
        ApplyTextStyleProperties(clone.RunProperties, source.RunProperties);
        return clone;
    }

    private static CharacterStyleDefinition CloneCharacterStyleDefinition(CharacterStyleDefinition source)
    {
        var clone = new CharacterStyleDefinition(source.Id)
        {
            Name = source.Name,
            BasedOnId = source.BasedOnId,
            NextStyleId = source.NextStyleId,
            LinkedStyleId = source.LinkedStyleId,
            UiPriority = source.UiPriority,
            QuickStyle = source.QuickStyle,
            SemiHidden = source.SemiHidden,
            UnhideWhenUsed = source.UnhideWhenUsed,
            AutoRedefine = source.AutoRedefine,
            Hidden = source.Hidden,
            Locked = source.Locked,
            PrimaryStyle = source.PrimaryStyle,
            CustomStyle = source.CustomStyle
        };

        ApplyTextStyleProperties(clone.RunProperties, source.RunProperties);
        return clone;
    }

    private static TableStyleDefinition CloneTableStyleDefinition(TableStyleDefinition source)
    {
        var clone = new TableStyleDefinition(source.Id)
        {
            Name = source.Name,
            BasedOnId = source.BasedOnId,
            NextStyleId = source.NextStyleId,
            LinkedStyleId = source.LinkedStyleId,
            UiPriority = source.UiPriority,
            QuickStyle = source.QuickStyle,
            SemiHidden = source.SemiHidden,
            UnhideWhenUsed = source.UnhideWhenUsed,
            AutoRedefine = source.AutoRedefine,
            Hidden = source.Hidden,
            Locked = source.Locked,
            PrimaryStyle = source.PrimaryStyle,
            CustomStyle = source.CustomStyle
        };

        ApplyTableProperties(clone.TableProperties, source.TableProperties);
        ApplyTableCellProperties(clone.CellProperties, source.CellProperties);
        foreach (var pair in source.Conditions)
        {
            clone.Conditions[pair.Key] = CloneTableStyleCondition(pair.Value);
        }

        return clone;
    }

    private static TableStyleConditionProperties CloneTableStyleCondition(TableStyleConditionProperties source)
    {
        var clone = new TableStyleConditionProperties();
        ApplyTableProperties(clone.TableProperties, source.TableProperties);
        ApplyTableCellProperties(clone.CellProperties, source.CellProperties);
        return clone;
    }

    private static void ApplyTableProperties(TableProperties target, TableProperties source)
    {
        target.Width = source.Width;
        target.WidthUnit = source.WidthUnit;
        target.Indent = source.Indent;
        target.IndentUnit = source.IndentUnit;
        target.Alignment = source.Alignment;
        target.LayoutMode = source.LayoutMode;
        target.CellSpacing = source.CellSpacing;
        target.CellSpacingUnit = source.CellSpacingUnit;
        target.CellPadding = source.CellPadding;
        target.ShadingColor = source.ShadingColor;
        target.Look = source.Look?.Clone();
        target.FloatingAnchor = source.FloatingAnchor is null ? null : CloneFloatingAnchor(source.FloatingAnchor);
        target.ColumnWidths.Clear();
        if (source.ColumnWidths.Count > 0)
        {
            target.ColumnWidths.AddRange(source.ColumnWidths);
        }

        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.Borders.InsideHorizontal = source.Borders.InsideHorizontal?.Clone();
        target.Borders.InsideVertical = source.Borders.InsideVertical?.Clone();
    }

    private static void ApplyTableCellProperties(TableCellProperties target, TableCellProperties source)
    {
        target.Padding = source.Padding;
        target.ShadingColor = source.ShadingColor;
        target.VerticalAlignment = source.VerticalAlignment;
        target.TextDirection = source.TextDirection;
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private static FloatingAnchor CloneFloatingAnchor(FloatingAnchor source)
    {
        return new FloatingAnchor
        {
            HorizontalReference = source.HorizontalReference,
            VerticalReference = source.VerticalReference,
            HorizontalAlignment = source.HorizontalAlignment,
            VerticalAlignment = source.VerticalAlignment,
            OffsetX = source.OffsetX,
            OffsetY = source.OffsetY,
            WrapStyle = source.WrapStyle,
            WrapSide = source.WrapSide,
            WrapPolygon = source.WrapPolygon is null ? null : new FloatingWrapPolygon(source.WrapPolygon.Points.ToArray()),
            BehindText = source.BehindText,
            AllowOverlap = source.AllowOverlap,
            ZOrder = source.ZOrder,
            Distance = source.Distance,
            AnchorOffset = source.AnchorOffset
        };
    }

    private static void ApplyParagraphStyleProperties(ParagraphStyleProperties target, ParagraphStyleProperties source)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.SpacingBeforeLines = source.SpacingBeforeLines;
        target.SpacingAfterLines = source.SpacingAfterLines;
        target.AutoSpacingBefore = source.AutoSpacingBefore;
        target.AutoSpacingAfter = source.AutoSpacingAfter;
        target.LineSpacing = source.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule;
        target.IndentLeft = source.IndentLeft;
        target.IndentRight = source.IndentRight;
        target.FirstLineIndent = source.FirstLineIndent;
        target.TabStops.Clear();
        foreach (var tab in source.TabStops)
        {
            target.TabStops.Add(tab.Clone());
        }

        target.KeepWithNext = source.KeepWithNext;
        target.KeepLinesTogether = source.KeepLinesTogether;
        target.WidowControl = source.WidowControl;
        target.PageBreakBefore = source.PageBreakBefore;
        target.ContextualSpacing = source.ContextualSpacing;
        target.Bidi = source.Bidi;
        target.TextDirection = source.TextDirection;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.ShadingColor = source.ShadingColor;
        target.SuppressLineNumbers = source.SuppressLineNumbers;
        target.DropCap = source.DropCap?.Clone();
        target.Frame = source.Frame?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private static ParagraphStyleProperties CloneParagraphStyleProperties(ParagraphStyleProperties source)
    {
        var clone = new ParagraphStyleProperties();
        ApplyParagraphStyleProperties(clone, source);
        return clone;
    }

    private static void ApplyTextStyleProperties(TextStyleProperties target, TextStyleProperties source)
    {
        target.FontFamily = source.FontFamily;
        target.FontFamilyAscii = source.FontFamilyAscii;
        target.FontFamilyHighAnsi = source.FontFamilyHighAnsi;
        target.FontFamilyEastAsia = source.FontFamilyEastAsia;
        target.FontFamilyComplexScript = source.FontFamilyComplexScript;
        target.FontSize = source.FontSize;
        target.FontSizeComplexScript = source.FontSizeComplexScript;
        target.FontWeight = source.FontWeight;
        target.FontStyle = source.FontStyle;
        target.Color = source.Color;
        target.ThemeColor = source.ThemeColor;
        target.ThemeTint = source.ThemeTint;
        target.ThemeShade = source.ThemeShade;
        target.VerticalPosition = source.VerticalPosition;
        target.BaselineOffset = source.BaselineOffset;
        target.LetterSpacing = source.LetterSpacing;
        target.HorizontalScale = source.HorizontalScale;
        target.Kerning = source.Kerning;
        target.Caps = source.Caps;
        target.SmallCaps = source.SmallCaps;
        target.Underline = source.Underline;
        target.UnderlineStyle = source.UnderlineStyle;
        target.UnderlineColor = source.UnderlineColor;
        target.UnderlineThemeColor = source.UnderlineThemeColor;
        target.UnderlineThemeTint = source.UnderlineThemeTint;
        target.UnderlineThemeShade = source.UnderlineThemeShade;
        target.Strikethrough = source.Strikethrough;
        target.HighlightColor = source.HighlightColor;
        target.Hidden = source.Hidden;
        target.ThemeFontAscii = source.ThemeFontAscii;
        target.ThemeFontHighAnsi = source.ThemeFontHighAnsi;
        target.ThemeFontEastAsia = source.ThemeFontEastAsia;
        target.ThemeFontComplexScript = source.ThemeFontComplexScript;
        target.Language = source.Language;
        target.LanguageEastAsia = source.LanguageEastAsia;
        target.LanguageBidi = source.LanguageBidi;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.OpenTypeFeatures = source.OpenTypeFeatures?.Clone();
        target.Effects = source.Effects?.Clone();
    }

    private readonly record struct StyleDefinitionBase(string? BasedOnId, string? NextStyleId, string? LinkedStyleId);

    private readonly record struct StyleKeyInfo(EditorStyleType Type, string Id);
}
